using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.System;
using DirSnap2.Services;

namespace DirSnap2.Views;

/// <summary>
/// Markdown editing with syntax colors: a RichEditBox used as a plain-text editor (only the text is
/// read and saved), colored with the same tokenizer as the Markdown preview.
/// - Colors are re-applied shortly after each change, only on the changed lines (to the end of the
///   file when a ``` fence is involved).
/// - Each recolor is one undo step: Ctrl+Z / Ctrl+Y are handled here and step over those
///   color-only steps, so the user only sees text changes being undone/redone.
/// - Paste inserts plain text; the RichEdit formatting shortcuts are blocked.
/// The other parts of the view use the helpers below (GetEditorText, SelectInEditor, ...) to work
/// with whichever editor is visible (EditorTextBox for other files, MarkdownEditBox for .md).
/// </summary>
public sealed partial class WorkspaceTabView
{
    /// <summary>Above this size the Markdown editor stays plain (no colors), to stay responsive.</summary>
    private const int MarkdownEditColorLimit = 1_000_000;

    private string _mdText = string.Empty;          // editor text (lone CR line breaks, no final paragraph mark)
    private string _mdColoredText = string.Empty;   // text at the last recolor
    private bool _mdColoredDark;
    private bool _mdColorsValid;
    private bool _mdUpdating;                        // programmatic document changes in progress
    private bool _mdLoading;                         // SetText of a new text in progress
    // The document must not be touched in the same pass that turns the editor Collapsed -> Visible
    // (AccessViolation in SetText, native crash): every show loads the text after a layout pass.
    private bool _mdDocReady;       // the visible editor holds the current text and can be used
    private bool _mdLoadScheduled;  // a deferred load is waiting
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _mdRecolorTimer;
    private ScrollViewer? _mdEditScrollViewer;
    private readonly List<MarkdownSpan> _mdSpans = new();

    private bool IsMarkdownEditVisible => MarkdownEditBox.Visibility == Visibility.Visible;

    /// <summary>A text editor (plain TextBox or Markdown editor) is shown.</summary>
    private bool IsTextEditorVisible => EditorTextBox.Visibility == Visibility.Visible || IsMarkdownEditVisible;

    private Control ActiveTextEditor => IsMarkdownEditVisible ? MarkdownEditBox : EditorTextBox;

    private void InitMarkdownEdit()
    {
        _mdRecolorTimer = DispatcherQueue.CreateTimer();
        _mdRecolorTimer.Interval = TimeSpan.FromMilliseconds(120);
        _mdRecolorTimer.IsRepeating = false;
        _mdRecolorTimer.Tick += (_, _) => RecolorMarkdownEdit(full: false);

        // No formatting toolbar on selection / right click: this is a plain-text editor
        MarkdownEditBox.SelectionFlyout = null;
        MarkdownEditBox.ContextFlyout = BuildMarkdownEditMenu();
        MarkdownEditBox.Loaded += (_, _) => HookEditorScrollViewer();
        MarkdownEditBox.SelectionChanged += (_, _) => ScheduleLineInfo();
    }

    // ==================== EDITOR-AGNOSTIC HELPERS ====================

    private string GetEditorText() => IsMarkdownEditVisible ? _mdText : EditorTextBox.Text ?? string.Empty;

    private int GetEditorSelectionStart()
    {
        if (!IsMarkdownEditVisible) return EditorTextBox.SelectionStart;
        if (!_mdDocReady) return 0;
        return Math.Clamp(MarkdownEditBox.Document.Selection.StartPosition, 0, _mdText.Length);
    }

    private string GetEditorSelectedText()
    {
        if (!IsMarkdownEditVisible) return EditorTextBox.SelectedText ?? string.Empty;
        if (!_mdDocReady) return string.Empty;
        return MarkdownEditBox.Document.Selection.Text ?? string.Empty;
    }

    private void SelectInEditor(int start, int length)
    {
        if (IsMarkdownEditVisible)
        {
            if (_mdDocReady) MarkdownEditBox.Document.Selection.SetRange(start, start + length);
        }
        else
            EditorTextBox.Select(start, length);
    }

    private void FocusTextEditor() => ActiveTextEditor.Focus(FocusState.Programmatic);

    private ScrollViewer? GetEditorScrollViewer()
    {
        HookEditorScrollViewer();
        return IsMarkdownEditVisible ? _mdEditScrollViewer : _editorScrollViewer;
    }

