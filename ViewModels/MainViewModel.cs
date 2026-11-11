using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DirSnap2.Models;
using DirSnap2.Services;

namespace DirSnap2.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly IFileSystemService _fileSystemService;
    private readonly ITextFileService _textFileService;
    private readonly IConfigService _configService;
    private readonly IThemeService _themeService;

    public ISnapListService SnapList { get; }
    public IThemeService ThemeService => _themeService;

    public ObservableCollection<WorkspaceTabViewModel> Tabs { get; } = new();
    public ObservableCollection<DriveInfoModel> Drives { get; } = new();
    public ObservableCollection<string> Favorites { get; } = new();
    public ObservableCollection<string> Recents { get; } = new();

    [ObservableProperty]
    private WorkspaceTabViewModel? _activeTab;

    [ObservableProperty]
    private int _activeTabIndex;

    [ObservableProperty]
    private string _currentAddressBarPath = "C:\\";

    [ObservableProperty]
    private bool _isSnapListDetached;

    public bool IsCurrentPathFavorite
    {
        get
        {
            var path = ActiveTab?.CurrentPath;
            if (string.IsNullOrWhiteSpace(path))
                path = CurrentAddressBarPath;
            if (string.IsNullOrWhiteSpace(path))
                return false;
            return _configService.IsFavorite(path);
        }
    }

    public string FavoriteStarGlyph => IsCurrentPathFavorite ? "\uE735" : "\uE734";

    public string FavoriteButtonToolTip => IsCurrentPathFavorite
        ? "Remove current folder from Favorites"
        : "Add current folder to Favorites";

    public void NotifyFavoriteProperties()
    {
        OnPropertyChanged(nameof(IsCurrentPathFavorite));
        OnPropertyChanged(nameof(FavoriteStarGlyph));
        OnPropertyChanged(nameof(FavoriteButtonToolTip));
    }

    partial void OnActiveTabChanged(WorkspaceTabViewModel? oldValue, WorkspaceTabViewModel? newValue)
    {
        if (oldValue != null)
        {
            oldValue.PropertyChanged -= OnActiveTabPropertyChanged;
        }
        if (newValue != null)
        {
            newValue.PropertyChanged += OnActiveTabPropertyChanged;
            CurrentAddressBarPath = newValue.CurrentPath;
            // The file/selection sync happens in WorkspaceTabView.OnDataContextChanged,
            // once the view is actually bound to the new tab.
        }
        foreach (var t in Tabs)
        {
            t.IsActiveTab = (t == newValue);
        }
        NotifyFavoriteProperties();
        SaveWorkspace();
    }

    private void OnActiveTabPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WorkspaceTabViewModel.CurrentPath) && ActiveTab != null)
        {
            CurrentAddressBarPath = ActiveTab.CurrentPath;
            NotifyFavoriteProperties();
            SaveWorkspace();
        }
        else if (e.PropertyName == nameof(WorkspaceTabViewModel.CurrentEditingFilePath))
        {
            SaveWorkspace();
        }
    }

    [RelayCommand]
    public void NavigateAddressBar()
    {
        if (ActiveTab != null && !string.IsNullOrWhiteSpace(CurrentAddressBarPath))
        {
            var target = CurrentAddressBarPath.Trim();
            if (Directory.Exists(target))
            {
                ActiveTab.NavigateTo(target);
                NotifyFavoriteProperties();
                SaveWorkspace();
            }
        }
    }

    public MainViewModel(
        IFileSystemService fileSystemService,
        ITextFileService textFileService,
        ISnapListService snapListService,
        IConfigService configService,
        IThemeService themeService)
    {
        _fileSystemService = fileSystemService;
        _textFileService = textFileService;
        _configService = configService;
        _themeService = themeService;

        SnapList = snapListService;

        ReloadDrives();
        ReloadFavoritesAndRecents();
        RestoreWorkspaceTabs();
    }

    public void ReloadDrives()
    {
        Drives.Clear();
        foreach (var drive in _fileSystemService.GetDrives())
        {
            Drives.Add(drive);
        }
    }

    public void ReloadFavoritesAndRecents()
    {
        Favorites.Clear();
        foreach (var fav in _configService.Current.Favorites)
        {
            Favorites.Add(fav);
        }

        Recents.Clear();
        foreach (var r in _configService.Current.Recents)
        {
            Recents.Add(r);
        }

        NotifyFavoriteProperties();
    }

    private void RestoreWorkspaceTabs()
    {
        Tabs.Clear();
        var tabStates = _configService.Current.Workspace.Tabs;

        if (tabStates != null && tabStates.Count > 0)
        {
            foreach (var state in tabStates.Take(10))
            {
                var tabVm = new WorkspaceTabViewModel(
                    _fileSystemService,
                    _textFileService,
                    SnapList,
                    _configService,
                    state);
                Tabs.Add(tabVm);
            }
        }

        if (Tabs.Count == 0)
        {
            var defaultVm = new WorkspaceTabViewModel(
                _fileSystemService,
                _textFileService,
                SnapList,
                _configService,
                new WorkspaceTabState { Path = _configService.Current.LastPath });
            Tabs.Add(defaultVm);
        }

        var activeIndex = _configService.Current.Workspace.Active;
        if (activeIndex >= 0 && activeIndex < Tabs.Count)
        {
            ActiveTab = Tabs[activeIndex];
            ActiveTabIndex = activeIndex;
        }
        else
        {
            ActiveTab = Tabs[0];
            ActiveTabIndex = 0;
        }

        foreach (var t in Tabs)
        {
            t.IsActiveTab = (t == ActiveTab);
        }
    }

    [RelayCommand]
    public void AddNewTab(string? path = null)
    {
        if (Tabs.Count >= 10)
        {
            // Maximum 10 tabs limit
            return;
        }

        var targetPath = !string.IsNullOrWhiteSpace(path)
            ? path
            : ActiveTab?.CurrentPath ?? _configService.Current.LastPath ?? "C:\\";

        var newTab = new WorkspaceTabViewModel(
            _fileSystemService,
            _textFileService,
            SnapList,
            _configService,
            new WorkspaceTabState { Path = targetPath });

        Tabs.Add(newTab);
        ActiveTab = newTab;
        ActiveTabIndex = Tabs.Count - 1;

        SaveWorkspace();
    }

    [RelayCommand]
    public void CloseTab(WorkspaceTabViewModel tab)
    {
        if (Tabs.Count <= 1)
        {
            // Do not close the last remaining tab: navigate it to C:\ instead
            tab.NavigateToInternal("C:\\", recordHistory: false);
            return;
        }

        int index = Tabs.IndexOf(tab);
        Tabs.Remove(tab);
        tab.Dispose();

        if (ActiveTab == tab)
        {
            int nextIndex = Math.Clamp(index, 0, Tabs.Count - 1);
            ActiveTab = Tabs[nextIndex];
            ActiveTabIndex = nextIndex;
        }

        SaveWorkspace();
    }

    [RelayCommand]
    public void ToggleFavorite(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;

        if (_configService.IsFavorite(path))
        {
            _configService.RemoveFavorite(path);
        }
        else
        {
            _configService.AddFavorite(path);
        }

        ReloadFavoritesAndRecents();
        _configService.Save();
    }

    public void SaveWorkspace()
    {
        _configService.Current.Workspace.Tabs = Tabs.Select(t => t.ToState()).ToList();
        _configService.Current.Workspace.Active = ActiveTab != null ? Tabs.IndexOf(ActiveTab) : 0;
        _configService.Save();
    }
}
