using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using DirSnap2.Services;

namespace DirSnap2;

public partial class App : Application
{
    private Window? _window;
    public static Window? MainAppWindow { get; private set; }

    public App()
    {
        AppLogger.Initialize();
        UnhandledException += App_UnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
        InitializeComponent();
    }

    private void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        AppLogger.Fatal($"[UI UnhandledException] Message={e.Message}", e.Exception);
        WriteCrashDump("UI UnhandledException", e.Exception ?? new Exception(e.Message));
    }

    private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        AppLogger.Error("[TaskScheduler UnobservedTaskException]", e.Exception);
        e.SetObserved();
    }

    private void CurrentDomain_UnhandledException(object sender, System.UnhandledExceptionEventArgs e)
    {
        var ex = e.ExceptionObject as Exception ?? new Exception(e.ExceptionObject?.ToString());
        AppLogger.Fatal($"[Fatal CurrentDomain Exception] IsTerminating={e.IsTerminating}", ex);
        WriteCrashDump("Fatal CurrentDomain Exception", ex);
        ShowFatalMessage(ex);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

    private static void WriteCrashDump(string category, Exception ex)
    {
        try
        {
            var logPath = Path.Combine(AppContext.BaseDirectory, "crash.log");
            File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{category}]\n{ex}\n\n");
        }
        catch
        {
            // Ignore failure writing crash dump file
        }
    }

    private static void ShowFatalMessage(Exception ex)
    {
        try
        {
            WriteCrashDump("ShowFatalMessage", ex);

            var detail = $"{ex.GetType().Name}: {ex.Message}\n\n{ex.StackTrace}";
            if (detail.Length > 800) detail = detail.Substring(0, 800) + "...";

            MessageBoxW(IntPtr.Zero,
                $"{AppConstants.AppName} encountered an unexpected error and must close.\n\n{detail}\n\nA log has been saved to:\n{AppLogger.LogFilePath}",
                $"{AppConstants.AppName} - Fatal Error", 0x10 /* MB_ICONERROR */);
        }
        catch
        {
            // Fallback when dialog cannot be displayed
        }
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            AppLogger.Info("App.OnLaunched starting...");
            var window = new MainWindow();
            _window = window;
            MainAppWindow = _window;
            _window.Activate();
            AppLogger.Info("MainWindow activated successfully.");
        }
        catch (Exception ex)
        {
            AppLogger.Fatal("App.OnLaunched fatal exception", ex);
            ShowFatalMessage(ex);
            throw;
        }
    }
}
