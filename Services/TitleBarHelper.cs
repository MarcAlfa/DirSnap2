using Microsoft.UI;
using Microsoft.UI.Windowing;
using Windows.UI;

namespace DirSnap2.Services;

/// <summary>
/// Caption buttons (minimize / maximize / close) of the windows that draw their own title bar:
/// transparent, following the app theme, with a soft hover like Windows 11.
/// </summary>
public static class TitleBarHelper
{
    public static void ApplyCaptionColors(AppWindow appWindow, bool dark)
    {
        try
        {
            var tb = appWindow.TitleBar;
            var fg = dark ? Colors.White : Color.FromArgb(255, 0x1F, 0x23, 0x28);
            var inactiveFg = dark ? Color.FromArgb(255, 0x8A, 0x8A, 0x8A) : Color.FromArgb(255, 0x8A, 0x8F, 0x98);

            tb.ButtonBackgroundColor = Colors.Transparent;
            tb.ButtonInactiveBackgroundColor = Colors.Transparent;
            tb.ButtonForegroundColor = fg;
            tb.ButtonInactiveForegroundColor = inactiveFg;
            tb.ButtonHoverBackgroundColor = dark ? Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x14, 0x00, 0x00, 0x00);
            tb.ButtonHoverForegroundColor = fg;
            tb.ButtonPressedBackgroundColor = dark ? Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x24, 0x00, 0x00, 0x00);
            tb.ButtonPressedForegroundColor = fg;
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"[TitleBarHelper] Caption colors: {ex.Message}");
        }
    }
}
