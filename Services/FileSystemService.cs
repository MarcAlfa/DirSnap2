using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using DirSnap2.Models;

namespace DirSnap2.Services;

public interface IFileSystemService
{
    List<DriveInfoModel> GetDrives();
    (List<FSItem> Directories, List<FSItem> Files, string? Error) ScanDirectory(string path, bool showAll = false, IEnumerable<string>? allowedExtensions = null);
    List<TreeNodeItem> GetTreeChildren(string folderPath, bool showAll = false, IEnumerable<string>? allowedExtensions = null);
    IEnumerable<FSItem> FilterItems(IEnumerable<FSItem> items, string query);
    IEnumerable<FSItem> SortItems(IEnumerable<FSItem> items, string sortCol, bool descending);
    string ToWslPath(string winPath);
    void OpenInExplorer(string path);
    void OpenInTerminal(string folderPath);
}

public sealed class FileSystemService : IFileSystemService
{
    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool GetVolumeInformation(
        string rootPathName,
        StringBuilder? volumeNameBuffer,
        int volumeNameSize,
        out uint volumeSerialNumber,
        out uint maximumComponentLength,
        out uint fileSystemFlags,
        StringBuilder? fileSystemNameBuffer,
        int fileSystemNameSize);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
    private static extern uint GetDriveType(string rootPathName);

