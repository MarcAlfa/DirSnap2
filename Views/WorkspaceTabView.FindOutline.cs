using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;
using Windows.UI;
using DirSnap2.Services;

namespace DirSnap2.Views;

/// <summary>
/// Find bar (search inside the preview or editor, also in read-only mode) and the
/// Outline panel (functions / classes / procedures / headings of the open file).
/// </summary>
public sealed partial class WorkspaceTabView
{
    // ==================== FIND ====================

    /// <summary>A match: Index/Length in EditorTextBox.Text, or Paragraph/Offset in the Markdown viewer.</summary>
    private readonly record struct FindMatch(int Index, int Length, int Paragraph, int ParagraphOffset);

    private const int MaxHighlightedMatches = 5000;

    private readonly List<FindMatch> _findMatches = new();
    private int _findCurrent = -1;
    private bool _findMatchesFromMarkdown;
    private bool _lastPointerInEditor;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _findRefreshTimer;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _outlineRefreshTimer;
    private int _outlineVersion;

    private static readonly SolidColorBrush AllMatchesBrush = new(Color.FromArgb(0x70, 0xFF, 0xD1, 0x66));   // soft yellow
    private static readonly SolidColorBrush CurrentMatchBrush = new(Color.FromArgb(0xD0, 0xF4, 0x8C, 0x06));  // orange

    private bool IsFindOpen => FindBar.Visibility == Visibility.Visible;

    private void InitFindAndOutline()
    {
        // Ctrl+F goes to the find bar when the user last worked in the preview, to the quick filter otherwise
        EditorPanel.AddHandler(PointerPressedEvent, new PointerEventHandler((_, _) => _lastPointerInEditor = true), true);
        LeftPanel.AddHandler(PointerPressedEvent, new PointerEventHandler((_, _) => _lastPointerInEditor = false), true);

        _findRefreshTimer = DispatcherQueue.CreateTimer();
        _findRefreshTimer.Interval = TimeSpan.FromMilliseconds(300);
        _findRefreshTimer.IsRepeating = false;
        _findRefreshTimer.Tick += (_, _) => { if (IsFindOpen) RunSearch(navigate: false); };

        _outlineRefreshTimer = DispatcherQueue.CreateTimer();
        _outlineRefreshTimer.Interval = TimeSpan.FromMilliseconds(400);
        _outlineRefreshTimer.IsRepeating = false;
        _outlineRefreshTimer.Tick += (_, _) => UpdateOutline();
    }

    /// <summary>Ctrl+F: opens the find bar when the focus (or the last click) is in the preview.</summary>
    public bool TryOpenFindFromShortcut()
    {
        if (ViewModel is not { IsPreviewUnlocked: true })
            return false;

        bool inEditor = IsFocusInside(EditorPanel);
        bool inLeft = IsFocusInside(LeftPanel);
        if (!inEditor && (inLeft || !_lastPointerInEditor))
            return false;

        OpenFind();
        return true;
    }

    /// <summary>F3 / Shift+F3.</summary>
    public void FindNextFromShortcut(bool backwards)
    {
        if (ViewModel is not { IsPreviewUnlocked: true }) return;

        if (!IsFindOpen || string.IsNullOrEmpty(FindTextBox.Text))
        {
            OpenFind();
            return;
        }
        MoveToMatch(backwards ? -1 : +1);
    }

    private bool IsFocusInside(DependencyObject container)
    {
        if (XamlRoot == null) return false;
        var current = FocusManager.GetFocusedElement(XamlRoot) as DependencyObject;
        while (current != null)
        {
            if (ReferenceEquals(current, container)) return true;
            current = VisualTreeHelper.GetParent(current);
        }
        return false;
    }

    private void OpenFind()
    {
        if (ViewModel is not { IsPreviewUnlocked: true }) return;

        // Pre-fill with the selected text (single line, like most editors)
        string selected = EditorTextBox.Visibility == Visibility.Visible
            ? EditorTextBox.SelectedText
            : MarkdownRichTextBlock.SelectedText;
        if (!string.IsNullOrEmpty(selected) && selected.Length <= 200 && selected.IndexOfAny(new[] { '\r', '\n' }) < 0)
        {
            FindTextBox.Text = selected; // TextChanged runs the search
        }

        FindMatchCaseToggle.IsChecked = ViewModel.ConfigService.Current.FindMatchCase;
        FindBar.Visibility = Visibility.Visible;
        FindTextBox.Focus(FocusState.Keyboard);
        FindTextBox.SelectAll();
        RunSearch(navigate: true);
    }