    /// <summary>
    /// Rect of the character at <paramref name="index"/>, relative to the visible area of the editor
    /// (like TextBox.GetRectFromCharacterIndex).
    /// </summary>
    private Rect GetEditorCharRect(int index)
    {
        if (!IsMarkdownEditVisible)
            return EditorTextBox.GetRectFromCharacterIndex(index, false);

        if (!_mdDocReady) return Rect.Empty;
        // RichEdit rects are relative to the text content: add the padding, remove the scrolling
        MarkdownEditBox.Document.GetRange(index, index).GetRect(PointOptions.None, out var r, out _);
        var sv = _mdEditScrollViewer;
        var pad = MarkdownEditBox.Padding;
        return new Rect(r.X + pad.Left - (sv?.HorizontalOffset ?? 0), r.Y + pad.Top - (sv?.VerticalOffset ?? 0), r.Width, r.Height);
    }

    // ==================== LOAD / TEXT SYNC ====================

    private static string NormalizeLineBreaks(string text) =>
        text.Replace("\r\n", "\r").Replace('\n', '\r');

    /// <summary>Text of the document without the final paragraph mark RichEdit always keeps.</summary>
    private string ReadMarkdownEditText()
    {
        MarkdownEditBox.Document.GetText(TextGetOptions.None, out var text);
        text ??= string.Empty;
        return text.EndsWith('\r') ? text[..^1] : text;
    }

    /// <summary>
    /// Shows the Markdown editor; (re)loads the text when it was hidden or the view model text is
    /// not the one being edited (another file, reload from disk).
    /// </summary>
    private void ShowMarkdownEditor()
    {
        if (ViewModel == null) return;

        bool wasVisible = IsMarkdownEditVisible;
        MarkdownEditBox.Visibility = Visibility.Visible;
        MarkdownEditBox.TextWrapping = ViewModel.EditorTextWrapping;

        if (!wasVisible)
        {
            _mdDocReady = false;
            ScheduleMarkdownLoad(0); // after the layout pass that shows the editor
            return;
        }
        if (!_mdDocReady) return; // the deferred load will read the latest text

        var vmText = ViewModel.EditorText ?? string.Empty;
        bool same = ReferenceEquals(vmText, _mdText) || vmText == _mdText || NormalizeLineBreaks(vmText) == _mdText;
        if (!same)
        {
            LoadMarkdownEditor(vmText);
        }
        else if (_mdColoredDark != (ThemeService.ActiveTheme != ElementTheme.Light))
        {
            RecolorMarkdownEdit(full: true);
        }
        HookEditorScrollViewer();
    }

    private void HideMarkdownEditor()
    {
        _mdRecolorTimer?.Stop();
        _mdDocReady = false;
        MarkdownEditBox.Visibility = Visibility.Collapsed;
    }

