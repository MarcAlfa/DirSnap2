using System.IO;
using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.UI;
using WinRT.Interop;

namespace DirSnap2.Services;

/// <summary>
/// Caption buttons (minimize / maximize / close) and window/taskbar icon helpers
/// for windows that draw their own title bar.
/// </summary>
public static class TitleBarHelper
{
    private const int WM_SETICON = 0x0080;
    private const int ICON_SMALL = 0;
    private const int ICON_BIG = 1;
    private const uint IMAGE_ICON = 1;
    private const uint LR_LOADFROMFILE = 0x00000010;
    private const int SM_CXICON = 11;
    private const int SM_CYICON = 12;
    private const int SM_CXSMICON = 49;
    private const int SM_CYSMICON = 50;
    private const int GCLP_HICON = -14;
    private const int GCLP_HICONSM = -34;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadImageW(IntPtr hInst, string name, uint type, int cx, int cy, uint fuLoad);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetClassLongPtrW")]
    private static extern IntPtr SetClassLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconExW(string lpszFile, int nIconIndex, out IntPtr phiconLarge, out IntPtr phiconSmall, uint nIcons);

    /// <summary>Finds the application .ico path, checking assets\app_icon.ico and root app_icon.ico.</summary>
    public static string? ResolveAppIconPath()
    {
        var assetsPath = Path.Combine(AppContext.BaseDirectory, "assets", "app_icon.ico");
        if (File.Exists(assetsPath))
            return assetsPath;

        var rootPath = Path.Combine(AppContext.BaseDirectory, "app_icon.ico");
        return File.Exists(rootPath) ? rootPath : null;
    }

    /// <summary>
    /// Sets both the WinUI AppWindow icon and the Win32 HWND/class icons (WM_SETICON)
    /// so the Windows 11 taskbar and Alt+Tab switcher display the icon reliably on unpackaged builds.
    /// </summary>
    public static void ApplyWindowIcon(Window window, AppWindow appWindow)
    {
        try
        {
            var icoPath = ResolveAppIconPath();
            if (icoPath != null)
            {
                appWindow.SetIcon(icoPath);
            }

            var hwnd = WindowNative.GetWindowHandle(window);
            if (hwnd == IntPtr.Zero)
                return;

            IntPtr hSmall = IntPtr.Zero;
            IntPtr hBig = IntPtr.Zero;

            if (icoPath != null)
            {
                int smX = Math.Max(16, GetSystemMetrics(SM_CXSMICON));
                int smY = Math.Max(16, GetSystemMetrics(SM_CYSMICON));
                int lgX = Math.Max(32, GetSystemMetrics(SM_CXICON));
                int lgY = Math.Max(32, GetSystemMetrics(SM_CYICON));

                hSmall = LoadImageW(IntPtr.Zero, icoPath, IMAGE_ICON, smX, smY, LR_LOADFROMFILE);
                hBig = LoadImageW(IntPtr.Zero, icoPath, IMAGE_ICON, lgX, lgY, LR_LOADFROMFILE);
            }

            if ((hSmall == IntPtr.Zero || hBig == IntPtr.Zero) && Environment.ProcessPath is { } exePath && File.Exists(exePath))
            {
                ExtractIconExW(exePath, 0, out var exeBig, out var exeSmall, 1);
                if (hBig == IntPtr.Zero) hBig = exeBig;
                if (hSmall == IntPtr.Zero) hSmall = exeSmall;
            }

            if (hSmall != IntPtr.Zero)
            {
                SendMessage(hwnd, WM_SETICON, (IntPtr)ICON_SMALL, hSmall);
                SetClassLongPtr(hwnd, GCLP_HICONSM, hSmall);
            }

            if (hBig != IntPtr.Zero)
            {
                SendMessage(hwnd, WM_SETICON, (IntPtr)ICON_BIG, hBig);
                SetClassLongPtr(hwnd, GCLP_HICON, hBig);
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"[TitleBarHelper] Window icon: {ex.Message}");
        }
    }
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
