using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using DirSnap2.Services;

namespace DirSnap2.Views;

/// <summary>
/// Settings & About dialog.
/// Allows customizing theme, accent color, preview extensions, Snap List defaults,
/// and displays application version and metadata.
/// </summary>
public sealed class SettingsDialog : ContentDialog
{
    private readonly IConfigService _configService;
    private readonly IThemeService _themeService;
    private readonly FrameworkElement _rootElement;

    private readonly StackPanel _extensionsPanel;
    private readonly TextBox _addExtensionBox;

    public SettingsDialog(
        IConfigService configService,
        IThemeService themeService,
        FrameworkElement rootElement)
    {
        _configService = configService;
        _themeService = themeService;
        _rootElement = rootElement;

        Title = "Settings & About";
        PrimaryButtonText = "Close";
        DefaultButton = ContentDialogButton.Primary;

        var scrollViewer = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MaxHeight = 520,
            Width = 460
        };

        var rootStack = new StackPanel
        {
            Spacing = 16,
            Padding = new Thickness(4)
        };

        // ==================== 1. APPEARANCE SECTION ====================
        rootStack.Children.Add(CreateSectionHeader("🎨 Appearance"));

        var appearancePanel = new StackPanel { Spacing = 10, Margin = new Thickness(8, 0, 0, 0) };

        // Theme Mode
        var themeRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
        var darkRadio = new RadioButton { Content = "Dark Theme", IsChecked = _themeService.CurrentTheme == ElementTheme.Dark };
        var lightRadio = new RadioButton { Content = "Light Theme", IsChecked = _themeService.CurrentTheme == ElementTheme.Light };

        darkRadio.Checked += (_, _) =>
        {
            _themeService.SetTheme(ElementTheme.Dark, _rootElement);
            _configService.Current.Theme = "dark";
            _configService.Save();
        };

        lightRadio.Checked += (_, _) =>
        {
            _themeService.SetTheme(ElementTheme.Light, _rootElement);
            _configService.Current.Theme = "light";
            _configService.Save();
        };

        themeRow.Children.Add(darkRadio);
        themeRow.Children.Add(lightRadio);
        appearancePanel.Children.Add(themeRow);

        // Accent Colors
        var accentTitle = new TextBlock
        {
            Text = "Accent Color:",
            FontSize = 12,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Margin = new Thickness(0, 4, 0, 2)
        };
        appearancePanel.Children.Add(accentTitle);

        var accentRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        accentRow.Children.Add(CreateAccentButton("blue", "#0078D4", "Blue"));
        accentRow.Children.Add(CreateAccentButton("green", "#10B981", "Green"));
        accentRow.Children.Add(CreateAccentButton("purple", "#8B5CF6", "Purple"));
        accentRow.Children.Add(CreateAccentButton("teal", "#14B8A6", "Teal"));
        accentRow.Children.Add(CreateAccentButton("orange", "#F97316", "Orange"));
        appearancePanel.Children.Add(accentRow);

        rootStack.Children.Add(appearancePanel);

        // ==================== 2. EXTENSIONS SECTION ====================
        rootStack.Children.Add(CreateSectionHeader("📄 Quick Preview Extensions"));

        var extHelp = new TextBlock
        {
            Text = "Files with these extensions appear in lists by default (even when 'Show all' is off) and preview automatically on selection in view-only mode until unlocked:",
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            Margin = new Thickness(8, 0, 0, 4)
        };
        rootStack.Children.Add(extHelp);

