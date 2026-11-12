using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using DirSnap2.Models;
using DirSnap2.Services;

namespace DirSnap2.Views;

/// <summary>
/// Folder Contents panel under the file list: shows the files and subfolders of the folder
/// selected in the list, without entering it. Height and collapsed state are saved in config.
/// </summary>
public sealed partial class WorkspaceTabView
{
    private const double MinPeekHeight = 60;
    private const double MinListHeight = 120;

    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _peekTimer;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _peekPollTimer;
    private int _peekVersion;
    private string? _peekPath;
    private int _peekSignature;
    private bool _peekPolling;

    private bool _isDraggingPeekSplitter;
    private double _peekSplitterStartY;
    private double _peekSplitterStartHeight;

    private void InitFolderPeek()
    {
        _peekTimer = DispatcherQueue.CreateTimer();
        _peekTimer.Interval = TimeSpan.FromMilliseconds(120); // arrow keys through the list stay smooth
        _peekTimer.IsRepeating = false;
        _peekTimer.Tick += (_, _) => UpdateFolderPeek();

        // Live update, like the main list: the shown folder is re-scanned every 2 seconds and the
        // panel is rebuilt only when something changed. Polling keeps no handle open on the folder.
        _peekPollTimer = DispatcherQueue.CreateTimer();
        _peekPollTimer.Interval = TimeSpan.FromSeconds(2);
        _peekPollTimer.IsRepeating = true;
        _peekPollTimer.Tick += (_, _) => PollFolderPeek();
        _peekPollTimer.Start();
        Unloaded += (_, _) => _peekPollTimer?.Stop();
        Loaded += (_, _) => _peekPollTimer?.Start();
    }

    private void PollFolderPeek()
    {
        if (_peekPolling || _peekPath == null || ViewModel is not { IsActiveTab: true }) return;
        if (FolderPeekBody.Visibility != Visibility.Visible) return; // collapsed: nothing to keep fresh
        UpdateFolderPeek(auto: true);
    }

    /// <summary>Changes when a name, date or size of the listed items changes.</summary>
    private static int GetPeekSignature(List<FSItem> items, string? error)
    {
        var hash = new HashCode();
        hash.Add(error);
        foreach (var i in items)
        {
            hash.Add(i.Name, StringComparer.OrdinalIgnoreCase);
            hash.Add(i.IsDirectory);
            hash.Add(i.ModifiedTime);
            hash.Add(i.Size);
        }
        return hash.ToHashCode();
    }

    private void ApplySavedFolderPeekLayout()
    {
        if (ViewModel == null) return;
        var cfg = ViewModel.ConfigService.Current;
        FolderPeekBody.Height = Math.Max(MinPeekHeight, cfg.FolderPeekHeight);
        SetFolderPeekBodyVisible(cfg.FolderPeekOpen);
    }

    private void SetFolderPeekBodyVisible(bool open)
    {
        FolderPeekBody.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        FolderPeekSplitter.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        FolderPeekCollapseIcon.Glyph = open ? "" : ""; // chevron down = open, up = collapsed
    }

    private void ScheduleFolderPeek()
    {
        if (_peekTimer == null) return;
        _peekTimer.Stop();
        _peekTimer.Start();
    }

