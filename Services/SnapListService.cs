using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using Windows.ApplicationModel.DataTransfer;
using CommunityToolkit.Mvvm.ComponentModel;
using DirSnap2.Models;

namespace DirSnap2.Services;

public interface ISnapListService
{
    ObservableCollection<SnapItem> Items { get; }
    int Count { get; }
    bool IncludeFullPath { get; set; }
    bool IncludeExtension { get; set; }

    event EventHandler? ItemsChanged;

    bool Add(FSItem item);
    int AddRange(IEnumerable<FSItem> items);
    bool Remove(SnapItem item);
    void RemoveAt(int index);
    void Clear();

    List<string> GetFormattedLines();
    string GetFullContent();
    void CopyToClipboard();
    void SendToNotepad();
    void ExportToTxt(string filePath);
    void ExportToCsv(string filePath);
}

public sealed class SnapListService : ObservableObject, ISnapListService
{
    private readonly HashSet<string> _pathLookup = new(StringComparer.OrdinalIgnoreCase);

    public ObservableCollection<SnapItem> Items { get; } = new();

    public int Count => Items.Count;

    private bool _includeFullPath;
    public bool IncludeFullPath
    {
        get => _includeFullPath;
        set
        {
            if (SetProperty(ref _includeFullPath, value))
            {
                ItemsChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    private bool _includeExtension = true;
    public bool IncludeExtension
    {
        get => _includeExtension;
        set
        {
            if (SetProperty(ref _includeExtension, value))
            {
                ItemsChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public event EventHandler? ItemsChanged;

    public SnapListService(IConfigService? configService = null)
    {
        if (configService != null)
        {
            _includeFullPath = configService.Current.IncludeFullPath;
            _includeExtension = configService.Current.IncludeExtension;
        }

        Items.CollectionChanged += (s, e) =>
        {
            OnPropertyChanged(nameof(Count));
            ItemsChanged?.Invoke(this, EventArgs.Empty);
        };
    }

    public bool Add(FSItem item)
    {
        if (item == null || string.IsNullOrWhiteSpace(item.Path))
            return false;

        var normalized = Path.GetFullPath(item.Path);
        if (_pathLookup.Contains(normalized))
            return false;

        _pathLookup.Add(normalized);
        var snapItem = SnapItem.FromFSItem(item);
        Items.Add(snapItem);
        return true;
    }

    public int AddRange(IEnumerable<FSItem> items)
    {
        int added = 0;
        foreach (var item in items)
        {
            if (Add(item))
            {
                added++;
            }
        }
        return added;
    }

    public bool Remove(SnapItem item)
    {
        if (item == null) return false;

        var normalized = Path.GetFullPath(item.Path);
        _pathLookup.Remove(normalized);
        return Items.Remove(item);
    }

    public void RemoveAt(int index)
    {
        if (index >= 0 && index < Items.Count)
        {
            var item = Items[index];
            _pathLookup.Remove(Path.GetFullPath(item.Path));
            Items.RemoveAt(index);
        }
    }

    public void Clear()
    {
        _pathLookup.Clear();
        Items.Clear();
    }

    public List<string> GetFormattedLines()
    {
        return Items.Select(it => it.Format(IncludeFullPath, IncludeExtension)).ToList();
    }

    public string GetFullContent()
    {
        var lines = GetFormattedLines();
        return string.Join(Environment.NewLine, lines);
    }

    public void CopyToClipboard()
    {
        var text = GetFullContent();
        var dataPackage = new DataPackage();
        dataPackage.SetText(text);
        Clipboard.SetContent(dataPackage);
    }

    public void SendToNotepad()
    {
        var lines = GetFormattedLines();
        var tempFile = Path.Combine(Path.GetTempPath(), $"DirSnap_{Guid.NewGuid():N}.txt");

        // Write with UTF-8 BOM so Notepad detects encoding properly
        File.WriteAllLines(tempFile, lines, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        Process.Start(new ProcessStartInfo
        {
            FileName = "notepad.exe",
            Arguments = $"\"{tempFile}\"",
            UseShellExecute = true
        });
    }

    public void ExportToTxt(string filePath)
    {
        var lines = GetFormattedLines();
        File.WriteAllLines(filePath, lines, new UTF8Encoding(false));
    }

    public void ExportToCsv(string filePath)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Name,Type,Size_Bytes,Modified_Timestamp,Full_Path");

        foreach (var it in Items)
        {
            var type = it.IsDirectory ? "Directory" : "File";
            var mtime = it.ModifiedTime != DateTime.MinValue ? it.ModifiedTime.ToString("yyyy-MM-dd HH:mm:ss") : string.Empty;
            var escapedName = EscapeCsv(it.Name);
            var escapedPath = EscapeCsv(it.Path);

            sb.AppendLine($"{escapedName},{type},{it.Size},{mtime},{escapedPath}");
        }

        File.WriteAllText(filePath, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    private static string EscapeCsv(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
        {
            return $"\"{value.Replace("\"", "\"\"")}\"";
        }
        return value;
    }
}