    /// <summary>0-based line the next search should select a match on (Search in files result), -1 = none.</summary>
    private int _pendingFindLine = -1;

    /// <summary>Opens the find bar on <paramref name="query"/> with the first match at/after the given line selected.</summary>
    private void ShowFindAtLine(string query, bool matchCase, int line)
    {
        if (ViewModel is not { IsPreviewUnlocked: true }) return;

        _pendingFindLine = line;
        FindMatchCaseToggle.IsChecked = matchCase;
        FindBar.Visibility = Visibility.Visible;
        FindTextBox.Text = query; // its TextChanged (later) finds the same match again
        RunSearch(navigate: true);
    }

    /// <summary>Index in the TextBox text (lone CR line breaks) where the 0-based line starts.</summary>
    private static int GetEditorLineStart(string text, int line)
    {
        int index = 0;
        for (int l = 0; l < line; l++)
        {
            int cr = text.IndexOf('\r', index);
            if (cr < 0) return text.Length;
            index = cr + 1;
            if (index < text.Length && text[index] == '\n') index++;
        }
        return index;
    }

    private void CloseFind(bool focusEditor)
    {
        FindBar.Visibility = Visibility.Collapsed;
        _findMatches.Clear();
        _findCurrent = -1;
        MarkdownRichTextBlock.TextHighlighters.Clear();

        if (focusEditor)
        {
            if (EditorTextBox.Visibility == Visibility.Visible)
                EditorTextBox.Focus(FocusState.Programmatic); // the selection (last match) stays
            else if (MarkdownScrollViewer.Visibility == Visibility.Visible)
                MarkdownScrollViewer.Focus(FocusState.Programmatic);
        }
    }

    /// <summary>Called when the text or the view mode changes: recount without moving the caret.</summary>
    private void ScheduleFindRefresh()
    {
        if (!IsFindOpen || _findRefreshTimer == null) return;
        _findRefreshTimer.Stop();
        _findRefreshTimer.Start();
    }

    private void RunSearch(bool navigate)
    {
        _findRefreshTimer?.Stop();
        var query = FindTextBox.Text;
        var previous = _findCurrent >= 0 && _findCurrent < _findMatches.Count ? _findMatches[_findCurrent] : (FindMatch?)null;

        _findMatches.Clear();
        _findCurrent = -1;
        MarkdownRichTextBlock.TextHighlighters.Clear();

        if (string.IsNullOrEmpty(query) || ViewModel is not { IsPreviewUnlocked: true })
        {
            UpdateFindCount();
            return;
        }

        var comparison = FindMatchCaseToggle.IsChecked == true ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        _findMatchesFromMarkdown = MarkdownScrollViewer.Visibility == Visibility.Visible;

        int startFrom; // match to select: the first one at/after this position
        if (_findMatchesFromMarkdown)
        {
            int p = 0;
            foreach (var block in MarkdownRichTextBlock.Blocks)
            {
                if (block is Paragraph para)
                {
                    var text = GetParagraphText(para, out _);
                    int idx = 0;
                    while ((idx = text.IndexOf(query, idx, comparison)) >= 0)
                    {
                        _findMatches.Add(new FindMatch(0, query.Length, p, idx));
                        idx += Math.Max(1, query.Length);
                    }
                }
                p++;
            }
            startFrom = 0;
            if (_pendingFindLine >= 0)
            {
                int line = _pendingFindLine;
                startFrom = _findMatches.FindIndex(m => m.Paragraph >= line);
            }
            else if (previous is { } prev && navigate)
                startFrom = _findMatches.FindIndex(m => m.Paragraph > prev.Paragraph || (m.Paragraph == prev.Paragraph && m.ParagraphOffset >= prev.ParagraphOffset));
        }
        else
        {
            var text = EditorTextBox.Text ?? string.Empty;
            int idx = 0;
            while ((idx = text.IndexOf(query, idx, comparison)) >= 0)
            {
                _findMatches.Add(new FindMatch(idx, query.Length, 0, 0));
                idx += Math.Max(1, query.Length);
            }
            // Start from the caret, so "find" continues from where the user is reading/typing
            int caret = _pendingFindLine >= 0
                ? GetEditorLineStart(text, _pendingFindLine)
                : previous?.Index ?? EditorTextBox.SelectionStart;
            startFrom = _findMatches.FindIndex(m => m.Index >= caret);
        }
        _pendingFindLine = -1;

        if (_findMatches.Count > 0)
        {
            _findCurrent = startFrom >= 0 ? startFrom : 0;
            if (!navigate && previous is { } keep)
            {
                // Background refresh: keep the same match if it still exists
                int same = _findMatchesFromMarkdown
                    ? _findMatches.FindIndex(m => m.Paragraph == keep.Paragraph && m.ParagraphOffset == keep.ParagraphOffset)
                    : _findMatches.FindIndex(m => m.Index == keep.Index);
                if (same >= 0) _findCurrent = same;
            }
        }

        ApplyMarkdownHighlights();
        if (navigate) ShowCurrentMatch();
        UpdateFindCount();
    }

