using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using DirSnap2.Services;

namespace DirSnap2.Views;

/// <summary>
/// Line navigation: "Ln x / total" in the footer, Go to line (Ctrl+G), and the Outline entry
/// of the function shown in the view (follows scrolling).
/// </summary>
public sealed partial class WorkspaceTabView
{
    /// <summary>Height ratio of the view where the "current" line is read (definitions jumped to land at 8%).</summary>
    private const double OutlineFollowRatio = 0.25;

    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _lineInfoTimer;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _outlineFollowTimer;

    // Line starts of EditorTextBox.Text (lone CR line breaks), rebuilt after each text change
    private int[]? _editorLineStarts;
    private int _editorTextLength;
    // ContentStart offsets of the Markdown paragraphs (one per line), rebuilt after each render
    private int[]? _markdownBlockOffsets;

    private void InitLineNav()
    {
        _lineInfoTimer = DispatcherQueue.CreateTimer();
        _lineInfoTimer.Interval = TimeSpan.FromMilliseconds(120);
        _lineInfoTimer.IsRepeating = false;
        _lineInfoTimer.Tick += (_, _) => UpdateLineInfo();

        _outlineFollowTimer = DispatcherQueue.CreateTimer();
        _outlineFollowTimer.Interval = TimeSpan.FromMilliseconds(120);
        _outlineFollowTimer.IsRepeating = false;
        _outlineFollowTimer.Tick += (_, _) => UpdateOutlineFollow();

        EditorTextBox.TextChanged += (_, _) =>
        {
            _editorLineStarts = null;
            ScheduleLineInfo();
            ScheduleOutlineFollow();
        };
        EditorTextBox.SelectionChanged += (_, _) => ScheduleLineInfo();
        MarkdownScrollViewer.ViewChanged += (_, _) => ScheduleOutlineFollow();
    }

    private void ScheduleLineInfo()
    {
        if (_lineInfoTimer == null) return;
        _lineInfoTimer.Stop();
        _lineInfoTimer.Start();
    }

    private void ScheduleOutlineFollow()
    {
        if (_outlineFollowTimer == null) return;
        _outlineFollowTimer.Stop();
        _outlineFollowTimer.Start();
    }

    /// <summary>The Markdown viewer was re-rendered: paragraph offsets changed.</summary>
    private void InvalidateLineNavMarkdown() => _markdownBlockOffsets = null;

    // ==================== FOOTER: LINE x / TOTAL ====================

    private void UpdateLineInfo()
    {
        _lineInfoTimer?.Stop();

        if (ViewModel is not { IsPreviewUnlocked: true, HasActiveFile: true })
        {
            FooterLineText.Visibility = Visibility.Collapsed;
            return;
        }

        if (MarkdownScrollViewer.Visibility == Visibility.Visible)
        {
            int count = MarkdownRichTextBlock.Blocks.Count;
            FooterLineText.Text = count == 1 ? "1 line" : $"{count:N0} lines";
        }
        else if (EditorTextBox.Visibility == Visibility.Visible)
        {
            var text = EditorTextBox.Text ?? string.Empty;
            int caret = Math.Clamp(EditorTextBox.SelectionStart, 0, text.Length);
            // Vectorized count: fast even on the 5 MB maximum
            int line = text.AsSpan(0, caret).Count('\r') + 1;
            int total = text.AsSpan().Count('\r') + 1;
            FooterLineText.Text = $"Ln {line:N0} / {total:N0}";
        }
        else
        {
            FooterLineText.Visibility = Visibility.Collapsed;
            return;
        }
        FooterLineText.Visibility = Visibility.Visible;
    }

    // ==================== GO TO LINE (CTRL+G) ====================

    /// <summary>Ctrl+G and a click on the footer line counter.</summary>
    public async void GoToLineFromShortcut()
    {
        var vm = ViewModel;
        if (vm is not { IsPreviewUnlocked: true, HasActiveFile: true } || XamlRoot == null) return;

        bool markdown = MarkdownScrollViewer.Visibility == Visibility.Visible;
        if (!markdown && EditorTextBox.Visibility != Visibility.Visible) return;

        int total = markdown
            ? MarkdownRichTextBlock.Blocks.Count
            : (EditorTextBox.Text ?? string.Empty).AsSpan().Count('\r') + 1;
        int current = markdown
            ? GetMarkdownLineAt(0.0) + 1
            : (EditorTextBox.Text ?? string.Empty).AsSpan(0, Math.Clamp(EditorTextBox.SelectionStart, 0, (EditorTextBox.Text ?? string.Empty).Length)).Count('\r') + 1;

        var box = new TextBox
        {
            Text = current.ToString(),
            MinWidth = 260,
            InputScope = new InputScope { Names = { new InputScopeName(InputScopeNameValue.Number) } },
            Header = $"Line number (1 - {total:N0})"
        };
        var errorText = new TextBlock
        {
            Foreground = (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"],
            FontSize = 12,
            Visibility = Visibility.Collapsed
        };
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(box);
        panel.Children.Add(errorText);
        box.Loaded += (_, _) =>
        {
            box.Focus(FocusState.Programmatic);
            box.SelectAll();
        };

        var dialog = new ContentDialog
        {
            Title = "Go to Line",
            Content = panel,
            PrimaryButtonText = "Go",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
            RequestedTheme = ThemeService.ActiveTheme
        };

        int target = -1;
        dialog.PrimaryButtonClick += (_, args) =>
        {
            var raw = box.Text.Trim().Replace(".", string.Empty).Replace(",", string.Empty);
            if (!int.TryParse(raw, out int n) || n < 1)
            {
                errorText.Text = "Type a line number.";
                errorText.Visibility = Visibility.Visible;
                args.Cancel = true;
                return;
            }
            target = Math.Min(n, total); // past the end: go to the last line
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary || target < 1 || !ReferenceEquals(vm, ViewModel))
            return;

        GoToLine(target - 1, string.Empty, topRatio: 1.0 / 3);
        if (EditorTextBox.Visibility == Visibility.Visible)
            EditorTextBox.Focus(FocusState.Programmatic); // the caret shows where the line is
    }

    /// <summary>
    /// The footer path sits in a horizontal panel (so the line counter follows it right away), where
    /// it would never be trimmed: its maximum width is the room left by the other footer parts.
    /// </summary>
    private void Footer_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (FooterLeftPanel.Parent is not FrameworkElement footer) return;
        double line = FooterLineText.Visibility == Visibility.Visible ? FooterLineText.ActualWidth + FooterLeftPanel.Spacing : 0;
        double max = footer.ActualWidth - FooterRightPanel.ActualWidth - line - 16;
        FooterPathText.MaxWidth = Math.Max(40, max);
    }

