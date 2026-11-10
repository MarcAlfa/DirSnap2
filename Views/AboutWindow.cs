using System.Diagnostics;
using System.IO;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics;
using DirSnap2.Services;

namespace DirSnap2.Views;

public sealed class AboutWindow : Window
{
    private readonly DispatcherTimer _animationTimer;
    private readonly Image _logoImage;
    private readonly Canvas _animationCanvas;

    private double _x = 20.0;
    private double _y = 20.0;
    private double _vx = 2.8;
    private double _vy = 2.3;
    private readonly double _logoWidth = 140.0;
    private readonly double _logoHeight = 80.0;

    public AboutWindow(IThemeService themeService)
    {
        Title = $"About {AppConstants.AppName}";

        // Configure window size and icon
        try
        {
            var appWindow = AppWindow;
            var icoPath = Path.Combine(AppContext.BaseDirectory, "assets", "app_icon.ico");
            if (File.Exists(icoPath))
            {
                appWindow.SetIcon(icoPath);
            }

            appWindow.Resize(new SizeInt32(520, 440));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AboutWindow] Window setup error: {ex.Message}");
        }

        // Main Layout
        var rootGrid = new Grid
        {
            Padding = new Thickness(20),
            RowSpacing = 14,
            Background = (Brush)Application.Current.Resources["AppBackgroundBrush"]
        };
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // Header Section
        var headerPanel = new StackPanel { Spacing = 4 };
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        titleRow.Children.Add(new FontIcon
        {
            Glyph = "\uE8B7",
            FontSize = 18,
            Foreground = (Brush)Application.Current.Resources["AppAccentBrush"]
        });
        titleRow.Children.Add(new TextBlock
        {
            Text = AppConstants.AppTitle,
            FontSize = 16,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold
        });
        headerPanel.Children.Add(titleRow);

        headerPanel.Children.Add(new TextBlock
        {
            Text = "Fast, native Windows 11 file explorer, directory snapshot & text editor",
            FontSize = 12,
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
        });
        headerPanel.Children.Add(new TextBlock
        {
            Text = "© alpha-studio.it • MarcAlfa",
            FontSize = 11,
            Foreground = (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"]
        });

        Grid.SetRow(headerPanel, 0);
        rootGrid.Children.Add(headerPanel);

        // Bouncing Canvas Container
        var canvasBorder = new Border
        {
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 18, 18, 18)),
            BorderBrush = (Brush)Application.Current.Resources["AppBorderBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6)
        };

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

        var logoPath = Path.Combine(AppContext.BaseDirectory, "assets", "as_logo_trasp.jpg");
        if (File.Exists(logoPath))
        {
            _logoImage.Source = new BitmapImage(new Uri(logoPath));
        }

        Canvas.SetLeft(_logoImage, _x);
        Canvas.SetTop(_logoImage, _y);
        _animationCanvas.Children.Add(_logoImage);

        canvasBorder.Child = _animationCanvas;
        Grid.SetRow(canvasBorder, 1);
        rootGrid.Children.Add(canvasBorder);

        // Bottom Action Row
        var bottomGrid = new Grid();
        bottomGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bottomGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var tributeText = new TextBlock
        {
            Text = "Paying tribute to PrinT-fILes (Alpha Studio, 2000)",
            FontSize = 10,
            Foreground = (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"],
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(tributeText, 0);
        bottomGrid.Children.Add(tributeText);

        var closeBtn = new Button
        {
            Content = "Close",
            Padding = new Thickness(18, 6, 18, 6),
            FontSize = 11,
            Style = (Style)Application.Current.Resources["AppAccentButtonStyle"]
        };
        closeBtn.Click += (_, _) => Close();
        Grid.SetColumn(closeBtn, 1);
        bottomGrid.Children.Add(closeBtn);

        Grid.SetRow(bottomGrid, 2);
        rootGrid.Children.Add(bottomGrid);

        Content = rootGrid;

        // Apply Theme
        if (Content is FrameworkElement root)
        {
            root.RequestedTheme = themeService.CurrentTheme;
        }

        // Bouncing animation timer
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

    private void AnimationTimer_Tick(object? sender, object e)
    {
        double canvasW = _animationCanvas.ActualWidth;
        double canvasH = _animationCanvas.ActualHeight;

        if (canvasW <= _logoWidth || canvasH <= _logoHeight)
            return;

        _x += _vx;
        _y += _vy;

        if (_x <= 0)
        {
            _x = 0;
            _vx = Math.Abs(_vx);
        }
        else if (_x + _logoWidth >= canvasW)
        {
            _x = canvasW - _logoWidth;
            _vx = -Math.Abs(_vx);
        }

        if (_y <= 0)
        {
            _y = 0;
            _vy = Math.Abs(_vy);
        }
        else if (_y + _logoHeight >= canvasH)
        {
            _y = canvasH - _logoHeight;
            _vy = -Math.Abs(_vy);
        }

        Canvas.SetLeft(_logoImage, _x);
        Canvas.SetTop(_logoImage, _y);
    }
}
