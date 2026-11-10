using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;

namespace DirSnap2;

public partial class App : Application
{
    private Window? _window;
    public static Window? MainAppWindow { get; private set; }

    public App()
    {
        UnhandledException += App_UnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
        InitializeComponent();
    }

    private void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        Debug.WriteLine($"[UI Exception] {e.Exception?.Message ?? e.Message}");
    }

    private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Debug.WriteLine($"[Task Exception] {e.Exception?.Message}");
        e.SetObserved();
    }

    private void CurrentDomain_UnhandledException(object sender, System.UnhandledExceptionEventArgs e)
    {
        var ex = e.ExceptionObject as Exception ?? new Exception(e.ExceptionObject?.ToString());
        Debug.WriteLine($"[Fatal Exception] {ex.Message}");
        ShowFatalMessage(ex);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

    private static void ShowFatalMessage(Exception ex)
    {
        try
        {
            var logPath = Path.Combine(AppContext.BaseDirectory, "crash.log");
            File.WriteAllText(logPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\n{ex}\n");

            var detail = $"{ex.GetType().Name}: {ex.Message}\n\n{ex.StackTrace}";
            if (detail.Length > 800) detail = detail.Substring(0, 800) + "...";

            MessageBoxW(IntPtr.Zero,
                $"{AppConstants.AppName} encountered an unexpected error and must close.\n\n{detail}",
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
            var window = new MainWindow();
            _window = window;
            MainAppWindow = _window;
            _window.Activate();
        }
        catch (Exception ex)
        {
            ShowFatalMessage(ex);
            throw;
        }
    }
}
