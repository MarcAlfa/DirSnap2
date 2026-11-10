using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DirSnap2.Models;
using DirSnap2.Services;

namespace DirSnap2.ViewModels;

public sealed partial class WorkspaceTabViewModel : ObservableObject
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
    public ObservableCollection<TreeNodeItem> TreeRoots { get; } = new();

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
    private bool _hasActiveFile;

    [ObservableProperty]
    private bool _isPreviewUnlocked;

    [ObservableProperty]
    private bool _requiresPreviewUnlock;

    [ObservableProperty]
    private string _editorText = string.Empty;

    [ObservableProperty]
    private bool _isEditorDirty;

    [ObservableProperty]
    private bool _isEditorLocked = true;

    [ObservableProperty]
    private bool _canUnlockToEdit;

    [ObservableProperty]
    private bool _isJsonFile;

    [ObservableProperty]
    private bool _isMarkdownFile;

    [ObservableProperty]
    private string _editorEncodingName = "UTF-8";

    [ObservableProperty]
    private string _editorNewlineName = "CRLF";

    [ObservableProperty]
    private bool _editorHasBom;

    [ObservableProperty]
    private bool _editorWrap = true;

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
        Refresh();
        if (!string.IsNullOrEmpty(CurrentEditingFilePath))
        {
            SelectFile(CurrentEditingFilePath);
        }
    }

    partial void OnSelectedItemChanged(FSItem? value)
    {
        if (value == null)
        {
            // Do not clear file selection if ListView is virtualizing or switching tab context
            return;
        }

        if (value.Name == ".." || value.IsDirectory)
        {
            ClearFileSelection();
        }
        else
        {
            SelectFile(value.Path);
        }
    }

    partial void OnFoldersCountChanged(int value) => OnPropertyChanged(nameof(FoldersCountText));
    partial void OnFilesCountChanged(int value) => OnPropertyChanged(nameof(FilesCountText));
    partial void OnEditorWrapChanged(bool value) => OnPropertyChanged(nameof(EditorTextWrapping));

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
    public void GoForward()
    {
        if (_forwardHistory.Count == 0) return;

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
    public void GoUp()
    {
        var parent = Path.GetDirectoryName(CurrentPath);
        if (!string.IsNullOrEmpty(parent) && Directory.Exists(parent))
        {
            NavigateTo(parent);
        }
    }

    [RelayCommand]
    public void Refresh()
    {
        try
        {
            var allowedExt = ShowAll ? null : _configService.Current.EditorExtensions;
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

            StatusMessage = $"{dirs.Count} folders, {files.Count} files";
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
            if (matching != null && SelectedItem != matching)
            {
                SelectedItem = matching;
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

    private void LoadTree()
    {
        TreeRoots.Clear();
        var allowedExt = ShowAll ? null : _configService.Current.EditorExtensions;
        var children = _fileSystemService.GetTreeChildren(CurrentPath, ShowAll, allowedExt);

        var rootNode = new TreeNodeItem
        {
            Name = Path.GetFileName(CurrentPath) is { Length: > 0 } fn ? fn : CurrentPath,
            Path = CurrentPath,
            IsDirectory = true,
            IsExpanded = true,
            HasUnrealizedChildren = false
        };

        foreach (var c in children)
        {
            rootNode.Children.Add(c);
        }

        TreeRoots.Add(rootNode);
    }

    public void ExpandTreeNode(TreeNodeItem node)
    {
        if (!node.IsDirectory || !node.HasUnrealizedChildren) return;

        node.IsLoading = true;
        try
        {
            var allowedExt = ShowAll ? null : _configService.Current.EditorExtensions;
            var children = _fileSystemService.GetTreeChildren(node.Path, ShowAll, allowedExt);
            node.Children.Clear();
            foreach (var c in children)
            {
                node.Children.Add(c);
            }
            node.HasUnrealizedChildren = false;
        }
        finally
        {
            node.IsLoading = false;
        }
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

        try
        {
            var fi = new FileInfo(filePath);
            CurrentEditingFileSizeText = FSItem.FormatSize(fi.Length);
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

        if (isConfigured)
        {
            // Configured extensions: preview immediately in read-only mode
            try
            {
                var result = _textFileService.ReadText(filePath);
                EditorText = result.Content;
                _loadedEncoding = result.Encoding;
                _loadedNewline = result.Newline;
                EditorHasBom = result.HasBom;

                EditorEncodingName = result.HasBom ? "UTF-8 BOM" : result.Encoding.EncodingName;
                EditorNewlineName = result.Newline == "\r\n" ? "CRLF" : "LF";

                IsPreviewUnlocked = true;
                RequiresPreviewUnlock = false;
                IsEditorLocked = true; // Read-only view
                CanUnlockToEdit = true;
                StatusMessage = $"Previewing '{CurrentEditingFileName}' ({CurrentEditingFileSizeText}) [Read-Only]";
            }
            catch (Exception ex)
            {
                EditorText = string.Empty;
                IsPreviewUnlocked = false;
                RequiresPreviewUnlock = false;
                IsEditorLocked = true;
                CanUnlockToEdit = false;
                StatusMessage = $"Cannot preview: {ex.Message}";
            }
        }
        else
        {
            // Non-configured extension: do not read content automatically! Show unlock prompt
            EditorText = string.Empty;
            IsPreviewUnlocked = false;
            RequiresPreviewUnlock = true;
            IsEditorLocked = true;
            CanUnlockToEdit = false;
            StatusMessage = $"Selected '{CurrentEditingFileName}' (Click 'Unlock Preview' to inspect)";
        }
    }

    public void ClearFileSelection()
    {
        CurrentEditingFilePath = null;
        CurrentEditingFileName = "No file selected";
        CurrentEditingFileSizeText = string.Empty;
        HasActiveFile = false;
        IsPreviewUnlocked = false;
        RequiresPreviewUnlock = false;
        IsEditorLocked = true;
        CanUnlockToEdit = false;
        IsJsonFile = false;
        IsMarkdownFile = false;
        EditorText = string.Empty;
        IsEditorDirty = false;
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

        try
        {
            var result = _textFileService.ReadText(CurrentEditingFilePath);
            EditorText = result.Content;
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
            IsEditorDirty = true;
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