    /// <param name="auto">Background poll: rebuilds the panel only when the folder changed, keeping selection and scroll.</param>
    private async void UpdateFolderPeek(bool auto = false)
    {
        if (!auto) _peekTimer?.Stop();
        var vm = ViewModel;
        int version = auto ? _peekVersion : ++_peekVersion;

        if (vm?.SelectedItem is not { IsDirectory: true } folder || folder.Name == "..")
        {
            if (auto) return;
            _peekPath = null;
            FolderPeekTitle.Text = "Folder contents";
            FolderPeekListView.ItemsSource = null;
            FolderPeekListView.Visibility = Visibility.Collapsed;
            FolderPeekHint.Text = "Select a folder in the list to see its files here.";
            FolderPeekHint.Visibility = Visibility.Visible;
            return;
        }

        var path = folder.Path;
        if (auto && !string.Equals(path, _peekPath, StringComparison.OrdinalIgnoreCase)) return;
        bool showAll = vm.ShowAll;
        var allowedExt = vm.ConfigService.Current.EditorExtensions.ToList();

        List<FSItem> items;
        string? error;
        if (auto) _peekPolling = true;
        try
        {
            // Same rules as the main list (Show all / allowed extensions), off the UI thread
            var result = await Task.Run(() => vm.FileSystemService.ScanDirectory(path, showAll, allowedExt));
            // Last modified first, folders and files mixed (like the main list sorted by date)
            items = vm.FileSystemService.SortItems(result.Directories.Concat(result.Files), "mtime", descending: true).ToList();
            error = result.Error;
        }
        catch (Exception ex)
        {
            AppLogger.Error($"[FolderPeek] Scan error for {path}", ex);
            items = new List<FSItem>();
            error = ex.Message;
        }
        finally
        {
            if (auto) _peekPolling = false;
        }

        if (version != _peekVersion) return; // selection changed meanwhile

        int signature = GetPeekSignature(items, error);
        if (auto && signature == _peekSignature) return; // nothing changed on disk
        _peekSignature = signature;

        // A background refresh keeps the selected row and the scroll position
        string? keepSelected = auto ? (FolderPeekListView.SelectedItem as FSItem)?.Path : null;
        var peekScroll = auto ? FindVisualChild<ScrollViewer>(FolderPeekListView) : null;
        double keepOffset = peekScroll?.VerticalOffset ?? 0;

        _peekPath = path;
        int dirs = items.Count(i => i.IsDirectory);
        int files = items.Count - dirs;
        FolderPeekTitle.Text = $"{folder.Name}   ·   {dirs} folders, {files} files";
        ToolTipService.SetToolTip(FolderPeekTitle, path);

        FolderPeekListView.ItemsSource = items;
        if (keepSelected != null)
            FolderPeekListView.SelectedItem = items.FirstOrDefault(i => string.Equals(i.Path, keepSelected, StringComparison.OrdinalIgnoreCase));
        if (peekScroll != null && keepOffset > 0)
        {
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
                () => peekScroll.ChangeView(null, keepOffset, null, true));
        }
        bool empty = items.Count == 0;
        FolderPeekListView.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        FolderPeekHint.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        FolderPeekHint.Text = error != null ? $"Cannot read the folder: {error}" : "Empty folder";
    }

    /// <summary>Double-click: a subfolder opens it, a file opens its folder and shows the file.</summary>
    private async void OpenPeekItem(FSItem item)
    {
        var vm = ViewModel;
        if (vm == null || _peekPath == null) return;

        if (item.IsDirectory)
        {
            vm.NavigateToInternal(item.Path, recordHistory: true);
            return;
        }

        if (vm.IsEditorDirty && !vm.IsCurrentFile(item.Path))
        {
            bool proceed = await vm.ConfirmDiscardUnsavedChangesAsync(XamlRoot);
            if (!proceed) return;
        }

        vm.NavigateToInternal(_peekPath, recordHistory: true);
        vm.SelectFile(item.Path);
    }

    private void FolderPeekListView_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (FolderPeekListView.SelectedItem is FSItem item)
            OpenPeekItem(item);
    }

    private void FolderPeekListView_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter && FolderPeekListView.SelectedItem is FSItem item)
        {
            OpenPeekItem(item);
            e.Handled = true;
        }
    }

    private void FolderPeekCollapse_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel == null) return;
        bool open = FolderPeekBody.Visibility != Visibility.Visible;
        SetFolderPeekBodyVisible(open);
        ViewModel.ConfigService.Current.FolderPeekOpen = open;
        ViewModel.ConfigService.Save();
    }

    // ==================== HEIGHT SPLITTER ====================

    private void FolderPeekSplitter_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeNorthSouth);
    }

    private void FolderPeekSplitter_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (!_isDraggingPeekSplitter)
            ProtectedCursor = null;
    }

    private void FolderPeekSplitter_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _isDraggingPeekSplitter = true;
        _peekSplitterStartY = e.GetCurrentPoint(this).Position.Y;
        _peekSplitterStartHeight = FolderPeekBody.ActualHeight;
        FolderPeekSplitter.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void FolderPeekSplitter_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_isDraggingPeekSplitter) return;

        // The panel is at the bottom: dragging up makes it taller
        double delta = e.GetCurrentPoint(this).Position.Y - _peekSplitterStartY;
        double max = Math.Max(MinPeekHeight, _peekSplitterStartHeight + FlatListView.ActualHeight - MinListHeight);
        FolderPeekBody.Height = Math.Clamp(_peekSplitterStartHeight - delta, MinPeekHeight, max);
    }

    private void FolderPeekSplitter_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_isDraggingPeekSplitter) return;
        FolderPeekSplitter.ReleasePointerCapture(e.Pointer);
        EndPeekSplitterDrag();
    }

    private void FolderPeekSplitter_PointerCaptureLost(object sender, PointerRoutedEventArgs e) => EndPeekSplitterDrag();

    private void EndPeekSplitterDrag()
    {
        if (!_isDraggingPeekSplitter) return;
        _isDraggingPeekSplitter = false;
        ProtectedCursor = null;

        if (ViewModel != null)
        {
            ViewModel.ConfigService.Current.FolderPeekHeight = Math.Round(FolderPeekBody.Height);
            ViewModel.ConfigService.Save();
        }
    }
}
