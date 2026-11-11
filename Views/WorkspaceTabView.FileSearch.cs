using System.Collections.ObjectModel;
using System.IO;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using DirSnap2.Models;
using DirSnap2.Services;

namespace DirSnap2.Views;

/// <summary>
/// "Search in files" panel (grep): finds a text in the files of the current folder and its
/// subfolders. Clicking a result opens the file with the find bar on that match.
/// </summary>
public sealed partial class WorkspaceTabView
{
    private readonly ObservableCollection<FileSearchHit> _fileSearchResults = new();
    private CancellationTokenSource? _fileSearchCts;
    private string _fileSearchQuery = string.Empty;
    private bool _fileSearchMatchCase;

    private bool IsFileSearchOpen => FileSearchPanel.Visibility == Visibility.Visible;
    private bool IsFileSearchRunning => _fileSearchCts != null;

    /// <summary>Ctrl+Shift+F and the toolbar button.</summary>
    public void OpenFileSearch()
    {
        if (ViewModel == null) return;
        var cfg = ViewModel.ConfigService.Current;

        if (!IsFileSearchOpen)
        {
            FileSearchListView.ItemsSource ??= _fileSearchResults;
            FileSearchSubfoldersCheck.IsChecked = cfg.FileSearchSubfolders;
            FileSearchMatchCaseToggle.IsChecked = cfg.FileSearchMatchCase;
            if (_fileSearchResults.Count == 0)
                FileSearchStatusText.Text = SearchedExtensionsText();
            FileSearchPanel.Visibility = Visibility.Visible;
        }
        FileSearchToggleButton.IsChecked = true;
        _filterBarOpen = true;
        UpdateFilterBarState();

        // Pre-fill with the text selected in the preview (single line)
        string selected = EditorTextBox.Visibility == Visibility.Visible
            ? EditorTextBox.SelectedText
            : MarkdownRichTextBlock.SelectedText;
        if (!string.IsNullOrWhiteSpace(selected) && selected.Length <= 200 && selected.IndexOfAny(new[] { '\r', '\n' }) < 0)
            FileSearchTextBox.Text = selected;

        FileSearchTextBox.Focus(FocusState.Keyboard);
        FileSearchTextBox.SelectAll();
    }

    private void CloseFileSearch()
    {
        CancelFileSearch();
        FileSearchPanel.Visibility = Visibility.Collapsed;
        FileSearchToggleButton.IsChecked = false;
        UpdateFilterBarState();
        FlatListView.Focus(FocusState.Programmatic);
    }

    private string SearchedExtensionsText()
    {
        var exts = ViewModel?.ConfigService.Current.EditorExtensions ?? new List<string>();
        return "Searches the files with these extensions: " + string.Join(" ", exts) + "  (Settings → editor extensions)";
    }

    private void CancelFileSearch()
    {
        _fileSearchCts?.Cancel();
    }

    private void SetFileSearchRunning(bool running)
    {
        FileSearchGoIcon.Glyph = running ? "" : ""; // stop / search
        ToolTipService.SetToolTip(FileSearchGoButton, running ? "Stop the search" : "Search (Enter)");
    }