    private void MoveToMatch(int delta)
    {
        if (_findMatches.Count == 0)
        {
            RunSearch(navigate: true);
            return;
        }
        _findCurrent = ((_findCurrent < 0 ? 0 : _findCurrent + delta) % _findMatches.Count + _findMatches.Count) % _findMatches.Count;
        ApplyMarkdownHighlights();
        ShowCurrentMatch();
        UpdateFindCount();
    }

    private void UpdateFindCount()
    {
        if (string.IsNullOrEmpty(FindTextBox.Text))
            FindCountText.Text = string.Empty;
        else if (_findMatches.Count == 0)
            FindCountText.Text = "No results";
        else
            FindCountText.Text = $"{_findCurrent + 1} of {_findMatches.Count}";
    }

    private void ShowCurrentMatch()
    {
        if (_findCurrent < 0 || _findCurrent >= _findMatches.Count) return;
        var m = _findMatches[_findCurrent];

        if (_findMatchesFromMarkdown)
        {
            if (GetMarkdownPointer(m.Paragraph, m.ParagraphOffset) is { } start)
                ScrollMarkdownTo(start);
        }
        else
        {
            SetEditorSelectionHighlight();
            EditorTextBox.Select(m.Index, m.Length);
            ScrollEditorToIndex(m.Index);
        }
    }

    private void ApplyMarkdownHighlights()
    {
        MarkdownRichTextBlock.TextHighlighters.Clear();
        if (!_findMatchesFromMarkdown || _findMatches.Count == 0) return;

        var all = new TextHighlighter { Background = AllMatchesBrush };
        var current = new TextHighlighter { Background = CurrentMatchBrush };

        for (int i = 0; i < _findMatches.Count && i < MaxHighlightedMatches; i++)
        {
            if (GetMarkdownRange(_findMatches[i]) is { } range)
                (i == _findCurrent ? current : all).Ranges.Add(range);
        }
        if (_findCurrent >= MaxHighlightedMatches && GetMarkdownRange(_findMatches[_findCurrent]) is { } cur)
            current.Ranges.Add(cur);

        MarkdownRichTextBlock.TextHighlighters.Add(all);
        MarkdownRichTextBlock.TextHighlighters.Add(current);
    }

    /// <summary>
    /// Start of each Markdown paragraph in TextHighlighter index space. TextRange counts only the
    /// characters of the Runs (paragraph breaks count 0), unlike TextPointer.Offset, which also
    /// counts element boundaries and drifts a few positions on every line. Reset on re-render.
    /// </summary>
    private int[]? _markdownParagraphStarts;

    private void InvalidateMarkdownIndex() => _markdownParagraphStarts = null;

    private TextRange? GetMarkdownRange(FindMatch m)
    {
        var blocks = MarkdownRichTextBlock.Blocks;
        if (_markdownParagraphStarts == null || _markdownParagraphStarts.Length != blocks.Count + 1)
        {
            var starts = new int[blocks.Count + 1];
            for (int i = 0; i < blocks.Count; i++)
            {
                int len = blocks[i] is Paragraph p ? GetParagraphText(p, out _).Length : 0;
                starts[i + 1] = starts[i] + len;
            }
            _markdownParagraphStarts = starts;
        }

        if (m.Paragraph < 0 || m.Paragraph >= blocks.Count) return null;
        int paraLength = _markdownParagraphStarts[m.Paragraph + 1] - _markdownParagraphStarts[m.Paragraph];
        int offset = Math.Clamp(m.ParagraphOffset, 0, paraLength);
        int length = Math.Clamp(m.Length, 0, paraLength - offset);
        if (length == 0) return null;
        return new TextRange { StartIndex = _markdownParagraphStarts[m.Paragraph] + offset, Length = length };
    }

