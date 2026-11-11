using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DirSnap2.Models;
using DirSnap2.Services;

namespace DirSnap2.ViewModels;

public sealed partial class WorkspaceTabViewModel : ObservableObject, IDisposable
{
    private readonly IFileSystemService _fileSystemService;
    private readonly ITextFileService _textFileService;
    private readonly ISnapListService _snapListService;
    private readonly IConfigService _configService;

    public IFileSystemService FileSystemService => _fileSystemService;
    public IConfigService ConfigService => _configService;

    private readonly Stack<string> _backHistory = new();
    private readonly Stack<string> _forwardHistory = new();

    private List<FSItem> _rawDirectories = new();
    private List<FSItem> _rawFiles = new();

    public BulkObservableCollection<FSItem> FlatItems { get; } = new();

    [ObservableProperty]
    private string _currentPath = "C:\\";

    [ObservableProperty]
    private string _tabTitle = "C:\\";

    [ObservableProperty]
    private bool _isActiveTab;

    private static readonly SolidColorBrush TransparentTabBrush = new(Microsoft.UI.Colors.Transparent);

    public SolidColorBrush TabHeaderBackground =>
        IsActiveTab && Application.Current.Resources.TryGetValue("AppAccentSubtleBrush", out var b1) && b1 is SolidColorBrush sb
            ? sb
            : TransparentTabBrush;

    public SolidColorBrush TabHeaderBorderBrush =>
        IsActiveTab && Application.Current.Resources.TryGetValue("AppAccentBrush", out var b2) && b2 is SolidColorBrush ab
            ? ab
            : TransparentTabBrush;

    public Windows.UI.Text.FontWeight TabHeaderFontWeight =>
        IsActiveTab ? Microsoft.UI.Text.FontWeights.Bold : Microsoft.UI.Text.FontWeights.Normal;

    partial void OnIsActiveTabChanged(bool value)
    {
        UpdateAutoRefreshTimer();
        OnPropertyChanged(nameof(TabHeaderBackground));
        OnPropertyChanged(nameof(TabHeaderBorderBrush));
        OnPropertyChanged(nameof(TabHeaderFontWeight));
    }

    [ObservableProperty]
    private string _mode = "flat"; // "flat" or "tree"

    [ObservableProperty]
    private string _filterQuery = string.Empty;

    [ObservableProperty]
    private bool _showAll;

    [ObservableProperty]
    private string _sortColumn = "name";

    [ObservableProperty]
    private bool _sortDescending;

    [ObservableProperty]
    private int _foldersCount;

    [ObservableProperty]
    private int _filesCount;

    public string FoldersCountText => $"+ {FoldersCount} folders";
    public string FilesCountText => $"+ {FilesCount} files";

    public TextWrapping EditorTextWrapping => EditorWrap ? TextWrapping.Wrap : TextWrapping.NoWrap;

    [ObservableProperty]
    private bool _canGoBack;

    [ObservableProperty]
    private bool _canGoForward;

    [ObservableProperty]
    private bool _canGoUp;

    [ObservableProperty]
    private FSItem? _selectedItem;

    // Text Editor & Preview properties
    [ObservableProperty]
    private string? _currentEditingFilePath;

    [ObservableProperty]
    private string _currentEditingFileName = "No file selected";

