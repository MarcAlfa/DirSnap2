using System.Diagnostics;
using System.IO;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics;
using Windows.UI;
using DirSnap2.Services;

namespace DirSnap2.Views;

/// <summary>
/// About DirSnap 2 window.
/// Displays application metadata and tribute with an interactive retro arcade bouncing logo.
/// Features wall impact flashes, bounce counters, and a sleek dark cyber aesthetic.
/// </summary>
public sealed class AboutWindow : Window
{
    private readonly DispatcherTimer _animationTimer;
    private readonly Image _logoImage;
    private readonly Canvas _animationCanvas;
    private readonly Border _canvasBorder;
    private readonly TextBlock _bounceCounterText;

    private readonly SolidColorBrush _borderNormalBrush;
    private readonly SolidColorBrush _borderFlashBrush;

    private double _x = 24.0;
    private double _y = 20.0;
    private double _vx = 2.6;
    private double _vy = 2.1;
    private readonly double _logoWidth = 148.0;
    private readonly double _logoHeight = 84.0;
    private int _bounceCount = 0;
    private int _borderFlashTicks = 0;

    public AboutWindow(IThemeService themeService, Window? parentWindow = null)
    {
        Title = $"About {AppConstants.AppName}";

        int aboutWidth = 540;
        int aboutHeight = 480;

        // Configure window size, icon, and position centered over parent window/monitor
        try
        {
            var appWindow = AppWindow;
            var icoPath = Path.Combine(AppContext.BaseDirectory, "assets", "app_icon.ico");
            if (File.Exists(icoPath))
            {
                appWindow.SetIcon(icoPath);
            }

            if (parentWindow != null)
            {
                var parentPos = parentWindow.AppWindow.Position;
                var parentSize = parentWindow.AppWindow.Size;
                int centerX = parentPos.X + (parentSize.Width - aboutWidth) / 2;
                int centerY = parentPos.Y + (parentSize.Height - aboutHeight) / 2;
                appWindow.MoveAndResize(new RectInt32(centerX, centerY, aboutWidth, aboutHeight));
            }
            else
            {
                appWindow.Resize(new SizeInt32(aboutWidth, aboutHeight));
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AboutWindow] Window setup error: {ex.Message}");
        }

        // Active Accent & Hover Brushes
        var accentBrush = Application.Current.Resources["AppAccentBrush"] as SolidColorBrush
            ?? new SolidColorBrush(Color.FromArgb(255, 16, 185, 129));
        var hoverBrush = Application.Current.Resources["AppAccentHoverBrush"] as SolidColorBrush
            ?? new SolidColorBrush(Color.FromArgb(255, 5, 150, 105));

        var accentColor = accentBrush.Color;
        _borderNormalBrush = new SolidColorBrush(Color.FromArgb(90, accentColor.R, accentColor.G, accentColor.B));
        _borderFlashBrush = new SolidColorBrush(Color.FromArgb(255, accentColor.R, accentColor.G, accentColor.B));

        // Main Layout (Rich dark slate window)
        var rootGrid = new Grid
        {
            Padding = new Thickness(22, 18, 22, 16),
            RowSpacing = 14,
            Background = new SolidColorBrush(Color.FromArgb(255, 26, 26, 28)),
            RequestedTheme = ElementTheme.Dark
        };
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // Header Section
        var headerPanel = new StackPanel { Spacing = 4 };
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        titleRow.Children.Add(new FontIcon
        {
            Glyph = "\uE8B7",
            FontSize = 18,
            Foreground = accentBrush
        });
        titleRow.Children.Add(new TextBlock
        {
            Text = AppConstants.AppTitle,
            FontSize = 16,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = new SolidColorBrush(Colors.White)
        });
        headerPanel.Children.Add(titleRow);

        headerPanel.Children.Add(new TextBlock
        {
            Text = "Fast, native Windows 11 file explorer, directory snapshot & text editor",
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 215, 215, 215))
        });
        headerPanel.Children.Add(new TextBlock
        {
            Text = "© alpha-studio.it • MarcAlfa",
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 165, 165, 165))
        });

        Grid.SetRow(headerPanel, 0);
        rootGrid.Children.Add(headerPanel);

        // ==================== BOUNCING LOGO ARENA ====================
        // Deep obsidian cyber/arcade background with subtle grid overlay & reactive glowing border
        _canvasBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(255, 16, 20, 28)),
            BorderBrush = _borderNormalBrush,
            BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(8)
        };

        var arenaGrid = new Grid();

        // Subtle tech grid lines in background
        var gridCanvas = new Canvas
        {
            IsHitTestVisible = false,
            Opacity = 0.18
        };
        gridCanvas.Loaded += (_, _) =>
        {
            gridCanvas.Children.Clear();
            var lineBrush = new SolidColorBrush(Color.FromArgb(255, accentColor.R, accentColor.G, accentColor.B));
            double w = _canvasBorder.ActualWidth;
            double h = _canvasBorder.ActualHeight;

            // Vertical grid lines
            for (double gx = 32; gx < w; gx += 32)
            {
                var line = new Microsoft.UI.Xaml.Shapes.Line
                {
                    X1 = gx, Y1 = 0, X2 = gx, Y2 = h,
                    Stroke = lineBrush, StrokeThickness = 0.8
                };
                gridCanvas.Children.Add(line);
            }
            // Horizontal grid lines
            for (double gy = 32; gy < h; gy += 32)
            {
                var line = new Microsoft.UI.Xaml.Shapes.Line
                {
                    X1 = 0, Y1 = gy, X2 = w, Y2 = gy,
                    Stroke = lineBrush, StrokeThickness = 0.8
                };
                gridCanvas.Children.Add(line);
            }
        };
        arenaGrid.Children.Add(gridCanvas);

        // Main animation canvas
        _animationCanvas = new Canvas
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };

        _logoImage = new Image
        {
            Width = _logoWidth,
            Height = _logoHeight,
            Stretch = Stretch.Uniform
        };

        // Prefer transparent high-contrast PNG asset, fallback to transparent or original jpg
        var logoPngPath = Path.Combine(AppContext.BaseDirectory, "assets", "as_logo_dark.png");
        var logoFallbackPng = Path.Combine(AppContext.BaseDirectory, "assets", "as_logo_trasp.png");
        var logoFallbackJpg = Path.Combine(AppContext.BaseDirectory, "assets", "as_logo_trasp.jpg");

        var effectiveLogoPath = File.Exists(logoPngPath) ? logoPngPath
            : File.Exists(logoFallbackPng) ? logoFallbackPng
            : logoFallbackJpg;

        if (File.Exists(effectiveLogoPath))
        {
            _logoImage.Source = new BitmapImage(new Uri(effectiveLogoPath));
        }

        Canvas.SetLeft(_logoImage, _x);
        Canvas.SetTop(_logoImage, _y);
        _animationCanvas.Children.Add(_logoImage);

        arenaGrid.Children.Add(_animationCanvas);

        // Retro HUD Overlay: Top-left pill + Bottom-right bounce counter
        var hudGrid = new Grid
        {
            IsHitTestVisible = false,
            Padding = new Thickness(12, 10, 12, 10)
        };

        var retroTag = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Background = new SolidColorBrush(Color.FromArgb(140, 20, 26, 38)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(70, accentColor.R, accentColor.G, accentColor.B)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 3, 8, 3)
        };
        retroTag.Child = new TextBlock
        {
            Text = "ALPHA STUDIO RETRO ARENA",
            FontSize = 9,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = accentBrush,
            FontFamily = new FontFamily("Consolas")
        };
        hudGrid.Children.Add(retroTag);

        _bounceCounterText = new TextBlock
        {
            Text = "BOUNCES: 0",
            FontSize = 10,
            FontFamily = new FontFamily("Consolas"),
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromArgb(180, 140, 160, 185)),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom
        };
        hudGrid.Children.Add(_bounceCounterText);

        arenaGrid.Children.Add(hudGrid);

        // Click to launch speed boost easter egg
        arenaGrid.PointerPressed += (_, _) =>
        {
            _vx = -_vx * 1.15;
            _vy = -_vy * 1.15;
            if (Math.Abs(_vx) > 5.5) _vx = Math.Sign(_vx) * 2.6;
            if (Math.Abs(_vy) > 4.5) _vy = Math.Sign(_vy) * 2.1;
            TriggerImpactFlash();
        };

        _canvasBorder.Child = arenaGrid;
        Grid.SetRow(_canvasBorder, 1);
        rootGrid.Children.Add(_canvasBorder);

        // Bottom Action Row
        var bottomGrid = new Grid();
        bottomGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bottomGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var tributeText = new TextBlock
        {
            Text = "Paying tribute to PrinT-fILes (Alpha Studio, 2000)",
            FontSize = 10,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 160, 160, 160)),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(tributeText, 0);
        bottomGrid.Children.Add(tributeText);

        // Close button with dedicated hover/pointer states
        var closeBtn = new Button
        {
            Content = "Close",
            Padding = new Thickness(24, 6, 24, 6),
            FontSize = 11,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            CornerRadius = new CornerRadius(4),
            Background = accentBrush,
            Foreground = new SolidColorBrush(Colors.White),
            BorderThickness = new Thickness(0)
        };
        closeBtn.PointerEntered += (_, _) =>
        {
            closeBtn.Background = hoverBrush;
            closeBtn.Foreground = new SolidColorBrush(Colors.White);
        };
        closeBtn.PointerExited += (_, _) =>
        {
            closeBtn.Background = accentBrush;
            closeBtn.Foreground = new SolidColorBrush(Colors.White);
        };
        closeBtn.Click += (_, _) => Close();
        Grid.SetColumn(closeBtn, 1);
        bottomGrid.Children.Add(closeBtn);

        Grid.SetRow(bottomGrid, 2);
        rootGrid.Children.Add(bottomGrid);

        Content = rootGrid;

        // Bouncing animation timer (50 FPS)
        _animationTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(20)
        };
        _animationTimer.Tick += AnimationTimer_Tick;
        _animationTimer.Start();

        Closed += (_, _) =>
        {
            _animationTimer.Stop();
        };
    }

    private void TriggerImpactFlash()
    {
        _bounceCount++;
        _bounceCounterText.Text = $"BOUNCES: {_bounceCount}";
        _borderFlashTicks = 6;
        _canvasBorder.BorderBrush = _borderFlashBrush;
    }

    private void AnimationTimer_Tick(object? sender, object e)
    {
        double canvasW = _animationCanvas.ActualWidth;
        double canvasH = _animationCanvas.ActualHeight;

        if (canvasW <= _logoWidth || canvasH <= _logoHeight)
            return;

        // Handle impact border flash decay
        if (_borderFlashTicks > 0)
        {
            _borderFlashTicks--;
            if (_borderFlashTicks == 0)
            {
                _canvasBorder.BorderBrush = _borderNormalBrush;
            }
        }

        _x += _vx;
        _y += _vy;
        bool bounced = false;

        if (_x <= 0)
        {
            _x = 0;
            _vx = Math.Abs(_vx);
            bounced = true;
        }
        else if (_x + _logoWidth >= canvasW)
        {
            _x = canvasW - _logoWidth;
            _vx = -Math.Abs(_vx);
            bounced = true;
        }

        if (_y <= 0)
        {
            _y = 0;
            _vy = Math.Abs(_vy);
            bounced = true;
        }
        else if (_y + _logoHeight >= canvasH)
        {
            _y = canvasH - _logoHeight;
            _vy = -Math.Abs(_vy);
            bounced = true;
        }

        if (bounced)
        {
            TriggerImpactFlash();
        }

        Canvas.SetLeft(_logoImage, _x);
        Canvas.SetTop(_logoImage, _y);
    }
}