using System.Diagnostics;
using System.IO;
using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
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
    private AiChatWindow? _detachedAiChatWindow;

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
            root.PreviewKeyDown += Root_PreviewKeyDown;
        }

        Title = AppConstants.AppTitle;

        // AI Assistant flyout panel (the detached window gets its own panel on the same ViewModel)
        FlyoutAiChatPanel.DataContext = ViewModel.AiChat;
        FlyoutAiChatPanel.HostWindow = this;

        // The AI button turns accent-filled while a chat of this session is open
        _aiButtonDefaultStyle = AiAssistantWidgetButton.Style;
        ViewModel.AiChat.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AiChatViewModel.HasMessages))
                UpdateAiButtonState();
            else if (e.PropertyName == nameof(AiChatViewModel.IsBusy))
                UpdateAiButtonPulse();
        };
        UpdateAiButtonState();

        // 3. Apply Mica Backdrop
        TryApplyMicaBackdrop();

        // 4. Set Window Icon & Restore Multi-Monitor Geometry
        ConfigureWindow();
        ConfigureTitleBar();

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

        // 8. Global keyboard shortcuts
        RegisterKeyboardShortcuts();
    }

    // ==================== KEYBOARD SHORTCUTS ====================
    // Keep ShortcutsView.Groups (the list shown to the user) in sync with this method.

    private void RegisterKeyboardShortcuts()
    {
        if (Content is not FrameworkElement root)
            return;

        // The buttons already describe their shortcut in the tooltip
        root.KeyboardAcceleratorPlacementMode = KeyboardAcceleratorPlacementMode.Hidden;

        const VirtualKeyModifiers None = VirtualKeyModifiers.None;
        const VirtualKeyModifiers Ctrl = VirtualKeyModifiers.Control;
        const VirtualKeyModifiers Alt = VirtualKeyModifiers.Menu;

        void Add(VirtualKey key, VirtualKeyModifiers modifiers, Action action)
        {
            var accelerator = new KeyboardAccelerator { Key = key, Modifiers = modifiers };
            accelerator.Invoked += (_, e) =>
            {
                // Shortcuts act on the workspace: ignore them while a dialog is open
                if (IsContentDialogOpen())
                    return;

                e.Handled = true;
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    AppLogger.Error($"[MainWindow] Shortcut {modifiers}+{key} error: {ex.Message}", ex);
                }
            };
            root.KeyboardAccelerators.Add(accelerator);
        }

        // Navigation
        Add(VirtualKey.Left, Alt, () => ViewModel.ActiveTab?.GoBackCommand.Execute(null));
        Add(VirtualKey.Right, Alt, () => ViewModel.ActiveTab?.GoForwardCommand.Execute(null));
        Add(VirtualKey.Up, Alt, () => ViewModel.ActiveTab?.GoUpCommand.Execute(null));
        Add(VirtualKey.F5, None, () => ViewModel.ActiveTab?.RefreshCommand.Execute(null));
        Add(VirtualKey.L, Ctrl, () =>
        {
            AddressTextBox.Focus(FocusState.Keyboard);
            AddressTextBox.SelectAll();
        });
        // Ctrl+F: find in the file when working in the preview, quick filter otherwise
        Add(VirtualKey.F, Ctrl, () =>
        {
            if (!CurrentWorkspaceView.TryOpenFindFromShortcut())
                CurrentWorkspaceView.FocusFilter();
        });
        // Ctrl+G: go to a line of the open file
        Add(VirtualKey.G, Ctrl, () => CurrentWorkspaceView.GoToLineFromShortcut());
        // Ctrl+Shift+F: search text in the files of the current folder
        Add(VirtualKey.F, Ctrl | VirtualKeyModifiers.Shift, () => CurrentWorkspaceView.OpenFileSearch());
        Add(VirtualKey.F3, None, () => CurrentWorkspaceView.FindNextFromShortcut(backwards: false));
        Add(VirtualKey.F3, VirtualKeyModifiers.Shift, () => CurrentWorkspaceView.FindNextFromShortcut(backwards: true));

        // Tabs
        Add(VirtualKey.T, Ctrl, () => ViewModel.AddNewTabCommand.Execute(null));
        Add(VirtualKey.W, Ctrl, () =>
        {
            if (ViewModel.ActiveTab is { } tab)
                _ = CloseTabWithPromptAsync(tab);
        });
        Add(VirtualKey.Tab, Ctrl, () => SwitchTab(+1));
        Add(VirtualKey.Tab, Ctrl | VirtualKeyModifiers.Shift, () => SwitchTab(-1));

        // Editor
        Add(VirtualKey.S, Ctrl, () => ViewModel.ActiveTab?.SaveEditorFileCommand.Execute(null));
        Add(VirtualKey.E, Ctrl, () =>
        {
            if (ViewModel.ActiveTab is { IsEditAllowed: true } tab)
                tab.IsEditingEnabled = !tab.IsEditingEnabled;
        });

        // AI Assistant
        Add(VirtualKey.I, Ctrl, OpenAiAssistant);

        // Help
        Add(VirtualKey.F1, None, () => _ = ShowShortcutsAsync());
    }

    private void Root_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Tab)
            return;

        // Keep Ctrl+Tab / Ctrl+Shift+Tab (workspace tab switching) and Alt+Tab working
        var ctrl = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control);
        var alt = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Menu);
        if ((ctrl & Windows.UI.Core.CoreVirtualKeyStates.Down) != 0 ||
            (alt & Windows.UI.Core.CoreVirtualKeyStates.Down) != 0)
            return;

        // When editing a file in EditorTextBox, let EditorTextBox_PreviewKeyDown insert a tab character
        if (CurrentWorkspaceView?.IsEditorTextBoxFocused == true)
            return;

        // Disable Tab focus cycling across all other form controls
        e.Handled = true;
    }

    private bool IsContentDialogOpen()
    {
        try
        {
            var xamlRoot = (Content as FrameworkElement)?.XamlRoot;
            if (xamlRoot == null)
                return false;

            foreach (var popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(xamlRoot))
            {
                if (popup.Child is ContentDialog)
                    return true;
            }
        }
        catch
        {
            // If it cannot be determined, let the shortcut through
        }
        return false;
    }

    private void SwitchTab(int direction)
    {
        int count = ViewModel.Tabs.Count;
        if (count < 2 || ViewModel.ActiveTab == null)
            return;

        int index = ViewModel.Tabs.IndexOf(ViewModel.ActiveTab);
        ViewModel.ActiveTab = ViewModel.Tabs[((index + direction) % count + count) % count];
    }

    private async Task ShowShortcutsAsync()
    {
        try
        {
            await ShortcutsView.ShowDialogAsync(((FrameworkElement)Content).XamlRoot);
        }
        catch (Exception ex)
        {
            AppLogger.Error($"[MainWindow] Shortcuts dialog error: {ex.Message}", ex);
        }
    }

    private async Task CloseTabWithPromptAsync(WorkspaceTabViewModel tab)
    {
        try
        {
            if (tab.IsEditorDirty)
            {
                bool proceed = await tab.ConfirmDiscardUnsavedChangesAsync(((FrameworkElement)Content).XamlRoot);
                if (!proceed)
                    return;
            }
            ViewModel.CloseTabCommand.Execute(tab);
        }
        catch (Exception ex)
        {
            AppLogger.Error($"[MainWindow] Close tab error: {ex.Message}", ex);
        }
    }

    // ==================== TITLE BAR ====================

    /// <summary>
    /// The app draws its own title bar (icon, name, version) on the window background, like the
    /// Windows 11 apps; Windows keeps only the minimize / maximize / close buttons, made transparent.
    /// It no longer takes the "accent color on title bars" colour of the Windows settings.
    /// </summary>
    private void ConfigureTitleBar()
    {
        try
        {
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(AppTitleBar); // drag area, double-click maximize, right-click system menu

            AppTitleText.Text = AppConstants.AppName;
            AppVersionText.Text = $"v{AppConstants.AppVersion}";

            var icoPath = TitleBarHelper.ResolveAppIconPath();
            if (icoPath != null)
                AppTitleBarIcon.Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(icoPath));

            UpdateTitleBarColors();
            if (Content is FrameworkElement root)
                root.ActualThemeChanged += (_, _) => UpdateTitleBarColors();

            AppTitleBar.SizeChanged += (_, _) => UpdateTitleBarInsets();
            AppTitleBar.Loaded += (_, _) => UpdateTitleBarInsets();

            // Dimmed title when the window is not the active one, like native windows
            Activated += (_, e) =>
            {
                double opacity = e.WindowActivationState == WindowActivationState.Deactivated ? 0.55 : 1.0;
                AppTitleBarText.Opacity = opacity;
                AppTitleBarIcon.Opacity = opacity;
            };
        }
        catch (Exception ex)
        {
            AppLogger.Error("[MainWindow] Custom title bar setup failed", ex);
        }
    }

    private void UpdateTitleBarInsets()
    {
        try
        {
            var tb = AppWindow.TitleBar;
            double scale = AppTitleBar.XamlRoot?.RasterizationScale ?? 1.0;
            if (scale <= 0) scale = 1.0;
            TitleBarLeftInset.Width = new GridLength(tb.LeftInset / scale);
            TitleBarRightInset.Width = new GridLength(tb.RightInset / scale);
        }
        catch { }
    }

    /// <summary>Caption buttons follow the app theme (dark / light), with a soft hover like Windows 11.</summary>
    private void UpdateTitleBarColors()
    {
        bool dark = (Content as FrameworkElement)?.ActualTheme != ElementTheme.Light;
        TitleBarHelper.ApplyCaptionColors(AppWindow, dark);
    }

    private void TryApplyMicaBackdrop()
    {
        if (MicaController.IsSupported())
        {
            SystemBackdrop = new Microsoft.UI.Xaml.Media.MicaBackdrop { Kind = Microsoft.UI.Composition.SystemBackdrops.MicaKind.Base };
        }
    }

    private bool _isClosingConfirmed;

    private void ConfigureWindow()
    {
        try
        {
            var appWindow = AppWindow;
            TitleBarHelper.ApplyWindowIcon(this, appWindow);

            // Restore geometry & monitor index
            WindowPlacementService.RestoreWindow(appWindow, _configService.Current.Window);

            // Hook AppWindow Closing to guarantee state save and prompt for unsaved files
            appWindow.Closing += async (s, e) =>
            {
                if (_isClosingConfirmed)
                    return;

                var dirtyTabs = ViewModel.Tabs.Where(t => t.IsEditorDirty).ToList();
                if (dirtyTabs.Count > 0)
                {
                    e.Cancel = true;
                    foreach (var dirtyTab in dirtyTabs)
                    {
                        ViewModel.ActiveTab = dirtyTab;
                        bool proceed = await dirtyTab.ConfirmDiscardUnsavedChangesAsync(((FrameworkElement)Content).XamlRoot);
                        if (!proceed)
                        {
                            return; // User canceled exit
                        }
                    }

                    _isClosingConfirmed = true;
                    Close();
                    return;
                }

                try
                {
                    WindowPlacementService.SaveWindow(appWindow, _configService.Current.Window);
                    ViewModel.SaveWorkspace();
                }
                catch (Exception ex)
                {
                    AppLogger.Error($"[MainWindow] AppWindow.Closing save error: {ex.Message}", ex);
                }
            };
        }
        catch (Exception ex)
        {
            AppLogger.Error($"[MainWindow] ConfigureWindow error: {ex.Message}", ex);
        }
    }

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        try
        {
            // Close detached windows if open
            _detachedSnapListWindow?.Close();
            ViewModel.AiChat.Stop();
            _detachedAiChatWindow?.SavePlacement();
            _detachedAiChatWindow?.Close();

            // Save window placement, monitor index, and maximized state
            WindowPlacementService.SaveWindow(AppWindow, _configService.Current.Window);

            // Save tabs and settings
            ViewModel.SaveWorkspace();
        }
        catch (Exception ex)
        {
            AppLogger.Error($"[MainWindow] MainWindow_Closed error: {ex.Message}", ex);
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
            _ = CloseTabWithPromptAsync(tab);
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
                if (ViewModel.ActiveTab != null)
                {
                    ViewModel.ActiveTab.StatusMessage = $"Exported {ViewModel.SnapList.Count} items to '{file.Name}'";
                }
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error($"[MainWindow] ExportTxt error: {ex.Message}", ex);
            if (ViewModel.ActiveTab != null)
            {
                ViewModel.ActiveTab.StatusMessage = $"Export to TXT failed: {ex.Message}";
            }
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
                if (ViewModel.ActiveTab != null)
                {
                    ViewModel.ActiveTab.StatusMessage = $"Exported {ViewModel.SnapList.Count} items to '{file.Name}'";
                }
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error($"[MainWindow] ExportCsv error: {ex.Message}", ex);
            if (ViewModel.ActiveTab != null)
            {
                ViewModel.ActiveTab.StatusMessage = $"Export to CSV failed: {ex.Message}";
            }
        }
    }

    // ==================== AI ASSISTANT ====================

    /// <summary>Toolbar button and Ctrl+I: the flyout, or the detached window when there is one.</summary>
    private void OpenAiAssistant()
    {
        if (_detachedAiChatWindow != null)
        {
            _detachedAiChatWindow.Activate();
            return;
        }
        FlyoutBase.ShowAttachedFlyout(AiAssistantWidgetButton);
    }

    private void AiAssistantWidgetButton_Click(object sender, RoutedEventArgs e) => OpenAiAssistant();

    private Style? _aiButtonDefaultStyle;
    private Microsoft.UI.Xaml.Media.Animation.Storyboard? _aiButtonPulse;

    /// <summary>The AI button pulses (slow fade in/out) while a reply is being thought or written.</summary>
    private void UpdateAiButtonPulse()
    {
        try
        {
            if (ViewModel.AiChat.IsBusy)
            {
                if (_aiButtonPulse == null)
                {
                    var fade = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
                    {
                        From = 1.0,
                        To = 0.35,
                        Duration = new Duration(TimeSpan.FromMilliseconds(700)),
                        AutoReverse = true,
                        RepeatBehavior = Microsoft.UI.Xaml.Media.Animation.RepeatBehavior.Forever,
                        EasingFunction = new Microsoft.UI.Xaml.Media.Animation.SineEase
                        {
                            EasingMode = Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseInOut
                        }
                    };
                    Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(fade, AiAssistantWidgetButton);
                    Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(fade, "Opacity");
                    _aiButtonPulse = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
                    _aiButtonPulse.Children.Add(fade);
                }
                _aiButtonPulse.Begin();
            }
            else
            {
                _aiButtonPulse?.Stop();
                AiAssistantWidgetButton.Opacity = 1.0;
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"[MainWindow] AI button pulse: {ex.Message}");
        }
    }

    private void UpdateAiButtonState()
    {
        bool active = ViewModel.AiChat.HasMessages;
        AiAssistantWidgetButton.Style = active && Application.Current.Resources.TryGetValue("AppAccentButtonStyle", out var s)
            ? s as Style
            : _aiButtonDefaultStyle;
        if (active)
            AiAssistantButtonText.Foreground = new SolidColorBrush(Colors.White);
        else
            AiAssistantButtonText.ClearValue(TextBlock.ForegroundProperty);
        ToolTipService.SetToolTip(AiAssistantWidgetButton, active
            ? "AI Assistant - chat in progress (Ctrl+I)"
            : "Open AI Assistant (Ctrl+I)");
    }

    private void AiChatFlyout_Opened(object sender, object e)
    {
        ViewModel.AiChat.RefreshContextLabels();
        FlyoutAiChatPanel.FocusInput();
    }

    private void FlyoutAiChatPanel_DetachRequested(object? sender, EventArgs e)
    {
        AiChatFlyout.Hide();

        if (_detachedAiChatWindow == null)
        {
            _detachedAiChatWindow = new AiChatWindow(
                ViewModel.AiChat,
                _configService,
                _themeService.CurrentTheme,
                AppWindow,
                () => _ = OpenSettingsAsync(),
                () =>
                {
                    _detachedAiChatWindow = null;
                    ViewModel.IsAiChatDetached = false;
                });
            ViewModel.IsAiChatDetached = true;
        }
        _detachedAiChatWindow.Activate();
    }

    private void AiChatPanel_SettingsRequested(object? sender, EventArgs e)
    {
        AiChatFlyout.Hide();
        _ = OpenSettingsAsync();
    }

    // ==================== SETTINGS & ABOUT ====================

    private void SettingsButton_Click(object sender, RoutedEventArgs e) => _ = OpenSettingsAsync();

    private async Task OpenSettingsAsync()
    {
        if (IsContentDialogOpen())
            return;
        try
        {
            Activate();
            var dialog = new Views.SettingsDialog(_configService, _themeService, (FrameworkElement)Content, this)
            {
                XamlRoot = ((FrameworkElement)Content).XamlRoot
            };
            await dialog.ShowAsync();
            foreach (var tab in ViewModel.Tabs)
            {
                tab.Refresh();
            }
            ViewModel.AiChat.RefreshSettings();
            _detachedAiChatWindow?.ApplyTheme(_themeService.CurrentTheme);
        }
        catch (Exception ex)
        {
            AppLogger.Error($"[MainWindow] SettingsDialog error: {ex.Message}", ex);
        }
    }
}
