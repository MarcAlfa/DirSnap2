using System.IO;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace DirSnap2.Models;

/// <summary>
/// Represents a filesystem item (directory or file) with icons and formatting.
/// </summary>
public sealed class FSItem
{
    private static readonly Dictionary<string, SolidColorBrush> BrushCache = new(StringComparer.OrdinalIgnoreCase);

    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public bool IsDirectory { get; set; }
    public long Size { get; set; }
    public DateTime ModifiedTime { get; set; }
    public bool IsEvenRow { get; set; }
    public bool IsCurrentFolder { get; set; }

    public SolidColorBrush? TreeItemHighlightBrush =>
        IsCurrentFolder && Application.Current.Resources.TryGetValue("AppAccentSubtleBrush", out var brush)
            ? brush as SolidColorBrush
            : null;

    public Windows.UI.Text.FontWeight TreeItemFontWeight =>
        IsCurrentFolder ? Microsoft.UI.Text.FontWeights.Bold : Microsoft.UI.Text.FontWeights.Normal;

    private static readonly SolidColorBrush TransparentBrush = new(Colors.Transparent);
    private static readonly SolidColorBrush DarkZebraBrush = new(Color.FromArgb(16, 255, 255, 255)); // 6% subtle white tint for dark mode
    private static readonly SolidColorBrush LightZebraBrush = new(Color.FromArgb(14, 0, 0, 0));       // 5% subtle black tint for light mode

    public SolidColorBrush RowBackgroundBrush
    {
        get
        {
            if (!IsEvenRow) return TransparentBrush;
            return Application.Current.RequestedTheme == ApplicationTheme.Light
                ? LightZebraBrush
                : DarkZebraBrush;
        }
    }

    public string Extension =>
        IsDirectory ? string.Empty : System.IO.Path.GetExtension(Name).ToLowerInvariant();

    public string Glyph
    {
        get
        {
            if (Name == "..") return "\uE748"; // Up arrow / parent
            if (IsDirectory) return "\uE8B7";  // Folder glyph

            return Extension switch
            {
                ".md" or ".markdown" => "\uE8A5",     // Markdown document
                ".txt" or ".log" or ".cfg" or ".ini" or ".env" => "\uE8A5", // Text / config
                ".json" => "\uE943",                  // JSON / Code
                ".xml" or ".yaml" or ".yml" or ".toml" => "\uE943", // Markup
                ".cs" or ".py" or ".js" or ".ts" or ".html" or ".css" or ".cpp" or ".c" or ".h" or ".sql" => "\uE943", // Code
                ".bat" or ".ps1" or ".cmd" or ".sh" => "\uE756", // Script / Terminal
                ".csv" or ".tsv" or ".xlsx" or ".xls" => "\uE9F9", // Table / Spreadsheet
                ".zip" or ".rar" or ".7z" or ".tar" or ".gz" => "\uF012", // Archive
                ".png" or ".jpg" or ".jpeg" or ".gif" or ".svg" or ".bmp" or ".webp" or ".ico" => "\uEB9F", // Image
                ".exe" or ".msi" or ".dll" => "\uE756", // Binary executable
                ".pdf" or ".doc" or ".docx" => "\uE8A5", // Document
                _ => "\uE7C3" // Generic file
            };
        }
    }

    public string ColorHex
    {
        get
        {
            if (Name == "..") return "#60A5FA"; // Soft blue
            if (IsDirectory) return "#F59E0B";  // Warm folder yellow / amber

            return Extension switch
            {
                ".md" or ".markdown" => "#38BDF8", // Sky blue
                ".txt" or ".log" or ".cfg" or ".ini" or ".env" => "#34D399", // Emerald green
                ".json" => "#FBBF24", // Amber code
                ".xml" or ".yaml" or ".yml" or ".toml" => "#A78BFA", // Violet markup
                ".cs" or ".py" or ".js" or ".ts" or ".html" or ".css" or ".cpp" or ".c" or ".h" or ".sql" => "#818CF8", // Indigo code
                ".bat" or ".ps1" or ".cmd" or ".sh" => "#2DD4BF", // Teal script
                ".csv" or ".tsv" or ".xlsx" or ".xls" => "#10B981", // Green sheet
                ".zip" or ".rar" or ".7z" or ".tar" or ".gz" => "#FB923C", // Orange archive
                ".png" or ".jpg" or ".jpeg" or ".gif" or ".svg" or ".bmp" or ".webp" or ".ico" => "#F472B6", // Pink image
                ".exe" or ".msi" or ".dll" => "#F87171", // Coral red binary
                _ => "#9CA3AF" // Muted gray
            };
        }
    }

    public SolidColorBrush GlyphBrush => GetCachedBrush(ColorHex);

    public string FormattedSize =>
        IsDirectory ? string.Empty : FormatSize(Size);

    public string FormattedModifiedTime =>
        ModifiedTime != DateTime.MinValue ? ModifiedTime.ToString("yyyy-MM-dd HH:mm") : string.Empty;

    public string FormatDisplayName(bool withExtension = true)
    {
        if (IsDirectory || withExtension)
            return Name;

        return System.IO.Path.GetFileNameWithoutExtension(Name);
    }

    public string FormatExport(bool fullPath = false, bool withExtension = true)
    {
        if (IsDirectory)
            return fullPath ? Path : Name;

        if (fullPath)
        {
            if (withExtension)
                return Path;

            var dir = System.IO.Path.GetDirectoryName(Path);
            var stem = System.IO.Path.GetFileNameWithoutExtension(Path);
            return string.IsNullOrEmpty(dir) ? stem : System.IO.Path.Combine(dir, stem);
        }
        else
        {
            return withExtension ? Name : System.IO.Path.GetFileNameWithoutExtension(Name);
        }
    }

    public static string FormatSize(long sizeBytes)
    {
        if (sizeBytes < 1024)
            return $"{sizeBytes} B";
        if (sizeBytes < 1024 * 1024)
            return $"{sizeBytes / 1024.0:F1} KB";
        if (sizeBytes < 1024L * 1024 * 1024)
            return $"{sizeBytes / (1024.0 * 1024):F1} MB";

        return $"{sizeBytes / (1024.0 * 1024 * 1024):F2} GB";
    }

    public static SolidColorBrush GetCachedBrush(string hexColor)
    {
        if (BrushCache.TryGetValue(hexColor, out var brush))
            return brush;

        var color = HexToColor(hexColor);
        brush = new SolidColorBrush(color);
        BrushCache[hexColor] = brush;
        return brush;
    }

    private static Color HexToColor(string hex)
    {
        hex = hex.TrimStart('#');
        if (hex.Length == 6)
        {
            byte r = Convert.ToByte(hex.Substring(0, 2), 16);
            byte g = Convert.ToByte(hex.Substring(2, 2), 16);
            byte b = Convert.ToByte(hex.Substring(4, 2), 16);
            return Color.FromArgb(255, r, g, b);
        }
        return Colors.Gray;
    }

    public override string ToString() => Path;
}