    public List<DriveInfoModel> GetDrives()
    {
        var result = new List<DriveInfoModel>();

        try
        {
            var drives = DriveInfo.GetDrives();
            foreach (var d in drives)
            {
                if (!d.IsReady)
                {
                    result.Add(new DriveInfoModel
                    {
                        Letter = d.Name.TrimEnd('\\'),
                        Path = d.Name,
                        Label = string.Empty,
                        DriveType = d.DriveType.ToString()
                    });
                    continue;
                }

                string label = string.Empty;
                try
                {
                    label = d.VolumeLabel;
                }
                catch
                {
                    // Fallback to Win32 API if DriveInfo.VolumeLabel throws
                    var volName = new StringBuilder(261);
                    if (GetVolumeInformation(d.Name, volName, volName.Capacity, out _, out _, out _, null, 0))
                    {
                        label = volName.ToString();
                    }
                }

                result.Add(new DriveInfoModel
                {
                    Letter = d.Name.TrimEnd('\\'),
                    Path = d.Name,
                    Label = label,
                    DriveType = d.DriveType.ToString()
                });
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[FileSystemService] GetDrives error: {ex.Message}");
        }

        return result;
    }

    public (List<FSItem> Directories, List<FSItem> Files, string? Error) ScanDirectory(
        string path,
        bool showAll = false,
        IEnumerable<string>? allowedExtensions = null)
    {
        var dirs = new List<FSItem>();
        var files = new List<FSItem>();
        string? error = null;

        if (!Directory.Exists(path))
        {
            return (dirs, files, $"Directory not found: {path}");
        }

        var allowedExtSet = allowedExtensions != null
            ? new HashSet<string>(allowedExtensions.Select(e => e.ToLowerInvariant()))
            : null;

        try
        {
            var dirInfo = new DirectoryInfo(path);
            var entries = dirInfo.EnumerateFileSystemInfos();

            foreach (var entry in entries)
            {
                try
                {
                    bool isHidden = (entry.Attributes & FileAttributes.Hidden) != 0;
                    bool isSystem = (entry.Attributes & FileAttributes.System) != 0;

                    if (!showAll && (isHidden || isSystem))
                        continue;

                    if (entry is DirectoryInfo di)
                    {
                        dirs.Add(new FSItem
                        {
                            Name = di.Name,
                            Path = di.FullName,
                            IsDirectory = true,
                            Size = 0,
                            ModifiedTime = di.LastWriteTime
                        });
                    }
                    else if (entry is FileInfo fi)
                    {
                        var ext = fi.Extension.ToLowerInvariant();
                        if (!showAll && allowedExtSet != null && !allowedExtSet.Contains(ext))
                        {
                            // In normal mode, if allowedExtensions is specified, skip others
                            continue;
                        }

                        files.Add(new FSItem
                        {
                            Name = fi.Name,
                            Path = fi.FullName,
                            IsDirectory = false,
                            Size = fi.Length,
                            ModifiedTime = fi.LastWriteTime
                        });
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    // Skip inaccessible entries
                }
                catch (IOException)
                {
                    // Skip locked/inaccessible files
                }
            }

            dirs.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            files.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex)
        {
            error = ex.Message;
        }

        return (dirs, files, error);
    }

    public List<TreeNodeItem> GetTreeChildren(
        string folderPath,
        bool showAll = false,
        IEnumerable<string>? allowedExtensions = null)
    {
        var result = new List<TreeNodeItem>();
        if (!Directory.Exists(folderPath))
            return result;

        var allowedExtSet = allowedExtensions != null
            ? new HashSet<string>(allowedExtensions.Select(e => e.ToLowerInvariant()))
            : null;

        try
        {
            var dirInfo = new DirectoryInfo(folderPath);
            var entries = dirInfo.EnumerateFileSystemInfos();

            var dirs = new List<TreeNodeItem>();
            var files = new List<TreeNodeItem>();

            foreach (var entry in entries)
            {
                try
                {
                    bool isHidden = (entry.Attributes & FileAttributes.Hidden) != 0;
                    bool isSystem = (entry.Attributes & FileAttributes.System) != 0;

                    if (!showAll && (isHidden || isSystem))
                        continue;

                    if (entry is DirectoryInfo di)
                    {
                        dirs.Add(new TreeNodeItem
                        {
                            Name = di.Name,
                            Path = di.FullName,
                            IsDirectory = true,
                            Size = 0,
                            ModifiedTime = di.LastWriteTime,
                            HasUnrealizedChildren = true
                        });
                    }
                    else if (entry is FileInfo fi)
                    {
                        var ext = fi.Extension.ToLowerInvariant();
                        if (!showAll && allowedExtSet != null && !allowedExtSet.Contains(ext))
                        {
                            continue;
                        }

                        files.Add(new TreeNodeItem
                        {
                            Name = fi.Name,
                            Path = fi.FullName,
                            IsDirectory = false,
                            Size = fi.Length,
                            ModifiedTime = fi.LastWriteTime,
                            HasUnrealizedChildren = false
                        });
                    }
                }
                catch
                {
                    // Ignore inaccessible items
                }
            }

            dirs.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            files.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

            result.AddRange(dirs);
            result.AddRange(files);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[FileSystemService] GetTreeChildren error: {ex.Message}");
        }

        return result;
    }

    public IEnumerable<FSItem> FilterItems(IEnumerable<FSItem> items, string query)
    {
        var q = query?.Trim();
        if (string.IsNullOrEmpty(q))
            return items;

        if (q.StartsWith("*.") && q.Length > 2)
        {
            var ext = q.Substring(1).ToLowerInvariant();
            return items.Where(it => it.Extension.Equals(ext, StringComparison.OrdinalIgnoreCase) ||
                                     it.Name.EndsWith(ext, StringComparison.OrdinalIgnoreCase));
        }

        return items.Where(it => it.Name.Contains(q, StringComparison.OrdinalIgnoreCase));
    }

    public IEnumerable<FSItem> SortItems(IEnumerable<FSItem> items, string sortCol, bool descending)
    {
        var list = items.ToList();

        return sortCol.ToLowerInvariant() switch
        {
            "mtime" => descending
                ? list.OrderByDescending(i => i.ModifiedTime)
                : list.OrderBy(i => i.ModifiedTime),

            "size" => descending
                ? list.OrderByDescending(i => i.Size)
                : list.OrderBy(i => i.Size),

            _ => descending
                ? list.OrderByDescending(i => i.IsDirectory).ThenByDescending(i => i.Name, StringComparer.OrdinalIgnoreCase)
                : list.OrderByDescending(i => i.IsDirectory).ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
        };
    }

    public string ToWslPath(string winPath)
    {
        if (string.IsNullOrWhiteSpace(winPath)) return string.Empty;

        try
        {
            var full = Path.GetFullPath(winPath);
            if (full.Length >= 2 && full[1] == ':')
            {
                var drive = char.ToLowerInvariant(full[0]);
                var rest = full.Substring(2).Replace('\\', '/');
                return $"/mnt/{drive}{rest}";
            }
            return winPath.Replace('\\', '/');
        }
        catch
        {
            return winPath.Replace('\\', '/');
        }
    }

    public void OpenInExplorer(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;

        try
        {
            var normalized = Path.GetFullPath(path);
            if (File.Exists(normalized))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{normalized}\"",
                    UseShellExecute = true
                });
            }
            else if (Directory.Exists(normalized))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{normalized}\"",
                    UseShellExecute = true
                });
            }
            else
            {
                var parent = Path.GetDirectoryName(normalized);
                if (!string.IsNullOrEmpty(parent) && Directory.Exists(parent))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"\"{parent}\"",
                        UseShellExecute = true
                    });
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[FileSystemService] OpenInExplorer error: {ex.Message}");
        }
    }

    public void OpenInTerminal(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
            return;

        try
        {
            // Attempt to start Windows Terminal first, fallback to powershell
            var wtPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                @"Microsoft\WindowsApps\wt.exe");

            if (File.Exists(wtPath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = wtPath,
                    Arguments = $"-d \"{folderPath}\"",
                    UseShellExecute = true
                });
            }
            else
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoExit -Command \"Set-Location -LiteralPath '{folderPath}'\"",
                    UseShellExecute = true
                });
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[FileSystemService] OpenInTerminal error: {ex.Message}");
        }
    }
}
