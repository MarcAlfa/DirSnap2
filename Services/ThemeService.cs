using System.Diagnostics;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace DirSnap2.Services;

public interface IThemeService
{
    ElementTheme CurrentTheme { get; }
    string CurrentAccent { get; }
    void Initialize(FrameworkElement rootElement, string? savedTheme, string? savedAccent);
    void SetTheme(ElementTheme theme, FrameworkElement rootElement);
    void SetAccentColor(string accentName);
}

public sealed class ThemeService : IThemeService
{
    public static ElementTheme ActiveTheme { get; set; } = ElementTheme.Dark;

    public ElementTheme CurrentTheme { get; private set; } = ElementTheme.Dark;
    public string CurrentAccent { get; private set; } = "blue";

    private static readonly Dictionary<string, (Color Primary, Color Hover, Color Subtle)> AccentColors =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["blue"] = (
                Color.FromArgb(255, 0, 120, 212),       // #0078D4
                Color.FromArgb(255, 16, 110, 190),      // #106EBE
                Color.FromArgb(38, 0, 120, 212)         // 15% opacity
            ),
            ["green"] = (
                Color.FromArgb(255, 16, 185, 129),      // #10B981
                Color.FromArgb(255, 5, 150, 105),       // #059669
                Color.FromArgb(38, 16, 185, 129)
            ),
            ["purple"] = (
                Color.FromArgb(255, 139, 92, 246),      // #8B5CF6
                Color.FromArgb(255, 124, 58, 237),      // #7C3AED
                Color.FromArgb(38, 139, 92, 246)
            ),
            ["teal"] = (
                Color.FromArgb(255, 20, 184, 166),      // #14B8A6
                Color.FromArgb(255, 13, 148, 136),      // #0D9488
                Color.FromArgb(38, 20, 184, 166)
            ),
            ["orange"] = (
                Color.FromArgb(255, 249, 115, 22),      // #F97316
                Color.FromArgb(255, 234, 88, 12),       // #EA580C
                Color.FromArgb(38, 249, 115, 22)
            )
        };

    public void Initialize(FrameworkElement rootElement, string? savedTheme, string? savedAccent)
    {
        var theme = savedTheme?.ToLowerInvariant() switch
        {
            "light" => ElementTheme.Light,
            _ => ElementTheme.Dark
        };

        var accent = !string.IsNullOrWhiteSpace(savedAccent) && AccentColors.ContainsKey(savedAccent)
            ? savedAccent.ToLowerInvariant()
            : "blue";

        SetTheme(theme, rootElement);
        SetAccentColor(accent);
    }

    public void SetTheme(ElementTheme theme, FrameworkElement rootElement)
    {
        CurrentTheme = theme;
        ActiveTheme = theme;
        if (rootElement != null)
        {
            rootElement.RequestedTheme = theme;
        }

        // Apply theme-specific button and surface brushes dynamically
        ApplyThemeResources(theme);
    }

    private void ApplyThemeResources(ElementTheme theme)
    {
        var resources = Application.Current.Resources;
        bool isLight = theme == ElementTheme.Light;

        if (isLight)
        {
            // Visible light gray hover tint (#E2E5EA), pressed (#D5D9E0), dark text (#1F2328)
            var btnBgPointerOver = Color.FromArgb(255, 0xE2, 0xE5, 0xEA);
            var btnBgPressed = Color.FromArgb(255, 0xD5, 0xD9, 0xE0);
            var btnBorderPointerOver = Color.FromArgb(255, 0xB8, 0xBF, 0xC9);
            var textPrimary = Color.FromArgb(255, 0x1F, 0x23, 0x28);

            UpdateBrush(resources, "ButtonBackgroundPointerOver", btnBgPointerOver);
            UpdateBrush(resources, "ButtonBackgroundPressed", btnBgPressed);
            UpdateBrush(resources, "ButtonForegroundPointerOver", textPrimary);
            UpdateBrush(resources, "ButtonForegroundPressed", textPrimary);
            UpdateBrush(resources, "ButtonBorderBrushPointerOver", btnBorderPointerOver);

            UpdateBrush(resources, "ToggleButtonBackgroundPointerOver", btnBgPointerOver);
            UpdateBrush(resources, "ToggleButtonBackgroundPressed", btnBgPressed);
            UpdateBrush(resources, "ToggleButtonForegroundPointerOver", textPrimary);
            UpdateBrush(resources, "ToggleButtonForegroundPressed", textPrimary);
            UpdateBrush(resources, "ToggleButtonBorderBrushPointerOver", btnBorderPointerOver);

            UpdateBrush(resources, "ControlFillColorSecondaryBrush", btnBgPointerOver);
            UpdateBrush(resources, "ControlFillColorTertiaryBrush", btnBgPressed);
            UpdateBrush(resources, "SubtleFillColorSecondaryBrush", Color.FromArgb(0x1F, 0x00, 0x00, 0x00));
            UpdateBrush(resources, "SubtleFillColorTertiaryBrush", Color.FromArgb(0x2E, 0x00, 0x00, 0x00));
        }
        else
        {
            // Dark mode standard brushes
            var darkBtnBgPointerOver = Color.FromArgb(255, 0x38, 0x38, 0x38);
            var darkBtnBgPressed = Color.FromArgb(255, 0x2E, 0x2E, 0x2E);
            var darkBtnBorder = Color.FromArgb(255, 0x55, 0x55, 0x55);
            var textWhite = Colors.White;

            UpdateBrush(resources, "ButtonBackgroundPointerOver", darkBtnBgPointerOver);
            UpdateBrush(resources, "ButtonBackgroundPressed", darkBtnBgPressed);
            UpdateBrush(resources, "ButtonForegroundPointerOver", textWhite);
            UpdateBrush(resources, "ButtonForegroundPressed", textWhite);
            UpdateBrush(resources, "ButtonBorderBrushPointerOver", darkBtnBorder);

            UpdateBrush(resources, "ToggleButtonBackgroundPointerOver", darkBtnBgPointerOver);
            UpdateBrush(resources, "ToggleButtonBackgroundPressed", darkBtnBgPressed);
            UpdateBrush(resources, "ToggleButtonForegroundPointerOver", textWhite);
            UpdateBrush(resources, "ToggleButtonForegroundPressed", textWhite);
            UpdateBrush(resources, "ToggleButtonBorderBrushPointerOver", darkBtnBorder);

            UpdateBrush(resources, "ControlFillColorSecondaryBrush", Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF));
            UpdateBrush(resources, "ControlFillColorTertiaryBrush", Color.FromArgb(0x12, 0xFF, 0xFF, 0xFF));
            UpdateBrush(resources, "SubtleFillColorSecondaryBrush", Color.FromArgb(0x15, 0xFF, 0xFF, 0xFF));
            UpdateBrush(resources, "SubtleFillColorTertiaryBrush", Color.FromArgb(0x0F, 0xFF, 0xFF, 0xFF));
        }

        // Also ensure theme dictionaries contain these keys directly
        if (resources.ThemeDictionaries != null)
        {
            foreach (var dictKey in resources.ThemeDictionaries.Keys)
            {
                try
                {
                    if (resources.ThemeDictionaries[dictKey] is ResourceDictionary themeDict)
                    {
                        string dictKeyStr = dictKey?.ToString() ?? "";
                        bool isDictLight = dictKeyStr.Equals("Light", StringComparison.OrdinalIgnoreCase);
                        if (isDictLight)
                        {
                            var btnBgPointerOver = Color.FromArgb(255, 0xE2, 0xE5, 0xEA);
                            var btnBgPressed = Color.FromArgb(255, 0xD5, 0xD9, 0xE0);
                            var btnBorderPointerOver = Color.FromArgb(255, 0xB8, 0xBF, 0xC9);
                            var textPrimary = Color.FromArgb(255, 0x1F, 0x23, 0x28);

                            UpdateBrush(themeDict, "ButtonBackgroundPointerOver", btnBgPointerOver);
                            UpdateBrush(themeDict, "ButtonBackgroundPressed", btnBgPressed);
                            UpdateBrush(themeDict, "ButtonForegroundPointerOver", textPrimary);
                            UpdateBrush(themeDict, "ButtonForegroundPressed", textPrimary);
                            UpdateBrush(themeDict, "ButtonBorderBrushPointerOver", btnBorderPointerOver);

                            UpdateBrush(themeDict, "ToggleButtonBackgroundPointerOver", btnBgPointerOver);
                            UpdateBrush(themeDict, "ToggleButtonBackgroundPressed", btnBgPressed);
                            UpdateBrush(themeDict, "ToggleButtonForegroundPointerOver", textPrimary);
                            UpdateBrush(themeDict, "ToggleButtonForegroundPressed", textPrimary);
                            UpdateBrush(themeDict, "ToggleButtonBorderBrushPointerOver", btnBorderPointerOver);

                            UpdateBrush(themeDict, "ControlFillColorSecondaryBrush", btnBgPointerOver);
                            UpdateBrush(themeDict, "ControlFillColorTertiaryBrush", btnBgPressed);
                            UpdateBrush(themeDict, "SubtleFillColorSecondaryBrush", Color.FromArgb(0x1F, 0x00, 0x00, 0x00));
                            UpdateBrush(themeDict, "SubtleFillColorTertiaryBrush", Color.FromArgb(0x2E, 0x00, 0x00, 0x00));
                        }
                    }
                }
                catch { }
            }
        }
    }

    public void SetAccentColor(string accentName)
    {
        if (!AccentColors.TryGetValue(accentName, out var colors))
        {
            accentName = "blue";
            colors = AccentColors["blue"];
        }

        CurrentAccent = accentName;

        var resources = Application.Current.Resources;
        UpdateBrush(resources, "AppAccentBrush", colors.Primary);
        UpdateBrush(resources, "AppAccentHoverBrush", colors.Hover);
        UpdateBrush(resources, "AppAccentSubtleBrush", colors.Subtle);

        UpdateBrush(resources, "AccentButtonBackground", colors.Primary);
        UpdateBrush(resources, "AccentButtonBackgroundPointerOver", colors.Hover);
        UpdateBrush(resources, "AccentButtonBackgroundPressed", colors.Hover);
        UpdateBrush(resources, "AccentButtonForeground", Colors.White);
        UpdateBrush(resources, "AccentButtonForegroundPointerOver", Colors.White);
        UpdateBrush(resources, "AccentButtonForegroundPressed", Colors.White);

        // CheckBox and RadioButton glyph / fill overrides
        UpdateBrush(resources, "CheckBoxCheckBackgroundFillChecked", colors.Primary);
        UpdateBrush(resources, "CheckBoxCheckBackgroundFillCheckedPointerOver", colors.Hover);
        UpdateBrush(resources, "CheckBoxCheckBackgroundFillCheckedPressed", colors.Hover);
        UpdateBrush(resources, "RadioButtonCheckGlyphFillChecked", colors.Primary);
        UpdateBrush(resources, "RadioButtonCheckGlyphFillCheckedPointerOver", colors.Hover);
        UpdateBrush(resources, "RadioButtonCheckGlyphFillCheckedPressed", colors.Hover);

        // TabView active tab accent highlights
        UpdateBrush(resources, "TabViewItemHeaderBackgroundSelected", colors.Subtle);
        UpdateBrush(resources, "TabViewItemHeaderForegroundSelected", colors.Primary);
        UpdateBrush(resources, "TabViewItemStrokeSelected", colors.Primary);

        // Also update all ThemeDictionaries (Dark, Light, Default) to prevent theme overrides
        if (resources.ThemeDictionaries != null)
        {
            foreach (var dictKey in resources.ThemeDictionaries.Keys)
            {
                try
                {
                    if (resources.ThemeDictionaries[dictKey] is ResourceDictionary themeDict)
                    {
                        UpdateBrush(themeDict, "AppAccentBrush", colors.Primary);
                        UpdateBrush(themeDict, "AppAccentHoverBrush", colors.Hover);
                        UpdateBrush(themeDict, "AppAccentSubtleBrush", colors.Subtle);
                        UpdateBrush(themeDict, "AccentButtonBackground", colors.Primary);
                        UpdateBrush(themeDict, "AccentButtonBackgroundPointerOver", colors.Hover);
                        UpdateBrush(themeDict, "AccentButtonBackgroundPressed", colors.Hover);
                        UpdateBrush(themeDict, "AccentButtonForeground", Colors.White);
                        UpdateBrush(themeDict, "CheckBoxCheckBackgroundFillChecked", colors.Primary);
                        UpdateBrush(themeDict, "CheckBoxCheckBackgroundFillCheckedPointerOver", colors.Hover);
                        UpdateBrush(themeDict, "CheckBoxCheckBackgroundFillCheckedPressed", colors.Hover);
                        UpdateBrush(themeDict, "RadioButtonCheckGlyphFillChecked", colors.Primary);
                        UpdateBrush(themeDict, "RadioButtonCheckGlyphFillCheckedPointerOver", colors.Hover);
                        UpdateBrush(themeDict, "RadioButtonCheckGlyphFillCheckedPressed", colors.Hover);
                        UpdateBrush(themeDict, "TabViewItemHeaderBackgroundSelected", colors.Subtle);
                        UpdateBrush(themeDict, "TabViewItemHeaderForegroundSelected", colors.Primary);
                        UpdateBrush(themeDict, "TabViewItemStrokeSelected", colors.Primary);
                    }
                }
                catch
                {
                    // Ignore inaccessible or sealed theme dictionary
                }
            }
        }
    }

    private static void UpdateBrush(ResourceDictionary resources, string key, Color color)
    {
        try
        {
            // ALWAYS assign a new SolidColorBrush instance to avoid mutating
            // shared system brushes (e.g. TextFillColorPrimaryBrush) in XamlControlsResources
            resources[key] = new SolidColorBrush(color);
        }
        catch (Exception ex)
        {
            AppLogger.DebugLog($"[ThemeService] Skipping read-only resource '{key}': {ex.Message}");
        }
    }
}
