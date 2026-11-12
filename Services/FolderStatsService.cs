using System.IO;

namespace DirSnap2.Services;

/// <summary>
/// Totals of a folder (size, files and subfolders at every level) for the Folder info window.
/// Junctions/symbolic links are counted but not followed (no loops, no double counting).
/// </summary>
public static class FolderStatsService
{
    public sealed class Stats
    {
        public long TotalBytes;
        public int Files;
        public int Folders;
        public int DirectFiles;
        public int DirectFolders;
        public int Unreadable;
        public bool Cancelled;
        /// <summary>Most recently modified file at any level (null when the folder has no files).</summary>
        public string? NewestFile;
        public DateTime NewestFileTime;
    }

    /// <param name="onProgress">Called now and then (worker thread) with a snapshot of the partial totals.</param>
    public static Stats Compute(string root, Action<Stats> onProgress, CancellationToken ct)
    {
        var stats = new Stats();
        var options = new EnumerationOptions
        {
            IgnoreInaccessible = false,
            RecurseSubdirectories = false,
            AttributesToSkip = 0, // hidden and system items take space too
        };
        long lastProgress = Environment.TickCount64;

        var pending = new Stack<string>();
        pending.Push(root);
        bool isRoot = true;

        while (pending.Count > 0)
        {
            if (ct.IsCancellationRequested)
            {
                stats.Cancelled = true;
                break;
            }

            var dir = pending.Pop();
            try
            {
                foreach (var entry in new DirectoryInfo(dir).EnumerateFileSystemInfos("*", options))
                {
                    if (entry is DirectoryInfo sub)
                    {
                        stats.Folders++;
                        if (isRoot) stats.DirectFolders++;
                        if ((sub.Attributes & FileAttributes.ReparsePoint) == 0)
                            pending.Push(sub.FullName);
                    }
                    else if (entry is FileInfo file)
                    {
                        stats.Files++;
                        if (isRoot) stats.DirectFiles++;
                        try { stats.TotalBytes += file.Length; } catch { }
                        try
                        {
                            // Read from the same directory listing: no extra disk access
                            var time = file.LastWriteTime;
                            if (time > stats.NewestFileTime)
                            {
                                stats.NewestFileTime = time;
                                stats.NewestFile = file.FullName;
                            }
                        }
                        catch { }
                    }
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
            {
                stats.Unreadable++;
            }
            isRoot = false;

            if (Environment.TickCount64 - lastProgress > 150)
            {
                lastProgress = Environment.TickCount64;
                onProgress(Snapshot(stats));
            }
        }
        return stats;
    }

    private static Stats Snapshot(Stats s) => new()
    {
        TotalBytes = s.TotalBytes,
        Files = s.Files,
        Folders = s.Folders,
        DirectFiles = s.DirectFiles,
        DirectFolders = s.DirectFolders,
        Unreadable = s.Unreadable,
        NewestFile = s.NewestFile,
        NewestFileTime = s.NewestFileTime,
    };
}
