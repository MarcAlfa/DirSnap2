using System.IO;
using DirSnap2.Models;

namespace DirSnap2.Services;

/// <summary>
/// "Search in files" (grep): looks for a text in the files of a folder (and its subfolders)
/// whose extension is one of the editor extensions. Runs off the UI thread and can be cancelled.
/// </summary>
public static class FileSearchService
{
    public const int MaxLineHits = 2000;
    private const int MaxSnippetBefore = 40;
    private const int MaxSnippetAfter = 200;

    /// <summary>Folders never worth searching (version control data, packages).</summary>
    private static readonly HashSet<string> SkippedFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".svn", ".hg", ".vs", "node_modules", "__pycache__"
    };

    public sealed class Summary
    {
        public int FilesSearched { get; set; }
        public int FilesWithHits { get; set; }
        public int LineHits { get; set; }
        public bool LimitReached { get; set; }
        public bool Cancelled { get; set; }
    }

    /// <param name="onFile">Called (on the worker thread) for each file with matches: the file row, then its line rows.</param>
    /// <param name="onProgress">Called now and then with the number of files searched so far.</param>
    public static Summary Search(string root, string query, bool matchCase, bool subfolders,
        ICollection<string> extensions, Action<List<FileSearchHit>> onFile, Action<int> onProgress, CancellationToken ct)
    {
        var summary = new Summary();
        var comparison = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var exts = new HashSet<string>(extensions.Select(e => e.ToLowerInvariant()));
        var reader = new TextFileService();
        var lastProgress = Environment.TickCount64;
        string rootFull = ShellFileOperations.CleanPath(root);

        var pending = new Stack<string>();
        pending.Push(rootFull);
        while (pending.Count > 0)
        {
            if (ct.IsCancellationRequested) { summary.Cancelled = true; break; }
            var dir = pending.Pop();

            string[] files;
            try
            {
                files = Directory.GetFiles(dir);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                continue; // no access: skip the folder, like Explorer search
            }

            // Newest files first, like the file list
            var ordered = files
                .Where(f => exts.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .Select(f => new FileInfo(f))
                .Where(fi => SafeLength(fi) is > 0 and <= TextFileService.MaxEditableSize)
                .OrderByDescending(SafeWriteTime);

            foreach (var fi in ordered)
            {
                if (ct.IsCancellationRequested) { summary.Cancelled = true; break; }
                summary.FilesSearched++;

                var rows = SearchFile(reader, fi.FullName, rootFull, query, comparison, MaxLineHits - summary.LineHits);
                if (rows != null)
                {
                    summary.FilesWithHits++;
                    summary.LineHits += rows.Count - 1;
                    onFile(rows);
                    if (summary.LineHits >= MaxLineHits) { summary.LimitReached = true; break; }
                }

                if (Environment.TickCount64 - lastProgress > 150)
                {
                    lastProgress = Environment.TickCount64;
                    onProgress(summary.FilesSearched);
                }
            }
            if (summary.Cancelled || summary.LimitReached) break;

            if (!subfolders) break;
            try
            {
                // Pushed in reverse so they are searched in name order
                foreach (var sub in Directory.GetDirectories(dir).OrderByDescending(d => d, StringComparer.OrdinalIgnoreCase))
                {
                    var info = new DirectoryInfo(sub);
                    if (SkippedFolders.Contains(info.Name)) continue;
                    if ((info.Attributes & FileAttributes.ReparsePoint) != 0) continue; // junctions/links: avoid loops
                    pending.Push(sub);
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
            }
        }

        return summary;
    }

    private static long SafeLength(FileInfo fi)
    {
        try { return fi.Length; } catch { return -1; }
    }

    private static DateTime SafeWriteTime(FileInfo fi)
    {
        try { return fi.LastWriteTime; } catch { return DateTime.MinValue; }
    }

    /// <summary>The file row followed by its matching lines, or null when the text is not in the file.</summary>
    private static List<FileSearchHit>? SearchFile(TextFileService reader, string path, string root, string query,
        StringComparison comparison, int maxLines)
    {
        string text;
        try
        {
            text = reader.ReadText(path).Content; // same decoding as the preview (UTF-8 / ANSI), newlines as LF
        }
        catch
        {
            return null; // binary, locked or unreadable
        }

        int first = text.IndexOf(query, comparison);
        if (first < 0) return null;

        string fileName = Path.GetFileName(path);
        var lines = new List<FileSearchHit>();
        int lineNo = 0, lineStart = 0, total = 0;
        int pos = first;

        while (pos >= 0)
        {
            // Advance the line counter up to the match
            int nl;
            while ((nl = text.IndexOf('\n', lineStart)) >= 0 && nl < pos)
            {
                lineNo++;
                lineStart = nl + 1;
            }
            int lineEnd = text.IndexOf('\n', lineStart);
            if (lineEnd < 0) lineEnd = text.Length;

            // Count every match on this line, show the line once
            int onLine = 0;
            int p = pos;
            while (p >= 0 && p < lineEnd)
            {
                onLine++;
                p = text.IndexOf(query, p + Math.Max(1, query.Length), comparison);
            }
            total += onLine;

            if (lines.Count < maxLines)
                lines.Add(MakeLineHit(path, fileName, lineNo, text, lineStart, lineEnd, pos, query.Length));

            pos = p;
        }

        string folder = Path.GetDirectoryName(path) ?? string.Empty;
        string relative = folder.Length > root.Length ? folder.Substring(root.Length).TrimStart('\\') : string.Empty;
        var header = new FileSearchHit
        {
            FilePath = path,
            FileName = fileName,
            IsFile = true,
            Line = lines[0].Line,
            HeaderDetail = (relative.Length > 0 ? relative + "   ·   " : string.Empty) +
                           (total == 1 ? "1 match" : $"{total} matches"),
        };

        var rows = new List<FileSearchHit>(lines.Count + 1) { header };
        rows.AddRange(lines);
        return rows;
    }

    private static FileSearchHit MakeLineHit(string path, string fileName, int lineNo, string text,
        int lineStart, int lineEnd, int matchPos, int matchLength)
    {
        // Leading indentation is not useful in a one-line result
        int start = lineStart;
        while (start < matchPos && (text[start] == ' ' || text[start] == '\t')) start++;

        string before = text.Substring(start, matchPos - start);
        if (before.Length > MaxSnippetBefore)
            before = "…" + before.Substring(before.Length - MaxSnippetBefore);

        int afterStart = matchPos + matchLength;
        int afterLength = Math.Max(0, Math.Min(lineEnd, afterStart + MaxSnippetAfter) - afterStart);
        string after = text.Substring(afterStart, afterLength).TrimEnd('\r');

        return new FileSearchHit
        {
            FilePath = path,
            FileName = fileName,
            Line = lineNo,
            Before = before.Replace('\t', ' '),
            Match = text.Substring(matchPos, matchLength),
            After = after.Replace('\t', ' '),
        };
    }
}
