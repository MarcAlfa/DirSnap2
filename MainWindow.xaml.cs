using System.Diagnostics;
using System.IO;
using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using Windows.Storage.Pickers;
using Windows.System;
using DirSnap2.Models;
using DirSnap2.Services;
using DirSnap2.ViewModels;
using DirSnap2.Views;

namespace DirSnap2;

public sealed partial class MainWindow : Window
{
    public MainViewModel ViewModel { get; }

    private readonly IConfigService _configService;
    private readonly IFileSystemService _fileSystemService;
    private readonly ITextFileService _textFileService;
    private readonly ISnapListService _snapListService;
    private readonly IThemeService _themeService;

    private SnapListWindow? _detachedSnapListWindow;

    public MainWindow()
    {
        // 1. Initialize Core Services
        _configService = new ConfigService();
        _configService.Load();

        _fileSystemService = new FileSystemService();
        _textFileService = new TextFileService();
        _snapListService = new SnapListService(_configService);
        _themeService = new ThemeService();

        // 2. Initialize Main ViewModel
        ViewModel = new MainViewModel(
            _fileSystemService,
            _textFileService,
            _snapListService,
            _configService,
            _themeService);

        // Pre-apply configured Accent Color so that XAML resource lookups resolve immediately
        _themeService.SetAccentColor(_configService.Current.DefaultColorTheme);

        InitializeComponent();

        if (Content is FrameworkElement root)
        {
            root.DataContext = ViewModel;
        }

        Title = AppConstants.AppTitle;

        // 3. Apply Mica Backdrop
        TryApplyMicaBackdrop();

        // 4. Set Window Icon & Restore Multi-Monitor Geometry
        ConfigureWindow();

        // 5. Initialize Theme & Accent Color
        _themeService.Initialize(
            Content as FrameworkElement ?? (FrameworkElement)Content,
            _configService.Current.Theme,
            _configService.Current.DefaultColorTheme);

        // 6. Populate Menus
        PopulateDrivesMenu();
        PopulatePlacesMenu();
        UpdateFavoriteButtonState();

        ViewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName is nameof(MainViewModel.IsCurrentPathFavorite)
                or nameof(MainViewModel.FavoriteStarGlyph)
                or nameof(MainViewModel.ActiveTab)
                or nameof(MainViewModel.CurrentAddressBarPath))
            {
                UpdateFavoriteButtonState();
            }
        };

        // 7. Wire Closing Event
        Closed += MainWindow_Closed;
    }

    private void TryApplyMicaBackdrop()
    {
        if (MicaController.IsSupported())
        {
            SystemBackdrop = new Microsoft.UI.Xaml.Media.MicaBackdrop { Kind = Microsoft.UI.Composition.SystemBackdrops.MicaKind.Base };
        }
    }

    private void ConfigureWindow()
    {
        try
        {
            var appWindow = AppWindow;
            var icoPath = Path.Combine(AppContext.BaseDirectory, "assets", "app_icon.ico");
            if (File.Exists(icoPath))
            {
                appWindow.SetIcon(icoPath);
            }

            // Restore geometry & monitor index
            WindowPlacementService.RestoreWindow(appWindow, _configService.Current.Window);

            // Hook AppWindow Closing to guarantee state save
            appWindow.Closing += (s, e) =>
            {
                try
                {
                    WindowPlacementService.SaveWindow(appWindow, _configService.Current.Window);
                    ViewModel.SaveWorkspace();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[MainWindow] AppWindow.Closing save error: {ex.Message}");
                }
            };
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MainWindow] ConfigureWindow error: {ex.Message}");
        }
    }

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        try
        {
            // Close detached window if open
            _detachedSnapListWindow?.Close();

            // Save window placement, monitor index, and maximized state
            WindowPlacementService.SaveWindow(AppWindow, _configService.Current.Window);

            // Save tabs and settings
            ViewModel.SaveWorkspace();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MainWindow] MainWindow_Closed error: {ex.Message}");
        }
    }

    // ==================== TAB VIEW EVENTS ====================

    private void MainTabView_AddTabButtonClick(TabView sender, object args)
    {
        ViewModel.AddNewTabCommand.Execute(null);
    }

    private void MainTabView_TabCloseRequested(TabView sender, TabViewTabCloseRequestedEventArgs args)
    {
        if (args.Item is WorkspaceTabViewModel tab)
        {
            ViewModel.CloseTabCommand.Execute(tab);
        }
    }

    private void CurrentWorkspaceView_OpenInNewTabRequested(string path)
    {
        ViewModel.AddNewTabCommand.Execute(path);
    }

    // ==================== MENUS & NAVIGATION ====================

    private void PopulateDrivesMenu()
    {
        DrivesMenuFlyout.Items.Clear();
        foreach (var drive in ViewModel.Drives)
        {
            var item = new MenuFlyoutItem
            {
                Text = drive.DisplayName,
                Tag = drive.Path
            };
            item.Click += (s, e) =>
            {
                if (s is MenuFlyoutItem mfi && mfi.Tag is string p)
                {
                    ViewModel.ActiveTab?.NavigateToCommand.Execute(p);
                    UpdateFavoriteButtonState();
                }
            };
            DrivesMenuFlyout.Items.Add(item);
        }
    }

    private void PopulatePlacesMenu()
    {
        PlacesMenuFlyout.Items.Clear();

        // Favorites section
        var favHeader = new MenuFlyoutItem { Text = "⭐️ FAVORITES", IsEnabled = false };
        PlacesMenuFlyout.Items.Add(favHeader);

        foreach (var fav in ViewModel.Favorites)
        {
            var item = new MenuFlyoutItem { Text = fav, Tag = fav };
            item.Click += (s, e) =>
            {
                if (s is MenuFlyoutItem mfi && mfi.Tag is string p)
                {
                    ViewModel.ActiveTab?.NavigateToCommand.Execute(p);
                    UpdateFavoriteButtonState();
                }
            };
            PlacesMenuFlyout.Items.Add(item);
        }

        // Recents section
        PlacesMenuFlyout.Items.Add(new MenuFlyoutSeparator());
        var recHeader = new MenuFlyoutItem { Text = "🕒 RECENT FOLDERS", IsEnabled = false };
        PlacesMenuFlyout.Items.Add(recHeader);

        foreach (var rec in ViewModel.Recents.Take(12))
        {
            var item = new MenuFlyoutItem { Text = rec, Tag = rec };
            item.Click += (s, e) =>
            {
                if (s is MenuFlyoutItem mfi && mfi.Tag is string p)
                {
                    ViewModel.ActiveTab?.NavigateToCommand.Execute(p);
                    UpdateFavoriteButtonState();
                }
            };
            PlacesMenuFlyout.Items.Add(item);
        }
    }

    private void AddressTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            ViewModel.NavigateAddressBarCommand.Execute(null);
            PopulatePlacesMenu();
            UpdateFavoriteButtonState();
            e.Handled = true;
        }
    }

    private void GoAddressButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.NavigateAddressBarCommand.Execute(null);
        PopulatePlacesMenu();
        UpdateFavoriteButtonState();
    }

    private async void CopyAddressButton_Click(object sender, RoutedEventArgs e)
    {
        var path = AddressTextBox.Text?.Trim();
        if (string.IsNullOrEmpty(path))
            path = ViewModel.ActiveTab?.CurrentPath;

        if (!string.IsNullOrEmpty(path))
        {
            var dp = new DataPackage();
            dp.SetText(path);
            Clipboard.SetContent(dp);

            if (sender is Button btn && btn.Content is FontIcon icon)
            {
                var originalGlyph = icon.Glyph;
                icon.Glyph = "\uE73E"; // Checkmark
                await System.Threading.Tasks.Task.Delay(1200);
                icon.Glyph = originalGlyph;
            }
        }
    }

    private void ClearAddressButton_Click(object sender, RoutedEventArgs e)
    {
        AddressTextBox.Text = string.Empty;
        ViewModel.CurrentAddressBarPath = string.Empty;
        AddressTextBox.Focus(FocusState.Programmatic);
    }

    private void FavoriteCurrentPath_Click(object sender, RoutedEventArgs e)
    {
        var path = ViewModel.ActiveTab?.CurrentPath;
        if (string.IsNullOrWhiteSpace(path))
            path = ViewModel.CurrentAddressBarPath;

        if (!string.IsNullOrWhiteSpace(path))
        {
            ViewModel.ToggleFavoriteCommand.Execute(path);
            PopulatePlacesMenu();
            UpdateFavoriteButtonState();
        }
    }

    public void UpdateFavoriteButtonState()
    {
        if (FavoriteStarFontIcon != null)
        {
            FavoriteStarFontIcon.Glyph = ViewModel.FavoriteStarGlyph;
        }
        if (FavoriteCurrentPathButton != null)
        {
            ToolTipService.SetToolTip(FavoriteCurrentPathButton, ViewModel.FavoriteButtonToolTip);
        }
    }

    // ==================== SNAP LIST ACTIONS ====================

    public string GetSnapGlyph(bool isDirectory) =>
        isDirectory ? "\uE8B7" : "\uE8A5";

    private void ClearSnapList_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.SnapList.Clear();
    }

    private void DetachSnapList_Click(object sender, RoutedEventArgs e)
    {
        SnapListFlyout.Hide();

        if (_detachedSnapListWindow == null)
        {
            _detachedSnapListWindow = new SnapListWindow(ViewModel.SnapList, () =>
            {
                _detachedSnapListWindow = null;
                ViewModel.IsSnapListDetached = false;
            });
            ViewModel.IsSnapListDetached = true;
            _detachedSnapListWindow.Activate();
        }
        else
        {
            _detachedSnapListWindow.Activate();
        }
    }

    private void FlyoutRemoveItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: SnapItem item })
        {
            ViewModel.SnapList.Remove(item);
        }
    }

    private void FlyoutSnapListView_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Delete && FlyoutSnapListView.SelectedItems.Count > 0)
        {
            var toRemove = FlyoutSnapListView.SelectedItems.Cast<SnapItem>().ToList();
            foreach (var item in toRemove)
            {
                ViewModel.SnapList.Remove(item);
            }
            e.Handled = true;
        }
    }

    private void FlyoutSendToNotepad_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.SnapList.SendToNotepad();
    }

    private void FlyoutCopy_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.SnapList.CopyToClipboard();
    }

    private async void FlyoutExportTxt_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FileSavePicker();
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            picker.SuggestedStartLocation = PickerLocationId.Desktop;
            picker.FileTypeChoices.Add("Text Documents (*.txt)", new List<string>() { ".txt" });
            picker.SuggestedFileName = "DirSnap_Export";

            var file = await picker.PickSaveFileAsync();
            if (file != null)
            {
                ViewModel.SnapList.ExportToTxt(file.Path);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MainWindow] ExportTxt error: {ex.Message}");
        }
    }

    private async void FlyoutExportCsv_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FileSavePicker();
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            picker.SuggestedStartLocation = PickerLocationId.Desktop;
            picker.FileTypeChoices.Add("CSV Files (*.csv)", new List<string>() { ".csv" });
            picker.SuggestedFileName = "DirSnap_Export";

            var file = await picker.PickSaveFileAsync();
            if (file != null)
            {
                ViewModel.SnapList.ExportToCsv(file.Path);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MainWindow] ExportCsv error: {ex.Message}");
        }
    }

    // ==================== SETTINGS & ABOUT ====================

    private async void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new Views.SettingsDialog(_configService, _themeService, (FrameworkElement)Content, this)
            {
                XamlRoot = ((FrameworkElement)Content).XamlRoot
            };
            await dialog.ShowAsync();
            foreach (var tab in ViewModel.Tabs)
            {
                tab.Refresh();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MainWindow] SettingsDialog error: {ex.Message}");
        }
    }
}