        _extensionsPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Margin = new Thickness(8, 0, 0, 4)
        };
        RebuildExtensionChips();
        rootStack.Children.Add(_extensionsPanel);

        // Add Extension row
        var addRow = new Grid { Margin = new Thickness(8, 4, 0, 0) };
        addRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        addRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _addExtensionBox = new TextBox
        {
            PlaceholderText = "Add extension (e.g. .csv, .log, .py)...",
            FontSize = 12
        };
        Grid.SetColumn(_addExtensionBox, 0);

        var addBtn = new Button
        {
            Content = "Add",
            Margin = new Thickness(8, 0, 0, 0),
            Padding = new Thickness(14, 5, 14, 5),
            FontSize = 12,
            Style = (Style)Application.Current.Resources["AppAccentButtonStyle"]
        };
        addBtn.Click += (_, _) => AddExtension();
        _addExtensionBox.KeyDown += (_, ke) =>
        {
            if (ke.Key == Windows.System.VirtualKey.Enter)
            {
                AddExtension();
                ke.Handled = true;
            }
        };
        Grid.SetColumn(addBtn, 1);

        addRow.Children.Add(_addExtensionBox);
        addRow.Children.Add(addBtn);
        rootStack.Children.Add(addRow);

        // ==================== 3. SNAP LIST DEFAULTS ====================
        rootStack.Children.Add(CreateSectionHeader("📋 Snap List Cart Defaults"));

        var snapPanel = new StackPanel { Spacing = 6, Margin = new Thickness(8, 0, 0, 0) };
        var fullPathCheck = new CheckBox
        {
            Content = "Include full path by default",
            IsChecked = _configService.Current.IncludeFullPath,
            FontSize = 12
        };
        fullPathCheck.Click += (_, _) =>
        {
            _configService.Current.IncludeFullPath = fullPathCheck.IsChecked == true;
            _configService.Save();
        };

        var extCheck = new CheckBox
        {
            Content = "Include extension by default",
            IsChecked = _configService.Current.IncludeExtension,
            FontSize = 12
        };
        extCheck.Click += (_, _) =>
        {
            _configService.Current.IncludeExtension = extCheck.IsChecked == true;
            _configService.Save();
        };

        snapPanel.Children.Add(fullPathCheck);
        snapPanel.Children.Add(extCheck);
        rootStack.Children.Add(snapPanel);

        // ==================== 4. ABOUT SECTION ====================
        rootStack.Children.Add(CreateSectionHeader("ℹ️ About"));

        var aboutBorder = new Border
        {
            Background = (Brush)Application.Current.Resources["AppBackgroundBrush"],
            BorderBrush = (Brush)Application.Current.Resources["AppBorderBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12),
            Margin = new Thickness(8, 0, 0, 0)
        };

        var aboutStack = new StackPanel { Spacing = 4 };
        aboutStack.Children.Add(new TextBlock
        {
            Text = AppConstants.AppTitle,
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold
        });
        aboutStack.Children.Add(new TextBlock
        {
            Text = "Windows 11 Native Directory Snapshot & Text Editor Utility",
            FontSize = 12,
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
        });
        aboutStack.Children.Add(new TextBlock
        {
            Text = "C# / .NET 8 • WinUI 3 (Windows App SDK 1.6 Unpackaged) • Fluent Design & Mica",
            FontSize = 11,
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            Margin = new Thickness(0, 4, 0, 0)
        });

        aboutBorder.Child = aboutStack;
        rootStack.Children.Add(aboutBorder);

        scrollViewer.Content = rootStack;
        Content = scrollViewer;
    }

    private static TextBlock CreateSectionHeader(string text)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Margin = new Thickness(0, 10, 0, 2)
        };
    }

    private Button CreateAccentButton(string key, string hexColor, string name)
    {
        var color = Color.FromArgb(
            255,
            Convert.ToByte(hexColor.Substring(1, 2), 16),
            Convert.ToByte(hexColor.Substring(3, 2), 16),
            Convert.ToByte(hexColor.Substring(5, 2), 16));

        var btn = new Button
        {
            Width = 32,
            Height = 32,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(16),
            Background = new SolidColorBrush(color),
            BorderBrush = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0)
        };
        ToolTipService.SetToolTip(btn, name);

        bool isActive = string.Equals(_themeService.CurrentAccent, key, StringComparison.OrdinalIgnoreCase);
        if (isActive)
        {
            btn.BorderBrush = new SolidColorBrush(Colors.White);
            btn.BorderThickness = new Thickness(2);
        }

        btn.Click += (_, _) =>
        {
            _themeService.SetAccentColor(key);
            _configService.Current.DefaultColorTheme = key;
            _configService.Save();
        };

        return btn;
    }

    private void RebuildExtensionChips()
    {
        _extensionsPanel.Children.Clear();

        var exts = _configService.Current.EditorExtensions;
        foreach (var ext in exts)
        {
            var chip = new Border
            {
                Background = (Brush)Application.Current.Resources["AppBackgroundBrush"],
                BorderBrush = (Brush)Application.Current.Resources["AppBorderBrush"],
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 3, 4, 3)
            };

            var chipStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            chipStack.Children.Add(new TextBlock
            {
                Text = ext,
                FontSize = 11,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            });

            var removeBtn = new Button
            {
                Padding = new Thickness(2),
                Width = 18,
                Height = 18,
                Background = new SolidColorBrush(Colors.Transparent),
                BorderThickness = new Thickness(0)
            };
            removeBtn.Content = new FontIcon { Glyph = "\uE711", FontSize = 8 };
            string capturedExt = ext;
            removeBtn.Click += (_, _) => RemoveExtension(capturedExt);

            chipStack.Children.Add(removeBtn);
            chip.Child = chipStack;
            _extensionsPanel.Children.Add(chip);
        }
    }

    private void AddExtension()
    {
        var raw = _addExtensionBox.Text?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(raw)) return;

        if (!raw.StartsWith('.'))
            raw = "." + raw;

        if (!_configService.Current.EditorExtensions.Contains(raw))
        {
            _configService.Current.EditorExtensions.Add(raw);
            _configService.Save();
            _configService.NotifyChanged();
            RebuildExtensionChips();
            _addExtensionBox.Text = string.Empty;
        }
    }

    private void RemoveExtension(string ext)
    {
        if (_configService.Current.EditorExtensions.Remove(ext))
        {
            _configService.Save();
            _configService.NotifyChanged();
            RebuildExtensionChips();
        }
    }
}
