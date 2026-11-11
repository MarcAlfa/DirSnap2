using System.Diagnostics;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
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
    private readonly Window? _parentWindow;

    private readonly StackPanel _extensionsPanel;
    private readonly TextBox _addExtensionBox;
    private readonly List<Button> _accentButtons = new();
    private readonly RadioButton _darkRadio;
    private readonly RadioButton _lightRadio;
    private readonly CheckBox _fullPathCheck;
    private readonly CheckBox _extCheck;
    private readonly Button _addBtn;
    private readonly Button _openAboutBtn;

    // References for dynamic light/dark contrast updates
    private readonly TextBlock _appearanceHeader;
    private readonly TextBlock _accentTitle;
    private readonly TextBlock _fontSizeTitle;
    private readonly TextBlock _fontSizeHelp;
    private readonly NumberBox _fontSizeBox;
    private readonly TextBlock _fontSizeSampleText;
    private readonly TextBlock _extensionsHeader;
    private readonly TextBlock _extHelp;
    private readonly TextBlock _snapHeader;
    private readonly TextBlock _aboutHeader;
    private readonly Border _aboutBorder;
    private readonly TextBlock _aboutTitleText;
    private readonly TextBlock _aboutCopyText;

    public SettingsDialog(
        IConfigService configService,
        IThemeService themeService,
        FrameworkElement rootElement,
        Window? parentWindow = null)
    {
        _configService = configService;
        _themeService = themeService;
        _rootElement = rootElement;
        _parentWindow = parentWindow;

        Title = "Settings & About";
        PrimaryButtonText = "Close";
        DefaultButton = ContentDialogButton.Primary;
        RequestedTheme = _themeService.CurrentTheme;

        // Force exact dialog width and left drawer alignment with left margin
        Resources["ContentDialogMinWidth"] = 660.0;
        Resources["ContentDialogMaxWidth"] = 660.0;
        Width = 660;
        MinWidth = 660;
        MaxWidth = 660;
        HorizontalAlignment = HorizontalAlignment.Left;
        VerticalAlignment = VerticalAlignment.Center;
        Margin = new Thickness(24, 0, 0, 0);

        // Smooth slide-in animation from outside the screen on the left
        var slideTransform = new TranslateTransform { X = -800 };
        RenderTransform = slideTransform;

        Opened += (_, _) =>
        {
            var sb = new Storyboard();

            var slideAnim = new DoubleAnimation
            {
                From = -800,
                To = 0,
                Duration = new Duration(TimeSpan.FromMilliseconds(350)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(slideAnim, slideTransform);
            Storyboard.SetTargetProperty(slideAnim, "X");
            sb.Children.Add(slideAnim);

            var fadeAnim = new DoubleAnimation
            {
                From = 0.0,
                To = 1.0,
                Duration = new Duration(TimeSpan.FromMilliseconds(250)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(fadeAnim, this);
            Storyboard.SetTargetProperty(fadeAnim, "Opacity");
            sb.Children.Add(fadeAnim);

            sb.Begin();
        };

        var scrollViewer = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            MinHeight = 560,
            MaxHeight = 720,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        var rootStack = new StackPanel
        {
            Spacing = 12,
            Padding = new Thickness(6, 4, 16, 4),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        // ==================== 1. APPEARANCE SECTION ====================
        _appearanceHeader = CreateSectionHeader("🎨 Appearance");
        rootStack.Children.Add(_appearanceHeader);

        var appearancePanel = new StackPanel { Spacing = 8, Margin = new Thickness(8, 0, 0, 0) };

        // Theme Mode
        var themeRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 20 };
        _darkRadio = new RadioButton
        {
            Content = "Dark Theme",
            IsChecked = _themeService.CurrentTheme == ElementTheme.Dark
        };
        _lightRadio = new RadioButton
        {
            Content = "Light Theme",
            IsChecked = _themeService.CurrentTheme == ElementTheme.Light
        };

        _darkRadio.Checked += (_, _) =>
        {
            _themeService.SetTheme(ElementTheme.Dark, _rootElement);
            RequestedTheme = ElementTheme.Dark;
            _configService.Current.Theme = "dark";
            _configService.Save();
            UpdateThemeVisuals(true);
            if (_parentWindow is MainWindow mw)
            {
                foreach (var tab in mw.ViewModel.Tabs) tab.Refresh();
            }
        };

        _lightRadio.Checked += (_, _) =>
        {
            _themeService.SetTheme(ElementTheme.Light, _rootElement);
            RequestedTheme = ElementTheme.Light;
            _configService.Current.Theme = "light";
            _configService.Save();
            UpdateThemeVisuals(false);
            if (_parentWindow is MainWindow mw)
            {
                foreach (var tab in mw.ViewModel.Tabs) tab.Refresh();
            }
        };

        themeRow.Children.Add(_darkRadio);
        themeRow.Children.Add(_lightRadio);
        appearancePanel.Children.Add(themeRow);

        // Accent Colors
        _accentTitle = new TextBlock
        {
            Text = "Accent Color:",
            FontSize = 12,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Margin = new Thickness(0, 4, 0, 2)
        };
        appearancePanel.Children.Add(_accentTitle);

        var accentRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        accentRow.Children.Add(CreateAccentButton("blue", "#0078D4", "Blue"));
        accentRow.Children.Add(CreateAccentButton("green", "#10B981", "Green"));
        accentRow.Children.Add(CreateAccentButton("purple", "#8B5CF6", "Purple"));
        accentRow.Children.Add(CreateAccentButton("teal", "#14B8A6", "Teal"));
        accentRow.Children.Add(CreateAccentButton("orange", "#F97316", "Orange"));
        appearancePanel.Children.Add(accentRow);

        // Preview Window Font Size (preview panel only)
        _fontSizeTitle = new TextBlock
        {
            Text = "Preview Window Font Size (Preview panel only):",
            FontSize = 12,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Margin = new Thickness(0, 10, 0, 2)
        };
        _fontSizeHelp = new TextBlock
        {
            Text = "Adjust font size in points for the text editor and markdown preview panel (default: 13 pt).",
            FontSize = 11,
            Margin = new Thickness(0, 0, 0, 4)
        };
        appearancePanel.Children.Add(_fontSizeTitle);
        appearancePanel.Children.Add(_fontSizeHelp);

        var fontSizeControlRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center
        };

        _fontSizeBox = new NumberBox
        {
            Value = _configService.Current.EditorFontSize > 0 ? _configService.Current.EditorFontSize : 13,
            Minimum = 9,
            Maximum = 32,
            SmallChange = 1,
            LargeChange = 2,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline,
            Width = 110,
            FontSize = 12
        };

        _fontSizeSampleText = new TextBlock
        {
            Text = "AaBbCc 123 - Sample Preview",
            FontFamily = new FontFamily("Consolas, Cascadia Code, Courier New"),
            FontSize = _fontSizeBox.Value,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0)
        };

        _fontSizeBox.ValueChanged += (sender, _) =>
        {
            if (double.IsNaN(sender.Value) || sender.Value < 8 || sender.Value > 48)
                return;

            double newSize = Math.Round(sender.Value);
            _fontSizeSampleText.FontSize = newSize;
            _configService.Current.EditorFontSize = newSize;
            _configService.Save();

            if (_parentWindow is MainWindow mw)
            {
                foreach (var tab in mw.ViewModel.Tabs)
                {
                    tab.NotifyEditorFontSizeChanged();
                }
            }
        };

        fontSizeControlRow.Children.Add(_fontSizeBox);

        double[] presets = { 11, 12, 13, 14, 16, 18 };
        foreach (var p in presets)
        {
            var pBtn = new Button
            {
                Content = p == 13 ? "13 (Def)" : p.ToString(),
                Padding = new Thickness(7, 3, 7, 3),
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center
            };
            double val = p;
            pBtn.Click += (_, _) => _fontSizeBox.Value = val;
            fontSizeControlRow.Children.Add(pBtn);
        }

        fontSizeControlRow.Children.Add(_fontSizeSampleText);
        appearancePanel.Children.Add(fontSizeControlRow);

        rootStack.Children.Add(appearancePanel);

        // ==================== 2. EXTENSIONS SECTION ====================
        _extensionsHeader = CreateSectionHeader("📄 Quick Preview Extensions");
        rootStack.Children.Add(_extensionsHeader);

        _extHelp = new TextBlock
        {
            Text = "Files with these extensions appear in lists by default (even when 'Show all' is off) and preview automatically on selection in view-only mode until unlocked:",
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(8, 0, 0, 2)
        };
        rootStack.Children.Add(_extHelp);

        var chipsScroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Margin = new Thickness(8, 0, 0, 2)
        };
        _extensionsPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6
        };
        chipsScroll.Content = _extensionsPanel;
        rootStack.Children.Add(chipsScroll);

        // Add Extension row
        var addRow = new Grid
        {
            Margin = new Thickness(8, 2, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        addRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        addRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _addExtensionBox = new TextBox
        {
            PlaceholderText = "Add extension (e.g. .csv, .log, .py)...",
            FontSize = 12
        };
        Grid.SetColumn(_addExtensionBox, 0);

        _addBtn = new Button
        {
            Style = AccentButtonStyle,
            Content = "Add",
            Margin = new Thickness(10, 0, 0, 0),
            Padding = new Thickness(18, 5, 18, 5),
            FontSize = 12,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        };
        _addBtn.Click += (_, _) => AddExtension();
        _addExtensionBox.KeyDown += (_, ke) =>
        {
            if (ke.Key == Windows.System.VirtualKey.Enter)
            {
                AddExtension();
                ke.Handled = true;
            }
        };
        Grid.SetColumn(_addBtn, 1);

        addRow.Children.Add(_addExtensionBox);
        addRow.Children.Add(_addBtn);
        rootStack.Children.Add(addRow);

        // ==================== 3. SNAP LIST DEFAULTS ====================
        _snapHeader = CreateSectionHeader("📋 Snap List Cart Defaults");
        rootStack.Children.Add(_snapHeader);

        var snapPanel = new StackPanel { Spacing = 6, Margin = new Thickness(8, 0, 0, 0) };
        _fullPathCheck = new CheckBox
        {
            Content = "Include full path by default",
            IsChecked = _configService.Current.IncludeFullPath,
            FontSize = 12
        };
        _fullPathCheck.Click += (_, _) =>
        {
            _configService.Current.IncludeFullPath = _fullPathCheck.IsChecked == true;
            _configService.Save();
        };

        _extCheck = new CheckBox
        {
            Content = "Include extension by default",
            IsChecked = _configService.Current.IncludeExtension,
            FontSize = 12
        };
        _extCheck.Click += (_, _) =>
        {
            _configService.Current.IncludeExtension = _extCheck.IsChecked == true;
            _configService.Save();
        };

        snapPanel.Children.Add(_fullPathCheck);
        snapPanel.Children.Add(_extCheck);
        rootStack.Children.Add(snapPanel);

        // ==================== 4. ABOUT SECTION ====================
        _aboutHeader = CreateSectionHeader("ℹ️ About");
        rootStack.Children.Add(_aboutHeader);

        _aboutBorder = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(16, 12, 16, 12),
            Margin = new Thickness(8, 0, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        var aboutGrid = new Grid { HorizontalAlignment = HorizontalAlignment.Stretch };
        aboutGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        aboutGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var aboutTextStack = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        _aboutTitleText = new TextBlock
        {
            Text = AppConstants.AppTitle,
            FontSize = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold
        };
        _aboutCopyText = new TextBlock
        {
            Text = "© 2026 alpha-studio.it",
            FontSize = 11
        };
        aboutTextStack.Children.Add(_aboutTitleText);
        aboutTextStack.Children.Add(_aboutCopyText);
        Grid.SetColumn(aboutTextStack, 0);
        aboutGrid.Children.Add(aboutTextStack);

        _openAboutBtn = new Button
        {
            Style = AccentButtonStyle,
            Content = "Details",
            Margin = new Thickness(12, 0, 0, 0),
            Padding = new Thickness(20, 6, 20, 6),
            FontSize = 11,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        _openAboutBtn.Click += (_, _) =>
        {
            var aboutWin = new AboutWindow(_themeService, _parentWindow);
            aboutWin.Activate();
        };
        Grid.SetColumn(_openAboutBtn, 1);
        aboutGrid.Children.Add(_openAboutBtn);

        _aboutBorder.Child = aboutGrid;
        rootStack.Children.Add(_aboutBorder);

        scrollViewer.Content = rootStack;
        Content = scrollViewer;

        // Apply theme contrast and accent colors
        UpdateThemeVisuals(_themeService.CurrentTheme == ElementTheme.Dark);
        ApplyAccentToDialog();
    }

    private void UpdateThemeVisuals(bool isDark)
    {
        var primaryBrush = isDark
            ? new SolidColorBrush(Color.FromArgb(255, 245, 245, 245))
            : new SolidColorBrush(Color.FromArgb(255, 20, 20, 20));

        var secondaryBrush = isDark
            ? new SolidColorBrush(Color.FromArgb(255, 200, 200, 200))
            : new SolidColorBrush(Color.FromArgb(255, 95, 95, 95));

        var cardBg = isDark
            ? new SolidColorBrush(Color.FromArgb(255, 38, 38, 38))
            : new SolidColorBrush(Color.FromArgb(255, 245, 245, 245));

        var cardBorder = isDark
            ? new SolidColorBrush(Color.FromArgb(255, 65, 65, 65))
            : new SolidColorBrush(Color.FromArgb(255, 215, 215, 215));

        _appearanceHeader.Foreground = primaryBrush;
        _accentTitle.Foreground = primaryBrush;
        _fontSizeTitle.Foreground = primaryBrush;
        _fontSizeHelp.Foreground = secondaryBrush;
        _fontSizeSampleText.Foreground = primaryBrush;
        _fontSizeBox.Foreground = primaryBrush;
        _extensionsHeader.Foreground = primaryBrush;
        _extHelp.Foreground = secondaryBrush;
        _snapHeader.Foreground = primaryBrush;
        _aboutHeader.Foreground = primaryBrush;

        _darkRadio.Foreground = primaryBrush;
        _lightRadio.Foreground = primaryBrush;
        _fullPathCheck.Foreground = primaryBrush;
        _extCheck.Foreground = primaryBrush;

        _addExtensionBox.Foreground = primaryBrush;

        _aboutBorder.Background = cardBg;
        _aboutBorder.BorderBrush = cardBorder;
        _aboutTitleText.Foreground = primaryBrush;
        _aboutCopyText.Foreground = secondaryBrush;

        RefreshAccentSelectionRings();
        RebuildExtensionChips();
    }

    private void ApplyAccentToDialog()
    {
        var accentBrush = Application.Current.Resources["AppAccentBrush"] as SolidColorBrush
            ?? new SolidColorBrush(Color.FromArgb(255, 16, 185, 129));
        var hoverBrush = Application.Current.Resources["AppAccentHoverBrush"] as SolidColorBrush
            ?? new SolidColorBrush(Color.FromArgb(255, 5, 150, 105));

        // Override dialog-level resources for accent buttons & controls
        Resources["AccentButtonBackground"] = accentBrush;
        Resources["AccentButtonBackgroundPointerOver"] = hoverBrush;
        Resources["AccentButtonBackgroundPressed"] = hoverBrush;
        Resources["AccentButtonForeground"] = new SolidColorBrush(Colors.White);
        Resources["AccentButtonForegroundPointerOver"] = new SolidColorBrush(Colors.White);
        Resources["AccentButtonForegroundPressed"] = new SolidColorBrush(Colors.White);

        // CheckBox and RadioButton glyph / fill overrides in dialog
        Resources["CheckBoxCheckBackgroundFillChecked"] = accentBrush;
        Resources["CheckBoxCheckBackgroundFillCheckedPointerOver"] = hoverBrush;
        Resources["CheckBoxCheckBackgroundFillCheckedPressed"] = hoverBrush;
        Resources["CheckBoxCheckGlyphFillChecked"] = new SolidColorBrush(Colors.White);
        Resources["RadioButtonCheckGlyphFillChecked"] = accentBrush;
        Resources["RadioButtonCheckGlyphFillCheckedPointerOver"] = hoverBrush;
        Resources["RadioButtonCheckGlyphFillCheckedPressed"] = hoverBrush;

        // Style the ContentDialog PrimaryButton ("Close"):
        // Right-aligned, active accent color, crisp white text
        var primaryStyle = new Style(typeof(Button)) { BasedOn = AccentButtonStyle };
        primaryStyle.Setters.Add(new Setter(Button.BackgroundProperty, accentBrush));
        primaryStyle.Setters.Add(new Setter(Button.ForegroundProperty, new SolidColorBrush(Colors.White)));
        primaryStyle.Setters.Add(new Setter(Button.CornerRadiusProperty, new CornerRadius(4)));
        primaryStyle.Setters.Add(new Setter(Button.PaddingProperty, new Thickness(28, 6, 28, 6)));
        primaryStyle.Setters.Add(new Setter(Button.MinWidthProperty, 110.0));
        primaryStyle.Setters.Add(new Setter(Button.FontWeightProperty, Microsoft.UI.Text.FontWeights.SemiBold));
        primaryStyle.Setters.Add(new Setter(Button.HorizontalAlignmentProperty, HorizontalAlignment.Right));
        primaryStyle.Setters.Add(new Setter(Button.MarginProperty, new Thickness(0, 0, 16, 0)));
        PrimaryButtonStyle = primaryStyle;

        // Directly color Add and Details buttons with active accent
        if (_addBtn != null) _addBtn.Background = accentBrush;
        if (_openAboutBtn != null) _openAboutBtn.Background = accentBrush;
    }

    private static TextBlock CreateSectionHeader(string text)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Margin = new Thickness(0, 6, 0, 2)
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
            Style = AccentButtonStyle,
            Width = 32,
            Height = 32,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(16),
            Background = new SolidColorBrush(color),
            BorderBrush = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            Tag = key
        };
        ToolTipService.SetToolTip(btn, name);

        _accentButtons.Add(btn);
        RefreshAccentSelectionRings();

        btn.Click += (_, _) =>
        {
            _themeService.SetAccentColor(key);
            _configService.Current.DefaultColorTheme = key;
            _configService.Save();

            // Refresh selection rings on accent buttons
            RefreshAccentSelectionRings();

            // Immediately refresh Close and dialog buttons
            ApplyAccentToDialog();
        };

        return btn;
    }

    /// <summary>
    /// Shared accent button style (App.xaml) with consistent hover/pressed feedback in both themes.
    /// </summary>
    private static Style? AccentButtonStyle =>
        Application.Current.Resources.TryGetValue("AppAccentButtonStyle", out var s) ? s as Style : null;

    /// <summary>
    /// Highlights the active accent swatch with a ring that contrasts with the current theme
    /// (white ring on Dark, near-black ring on Light).
    /// </summary>
    private void RefreshAccentSelectionRings()
    {
        var ringColor = _themeService.CurrentTheme == ElementTheme.Light
            ? Color.FromArgb(255, 0x1F, 0x23, 0x28)
            : Colors.White;

        foreach (var b in _accentButtons)
        {
            bool active = string.Equals(b.Tag as string, _themeService.CurrentAccent, StringComparison.OrdinalIgnoreCase);
            b.BorderBrush = new SolidColorBrush(active ? ringColor : Colors.Transparent);
            b.BorderThickness = active ? new Thickness(2) : new Thickness(0);
        }
    }

    private void RebuildExtensionChips()
    {
        _extensionsPanel.Children.Clear();

        bool isDark = _themeService.CurrentTheme == ElementTheme.Dark;
        var chipBg = isDark ? new SolidColorBrush(Color.FromArgb(255, 38, 38, 38)) : new SolidColorBrush(Color.FromArgb(255, 235, 235, 235));
        var chipBorder = isDark ? new SolidColorBrush(Color.FromArgb(255, 65, 65, 65)) : new SolidColorBrush(Color.FromArgb(255, 205, 205, 205));
        var chipTextBrush = isDark ? new SolidColorBrush(Color.FromArgb(255, 245, 245, 245)) : new SolidColorBrush(Color.FromArgb(255, 25, 25, 25));
        var chipRemoveBrush = isDark ? new SolidColorBrush(Color.FromArgb(255, 180, 180, 180)) : new SolidColorBrush(Color.FromArgb(255, 110, 110, 110));

        var exts = _configService.Current.EditorExtensions;
        foreach (var ext in exts)
        {
            var chip = new Border
            {
                Background = chipBg,
                BorderBrush = chipBorder,
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
                Foreground = chipTextBrush,
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
            removeBtn.Content = new FontIcon
            {
                Glyph = "\uE711",
                FontSize = 8,
                Foreground = chipRemoveBrush
            };
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