using System.IO;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics;
using DirSnap2.Models;
using DirSnap2.Services;
using DirSnap2.ViewModels;

namespace DirSnap2.Views;

/// <summary>
/// Detached floating AI Assistant window (resizable, always on top by default).
/// Same conversation as the toolbar flyout: both show <see cref="AiChatViewModel"/>.
/// Follows the app theme, draws its own title bar like the main window and remembers
/// position, size, monitor and the "always on top" pin.
/// </summary>
public sealed class AiChatWindow : Window
{
    private const int MinWidth = 380;
    private const int MinHeight = 420;
    private const int DefaultWidth = 500;
    private const int DefaultHeight = 720;

    private readonly IConfigService _configService;
    private readonly Action _onClosedCallback;
    private readonly Grid _root;
    private readonly Grid _titleBar;
    private readonly FrameworkElement _titleContent;

    public AiChatPanel Panel { get; }

    public AiChatWindow(
        AiChatViewModel viewModel,
        IConfigService configService,
        ElementTheme theme,
        AppWindow mainAppWindow,
        Action openSettings,
        Action onClosedCallback)
    {
        _configService = configService;
        _onClosedCallback = onClosedCallback;

        Title = "AI Assistant - DirSnap 2";
        if (MicaController.IsSupported())
            SystemBackdrop = new MicaBackdrop { Kind = MicaKind.Base };

        // Built from XAML so the background is a real ThemeResource and follows RequestedTheme
        // (a brush read from Application.Resources in code would follow the Windows theme instead)
        _root = (Grid)XamlReader.Load(
            """
            <Grid xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                  Background="{ThemeResource ApplicationPageBackgroundThemeBrush}">
                <Grid.RowDefinitions>
                    <RowDefinition Height="34" />
                    <RowDefinition Height="*" />
                </Grid.RowDefinitions>
                <Grid Background="Transparent">
                    <StackPanel Orientation="Horizontal" Spacing="10" VerticalAlignment="Center" Margin="14,0,0,0">
                        <Image Width="16" Height="16" VerticalAlignment="Center" />
                        <TextBlock Text="DirSnap 2 · AI Assistant" FontSize="12" FontWeight="SemiBold" VerticalAlignment="Center" />
                    </StackPanel>
                </Grid>
                <Border Grid.Row="1" Padding="14,6,14,12" />
            </Grid>
            """);
        _root.RequestedTheme = theme;

        _titleBar = (Grid)_root.Children[0];
        _titleContent = (FrameworkElement)_titleBar.Children[0];
        var host = (Border)_root.Children[1];

        var icoPath = TitleBarHelper.ResolveAppIconPath();
        if (icoPath != null && ((StackPanel)_titleContent).Children[0] is Image icon)
            icon.Source = new BitmapImage(new Uri(icoPath));

        bool pinned = configService.Current.AiWindowPinned;
        Panel = new AiChatPanel
        {
            DataContext = viewModel,
            HostWindow = this
        };
        Panel.SetDetachedMode(detached: true, pinned: pinned);
        Panel.PinChanged += (_, isPinned) =>
        {
            SetAlwaysOnTop(isPinned);
            _configService.Current.AiWindowPinned = isPinned;
            _configService.Save();
        };
        Panel.SettingsRequested += (_, _) => openSettings();
        host.Child = Panel;
        Content = _root;

        ConfigureTitleBar(theme);
        ConfigureAppWindow(mainAppWindow, pinned, icoPath);

        Activated += (_, e) =>
        {
            bool inactive = e.WindowActivationState == WindowActivationState.Deactivated;
            _titleContent.Opacity = inactive ? 0.55 : 1.0;
            if (!inactive)
                Panel.FocusInput();
        };

        // Closed by the user (X, Alt+F4): save the placement while the window still exists.
        // When the app exits, MainWindow calls SavePlacement before closing this window.
        AppWindow.Closing += (_, _) => SavePlacement();
        Closed += (_, _) => _onClosedCallback?.Invoke();
    }

    public void ApplyTheme(ElementTheme theme)
    {
        _root.RequestedTheme = theme;
        TitleBarHelper.ApplyCaptionColors(AppWindow, theme != ElementTheme.Light);
    }

    private void ConfigureTitleBar(ElementTheme theme)
    {
        try
        {
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(_titleBar);
            TitleBarHelper.ApplyCaptionColors(AppWindow, theme != ElementTheme.Light);
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"[AiChatWindow] Custom title bar: {ex.Message}");
        }
    }

    private void SetAlwaysOnTop(bool onTop)
    {
        if (AppWindow.Presenter is OverlappedPresenter presenter)
            presenter.IsAlwaysOnTop = onTop;
    }

    private void ConfigureAppWindow(AppWindow mainAppWindow, bool pinned, string? icoPath)
    {
        try
        {
            TitleBarHelper.ApplyWindowIcon(this, AppWindow);

            var saved = _configService.Current.AiWindow;
            if (saved != null)
            {
                WindowPlacementService.RestoreWindow(AppWindow, saved, MinWidth, MinHeight);
            }
            else
            {
                // First time: on the monitor of the main window, docked near its right edge
                var work = DisplayArea.GetFromWindowId(mainAppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
                int w = Math.Min(DefaultWidth, work.Width - 40);
                int h = Math.Min(DefaultHeight, work.Height - 40);
                int x = work.X + work.Width - w - 40;
                int y = work.Y + Math.Max(0, (work.Height - h) / 2);
                AppWindow.MoveAndResize(new RectInt32(x, y, w, h));
                WindowPlacementService.TrackNormalBounds(AppWindow);
            }

            SetAlwaysOnTop(pinned);
        }
        catch (Exception ex)
        {
            AppLogger.Error($"[AiChatWindow] ConfigureAppWindow error: {ex.Message}", ex);
        }
    }

    public void SavePlacement()
    {
        try
        {
            var placement = _configService.Current.AiWindow ?? new WindowPlacement();
            WindowPlacementService.SaveWindow(AppWindow, placement, MinWidth, MinHeight);
            _configService.Current.AiWindow = placement;
            _configService.Save();
        }
        catch (Exception ex)
        {
            AppLogger.Error($"[AiChatWindow] Save placement error: {ex.Message}", ex);
        }
    }
}