    private void FooterLineText_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e) => GoToLineFromShortcut();

    // ==================== OUTLINE FOLLOWS SCROLLING ====================

    /// <summary>Selects the outline entry of the function/heading shown in the view.</summary>
    private void UpdateOutlineFollow()
    {
        _outlineFollowTimer?.Stop();
        if (OutlinePanel.Visibility != Visibility.Visible || OutlineListView.ItemsSource is not List<OutlineItem> { Count: > 0 } items)
            return;

        int line;
        if (MarkdownScrollViewer.Visibility == Visibility.Visible)
            line = GetMarkdownLineAt(OutlineFollowRatio);
        else if (EditorTextBox.Visibility == Visibility.Visible)
            line = GetEditorLineAt(OutlineFollowRatio);
        else
            return;
        if (line < 0) return;

        // Last entry starting at/above that line (a nested method wins over its class)
        OutlineItem? current = null;
        foreach (var item in items)
        {
            if (item.Line <= line) current = item;
            else break;
        }

        if (current == null)
        {
            OutlineListView.SelectedItem = null;
            return;
        }
        if (!ReferenceEquals(OutlineListView.SelectedItem, current))
        {
            OutlineListView.SelectedItem = current;
            OutlineListView.ScrollIntoView(current);
        }
    }

    /// <summary>0-based Markdown line (paragraph) at <paramref name="ratio"/> of the view height.</summary>
    private int GetMarkdownLineAt(double ratio)
    {
        var blocks = MarkdownRichTextBlock.Blocks;
        if (blocks.Count == 0) return -1;

        try
        {
            if (_markdownBlockOffsets == null || _markdownBlockOffsets.Length != blocks.Count)
            {
                var offsets = new int[blocks.Count];
                for (int i = 0; i < blocks.Count; i++)
                    offsets[i] = blocks[i].ContentStart.Offset;
                _markdownBlockOffsets = offsets;
            }

            var y = Math.Max(1, MarkdownScrollViewer.ViewportHeight * ratio);
            var pt = MarkdownScrollViewer.TransformToVisual(MarkdownRichTextBlock).TransformPoint(new Point(4, y));
            var pointer = MarkdownRichTextBlock.GetPositionFromPoint(new Point(Math.Max(0, pt.X), Math.Max(0, pt.Y)));
            if (pointer == null) return -1;

            // Last paragraph starting at/before the pointer
            int idx = Array.BinarySearch(_markdownBlockOffsets, pointer.Offset);
            if (idx < 0) idx = ~idx - 1;
            return Math.Clamp(idx, 0, blocks.Count - 1);
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"[OutlineFollow] Markdown position: {ex.Message}");
            return -1;
        }
    }

    /// <summary>
    /// 0-based editor line at <paramref name="ratio"/> of the view height: estimated from the scroll
    /// position, then corrected with the character rects (reliable for text on screen).
    /// </summary>
    private int GetEditorLineAt(double ratio)
    {
        HookEditorScrollViewer();
        var sv = _editorScrollViewer;
        if (sv == null) return -1;

        if (_editorLineStarts == null)
        {
            var text = EditorTextBox.Text ?? string.Empty;
            _editorTextLength = text.Length;
            var starts = new List<int>(1024) { 0 };
            for (int i = text.IndexOf('\r'); i >= 0; i = text.IndexOf('\r', i + 1))
                starts.Add(i + 1);
            _editorLineStarts = starts.ToArray();
        }
        var lineStarts = _editorLineStarts;
        int total = lineStarts.Length;
        if (total == 1 || _editorTextLength == 0) return 0;

        double viewport = sv.ViewportHeight;
        double target = viewport * ratio;
        double padTop = EditorTextBox.Padding.Top;
        double contentHeight = Math.Max(1, sv.ExtentHeight - padTop - EditorTextBox.Padding.Bottom);
        int line = (int)Math.Clamp((sv.VerticalOffset + target - padTop) / contentHeight * total, 0, total - 1);

        int lastIndex = Math.Max(0, _editorTextLength - 1);
        double Top(int l) => EditorTextBox.GetRectFromCharacterIndex(Math.Min(lineStarts[l], lastIndex), false).Top;
        bool Plausible(double top) => top > -viewport * 4 && top < viewport * 5;

        // Exact without wrapping (uniform line height); with wrapping, walk to the right line
        for (int step = 0; step < 300; step++)
        {
            double top = Top(line);
            if (!Plausible(top)) break;
            if (top > target && line > 0)
            {
                line--;
                continue;
            }
            if (line + 1 < total)
            {
                double next = Top(line + 1);
                if (Plausible(next) && next <= target)
                {
                    line++;
                    continue;
                }
            }
            break;
        }
        return line;
    }
}