    [ObservableProperty]
    private string _currentEditingFileSizeText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsReadOnlyBadgeVisible))]
    [NotifyPropertyChangedFor(nameof(IsEditAllowed))]
    private bool _hasActiveFile;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsReadOnlyBadgeVisible))]
    [NotifyPropertyChangedFor(nameof(CanFormatJson))]
    [NotifyPropertyChangedFor(nameof(IsEditAllowed))]
    private bool _isPreviewUnlocked;

    [ObservableProperty]
    private bool _requiresPreviewUnlock;

    [ObservableProperty]
    private bool _canUnlockPreview;

    [ObservableProperty]
    private string _previewNoticeText = string.Empty;

    [ObservableProperty]
    private string _previewNoticeGlyph = "\uE890";

    [ObservableProperty]
    private string _editorText = string.Empty;

    [ObservableProperty]
    private string _originalText = string.Empty;

    public bool IsLoadingFile { get; private set; }

    [ObservableProperty]
    private bool _isEditorDirty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsReadOnlyBadgeVisible))]
    [NotifyPropertyChangedFor(nameof(IsEditingEnabled))]
    [NotifyPropertyChangedFor(nameof(EditModeToolTip))]
    private bool _isEditorLocked = true;

    public bool IsEditingEnabled
    {
        get => !IsEditorLocked;
        set
        {
            if (value != !IsEditorLocked)
            {
                if (value)
                    UnlockEditor();
                else
                    LockEditor();
            }
        }
    }

    public string EditModeToolTip => IsEditorLocked
        ? "Unlock file to enable editing"
        : "In modification (Click to lock read-only)";

    public string WordWrapToolTip => EditorWrap
        ? "Word Wrap: On (Click to turn off)"
        : "Word Wrap: Off (Click to turn on)";

    [ObservableProperty]
    private bool _canUnlockToEdit;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanFormatJson))]
    private bool _isJsonFile;

    [ObservableProperty]
    private bool _isMarkdownFile;

    /// <summary>
    /// Editing is offered only for the extensions enabled in Settings. Any other text file
    /// can be previewed (after "Unlock Preview") but never modified.
    /// </summary>
    public bool IsEditAllowed =>
        HasActiveFile && IsPreviewUnlocked && !string.IsNullOrEmpty(CurrentEditingFilePath) &&
        _configService.Current.EditorExtensions.Contains(Path.GetExtension(CurrentEditingFilePath).ToLowerInvariant());

    partial void OnCurrentEditingFilePathChanged(string? value) => OnPropertyChanged(nameof(IsEditAllowed));

    public bool IsReadOnlyBadgeVisible => HasActiveFile && IsPreviewUnlocked && IsEditorLocked;
    public bool CanFormatJson => IsJsonFile && IsPreviewUnlocked;

    [ObservableProperty]
    private string _editorEncodingName = "UTF-8";

    [ObservableProperty]
    private string _editorNewlineName = "CRLF";

    [ObservableProperty]
    private bool _editorHasBom;

    [ObservableProperty]
    private bool _editorWrap = true;

    [ObservableProperty]
    private double _editorFontSize = 13;

    public void NotifyEditorFontSizeChanged()
    {
        EditorFontSize = _configService.Current.EditorFontSize > 0 ? _configService.Current.EditorFontSize : 13;
    }

    [ObservableProperty]
    private string _folderSummaryText = "0 folders, 0 files";

    [ObservableProperty]
    private string _statusMessage = "Ready";

    private Encoding _loadedEncoding = Encoding.UTF8;
    private string _loadedNewline = "\r\n";

    public WorkspaceTabViewModel(
        IFileSystemService fileSystemService,
        ITextFileService textFileService,
        ISnapListService snapListService,
        IConfigService configService,
        WorkspaceTabState? initialTabState = null)
    {
        _fileSystemService = fileSystemService;
        _textFileService = textFileService;
        _snapListService = snapListService;
        _configService = configService;
        _configService.ConfigChanged += OnConfigChanged;
        _dispatcher = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        // FSItem owns static brushes: make sure they are created on the UI thread,
        // because folder scans build FSItem instances on a worker thread.
        System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(typeof(FSItem).TypeHandle);
        _editorFontSize = _configService.Current.EditorFontSize > 0 ? _configService.Current.EditorFontSize : 13;

        if (initialTabState != null)
        {
            _currentPath = Directory.Exists(initialTabState.Path) ? initialTabState.Path : "C:\\";
            _mode = initialTabState.Mode ?? "flat";
            _filterQuery = initialTabState.Filter ?? string.Empty;
            _showAll = initialTabState.ShowAll;
            _sortColumn = initialTabState.SortCol ?? "name";
            _sortDescending = initialTabState.SortDesc;
            _editorWrap = initialTabState.EditorWrap;
            if (!string.IsNullOrEmpty(initialTabState.SelectedFile) && File.Exists(initialTabState.SelectedFile))
            {
                _currentEditingFilePath = initialTabState.SelectedFile;
            }
        }

        UpdateTabTitle();
        NavigateToInternal(_currentPath, recordHistory: false);

        if (!string.IsNullOrEmpty(_currentEditingFilePath) && File.Exists(_currentEditingFilePath))
        {
            SelectFile(_currentEditingFilePath);
        }
    }

    public WorkspaceTabState ToState()
    {
        return new WorkspaceTabState
        {
            Path = CurrentPath,
            Mode = Mode,
            Filter = FilterQuery,
            ShowAll = ShowAll,
            SortCol = SortColumn,
            SortDesc = SortDescending,
            SelectedFile = CurrentEditingFilePath,
            EditorWrap = EditorWrap
        };
    }

    public string CurrentFolderName
    {
        get
        {
            if (string.IsNullOrWhiteSpace(CurrentPath)) return string.Empty;
            var dirName = Path.GetFileName(CurrentPath.TrimEnd('\\', '/'));
            return !string.IsNullOrEmpty(dirName) ? dirName : CurrentPath;
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _scanVersion++;
        _autoRefreshTimer?.Stop();
        _configService.ConfigChanged -= OnConfigChanged;
    }

    private void UpdateTabTitle()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(CurrentPath))
            {
                TabTitle = "New Tab";
                OnPropertyChanged(nameof(CurrentFolderName));
                return;
            }

            var dirName = Path.GetFileName(CurrentPath.TrimEnd('\\', '/'));
            TabTitle = !string.IsNullOrEmpty(dirName) ? dirName : CurrentPath;
        }
        catch
        {
            TabTitle = CurrentPath;
        }
        OnPropertyChanged(nameof(CurrentFolderName));
    }

    public event Action<string>? TreeSyncRequested;

    private void OnConfigChanged()
    {
        EditorFontSize = _configService.Current.EditorFontSize > 0 ? _configService.Current.EditorFontSize : 13;
        Refresh();
        OnPropertyChanged(nameof(IsEditAllowed));
        // Re-evaluates the open file (its extension may have been added/removed in Settings),
        // unless it is being edited
        SyncSelectedFile(force: true);
    }

    private readonly SemaphoreSlim _unsavedDialogLock = new(1, 1);

    /// <summary>
    /// Checks if there are unsaved changes. If so, prompts the user via a native ContentDialog:
    /// - "Save": saves the file and returns true (or false if saving fails).
    /// - "Don't Save": restores the last loaded/saved text and returns true.
    /// - "Cancel": leaves everything as-is and returns false.
    /// </summary>
    public async Task<bool> ConfirmDiscardUnsavedChangesAsync(XamlRoot? xamlRoot = null)
    {
        if (!IsEditorDirty || string.IsNullOrEmpty(CurrentEditingFilePath))
            return true;

        var root = xamlRoot ?? App.MainAppWindow?.Content?.XamlRoot;
        if (root == null)
            return true;

        if (!await _unsavedDialogLock.WaitAsync(0))
        {
            // Dialog already in progress
            return false;
        }

        try
        {
            var dialog = new ContentDialog
            {
                Title = "Unsaved Changes",
                Content = $"Do you want to save the changes you made to '{CurrentEditingFileName}' before proceeding?",
                PrimaryButtonText = "Save",
                SecondaryButtonText = "Don't Save",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = root,
                RequestedTheme = ThemeService.ActiveTheme
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                return await SaveEditorFileAsync(root);
            }
            else if (result == ContentDialogResult.Secondary)
            {
                DiscardEditorChanges();
                return true;
            }
            else
            {
                // Cancel
                return false;
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error("[ConfirmDiscardUnsavedChangesAsync] Error showing dialog", ex);
            return false;
        }
        finally
        {
            _unsavedDialogLock.Release();
        }
    }

    partial void OnSelectedItemChanged(FSItem? oldValue, FSItem? newValue)
    {
        if (oldValue != null)
        {
            oldValue.IsSelected = false;
        }

        if (newValue != null)
        {
            newValue.IsSelected = true;
        }

        // Selection only drives the row highlight here. Loading a file is always an explicit
        // SelectFile() call made by the view AFTER the unsaved-changes check: loading from this
        // setter (fired by the TwoWay ListView binding and by every list rebuild) silently
        // threw away pending edits.
    }

    public bool IsCurrentFile(string? path) =>
        HasActiveFile && !string.IsNullOrEmpty(path) &&
        string.Equals(path, CurrentEditingFilePath, StringComparison.OrdinalIgnoreCase);

    /// <summary>Puts the list highlight back on the file shown in the editor (no reload).</summary>
    public void ReselectCurrentFileInList()
    {
        if (string.IsNullOrEmpty(CurrentEditingFilePath))
            return;

        var matching = FlatItems.FirstOrDefault(x => string.Equals(x.Path, CurrentEditingFilePath, StringComparison.OrdinalIgnoreCase));
        if (matching != null)
        {
            if (SelectedItem != matching)
                SelectedItem = matching;
            matching.IsSelected = true;
        }
    }

    /// <summary>Drops unsaved edits and shows the last loaded/saved text again.</summary>
    public void DiscardEditorChanges()
    {
        IsLoadingFile = true;
        try
        {
            EditorText = OriginalText;
            IsEditorDirty = false;
        }
        finally
        {
            IsLoadingFile = false;
        }
    }

    partial void OnFoldersCountChanged(int value) => OnPropertyChanged(nameof(FoldersCountText));
    partial void OnFilesCountChanged(int value) => OnPropertyChanged(nameof(FilesCountText));
    partial void OnEditorWrapChanged(bool value)
    {
        OnPropertyChanged(nameof(EditorTextWrapping));
        OnPropertyChanged(nameof(WordWrapToolTip));
        if (ConfigService?.Current != null)
        {
            ConfigService.Current.EditorWrap = value;
            ConfigService.Save();
        }
    }

    partial void OnFilterQueryChanged(string value)
    {
        ApplyFilterAndSort();
    }

    partial void OnShowAllChanged(bool value)
    {
        Refresh();
    }

    partial void OnModeChanged(string value)
    {
        if (value == "tree")
        {
            TreeSyncRequested?.Invoke(CurrentPath);
        }
    }

    partial void OnEditorTextChanged(string value)
    {
        if (IsLoadingFile || string.IsNullOrEmpty(CurrentEditingFilePath))
        {
            IsEditorDirty = false;
            return;
        }

        // While locked the text can only change through the TextBox echoing it back with its
        // own line endings, or a view-only "Format JSON": neither may set or clear the flag
        // (a file locked with pending edits must stay dirty).
        if (IsEditorLocked)
            return;

        IsEditorDirty = !TextFileService.EqualsIgnoringNewlineStyle(value, OriginalText);
    }

    // Navigating never closes the file shown in the editor (pending edits stay there),
    // so none of the navigation commands asks about unsaved changes.
    [RelayCommand]
    public void NavigateTo(string path)
    {
        NavigateToInternal(path, recordHistory: true);
    }

    public void NavigateToInternal(string path, bool recordHistory)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            return;

        var normalized = Path.GetFullPath(path);

        if (recordHistory && !string.Equals(CurrentPath, normalized, StringComparison.OrdinalIgnoreCase))
        {
            _backHistory.Push(CurrentPath);
            _forwardHistory.Clear();
            CanGoBack = _backHistory.Count > 0;
            CanGoForward = false;
        }

        CurrentPath = normalized;
        UpdateTabTitle();
        CanGoUp = Path.GetDirectoryName(CurrentPath) != null;

        _configService.AddRecent(CurrentPath);

        Refresh();
    }

    [RelayCommand]
    public void GoBack()
    {
        if (_backHistory.Count == 0) return;

        NavigateHistory(_backHistory, _forwardHistory);
    }

    [RelayCommand]
    public void GoForward()
    {
        if (_forwardHistory.Count == 0) return;

        NavigateHistory(_forwardHistory, _backHistory);
    }

    private void NavigateHistory(Stack<string> from, Stack<string> to)
    {
        // Skip history entries whose folder has been deleted/renamed/unplugged meanwhile
        string? target = null;
        int skipped = 0;
        while (from.Count > 0)
        {
            var candidate = from.Pop();
            if (Directory.Exists(candidate))
            {
                target = candidate;
                break;
            }
            skipped++;
        }

        if (target != null)
        {
            to.Push(CurrentPath);
            CurrentPath = target;
            UpdateTabTitle();
            CanGoUp = Path.GetDirectoryName(CurrentPath) != null;
            Refresh();
        }

        CanGoBack = _backHistory.Count > 0;
        CanGoForward = _forwardHistory.Count > 0;

        if (target == null)
            StatusMessage = "That folder no longer exists";
        else if (skipped > 0)
            _pendingStatusNote = $"skipped {skipped} folder(s) that no longer exist";
    }

    [RelayCommand]
    public void GoUp()
    {
        var parent = Path.GetDirectoryName(CurrentPath);
        if (!string.IsNullOrEmpty(parent) && Directory.Exists(parent))
        {
            NavigateToInternal(parent, recordHistory: true);
        }
    }

    private readonly Microsoft.UI.Dispatching.DispatcherQueue? _dispatcher;
    private int _scanVersion;
    private bool _disposed;
    private string? _pendingStatusNote;

    private int _scansInFlight;
    private string? _lastScanError;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _autoRefreshTimer;
    private static readonly TimeSpan AutoRefreshMinInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan AutoRefreshMaxInterval = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Rescans the current folder. The scan runs on a worker thread (slow/network drives and
    /// huge folders no longer freeze the window); only the latest request is applied.
    /// </summary>
    [RelayCommand]
    public void Refresh()
    {
        _ = RefreshAsync(auto: false);
    }

    /// <summary>
    /// Live update: while this tab is the visible one its folder is re-scanned every couple of
    /// seconds, and the list is rebuilt only when something actually changed on disk.
    /// Polling (instead of a FileSystemWatcher) keeps no handle open on the folder, so it never
    /// blocks renaming/deleting it or its parents from other programs, and it also works on
    /// network shares.
    /// </summary>
    private void UpdateAutoRefreshTimer()
    {
        if (_dispatcher == null)
            return;

        if (IsActiveTab && !_disposed)
        {
            if (_autoRefreshTimer == null)
            {
                _autoRefreshTimer = _dispatcher.CreateTimer();
                _autoRefreshTimer.Interval = AutoRefreshMinInterval;
                _autoRefreshTimer.Tick += (_, _) => OnAutoRefreshTick();
            }
            _autoRefreshTimer.Start();

            // Catch up at once with whatever changed while the tab was in the background
            OnAutoRefreshTick();
        }
        else
        {
            _autoRefreshTimer?.Stop();
        }
    }

    private void OnAutoRefreshTick()
    {
        if (_disposed || !IsActiveTab)
            return;

        try
        {
            ReloadOpenFileIfChangedOnDisk();
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"[AutoRefresh] Open file check failed: {ex.Message}");
        }

        if (_scansInFlight == 0)
        {
            _ = RefreshAsync(auto: true);
        }
    }

    private async Task RefreshAsync(bool auto)
    {
        // An automatic scan never supersedes a manual one; a manual one supersedes everything
        int version = auto ? _scanVersion : ++_scanVersion;
        string path = CurrentPath;
        bool showAll = ShowAll;
        var allowedExt = _configService.Current.EditorExtensions.ToList();

        List<FSItem> dirs = new();
        List<FSItem> files = new();
        string? error;

        _scansInFlight++;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            (dirs, files, error) = await Task.Run(() => _fileSystemService.ScanDirectory(path, showAll, allowedExt));
        }
        catch (Exception ex)
        {
            error = ex.Message;
        }
        stopwatch.Stop();

        void Finish()
        {
            _scansInFlight--;

            if (auto && _autoRefreshTimer != null)
            {
                // Slow folders (network, huge) are polled less often: ~10x the scan time
                var wanted = TimeSpan.FromMilliseconds(stopwatch.ElapsedMilliseconds * 10);
                if (wanted < AutoRefreshMinInterval) wanted = AutoRefreshMinInterval;
                if (wanted > AutoRefreshMaxInterval) wanted = AutoRefreshMaxInterval;
                _autoRefreshTimer.Interval = wanted;
            }

            ApplyScanResult(version, dirs, files, error, auto);
        }

        if (_dispatcher != null && !_dispatcher.HasThreadAccess)
        {
            _dispatcher.TryEnqueue(Finish);
        }
        else
        {
            Finish();
        }
    }

    private static bool SameEntries(List<FSItem> a, List<FSItem> b)
    {
        if (a.Count != b.Count)
            return false;

        for (int i = 0; i < a.Count; i++)
        {
            var x = a[i];
            var y = b[i];
            if (x.Size != y.Size ||
                x.ModifiedTime != y.ModifiedTime ||
                x.IsShowAllOnly != y.IsShowAllOnly ||
                !string.Equals(x.Path, y.Path, StringComparison.Ordinal))
            {
                return false;
            }
        }
        return true;
    }

    private void ApplyScanResult(int version, List<FSItem> dirs, List<FSItem> files, string? error, bool auto)
    {
        // A newer scan was requested meanwhile (or the tab was closed): drop this one
        if (_disposed || version != _scanVersion)
            return;

        // Automatic scan with nothing new on disk: leave list, selection and scroll alone
        if (auto && error == _lastScanError && SameEntries(_rawDirectories, dirs) && SameEntries(_rawFiles, files))
            return;

        try
        {
            // On failure the lists are empty (or partial): never keep showing the previous
            // folder's content under the new path.
            _lastScanError = error;
            _rawDirectories = dirs;
            _rawFiles = files;
            FoldersCount = dirs.Count;
            FilesCount = files.Count;

            ApplyFilterAndSort();

            if (Mode == "tree")
            {
                TreeSyncRequested?.Invoke(CurrentPath);
            }

            long totalBytes = 0;
            foreach (var f in files) totalBytes += f.Size;
            string sizeStr = totalBytes > 0 ? $", {FSItem.FormatSize(totalBytes)}" : string.Empty;
            FolderSummaryText = $"{dirs.Count} folders, {files.Count} files{sizeStr}";

            // A background update must not wipe a message the user is reading ("Saved ...")
            if (!auto || error != null)
            {
                StatusMessage = error != null ? $"Error: {error}" : FolderSummaryText;
            }

            if (_pendingStatusNote != null)
            {
                // Shown here because the scan result would otherwise overwrite it at once
                StatusMessage += $" ({_pendingStatusNote})";
                _pendingStatusNote = null;
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Scan error: {ex.Message}";
        }
    }

    private void ApplyFilterAndSort()
    {
        var filteredDirs = _fileSystemService.FilterItems(_rawDirectories, FilterQuery);
        var filteredFiles = _fileSystemService.FilterItems(_rawFiles, FilterQuery);

        var all = filteredDirs.Concat(filteredFiles);
        var sorted = _fileSystemService.SortItems(all, SortColumn, SortDescending);

        var rows = new List<FSItem>();
        int rowIndex = 0;

        // Add ".." entry at the top if we can navigate up
        var parent = Path.GetDirectoryName(CurrentPath);
        if (!string.IsNullOrEmpty(parent))
        {
            rows.Add(new FSItem
            {
                Name = "..",
                Path = parent,
                IsDirectory = true,
                Size = 0,
                ModifiedTime = DateTime.MinValue,
                IsEvenRow = (rowIndex++ % 2 == 0)
            });
        }

        foreach (var item in sorted)
        {
            item.IsEvenRow = (rowIndex++ % 2 == 0);
            rows.Add(item);
        }

        // The rebuild clears the ListView selection: remember a selected folder to restore it
        var selectedFolderPath = SelectedItem is { IsDirectory: true } sel ? sel.Path : null;
        var selectedWasParentRow = SelectedItem?.Name == "..";

        // One Reset notification instead of one per row
        FlatItems.ReplaceAll(rows);

        var folderRow = selectedFolderPath == null
            ? null
            : rows.FirstOrDefault(x => x.IsDirectory && (x.Name == "..") == selectedWasParentRow &&
                                       string.Equals(x.Path, selectedFolderPath, StringComparison.OrdinalIgnoreCase));
        if (folderRow != null)
        {
            SelectedItem = folderRow;
        }
        else
        {
            ReselectCurrentFileInList();
        }
    }

    [RelayCommand]
    public void ChangeSort(string column)
    {
        if (string.Equals(SortColumn, column, StringComparison.OrdinalIgnoreCase))
        {
            SortDescending = !SortDescending;
        }
        else
        {
            SortColumn = column;
            SortDescending = false;
        }

        // Per-tab sort is persisted with the workspace (MainViewModel.SaveWorkspace)
        ApplyFilterAndSort();
    }

    // ==================== SNAP LIST ACTIONS ====================

    [RelayCommand]
    public void AddItemToSnapList(FSItem item)
    {
        if (item == null || item.Name == "..") return;

        if (_snapListService.Add(item))
        {
            StatusMessage = $"Added '{item.Name}' to Snap List";
        }
        else
        {
            StatusMessage = $"'{item.Name}' is already in Snap List";
        }
    }

    [RelayCommand]
    public void AddAllFoldersToSnapList()
    {
        int added = _snapListService.AddRange(_rawDirectories);
        StatusMessage = $"Added {added} folders to Snap List";
    }

    [RelayCommand]
    public void AddAllFilesToSnapList()
    {
        int added = _snapListService.AddRange(_rawFiles);
        StatusMessage = $"Added {added} files to Snap List";
    }

    // ==================== EDITOR & PREVIEW ACTIONS ====================

    private static readonly HashSet<string> BinaryExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        // Audio
        ".wav", ".mp3", ".flac", ".ogg", ".aac", ".m4a", ".wma", ".aiff", ".aif", ".alac", ".opus", 
        ".mid", ".midi", ".ape", ".ac3", ".dts", ".mka",
        // Video
        ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".webm", ".m4v", ".mpg", ".mpeg", ".3gp", 
        ".ts", ".vob", ".ogv",
        // Images
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".ico", ".tiff", ".tif", ".psd", ".raw", 
        ".cr2", ".nef", ".heic", ".heif", ".avif",
        // Executables, Libraries, Binaries
        ".exe", ".dll", ".sys", ".bin", ".iso", ".img", ".so", ".dylib", ".msi", ".obj", ".o", 
        ".pdb", ".lib", ".com", ".drv", ".ocx", ".class",
        // Archives & Compressed
        ".zip", ".rar", ".7z", ".tar", ".gz", ".bz2", ".xz", ".cab", ".zst", ".tgz", ".7-zip",
        // Documents & Databases
        ".pdf", ".docx", ".xlsx", ".pptx", ".doc", ".xls", ".ppt", ".db", ".sqlite", ".sqlite3", 
        ".parquet", ".mdb", ".accdb", ".dat",
        // Fonts
        ".ttf", ".otf", ".woff", ".woff2", ".eot"
    };

    private static string GetBinaryGlyph(string ext)
    {
        return ext switch
        {
            ".wav" or ".mp3" or ".flac" or ".ogg" or ".aac" or ".m4a" or ".wma" or ".aiff" or ".aif" or ".alac" or ".opus" or ".mid" or ".midi" or ".ape" => "\uEC4F", // Audio glyph
            ".mp4" or ".mkv" or ".avi" or ".mov" or ".wmv" or ".flv" or ".webm" or ".m4v" => "\uE714", // Video glyph
            ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".webp" or ".ico" or ".tiff" or ".tif" => "\uEB9F", // Image glyph
            ".zip" or ".rar" or ".7z" or ".tar" or ".gz" or ".bz2" or ".xz" or ".cab" => "\uF012", // Archive glyph
            ".pdf" => "\uEA90", // PDF glyph
            _ => "\uEB9F" // Generic binary file glyph
        };
    }

    private static bool IsBinaryFileFast(string filePath)
    {
        try
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            byte[] buffer = new byte[1024];
            int read = fs.Read(buffer, 0, buffer.Length);
            for (int i = 0; i < read; i++)
            {
                if (buffer[i] == 0) return true;
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    public void SelectFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            ClearFileSelection();
            return;
        }

        CurrentEditingFilePath = filePath;
        CurrentEditingFileName = Path.GetFileName(filePath);
        HasActiveFile = true;
        IsEditorDirty = false;

        var matching = FlatItems.FirstOrDefault(x => string.Equals(x.Path, filePath, StringComparison.OrdinalIgnoreCase));
        if (matching != null && SelectedItem != matching)
        {
            SelectedItem = matching;
        }

        long fileLength = 0;
        try
        {
            var fi = new FileInfo(filePath);
            fileLength = fi.Length;
            CurrentEditingFileSizeText = FSItem.FormatSize(fileLength);
        }
        catch
        {
            CurrentEditingFileSizeText = string.Empty;
        }

        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        IsJsonFile = ext == ".json";
        IsMarkdownFile = ext is ".md" or ".markdown";

        var configuredExts = _configService.Current.EditorExtensions;
        bool isConfigured = configuredExts.Contains(ext);

        // Check if file is a known binary format or exceeds max preview size (5MB)
        bool isKnownBinary = BinaryExtensions.Contains(ext);
        bool isOversized = fileLength > TextFileService.MaxEditableSize;
        bool isBinaryContent = isKnownBinary || (!isConfigured && fileLength > 0 && IsBinaryFileFast(filePath));

        if (isBinaryContent || isOversized)
        {
            // Binary or oversized file: never offer text preview or unlock
            IsLoadingFile = true;
            try
            {
                OriginalText = string.Empty;
                EditorText = string.Empty;
                IsEditorDirty = false;
            }
            finally
            {
                IsLoadingFile = false;
            }
            IsPreviewUnlocked = false;
            RequiresPreviewUnlock = true;
            CanUnlockPreview = false;
            IsEditorLocked = true;
            CanUnlockToEdit = false;
            EditorEncodingName = isBinaryContent ? "Binary" : "Oversized";
            EditorNewlineName = "-";

            if (isOversized)
            {
                PreviewNoticeGlyph = "\uE890";
                PreviewNoticeText = $"File size ({FSItem.FormatSize(fileLength)}) exceeds the 5 MB preview limit.";
            }
            else
            {
                PreviewNoticeGlyph = GetBinaryGlyph(ext);
                PreviewNoticeText = $"Binary file format ({ext}). Text preview is not available.";
            }
            return;
        }

        if (isConfigured)
        {
            // Configured extensions: preview immediately in read-only mode
            try
            {
                var result = _textFileService.ReadText(filePath);
                IsLoadingFile = true;
                try
                {
                    OriginalText = result.Content;
                    EditorText = result.Content;
                    IsEditorDirty = false;
                }
                finally
                {
                    IsLoadingFile = false;
                }
                _loadedEncoding = result.Encoding;
                _loadedNewline = result.Newline;
                _loadedStamp = ReadStamp(CurrentEditingFilePath);
                _pendingStamp = null;
                EditorHasBom = result.HasBom;

                EditorEncodingName = result.HasBom ? "UTF-8 BOM" : result.Encoding.EncodingName;
                EditorNewlineName = TextFileService.GetNewlineDisplayName(result.Newline);

                IsPreviewUnlocked = true;
                RequiresPreviewUnlock = false;
                CanUnlockPreview = false;
                IsEditorLocked = true; // Read-only view
                CanUnlockToEdit = true;
            }
            catch (Exception ex)
            {
                IsLoadingFile = true;
                try
                {
                    OriginalText = string.Empty;
                    EditorText = string.Empty;
                    IsEditorDirty = false;
                }
                finally
                {
                    IsLoadingFile = false;
                }
                IsPreviewUnlocked = false;
                RequiresPreviewUnlock = true;
                CanUnlockPreview = false;
                IsEditorLocked = true;
                CanUnlockToEdit = false;
                PreviewNoticeGlyph = "\uE783";
                PreviewNoticeText = $"Cannot preview: {ex.Message}";
                StatusMessage = $"Cannot preview: {ex.Message}";
            }
        }
        else
        {
            // Non-configured text extension: show unlock prompt
            IsLoadingFile = true;
            try
            {
                OriginalText = string.Empty;
                EditorText = string.Empty;
                IsEditorDirty = false;
            }
            finally
            {
                IsLoadingFile = false;
            }
            IsPreviewUnlocked = false;
            RequiresPreviewUnlock = true;
            CanUnlockPreview = true;
            IsEditorLocked = true;
            CanUnlockToEdit = false;
            EditorEncodingName = "Unknown";
            EditorNewlineName = "-";
            PreviewNoticeGlyph = "\uE890";
            PreviewNoticeText = "This file type is not in your configured fast-preview list. Unlock preview to inspect text content (supported up to 5MB).";
        }
    }

    public void ClearFileSelection()
    {
        // The row highlight (accent text) follows SelectedItem and is handled in
        // OnSelectedItemChanged: do not reset it here, or a selected folder loses it.
        IsLoadingFile = true;
        try
        {
            CurrentEditingFilePath = null;
            CurrentEditingFileName = "No file selected";
            CurrentEditingFileSizeText = string.Empty;
            HasActiveFile = false;
            IsPreviewUnlocked = false;
            RequiresPreviewUnlock = false;
            CanUnlockPreview = false;
            PreviewNoticeText = string.Empty;
            PreviewNoticeGlyph = "\uE890";
            IsEditorLocked = true;
            CanUnlockToEdit = false;
            IsJsonFile = false;
            IsMarkdownFile = false;
            OriginalText = string.Empty;
            EditorText = string.Empty;
            IsEditorDirty = false;
        }
        finally
        {
            IsLoadingFile = false;
        }
    }

    /// <summary>Last write time + size of the file as it was when loaded/saved by this tab.</summary>
    private readonly record struct FileStamp(DateTime WriteUtc, long Length);

    private FileStamp? _loadedStamp;
    private FileStamp? _pendingStamp;

    private static FileStamp? ReadStamp(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return null;

        try
        {
            var fi = new FileInfo(path);
            return fi.Exists ? new FileStamp(fi.LastWriteTimeUtc, fi.Length) : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Read-only preview of a file that another program has modified: show the new content.
    /// Never touches a file that is unlocked for editing or has pending edits.
    /// </summary>
    private void ReloadOpenFileIfChangedOnDisk()
    {
        if (!HasActiveFile || !IsPreviewUnlocked || IsEditorDirty || !IsEditorLocked ||
            !IsEditAllowed || _loadedStamp is not { } loaded)
        {
            return;
        }

        if (ReadStamp(CurrentEditingFilePath) is not { } onDisk || onDisk == loaded)
        {
            _pendingStamp = null;
            return;
        }

        // Wait until the file looks the same on two checks in a row: it may still be being written
        if (_pendingStamp != onDisk)
        {
            _pendingStamp = onDisk;
            return;
        }

        _pendingStamp = null;
        SelectFile(CurrentEditingFilePath!);
    }

    public void OpenFile(string filePath)
    {
        SelectFile(filePath);
    }

    /// <summary>
    /// Called when this tab becomes the visible one. Re-reads the file from disk only when
    /// that is harmless; a file being edited (unlocked or with unsaved changes) is left
    /// exactly as it is, so switching tabs never discards edits.
    /// </summary>
    public void SyncSelectedFile(bool force = false)
    {
        if (string.IsNullOrEmpty(CurrentEditingFilePath))
            return;

        if (IsEditorDirty || !IsEditorLocked)
        {
            ReselectCurrentFileInList();
            return;
        }

        // Preview already up to date with the disk: keep it as it is
        if (!force && IsPreviewUnlocked && _loadedStamp is { } loaded &&
            ReadStamp(CurrentEditingFilePath) is { } onDisk && onDisk == loaded)
        {
            ReselectCurrentFileInList();
            return;
        }

        if (File.Exists(CurrentEditingFilePath))
        {
            SelectFile(CurrentEditingFilePath);
        }
    }

    [RelayCommand]
    public void UnlockPreview()
    {
        if (string.IsNullOrWhiteSpace(CurrentEditingFilePath) || !File.Exists(CurrentEditingFilePath))
            return;

        if (!CanUnlockPreview)
            return;

        try
        {
            var result = _textFileService.ReadText(CurrentEditingFilePath);
            IsLoadingFile = true;
            try
            {
                OriginalText = result.Content;
                EditorText = result.Content;
                IsEditorDirty = false;
            }
            finally
            {
                IsLoadingFile = false;
            }
            _loadedEncoding = result.Encoding;
            _loadedNewline = result.Newline;
            _loadedStamp = ReadStamp(CurrentEditingFilePath);
            _pendingStamp = null;
            EditorHasBom = result.HasBom;

            EditorEncodingName = result.HasBom ? "UTF-8 BOM" : result.Encoding.EncodingName;
            EditorNewlineName = TextFileService.GetNewlineDisplayName(result.Newline);

            var ext = Path.GetExtension(CurrentEditingFilePath).ToLowerInvariant();
            IsJsonFile = ext == ".json";
            IsMarkdownFile = ext is ".md" or ".markdown";

            IsPreviewUnlocked = true;
            RequiresPreviewUnlock = false;
            IsEditorLocked = true;
            CanUnlockToEdit = _configService.Current.EditorExtensions.Contains(ext);
            StatusMessage = $"Preview unlocked for '{CurrentEditingFileName}' [Read-Only]";
        }
        catch (Exception ex)
        {
            RequiresPreviewUnlock = true;
            CanUnlockPreview = false;
            PreviewNoticeGlyph = "\uE783";
            PreviewNoticeText = $"Cannot preview: {ex.Message}";
            StatusMessage = $"Cannot unlock preview: {ex.Message}";
        }
    }

    [RelayCommand]
    public void UnlockEditor()
    {
        if (!HasActiveFile || !IsPreviewUnlocked)
            return;

        if (!IsEditAllowed)
        {
            StatusMessage = "This file type is preview-only. Add its extension in Settings to edit it.";
            OnPropertyChanged(nameof(IsEditingEnabled));
            return;
        }

        IsEditorLocked = false;
        CanUnlockToEdit = false;
        // Text changed while read-only (Format JSON) becomes a pending edit once editable
        IsEditorDirty = !TextFileService.EqualsIgnoringNewlineStyle(EditorText, OriginalText);
        StatusMessage = $"Editing enabled for '{CurrentEditingFileName}'";
    }

    [RelayCommand]
    public void LockEditor()
    {
        if (!HasActiveFile || !IsPreviewUnlocked)
            return;

        IsEditorLocked = true;
        StatusMessage = IsEditorDirty
            ? $"Locked '{CurrentEditingFileName}' (read-only) - unsaved changes can still be saved"
            : $"Locked '{CurrentEditingFileName}' (read-only)";
    }

    [RelayCommand]
    public Task SaveEditorFile() => SaveEditorFileAsync();

    /// <summary>Saves the editor content. Returns true only when the file was written.</summary>
    public async Task<bool> SaveEditorFileAsync(XamlRoot? xamlRoot = null)
    {
        if (string.IsNullOrWhiteSpace(CurrentEditingFilePath))
            return false;

        // Locking a file with pending edits must not trap them: Save stays available
        if (IsEditorLocked && !IsEditorDirty)
        {
            StatusMessage = "File is locked in read-only mode. Click 'Unlock to Edit' to enable saving.";
            return false;
        }

        // Another program changed the file after it was loaded here: never overwrite silently
        if (_loadedStamp is { } loaded && ReadStamp(CurrentEditingFilePath) is { } onDisk && onDisk != loaded)
        {
            var choice = await AskFileChangedOnDiskAsync(xamlRoot);
            if (choice == ContentDialogResult.Secondary)
            {
                // Explicit choice: drop my edits and take the version on disk
                SelectFile(CurrentEditingFilePath);
                StatusMessage = $"Reloaded '{CurrentEditingFileName}' from disk";
                return false;
            }
            if (choice != ContentDialogResult.Primary)
            {
                StatusMessage = $"Not saved: '{CurrentEditingFileName}' was changed on disk by another program";
                return false;
            }
        }

        try
        {
            _textFileService.WriteText(CurrentEditingFilePath, EditorText, _loadedEncoding, _loadedNewline, EditorHasBom);
            OriginalText = TextFileService.NormalizeNewlines(EditorText);
            IsEditorDirty = false;
            _loadedStamp = ReadStamp(CurrentEditingFilePath);
            _pendingStamp = null;
            if (_loadedStamp is { } saved)
            {
                CurrentEditingFileSizeText = FSItem.FormatSize(saved.Length);
            }
            StatusMessage = $"Saved '{CurrentEditingFileName}'";
            return true;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error saving: {ex.Message}";
            return false;
        }
    }

    private async Task<ContentDialogResult> AskFileChangedOnDiskAsync(XamlRoot? xamlRoot)
    {
        var root = xamlRoot ?? App.MainAppWindow?.Content?.XamlRoot;
        if (root == null)
            return ContentDialogResult.None;

        try
        {
            var dialog = new ContentDialog
            {
                Title = "File Changed on Disk",
                Content = $"'{CurrentEditingFileName}' was modified by another program after you opened it.\n\n" +
                          "Overwrite it with your version, or reload it from disk and lose your edits?",
                PrimaryButtonText = "Overwrite",
                SecondaryButtonText = "Reload from Disk",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = root,
                RequestedTheme = ThemeService.ActiveTheme
            };
            return await dialog.ShowAsync();
        }
        catch (Exception ex)
        {
            // Typically another dialog is already open: do not save behind the user's back
            AppLogger.Error("[AskFileChangedOnDiskAsync] Error showing dialog", ex);
            return ContentDialogResult.None;
        }
    }

    [RelayCommand]
    public void FormatJson()
    {
        if (!IsJsonFile || string.IsNullOrWhiteSpace(EditorText))
            return;

        var err = _textFileService.ValidateJson(EditorText);
        if (err != null)
        {
            StatusMessage = $"JSON format error: {err}";
            return;
        }

        try
        {
            EditorText = _textFileService.FormatJson(EditorText);
            if (IsEditorLocked)
            {
                // Read-only: formatting is just a nicer view, nothing to save until unlocked
                StatusMessage = "JSON formatted for viewing (unlock to edit and save it)";
            }
            else
            {
                IsEditorDirty = !TextFileService.EqualsIgnoringNewlineStyle(EditorText, OriginalText);
                StatusMessage = "JSON formatted successfully";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"JSON formatting failed: {ex.Message}";
        }
    }

    [RelayCommand]
    public void OpenInExplorer(string? target = null)
    {
        var targetPath = target ?? CurrentEditingFilePath ?? CurrentPath;
        _fileSystemService.OpenInExplorer(targetPath);
    }

    [RelayCommand]
    public void OpenInTerminal()
    {
        _fileSystemService.OpenInTerminal(CurrentPath);
    }
}