    /// <summary>Loads the view model text once the shown editor has been laid out (retries every 30 ms).</summary>
    private void ScheduleMarkdownLoad(int attempt)
    {
        if (_mdLoadScheduled && attempt == 0) return;
        _mdLoadScheduled = true;

        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(30);
        timer.IsRepeating = false;
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (!IsMarkdownEditVisible || _mdDocReady || ViewModel == null)
            {
                _mdLoadScheduled = false;
                return;
            }
            if ((!MarkdownEditBox.IsLoaded || MarkdownEditBox.ActualHeight <= 0) && attempt < 100)
            {
                ScheduleMarkdownLoad(attempt + 1);
                return;
            }

            _mdLoadScheduled = false;
            LoadMarkdownEditor(ViewModel.EditorText ?? string.Empty);
            _mdDocReady = true;
            HookEditorScrollViewer();
            ScheduleLineInfo();
            ScheduleOutlineFollow();
            ScheduleFindRefresh();
        };
        timer.Start();
    }

    private void LoadMarkdownEditor(string text)
    {
        _mdRecolorTimer?.Stop();
        var doc = MarkdownEditBox.Document;
        _mdUpdating = true;
        _mdLoading = true;
        try
        {
            doc.SetText(TextSetOptions.None, NormalizeLineBreaks(text));
            _mdText = ReadMarkdownEditText();
            _mdColorsValid = false;
            RecolorMarkdownEdit(full: true);
            doc.ClearUndoRedoHistory(); // the first coloring is not something to undo
            doc.Selection.SetRange(0, 0);
        }
        catch (Exception ex)
        {
            AppLogger.Error("[MarkdownEdit] Load error", ex);
        }
        finally
        {
            _mdUpdating = false;
            _mdLoading = false;
        }
        _editorLineStarts = null;
        _mdEditScrollViewer?.ChangeView(0, 0, null, true);
    }

    private void MarkdownEditBox_TextChanged(object sender, RoutedEventArgs e)
    {
        if (!IsMarkdownEditVisible || _mdLoading || !_mdDocReady) return;
        var text = ReadMarkdownEditText();
        if (text == _mdText) return; // formatting only (recolor)

        CommitMarkdownText(text);
        if (!_mdUpdating) ScheduleMarkdownRecolor();
    }

    /// <summary>The editor text changed: update the view model (dirty flag, save) and the line info.</summary>
    private void CommitMarkdownText(string text)
    {
        _mdText = text;
        if (ViewModel != null) ViewModel.EditorText = text;
        _editorLineStarts = null;
        ScheduleLineInfo();
        ScheduleOutlineFollow();
        UpdateScrollToTopButtonVisibility();
    }

    // ==================== COLORS ====================

    private void ScheduleMarkdownRecolor()
    {
        if (_mdRecolorTimer == null) return;
        _mdRecolorTimer.Stop();
        _mdRecolorTimer.Start();
    }

    /// <summary>Applies the Markdown colors to the changed lines (or to the whole text).</summary>
    private void RecolorMarkdownEdit(bool full)
    {
        _mdRecolorTimer?.Stop();
        var text = _mdText;
        bool dark = ThemeService.ActiveTheme != ElementTheme.Light;
        if (!_mdColorsValid || dark != _mdColoredDark) full = true;
        if (!full && text == _mdColoredText) return;

        var doc = MarkdownEditBox.Document;
        if (text.Length > MarkdownEditColorLimit)
        {
            if (full) ApplyPlainFormat(doc, 0, text.Length + 1, dark);
            _mdColoredText = text;
            _mdColoredDark = dark;
            _mdColorsValid = true;
            return;
        }

        // Line starts of the new text
        var lineStarts = new List<int> { 0 };
        for (int i = text.IndexOf('\r'); i >= 0; i = text.IndexOf('\r', i + 1))
            lineStarts.Add(i + 1);
        int lastLine = lineStarts.Count - 1;

        int fromLine = 0, toLine = lastLine;
        if (!full)
        {
            // Changed region: what is left after the common start and end of old and new text
            var old = _mdColoredText;
            int max = Math.Min(old.Length, text.Length);
            int prefix = 0;
            while (prefix < max && old[prefix] == text[prefix]) prefix++;
            int suffix = 0;
            while (suffix < max - prefix && old[old.Length - 1 - suffix] == text[text.Length - 1 - suffix]) suffix++;
            int newEnd = text.Length - suffix;
            int oldEnd = old.Length - suffix;

            fromLine = LineOf(lineStarts, prefix);
            toLine = LineOf(lineStarts, newEnd);

            // A ``` added or removed changes the code-block state of everything below
            int regionStart = lineStarts[fromLine];
            if (ContainsFence(text, regionStart, LineEnd(text, newEnd)) || ContainsFence(old, regionStart, LineEnd(old, oldEnd)))
                toLine = lastLine;
        }

        // Code-block state at the first line to color
        bool inFence = false;
        for (int l = 0; l < fromLine; l++)
        {
            int s = lineStarts[l];
            if (MarkdownHighlighter.IsFenceLine(text.Substring(s, LineEnd(text, s) - s)))
                inFence = !inFence;
        }

        var formats = new Dictionary<MarkdownSpanKind, ITextCharacterFormat>();
        ITextCharacterFormat FormatOf(MarkdownSpanKind kind)
        {
            if (!formats.TryGetValue(kind, out var f))
            {
                f = CreateMarkdownFormat(doc, kind, dark);
                formats[kind] = f;
            }
            return f;
        }

        _mdUpdating = true;
        doc.BatchDisplayUpdates();
        doc.BeginUndoGroup(); // one undo step, skipped by Ctrl+Z (see MarkdownUndoRedo)
        try
        {
            // Base format on the whole region (line breaks and empty lines included), then the spans
            int regionStart = lineStarts[fromLine];
            int regionEnd = toLine == lastLine ? text.Length + 1 : lineStarts[toLine + 1];
            doc.GetRange(regionStart, regionEnd).CharacterFormat = FormatOf(MarkdownSpanKind.Normal);

            for (int l = fromLine; l <= toLine; l++)
            {
                int start = lineStarts[l];
                var line = text.Substring(start, LineEnd(text, start) - start);
                MarkdownHighlighter.TokenizeLine(line, ref inFence, _mdSpans);
                foreach (var span in _mdSpans)
                {
                    if (span.Kind == MarkdownSpanKind.Normal || span.Length == 0) continue;
                    doc.GetRange(start + span.Start, start + span.Start + span.Length).CharacterFormat = FormatOf(span.Kind);
                }
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"[MarkdownEdit] Recolor: {ex.Message}");
        }
        finally
        {
            doc.EndUndoGroup();
            doc.ApplyDisplayUpdates();
            _mdUpdating = false;
        }

        _mdColoredText = text;
        _mdColoredDark = dark;
        _mdColorsValid = true;
    }

    private void ApplyPlainFormat(RichEditTextDocument doc, int start, int end, bool dark)
    {
        _mdUpdating = true;
        try
        {
            doc.GetRange(start, end).CharacterFormat = CreateMarkdownFormat(doc, MarkdownSpanKind.Normal, dark);
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"[MarkdownEdit] Plain format: {ex.Message}");
        }
        finally
        {
            _mdUpdating = false;
        }
    }

    private ITextCharacterFormat CreateMarkdownFormat(RichEditTextDocument doc, MarkdownSpanKind kind, bool dark)
    {
        var f = doc.GetDefaultCharacterFormat();
        f.Name = "Consolas";
        f.Size = (float)(MarkdownEditBox.FontSize * 0.75); // RichEdit sizes are in points, XAML FontSize in pixels
        f.ForegroundColor = MarkdownHighlighter.GetColor(kind, dark);
        f.Bold = FormatEffect.Off;
        f.Italic = FormatEffect.Off;
        f.Weight = MarkdownHighlighter.GetWeight(kind);
        f.Underline = kind == MarkdownSpanKind.Link ? UnderlineType.Single : UnderlineType.None;
        return f;
    }

    private static int LineOf(List<int> lineStarts, int index)
    {
        int idx = lineStarts.BinarySearch(index);
        return idx >= 0 ? idx : ~idx - 1;
    }

    /// <summary>End (exclusive, before the line break) of the line containing <paramref name="index"/>.</summary>
    private static int LineEnd(string text, int index)
    {
        if (index >= text.Length) return text.Length;
        int cr = text.IndexOf('\r', index);
        return cr < 0 ? text.Length : cr;
    }

    private static bool ContainsFence(string text, int start, int end) =>
        end > start && text.IndexOf("```", start, end - start, StringComparison.Ordinal) >= 0;

    // ==================== UNDO / REDO ====================

    /// <summary>
    /// Undo/redo of text changes only: the color steps recorded between them are stepped over, and
    /// a redo also re-applies the colors that followed the redone change.
    /// </summary>
    private void MarkdownUndoRedo(bool redo)
    {
        var doc = MarkdownEditBox.Document;
        _mdRecolorTimer?.Stop(); // a recolor now would clear the redo history
        var before = _mdText;
        var now = before;

        _mdUpdating = true;
        try
        {
            for (int i = 0; i < 10000; i++)
            {
                if (redo ? !doc.CanRedo() : !doc.CanUndo()) break;
                if (redo) doc.Redo(); else doc.Undo();
                now = ReadMarkdownEditText();
                if (now != before) break;
            }

            if (redo && now != before)
            {
                // Redo the color step(s) that followed this change, but not the next text change
                for (int i = 0; i < 10000 && doc.CanRedo(); i++)
                {
                    doc.Redo();
                    if (ReadMarkdownEditText() != now)
                    {
                        doc.Undo();
                        break;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"[MarkdownEdit] Undo/redo: {ex.Message}");
            now = ReadMarkdownEditText();
        }
        finally
        {
            _mdUpdating = false;
        }

        if (now == before) return;

        CommitMarkdownText(now);
        _mdColoredText = now; // the colors came back with the history
        if (!doc.CanRedo())
            ScheduleMarkdownRecolor(); // nothing left to redo: safe to fix any stale color
    }

    // ==================== KEYS / CLIPBOARD ====================

    private static bool IsKeyDown(VirtualKey key) =>
        (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(key) & Windows.UI.Core.CoreVirtualKeyStates.Down) != 0;

    private void MarkdownEditBox_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        bool ctrl = IsKeyDown(VirtualKey.Control);
        bool shift = IsKeyDown(VirtualKey.Shift);
        bool alt = IsKeyDown(VirtualKey.Menu);

        if (!_mdDocReady)
        {
            e.Handled = true; // the text is still being loaded
            return;
        }

        if (e.Key == VirtualKey.Tab)
        {
            if (ctrl || alt) return; // Ctrl+Tab switches workspace tabs
            if (!shift) MarkdownEditBox.Document.Selection.TypeText("\t");
            e.Handled = true;
            return;
        }

        // RichEdit turns Shift+Enter into a soft line break (\v) and Ctrl+Enter into a page break:
        // in a text file both must be a normal line break
        if (e.Key == VirtualKey.Enter && (shift || ctrl) && !alt)
        {
            MarkdownEditBox.Document.Selection.TypeText("\r");
            e.Handled = true;
            return;
        }

        if (!ctrl || alt) return; // AltGr (Ctrl+Alt) types characters

        switch (e.Key)
        {
            case VirtualKey.Z:
                MarkdownUndoRedo(redo: shift);
                e.Handled = true;
                break;
            case VirtualKey.Y:
                MarkdownUndoRedo(redo: true);
                e.Handled = true;
                break;

            // RichEdit formatting shortcuts (alignment, line spacing, lists, caps, font size,
            // subscript/superscript): this is plain text. App shortcuts are not touched.
            case VirtualKey.R:
            case VirtualKey.J:
            case VirtualKey.L when shift:
            case VirtualKey.A when shift:
            case VirtualKey.E when shift:
            case >= VirtualKey.Number0 and <= VirtualKey.Number9:
            case >= (VirtualKey)186 and <= (VirtualKey)192: // ; = , - . / `
            case >= (VirtualKey)219 and <= (VirtualKey)222: // [ \ ] '
                e.Handled = true;
                break;
        }
    }

    private void MarkdownEditBox_Paste(object sender, TextControlPasteEventArgs e)
    {
        e.Handled = true; // never paste formatted text
        if (_mdDocReady) _ = PasteIntoMarkdownEditorAsync();
    }

    private async Task PasteIntoMarkdownEditorAsync()
    {
        try
        {
            var content = Clipboard.GetContent();
            if (!content.Contains(StandardDataFormats.Text)) return;
            var text = NormalizeLineBreaks(await content.GetTextAsync() ?? string.Empty);
            var selection = MarkdownEditBox.Document.Selection;
            selection.SetText(TextSetOptions.None, text);
            selection.Collapse(false);
            selection.ScrollIntoView(PointOptions.None);
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"[MarkdownEdit] Paste: {ex.Message}");
        }
    }

    private void CopyMarkdownSelection(bool cut)
    {
        try
        {
            var selection = MarkdownEditBox.Document.Selection;
            var text = selection.Text ?? string.Empty;
            if (text.Length == 0) return;
            var package = new DataPackage();
            package.SetText(text.Replace("\r", "\r\n"));
            Clipboard.SetContent(package);
            if (cut) selection.SetText(TextSetOptions.None, string.Empty);
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"[MarkdownEdit] Copy: {ex.Message}");
        }
    }

    private MenuFlyout BuildMarkdownEditMenu()
    {
        var menu = new MenuFlyout();

        MenuFlyoutItem Add(string text, Symbol icon, Action action)
        {
            var item = new MenuFlyoutItem { Text = text, Icon = new SymbolIcon(icon) };
            item.Click += (_, _) => { if (_mdDocReady) action(); };
            menu.Items.Add(item);
            return item;
        }

        var cut = Add("Cut", Symbol.Cut, () => CopyMarkdownSelection(cut: true));
        var copy = Add("Copy", Symbol.Copy, () => CopyMarkdownSelection(cut: false));
        Add("Paste", Symbol.Paste, () => _ = PasteIntoMarkdownEditorAsync());
        menu.Items.Add(new MenuFlyoutSeparator());
        var undo = Add("Undo", Symbol.Undo, () => MarkdownUndoRedo(redo: false));
        var redo = Add("Redo", Symbol.Redo, () => MarkdownUndoRedo(redo: true));
        menu.Items.Add(new MenuFlyoutSeparator());
        Add("Select All", Symbol.SelectAll, () => MarkdownEditBox.Document.Selection.SetRange(0, _mdText.Length + 1));

        menu.Opening += (_, _) =>
        {
            if (!_mdDocReady) return;
            var doc = MarkdownEditBox.Document;
            bool hasSelection = doc.Selection.Length != 0;
            cut.IsEnabled = hasSelection;
            copy.IsEnabled = hasSelection;
            undo.IsEnabled = doc.CanUndo();
            redo.IsEnabled = doc.CanRedo();
        };
        return menu;
    }
}
