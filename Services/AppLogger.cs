using System.Diagnostics;
using System.IO;

namespace DirSnap2.Services;

/// <summary>
/// Thread-safe file and debug logger for DirSnap 2.
/// Writes to logs/dirsnap2.log with automatic fallback to %LOCALAPPDATA%/DirSnap2/logs/dirsnap2.log.
/// </summary>
public static class AppLogger
{
    private static readonly object Lock = new();
    private static string? _logFilePath;

    public static string LogFilePath
    {
        get
        {
            if (_logFilePath == null)
            {
                lock (Lock)
                {
                    _logFilePath ??= DetermineLogFilePath();
                }
            }
            return _logFilePath;
        }
    }

    private static string DetermineLogFilePath()
    {
        try
        {
            var baseDir = AppContext.BaseDirectory;
            var dir = Path.Combine(baseDir, "logs");
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            var path = Path.Combine(dir, "dirsnap2.log");
            RotateIfLarge(path);
            // Test write
            File.AppendAllText(path, string.Empty);
            return path;
        }
        catch
        {
            try
            {
                var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var dir = Path.Combine(localAppData, "DirSnap2", "logs");
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                var path = Path.Combine(dir, "dirsnap2.log");
                RotateIfLarge(path);
                return path;
            }
            catch
            {
                return Path.Combine(Path.GetTempPath(), "dirsnap2.log");
            }
        }
    }

    private const long MaxLogBytes = 2 * 1024 * 1024;

    /// <summary>
    /// At startup a log over 2 MB becomes dirsnap2.old.log (replacing the previous one) and a new
    /// log starts: at most ~4 MB on disk, and the last session before the rotation is still there.
    /// </summary>
    private static void RotateIfLarge(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length <= MaxLogBytes) return;
            var old = Path.Combine(info.DirectoryName ?? string.Empty, "dirsnap2.old.log");
            File.Move(path, old, overwrite: true);
        }
        catch
        {
            // Another instance or an editor may hold the file: keep appending to it
        }
    }

    public static void Initialize()
    {
        Info($"=== DirSnap 2 v{AppConstants.AppVersion} Logger Initialized ===");
        Info($"Log File: {LogFilePath}");
        Info($"Working Directory: {Environment.CurrentDirectory}");
        Info($"Base Directory: {AppContext.BaseDirectory}");
        Info($"OS Version: {Environment.OSVersion}");
    }

    public static void Info(string message) => Log("INFO", message);
    public static void Warn(string message) => Log("WARN", message);
    public static void DebugLog(string message) => Log("DEBUG", message);

    public static void Error(string message, Exception? ex = null)
    {
        var msg = ex != null ? $"{message} | Exception: {ex.GetType().FullName}: {ex.Message}\n{ex.StackTrace}" : message;
        Log("ERROR", msg);
    }

    public static void Fatal(string message, Exception? ex = null)
    {
        var msg = ex != null ? $"{message} | Exception: {ex.GetType().FullName}: {ex.Message}\n{ex.StackTrace}" : message;
        Log("FATAL", msg);
    }

    private static void Log(string level, string message)
    {
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        var threadId = Environment.CurrentManagedThreadId;
        var entry = $"[{timestamp}] [{level,-5}] [Thread {threadId}] {message}";

        Debug.WriteLine(entry);

        lock (Lock)
        {
            try
            {
                var path = LogFilePath;
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                using var writer = new StreamWriter(stream);
                writer.WriteLine(entry);
                writer.Flush();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AppLogger] Failed to write log: {ex.Message}");
            }
        }
    }
}