    /// <summary>Text of a Markdown paragraph (the highlighter writes one paragraph per line, made of Runs).</summary>
    private static string GetParagraphText(Paragraph para, out List<(Run Run, int Start)> runs)
    {
        runs = new List<(Run, int)>();
        var sb = new System.Text.StringBuilder();
        foreach (var inline in para.Inlines)
        {
            if (inline is Run run)
            {
                runs.Add((run, sb.Length));
                sb.Append(run.Text);
            }
        }
        return sb.ToString();
    }

    private TextPointer? GetMarkdownPointer(int paragraphIndex, int offset)
    {
        if (paragraphIndex < 0 || paragraphIndex >= MarkdownRichTextBlock.Blocks.Count) return null;
        if (MarkdownRichTextBlock.Blocks[paragraphIndex] is not Paragraph para) return null;

        GetParagraphText(para, out var runs);
        if (runs.Count == 0) return para.ContentStart;

        for (int i = 0; i < runs.Count; i++)
        {
            var (run, start) = runs[i];
            int len = run.Text?.Length ?? 0;
            bool last = i == runs.Count - 1;
            // An offset at a run boundary belongs to the next run (start), except at the very end
            if (offset < start + len || (last && offset <= start + len))
                return run.ContentStart.GetPositionAtOffset(Math.Max(0, offset - start), LogicalDirection.Forward);
        }
        return runs[^1].Run.ContentEnd;
    }

    private void ScrollMarkdownTo(TextPointer pointer)
    {
        var rect = pointer.GetCharacterRect(LogicalDirection.Forward);
        double viewport = MarkdownScrollViewer.ViewportHeight;
        double top = rect.Top + MarkdownScrollViewer.Padding.Top;
        double? targetY = null;
        if (top < MarkdownScrollViewer.VerticalOffset || top + rect.Height > MarkdownScrollViewer.VerticalOffset + viewport)
            targetY = Math.Max(0, top - viewport / 3);

        double? targetX = null;
        double left = rect.Left + MarkdownScrollViewer.Padding.Left;
        if (left < MarkdownScrollViewer.HorizontalOffset || left > MarkdownScrollViewer.HorizontalOffset + MarkdownScrollViewer.ViewportWidth - 40)
            targetX = Math.Max(0, left - MarkdownScrollViewer.ViewportWidth / 3);

        if (targetY != null || targetX != null)
            MarkdownScrollViewer.ChangeView(targetX, targetY, null, true);
    }

    private int _editorScrollVersion;

    /// <summary>
    /// Scrolls the editor TextBox so that the character at <paramref name="index"/> is visible,
    /// <paramref name="topRatio"/> of the way down the view. GetRectFromCharacterIndex is not
    /// reliable for text far from the visible area (not laid out yet), so the scroll happens in
    /// two steps: a coarse jump from the line's position in the document, then a fine correction
    /// once that part of the text is on screen.
    /// </summary>
    private void ScrollEditorToIndex(int index, double topRatio = 1.0 / 3)
    {
        HookEditorScrollViewer();
        var sv = _editorScrollViewer;
        if (sv == null) return;

        var text = EditorTextBox.Text ?? string.Empty;
        index = Math.Clamp(index, 0, Math.Max(0, text.Length - 1));
        int version = ++_editorScrollVersion;

        // 1) Coarse: proportional position of the line in the document
        int line = 0, total = 1;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\r')
            {
                total++;
                if (i < index) line++;
            }
        }
        double viewport = sv.ViewportHeight;
        double padTop = EditorTextBox.Padding.Top;
        double contentHeight = Math.Max(0, sv.ExtentHeight - padTop - EditorTextBox.Padding.Bottom);
        double lineHeight = contentHeight / total;
        double estimatedTop = padTop + contentHeight * line / total;
        bool estimatedVisible = estimatedTop >= sv.VerticalOffset && estimatedTop + lineHeight <= sv.VerticalOffset + viewport;
        if (!estimatedVisible)
            sv.ChangeView(null, Math.Max(0, estimatedTop - viewport * topRatio), null, true);

