using Microsoft.UI.Xaml;

namespace DirSnap2.Models;

/// <summary>
/// A row of the "Search in files" results: either a file (header, with its match count)
/// or one of its matching lines (text split around the match so it can be shown in bold).
/// </summary>
public sealed class FileSearchHit
{
    public string FilePath { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public bool IsFile { get; init; }

    /// <summary>0-based line of the match (for a file row: its first match).</summary>
    public int Line { get; init; }

    // File row
    public string HeaderDetail { get; set; } = string.Empty;

    // Line row
    public string Before { get; init; } = string.Empty;
    public string Match { get; init; } = string.Empty;
    public string After { get; init; } = string.Empty;

    public string LineNumberText => IsFile ? string.Empty : (Line + 1).ToString();
    public string ToolTip => IsFile ? FilePath : $"{FileName} : {Line + 1}";
    public Visibility HeaderVisibility => IsFile ? Visibility.Visible : Visibility.Collapsed;
    public Visibility LineVisibility => IsFile ? Visibility.Collapsed : Visibility.Visible;
}
