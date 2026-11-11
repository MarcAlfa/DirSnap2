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

    public ObservableCollection<FSItem> FlatItems { get; } = new();

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
    private bool _hasActiveFile;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsReadOnlyBadgeVisible))]
    [NotifyPropertyChangedFor(nameof(CanFormatJson))]
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
        // Only re-select if the editor does not hold dirty unsaved changes
        if (!string.IsNullOrEmpty(CurrentEditingFilePath) && !IsEditorDirty)
        {
            SelectFile(CurrentEditingFilePath);
        }
    }

    private readonly SemaphoreSlim _unsavedDialogLock = new(1, 1);

    /// <summary>
    /// Checks if there are unsaved changes. If so, prompts the user via a native ContentDialog:
    /// - "Save": saves the file and returns true (or false if saving fails).
    /// - "Don't Save": discards modifications (IsEditorDirty = false) and returns true.
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
                SaveEditorFile();
                return !IsEditorDirty;
            }
            else if (result == ContentDialogResult.Secondary)
            {
                IsEditorDirty = false;
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

        if (newValue == null)
        {
            // Do not clear file selection if ListView is virtualizing or switching tab context
            return;
        }

        if (newValue.Name == ".." || newValue.IsDirectory)
        {
            ClearFileSelection();
        }
        else
        {
            SelectFile(newValue.Path);
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
        if (IsLoadingFile || IsEditorLocked || string.IsNullOrEmpty(CurrentEditingFilePath))
        {
            IsEditorDirty = false;
            return;
        }

        IsEditorDirty = !string.Equals(value, OriginalText, StringComparison.Ordinal);
    }

    [RelayCommand]
    public async Task NavigateTo(string path)
    {
        if (IsEditorDirty)
        {
            if (!await ConfirmDiscardUnsavedChangesAsync())
                return;
        }
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
    public async Task GoBack()
    {
        if (_backHistory.Count == 0) return;

        if (IsEditorDirty)
        {
            if (!await ConfirmDiscardUnsavedChangesAsync())
                return;
        }

        var prev = _backHistory.Pop();
        _forwardHistory.Push(CurrentPath);

        CanGoBack = _backHistory.Count > 0;
        CanGoForward = _forwardHistory.Count > 0;

        CurrentPath = prev;
        UpdateTabTitle();
        CanGoUp = Path.GetDirectoryName(CurrentPath) != null;
        Refresh();
    }

    [RelayCommand]
    public async Task GoForward()
    {
        if (_forwardHistory.Count == 0) return;

        if (IsEditorDirty)
        {
            if (!await ConfirmDiscardUnsavedChangesAsync())
                return;
        }

        var next = _forwardHistory.Pop();
        _backHistory.Push(CurrentPath);

        CanGoBack = _backHistory.Count > 0;
        CanGoForward = _forwardHistory.Count > 0;

        CurrentPath = next;
        UpdateTabTitle();
        CanGoUp = Path.GetDirectoryName(CurrentPath) != null;
        Refresh();
    }

    [RelayCommand]
    public async Task GoUp()
    {
        var parent = Path.GetDirectoryName(CurrentPath);
        if (!string.IsNullOrEmpty(parent) && Directory.Exists(parent))
        {
            if (IsEditorDirty)
            {
                if (!await ConfirmDiscardUnsavedChangesAsync())
                    return;
            }
            NavigateToInternal(parent, recordHistory: true);
        }
    }

    [RelayCommand]
    public void Refresh()
    {
        try
        {
            var allowedExt = _configService.Current.EditorExtensions;
            var (dirs, files, error) = _fileSystemService.ScanDirectory(CurrentPath, ShowAll, allowedExt);

            if (error != null)
            {
                StatusMessage = $"Error: {error}";
                return;
            }

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
            StatusMessage = FolderSummaryText;
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

        FlatItems.Clear();

        int rowIndex = 0;

        // Add ".." entry at the top if we can navigate up
        var parent = Path.GetDirectoryName(CurrentPath);
        if (!string.IsNullOrEmpty(parent))
        {
            FlatItems.Add(new FSItem
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
            FlatItems.Add(item);
        }

        if (!string.IsNullOrEmpty(CurrentEditingFilePath))
        {
            var matching = FlatItems.FirstOrDefault(x => string.Equals(x.Path, CurrentEditingFilePath, StringComparison.OrdinalIgnoreCase));
            if (matching != null)
            {
                if (SelectedItem != matching)
                {
                    SelectedItem = matching;
                }
                matching.IsSelected = true;
            }
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

        ApplyFilterAndSort();
        _configService.Save();
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
                EditorHasBom = result.HasBom;

                EditorEncodingName = result.HasBom ? "UTF-8 BOM" : result.Encoding.EncodingName;
                EditorNewlineName = result.Newline == "\r\n" ? "CRLF" : "LF";

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
        if (SelectedItem != null)
        {
            SelectedItem.IsSelected = false;
        }

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

    public void OpenFile(string filePath)
    {
        SelectFile(filePath);
    }

    public void SyncSelectedFile()
    {
        if (!string.IsNullOrEmpty(CurrentEditingFilePath) && File.Exists(CurrentEditingFilePath))
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
            EditorHasBom = result.HasBom;

            EditorEncodingName = result.HasBom ? "UTF-8 BOM" : result.Encoding.EncodingName;
            EditorNewlineName = result.Newline == "\r\n" ? "CRLF" : "LF";

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

        IsEditorLocked = false;
        CanUnlockToEdit = false;
        StatusMessage = $"Editing enabled for '{CurrentEditingFileName}'";
    }

    [RelayCommand]
    public void LockEditor()
    {
        if (!HasActiveFile || !IsPreviewUnlocked)
            return;

        IsEditorLocked = true;
        StatusMessage = $"Locked '{CurrentEditingFileName}' (read-only)";
    }

    [RelayCommand]
    public void SaveEditorFile()
    {
        if (string.IsNullOrWhiteSpace(CurrentEditingFilePath))
            return;

        if (IsEditorLocked)
        {
            StatusMessage = "File is locked in read-only mode. Click 'Unlock to Edit' to enable saving.";
            return;
        }

        try
        {
            _textFileService.WriteText(CurrentEditingFilePath, EditorText, _loadedEncoding, _loadedNewline, EditorHasBom);
            OriginalText = EditorText;
            IsEditorDirty = false;
            StatusMessage = $"Saved '{CurrentEditingFileName}'";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error saving: {ex.Message}";
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
            IsEditorDirty = !string.Equals(EditorText, OriginalText, StringComparison.Ordinal);
            StatusMessage = "JSON formatted successfully";
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