        // 2) Fine: once the area is laid out, the character rect (relative to the TextBox view) is exact
        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(estimatedVisible ? 0 : 60);
        timer.IsRepeating = false;
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (version != _editorScrollVersion || !ReferenceEquals(sv, _editorScrollViewer)) return;
            var rect = EditorTextBox.GetRectFromCharacterIndex(Math.Clamp(index, 0, Math.Max(0, (EditorTextBox.Text ?? string.Empty).Length - 1)), false);
            double vp = sv.ViewportHeight;
            double? targetY = null;
            // Ignore implausible rects (text still not laid out): the coarse jump stays
            if (rect.Top > -vp * 3 && rect.Top < vp * 4 && (rect.Top < 0 || rect.Bottom > vp))
                targetY = Math.Max(0, sv.VerticalOffset + rect.Top - vp * topRatio);

            double? targetX = null;
            double width = sv.ViewportWidth;
            if (!(ViewModel?.EditorWrap ?? true) && rect.Left > -width * 3 && rect.Left < width * 4)
            {
                if (rect.Left < 0 || rect.Left > width - 40)
                    targetX = Math.Max(0, sv.HorizontalOffset + rect.Left - width / 3);
            }

            if (targetY != null || targetX != null)
                sv.ChangeView(targetX, targetY, null, true);
        };
        timer.Start();
    }

    private void SetEditorSelectionHighlight()
    {
        // The match must stay visible while the focus is in the find box
        if (Application.Current.Resources.TryGetValue("AppAccentBrush", out var b) && b is SolidColorBrush accent)
            EditorTextBox.SelectionHighlightColorWhenNotFocused = new SolidColorBrush(accent.Color);
    }

    private void FindButton_Click(object sender, RoutedEventArgs e)
    {
        if (IsFindOpen) CloseFind(focusEditor: true);
        else OpenFind();
    }

    private void FindTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (IsFindOpen) RunSearch(navigate: true);
    }

    private void FindTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        var shift = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift);
        bool isShift = (shift & Windows.UI.Core.CoreVirtualKeyStates.Down) != 0;

        if (e.Key == VirtualKey.Enter)
        {
            MoveToMatch(isShift ? -1 : +1);
            e.Handled = true;
        }
        else if (e.Key == VirtualKey.Escape)
        {
            CloseFind(focusEditor: true);
            e.Handled = true;
        }
    }

    private void FindMatchCaseToggle_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel != null)
        {
            ViewModel.ConfigService.Current.FindMatchCase = FindMatchCaseToggle.IsChecked == true;
            ViewModel.ConfigService.Save();
        }
        RunSearch(navigate: true);
        FindTextBox.Focus(FocusState.Programmatic);
    }

    private void FindNext_Click(object sender, RoutedEventArgs e) => MoveToMatch(+1);
    private void FindPrevious_Click(object sender, RoutedEventArgs e) => MoveToMatch(-1);
    private void FindClose_Click(object sender, RoutedEventArgs e) => CloseFind(focusEditor: true);

    // ==================== OUTLINE ====================

    private void ScheduleOutlineRefresh()
    {
        if (_outlineRefreshTimer == null) return;
        _outlineRefreshTimer.Stop();
        _outlineRefreshTimer.Start();
    }

    private async void UpdateOutline()
    {
        _outlineRefreshTimer?.Stop();
        int version = ++_outlineVersion;
        var vm = ViewModel;

        void Hide()
        {
            OutlineToggleButton.Visibility = Visibility.Collapsed;
            OutlinePanel.Visibility = Visibility.Collapsed;
            OutlineListView.ItemsSource = null;
        }

        if (vm is not { IsPreviewUnlocked: true, HasActiveFile: true } || !CodeOutlineService.MayBeSupported(vm.CurrentEditingFilePath))
        {
            Hide();
            return;
        }

        // Parsed even when the panel is closed: for a .txt the content decides whether
        // it is PL/SQL, and so whether the outline button is shown at all
        var path = vm.CurrentEditingFilePath;
        var text = vm.EditorText;
        List<OutlineItem>? items;
        try
        {
            items = await Task.Run(() => CodeOutlineService.Parse(path, text));
        }
        catch (Exception ex)
        {
            AppLogger.Error("[Outline] Parse error", ex);
            items = new List<OutlineItem>();
        }

        if (version != _outlineVersion) return; // a newer refresh is on its way
        if (items == null)
        {
            Hide();
            return;
        }

        bool open = vm.ConfigService.Current.OutlinePanelOpen;
        OutlineToggleButton.Visibility = Visibility.Visible;
        OutlineToggleButton.IsChecked = open;
        OutlinePanel.Visibility = open ? Visibility.Visible : Visibility.Collapsed;

        OutlineListView.ItemsSource = items;
        OutlineCountText.Text = items.Count > 0 ? $"({items.Count})" : string.Empty;
        OutlineEmptyText.Text = CodeOutlineService.IsMarkdown(path) ? "No headings found" : "No functions found";
        OutlineEmptyText.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ScheduleOutlineFollow();
    }

    private void SetOutlineOpen(bool open)
    {
        if (ViewModel == null) return;
        ViewModel.ConfigService.Current.OutlinePanelOpen = open;
        ViewModel.ConfigService.Save();
        UpdateOutline();
    }

    private void OutlineToggleButton_Click(object sender, RoutedEventArgs e) => SetOutlineOpen(OutlineToggleButton.IsChecked == true);
    private void OutlineClose_Click(object sender, RoutedEventArgs e) => SetOutlineOpen(false);

    private void OutlineListView_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is OutlineItem item)
            GoToLine(item.Line, item.Name);
    }

    /// <summary>Scrolls the preview/editor to a 0-based line and highlights the name on it.</summary>
    private void GoToLine(int line, string name, double topRatio = 0.08)
    {
        if (MarkdownScrollViewer.Visibility == Visibility.Visible)
        {
            if (line < 0 || line >= MarkdownRichTextBlock.Blocks.Count || MarkdownRichTextBlock.Blocks[line] is not Paragraph para)
                return;

            var text = GetParagraphText(para, out _);
            int col = Math.Max(0, text.IndexOf(name, StringComparison.Ordinal));
            if (GetMarkdownPointer(line, 0) is { } lineStart)
            {
                // Put the heading near the top of the view
                var rect = lineStart.GetCharacterRect(LogicalDirection.Forward);
                MarkdownScrollViewer.ChangeView(null, Math.Max(0, rect.Top + MarkdownScrollViewer.Padding.Top - 8), null, true);
            }

            // Short highlight of the target (find highlights are restored afterwards)
            var range = GetMarkdownRange(new FindMatch(0, text.Length - col, line, col));
            if (range is { } r)
            {
                var flash = new TextHighlighter { Background = CurrentMatchBrush };
                flash.Ranges.Add(r);
                MarkdownRichTextBlock.TextHighlighters.Add(flash);
                var timer = DispatcherQueue.CreateTimer();
                timer.Interval = TimeSpan.FromMilliseconds(1200);
                timer.IsRepeating = false;
                timer.Tick += (_, _) => MarkdownRichTextBlock.TextHighlighters.Remove(flash);
                timer.Start();
            }
            return;
        }

        if (EditorTextBox.Visibility != Visibility.Visible) return;

        var all = EditorTextBox.Text ?? string.Empty;
        int index = 0;
        for (int l = 0; l < line && index >= 0; l++)
        {
            index = all.IndexOf('\r', index);
            if (index >= 0) index++;
        }
        if (index < 0) return;

        int lineEnd = all.IndexOf('\r', index);
        if (lineEnd < 0) lineEnd = all.Length;
        var lineText = all.Substring(index, lineEnd - index);
        int nameCol = lineText.IndexOf(name, StringComparison.Ordinal);

        SetEditorSelectionHighlight();
        if (nameCol >= 0)
            EditorTextBox.Select(index + nameCol, name.Length);
        else
            EditorTextBox.Select(index, 0);

        // Put the definition near the top of the view
        HookEditorScrollViewer();
        _editorScrollViewer?.ChangeView(0, null, null, true);
        ScrollEditorToIndex(index, topRatio);
    }
}