    private async void StartFileSearch()
    {
        var vm = ViewModel;
        if (vm == null) return;

        CancelFileSearch();

        var query = FileSearchTextBox.Text;
        if (string.IsNullOrEmpty(query.Trim()))
        {
            FileSearchStatusText.Text = "Type the text to find. " + SearchedExtensionsText();
            return;
        }

        string root = vm.CurrentPath;
        if (!Directory.Exists(root))
        {
            FileSearchStatusText.Text = "The current folder does not exist.";
            return;
        }

        bool matchCase = FileSearchMatchCaseToggle.IsChecked == true;
        bool subfolders = FileSearchSubfoldersCheck.IsChecked == true;
        var extensions = vm.ConfigService.Current.EditorExtensions.ToList();

        var cts = new CancellationTokenSource();
        _fileSearchCts = cts;
        _fileSearchQuery = query;
        _fileSearchMatchCase = matchCase;
        _fileSearchResults.Clear();
        SetFileSearchRunning(true);

        string where = subfolders ? $"'{Path.GetFileName(root.TrimEnd('\\'))}' and its subfolders" : $"'{Path.GetFileName(root.TrimEnd('\\'))}'";
        if (string.IsNullOrEmpty(Path.GetFileName(root.TrimEnd('\\')))) where = subfolders ? $"{root} and its subfolders" : root;
        FileSearchStatusText.Text = $"Searching in {where}...";

        var dq = DispatcherQueue;
        FileSearchService.Summary summary;
        try
        {
            summary = await Task.Run(() => FileSearchService.Search(root, query, matchCase, subfolders, extensions,
                rows => dq.TryEnqueue(() =>
                {
                    if (cts.IsCancellationRequested) return;
                    foreach (var r in rows) _fileSearchResults.Add(r);
                }),
                files => dq.TryEnqueue(() =>
                {
                    if (!cts.IsCancellationRequested)
                        FileSearchStatusText.Text = $"Searching in {where}...   {files} files searched";
                }),
                cts.Token));
        }
        catch (Exception ex)
        {
            AppLogger.Error($"[FileSearch] Error searching {root}", ex);
            summary = new FileSearchService.Summary();
            if (ReferenceEquals(_fileSearchCts, cts))
                FileSearchStatusText.Text = $"Search failed: {ex.Message}";
        }

        // A newer search (or the panel closing) took over
        if (!ReferenceEquals(_fileSearchCts, cts)) return;
        _fileSearchCts = null;
        cts.Dispose();
        SetFileSearchRunning(false);

        // Let the last queued result batches land before the summary
        dq.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            if (IsFileSearchRunning) return;
            string text = summary.LineHits == 0
                ? $"Not found in {summary.FilesSearched} files ({where})."
                : $"{summary.LineHits} lines in {summary.FilesWithHits} files   ·   {summary.FilesSearched} files searched in {where}";
            if (summary.LimitReached) text += $"   ·   stopped at {FileSearchService.MaxLineHits} lines, refine the search";
            if (summary.Cancelled) text += "   ·   stopped";
            FileSearchStatusText.Text = text;
        });
    }

    /// <summary>Opens the file of a result and shows the match with the find bar.</summary>
    private async void OpenFileSearchHit(FileSearchHit hit)
    {
        var vm = ViewModel;
        if (vm == null) return;

        if (!File.Exists(hit.FilePath))
        {
            vm.StatusMessage = $"'{hit.FileName}' does not exist anymore";
            return;
        }

        if (vm.IsEditorDirty && !vm.IsCurrentFile(hit.FilePath))
        {
            bool proceed = await vm.ConfirmDiscardUnsavedChangesAsync(XamlRoot);
            if (!proceed || !ReferenceEquals(vm, ViewModel)) return;
        }

        // Show the file's folder in the list (under the search panel), then the file
        var folder = Path.GetDirectoryName(hit.FilePath);
        if (folder != null && !IsSameFolder(vm.CurrentPath, folder))
            vm.NavigateToInternal(folder, recordHistory: true);
        if (!vm.IsCurrentFile(hit.FilePath))
            vm.SelectFile(hit.FilePath);

        string query = _fileSearchQuery;
        bool matchCase = _fileSearchMatchCase;
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            if (ReferenceEquals(vm, ViewModel) && vm.IsCurrentFile(hit.FilePath))
                ShowFindAtLine(query, matchCase, hit.Line);
        });
    }

    // ==================== SEARCH BAR (quick filter + Show all + In files) ====================

    private bool _filterBarOpen;

    /// <summary>Opens/closes the bar. Closing it also removes the quick filter and closes Search in files.</summary>
    private void SetFilterBarOpen(bool open)
    {
        _filterBarOpen = open;
        if (!open)
        {
            if (IsFileSearchOpen) CloseFileSearch();
            if (ViewModel != null && !string.IsNullOrEmpty(ViewModel.FilterQuery))
                ViewModel.FilterQuery = string.Empty; // a hidden filter would hide files without telling
        }
        UpdateFilterBarState();
    }

    /// <summary>Shown when opened, while a filter is typed (e.g. restored with the tab) or Search in files is open.</summary>
    private void UpdateFilterBarState()
    {
        var vm = ViewModel;
        bool visible = _filterBarOpen || IsFileSearchOpen || !string.IsNullOrEmpty(vm?.FilterQuery);
        FilterBar.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        SearchBarDot.Visibility = !visible && vm?.ShowAll == true ? Visibility.Visible : Visibility.Collapsed;
        ToolTipService.SetToolTip(SearchBarButton, !visible && vm?.ShowAll == true
            ? "Show the quick filter and Search in files (Ctrl+F)  ·  Show all is on"
            : "Show / hide the quick filter, Show all and Search in files (Ctrl+F)");
    }

    private void SearchBarButton_Click(object sender, RoutedEventArgs e)
    {
        if (FilterBar.Visibility == Visibility.Visible)
        {
            SetFilterBarOpen(false);
            FlatListView.Focus(FocusState.Programmatic);
        }
        else
        {
            SetFilterBarOpen(true);
            FilterTextBox.Focus(FocusState.Keyboard);
        }
    }

    private void FileSearchToggle_Click(object sender, RoutedEventArgs e)
    {
        if (FileSearchToggleButton.IsChecked == true)
            OpenFileSearch();
        else
            CloseFileSearch();
    }

    private void FilterTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        // Esc: clear the filter and hide the bar (Search in files stays if open)
        if (e.Key == VirtualKey.Escape)
        {
            if (IsFileSearchOpen)
            {
                if (ViewModel != null) ViewModel.FilterQuery = string.Empty;
            }
            else
            {
                SetFilterBarOpen(false);
            }
            FlatListView.Focus(FocusState.Programmatic);
            e.Handled = true;
        }
    }

    private void FileSearchClose_Click(object sender, RoutedEventArgs e) => CloseFileSearch();

    private void FileSearchGo_Click(object sender, RoutedEventArgs e)
    {
        if (IsFileSearchRunning)
            CancelFileSearch();
        else
            StartFileSearch();
    }

    private void FileSearchTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            StartFileSearch();
            e.Handled = true;
        }
        else if (e.Key == VirtualKey.Escape)
        {
            if (IsFileSearchRunning) CancelFileSearch();
            else CloseFileSearch();
            e.Handled = true;
        }
        else if (e.Key == VirtualKey.Down && _fileSearchResults.Count > 0)
        {
            // Down arrow goes to the results
            FileSearchListView.SelectedIndex = Math.Max(0, FileSearchListView.SelectedIndex);
            FileSearchListView.Focus(FocusState.Keyboard);
            e.Handled = true;
        }
    }

    private void FileSearchOption_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel == null) return;
        var cfg = ViewModel.ConfigService.Current;
        cfg.FileSearchSubfolders = FileSearchSubfoldersCheck.IsChecked == true;
        cfg.FileSearchMatchCase = FileSearchMatchCaseToggle.IsChecked == true;
        ViewModel.ConfigService.Save();

        // Same query with the new options
        if (!string.IsNullOrEmpty(FileSearchTextBox.Text.Trim()))
            StartFileSearch();
    }

    private void FileSearchListView_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is FileSearchHit hit)
            OpenFileSearchHit(hit);
    }

    private void FileSearchListView_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter && FileSearchListView.SelectedItem is FileSearchHit hit)
        {
            OpenFileSearchHit(hit);
            e.Handled = true;
        }
        else if (e.Key == VirtualKey.Escape)
        {
            CloseFileSearch();
            e.Handled = true;
        }
    }
}
