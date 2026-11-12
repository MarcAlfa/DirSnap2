using System.IO;
using System.Text;
using DirSnap2.Models;

namespace DirSnap2.Services;

/// <summary>
/// A file to attach. <paramref name="LiveText"/> is the editor buffer (unsaved edits included) when the
/// file is open in a tab; null means "read it from disk".
/// </summary>
public sealed record AiContextFile(string Source, string Path, string? LiveText);

public sealed class AiContextResult
{
    public string Text { get; init; } = string.Empty;
    public int FileCount { get; init; }
    public bool HasFolderListing { get; init; }
    public List<string> Skipped { get; init; } = new();

    public bool IsEmpty => string.IsNullOrEmpty(Text);
    public int CharCount => Text.Length;

    /// <summary>"📎 2 files + folder list · 34 KB" (empty when nothing is attached).</summary>
    public string Summary
    {
        get
        {
            if (IsEmpty)
                return string.Empty;
            var parts = new List<string>();
            if (FileCount > 0)
                parts.Add(FileCount == 1 ? "1 file" : $"{FileCount} files");
            if (HasFolderListing)
                parts.Add("folder list");
            return $"📎 {string.Join(" + ", parts)} · {FSItem.FormatSize(Encoding.UTF8.GetByteCount(Text))}";
        }
    }
}

/// <summary>Assembles the context (files and folder listing) the user chose to send with a question.</summary>
public static class AiContextBuilder
{
    public const int MaxFolderEntries = 2000;

    public const string SystemPrompt =
        "You are the built-in AI assistant of DirSnap 2, a Windows file explorer and text editor for developers. " +
        "Answer concisely and in the same language the user writes in. " +
        "Files the user attached are wrapped in <file source=\"...\" path=\"...\"> tags, a folder listing in " +
        "<folder_listing path=\"...\"> tags. Refer to files by name. " +
        "When comparing files, highlight logical, structural and code differences clearly. " +
        "When you return a full file or code meant to be pasted into a file, put it in a single fenced code block.";

    public static AiContextResult Build(
        IReadOnlyList<AiContextFile> files, string? folderPath, ITextFileService textFileService)
    {
        var sb = new StringBuilder();
        var skipped = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int fileCount = 0;

        foreach (var f in files)
        {
            if (string.IsNullOrWhiteSpace(f.Path) || !seen.Add(f.Path))
                continue;

            string? content = f.LiveText != null ? TextFileService.NormalizeNewlines(f.LiveText) : null;
            if (content == null)
            {
                try
                {
                    content = textFileService.ReadText(f.Path).Content;
                }
                catch (Exception ex)
                {
                    // Binary, larger than 5 MB, missing, locked...
                    skipped.Add($"{Path.GetFileName(f.Path)}: {ex.Message}");
                    continue;
                }
            }

            sb.Append("<file source=\"").Append(f.Source).Append("\" path=\"").Append(f.Path).Append("\">\n");
            sb.Append(content);
            if (!content.EndsWith('\n'))
                sb.Append('\n');
            sb.Append("</file>\n\n");
            fileCount++;
        }

        bool hasListing = false;
        if (!string.IsNullOrWhiteSpace(folderPath))
        {
            var listing = BuildFolderListing(folderPath, skipped);
            if (listing != null)
            {
                sb.Append(listing);
                hasListing = true;
            }
        }

        return new AiContextResult
        {
            Text = sb.ToString().TrimEnd(),
            FileCount = fileCount,
            HasFolderListing = hasListing,
            Skipped = skipped
        };
    }

    /// <summary>Names, sizes and dates of the folder's direct content (no file contents).</summary>
    private static string? BuildFolderListing(string folderPath, List<string> skipped)
    {
        try
        {
            var dir = new DirectoryInfo(folderPath);
            var entries = dir.EnumerateFileSystemInfos()
                .Where(e => (e.Attributes & FileAttributes.System) == 0)
                .OrderByDescending(e => e is DirectoryInfo)
                .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                .Take(MaxFolderEntries + 1)
                .ToList();

            var sb = new StringBuilder();
            sb.Append("<folder_listing path=\"").Append(dir.FullName).Append("\">\n");
            foreach (var e in entries.Take(MaxFolderEntries))
            {
                if (e is DirectoryInfo)
                    sb.Append(e.Name).Append("/\t\t").Append(e.LastWriteTime.ToString("yyyy-MM-dd HH:mm")).Append('\n');
                else if (e is FileInfo fi)
                    sb.Append(fi.Name).Append('\t').Append(FSItem.FormatSize(fi.Length)).Append('\t')
                      .Append(fi.LastWriteTime.ToString("yyyy-MM-dd HH:mm")).Append('\n');
            }
            if (entries.Count > MaxFolderEntries)
                sb.Append($"... (more than {MaxFolderEntries} entries, list truncated)\n");
            sb.Append("</folder_listing>\n");
            return sb.ToString();
        }
        catch (Exception ex)
        {
            skipped.Add($"Folder list: {ex.Message}");
            return null;
        }
    }
}
