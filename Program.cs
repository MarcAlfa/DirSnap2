using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace DirSnap2;

public static class Program
{
    private const string MutexName = "Local\\DirSnap2_SingleInstance_Mutex_v1";
    private static Mutex? _singleInstanceMutex;

    [STAThread]
    public static void Main(string[] args)
    {
        bool isFirstInstance;
        try
        {
            _singleInstanceMutex = new Mutex(true, MutexName, out isFirstInstance);
        }
        catch (AbandonedMutexException)
        {
            // Previous instance terminated unexpectedly without releasing mutex
            isFirstInstance = true;
        }

        if (!isFirstInstance)
        {
            // Another instance of DirSnap 2 is already running.
            // Bring the existing instance window to front and exit immediately.
            BringExistingInstanceToFront();
            return;
        }

        global::WinRT.ComWrappersSupport.InitializeComWrappers();
        global::Microsoft.UI.Xaml.Application.Start((p) =>
        {
            var context = new global::Microsoft.UI.Dispatching.DispatcherQueueSynchronizationContext(
                global::Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());
            global::System.Threading.SynchronizationContext.SetSynchronizationContext(context);
            new App();
        });

        // Ensure the mutex is not garbage collected while the application is running
        if (_singleInstanceMutex != null)
        {
            GC.KeepAlive(_singleInstanceMutex);
            try
            {
                _singleInstanceMutex.ReleaseMutex();
                _singleInstanceMutex.Dispose();
            }
            catch
            {
                // Ignored on application shutdown
            }
        }
    }

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    private const int SW_RESTORE = 9;
    private const int SW_SHOW = 5;

    private static void BringExistingInstanceToFront()
    {
        try
        {
            var currentProcess = Process.GetCurrentProcess();
            var processes = Process.GetProcessesByName(currentProcess.ProcessName);
            var otherProcesses = processes.Where(p => p.Id != currentProcess.Id).ToList();

            foreach (var proc in otherProcesses)
            {
                EnumWindows((hWnd, lParam) =>
                {
                    GetWindowThreadProcessId(hWnd, out uint procId);
                    if (procId == proc.Id && IsWindowVisible(hWnd))
                    {
                        var sb = new StringBuilder(256);
                        GetWindowText(hWnd, sb, 256);
                        string title = sb.ToString();

                        // Match main window by title containing "DirSnap"
                        if (title.Contains("DirSnap") || !string.IsNullOrWhiteSpace(title))
                        {
                            if (IsIconic(hWnd))
                            {
                                ShowWindow(hWnd, SW_RESTORE);
                            }
                            else
                            {
                                ShowWindow(hWnd, SW_SHOW);
                            }
                            SetForegroundWindow(hWnd);
                            return false; // Stop enumeration once main window is activated
                        }
                    }
                    return true;
                }, IntPtr.Zero);
            }
        }
        catch
        {
            // Ignore failure activating window
        }
    }
}
