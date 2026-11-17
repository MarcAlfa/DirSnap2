using System.Text.RegularExpressions;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using Windows.UI.Text;

namespace DirSnap2.Services;

/// <summary>Kind of a colored Markdown span (shared by the preview and the Markdown editor).</summary>
public enum MarkdownSpanKind
{
    Normal,
    Heading,
    Bold,
    Code,
    ListMarker,
    Link
}

/// <summary>A colored span of one line: start/length inside the line.</summary>
public readonly record struct MarkdownSpan(int Start, int Length, MarkdownSpanKind Kind);

/// <summary>
/// Lightweight Markdown syntax highlighter for RichTextBlock preview.
/// Replicates DirSnap v1 Markdown syntax colors:
/// - Headings: #4CC9F0 (Sky Blue)
/// - Bold: #FFD166 (Warm Yellow)
/// - Code & Fences: #90BE6D (Soft Green)
/// - Lists: #F4A261 (Orange)
/// - Links: #7AA2F7 (Blue)
/// The line tokenizer (<see cref="TokenizeLine"/>) is also used by the Markdown editor (RichEditBox).
/// </summary>
public static class MarkdownHighlighter
{
    private static readonly FontFamily MonoFont = new("Consolas, Cascadia Code, Courier New");

    // Dark Theme colors (optimized for dark background #1F1F1F / #252526)
    private static readonly Color DarkHeading = Color.FromArgb(255, 76, 201, 240);   // #4CC9F0 Sky Blue
    private static readonly Color DarkBold = Color.FromArgb(255, 255, 209, 102);     // #FFD166 Warm Yellow
    private static readonly Color DarkCode = Color.FromArgb(255, 144, 190, 109);     // #90BE6D Soft Green
    private static readonly Color DarkList = Color.FromArgb(255, 244, 162, 97);      // #F4A261 Orange
    private static readonly Color DarkLink = Color.FromArgb(255, 122, 162, 247);     // #7AA2F7 Blue
    private static readonly Color DarkNormal = Color.FromArgb(255, 235, 235, 235);   // #EBEBEB Crisp White

    // Light Theme colors (high-contrast, rich tones on light gray background)
    private static readonly Color LightHeading = Color.FromArgb(255, 5, 80, 174);    // #0550AE Deep Cobalt / Azure
    private static readonly Color LightBold = Color.FromArgb(255, 161, 92, 0);       // #A15C00 Rich Dark Amber
    private static readonly Color LightCode = Color.FromArgb(255, 22, 101, 52);      // #166534 Deep Forest Green
    private static readonly Color LightList = Color.FromArgb(255, 194, 65, 12);      // #C2410C Rich Terracotta / Rust
    private static readonly Color LightLink = Color.FromArgb(255, 9, 105, 218);      // #0969DA GitHub / Hyperlink Blue
    private static readonly Color LightNormal = Color.FromArgb(255, 31, 35, 40);     // #1F2328 Deep Charcoal / Dark Gray

    private static readonly Dictionary<(MarkdownSpanKind, bool), SolidColorBrush> BrushCache = new();

    private static readonly Regex HeadingRegex = new(@"^#{1,6}\s", RegexOptions.Compiled);
    private static readonly Regex ListRegex = new(@"^(\s*(?:[-*+]|\d+\.)\s)(.*)$", RegexOptions.Compiled);
    private static readonly Regex InlineRegex = new(@"(\*\*[^*\n]+\*\*|`[^`\n]+`|\[[^\]\n]+\]\([^)\n]+\))", RegexOptions.Compiled);

    public static Color GetColor(MarkdownSpanKind kind, bool isDarkTheme) => kind switch
    {
        MarkdownSpanKind.Heading => isDarkTheme ? DarkHeading : LightHeading,
        MarkdownSpanKind.Bold => isDarkTheme ? DarkBold : LightBold,
        MarkdownSpanKind.Code => isDarkTheme ? DarkCode : LightCode,
        MarkdownSpanKind.ListMarker => isDarkTheme ? DarkList : LightList,
        MarkdownSpanKind.Link => isDarkTheme ? DarkLink : LightLink,
        _ => isDarkTheme ? DarkNormal : LightNormal
    };

    private static SolidColorBrush GetBrush(MarkdownSpanKind kind, bool isDarkTheme)
    {
        if (!BrushCache.TryGetValue((kind, isDarkTheme), out var brush))
        {
            brush = new SolidColorBrush(GetColor(kind, isDarkTheme));
            BrushCache[(kind, isDarkTheme)] = brush;
        }
        return brush;
    }

    /// <summary>Font weight of a span kind (400 = normal).</summary>
    public static int GetWeight(MarkdownSpanKind kind) => kind switch
    {
        MarkdownSpanKind.Heading or MarkdownSpanKind.Bold => 700,
        MarkdownSpanKind.ListMarker => 600,
        _ => 400
    };

    /// <summary>True when the line opens or closes a fenced code block.</summary>
    public static bool IsFenceLine(string line) => line.TrimStart().StartsWith("```");

    /// <summary>
    /// Splits one line (without line break) into colored spans covering the whole line.
    /// <paramref name="inFence"/> carries the fenced-code state from line to line.
    /// </summary>
    public static void TokenizeLine(string line, ref bool inFence, List<MarkdownSpan> spans)
    {
        spans.Clear();

        // Fenced code block ```
        if (IsFenceLine(line))
        {
            spans.Add(new MarkdownSpan(0, line.Length, MarkdownSpanKind.Code));
            inFence = !inFence;
            return;
        }

        if (inFence)
        {
            spans.Add(new MarkdownSpan(0, line.Length, MarkdownSpanKind.Code));
            return;
        }

        // Heading line
        if (HeadingRegex.IsMatch(line))
        {
            spans.Add(new MarkdownSpan(0, line.Length, MarkdownSpanKind.Heading));
            return;
        }

        // List item
        int offset = 0;
        var listMatch = ListRegex.Match(line);
        if (listMatch.Success)
        {
            offset = listMatch.Groups[1].Length;
            spans.Add(new MarkdownSpan(0, offset, MarkdownSpanKind.ListMarker));
        }

        // Inline formatting of the rest
        int pos = offset;
        foreach (Match m in InlineRegex.Matches(line, offset))
        {
            if (m.Index > pos)
                spans.Add(new MarkdownSpan(pos, m.Index - pos, MarkdownSpanKind.Normal));

            var part = m.Value;
            var kind = part.StartsWith("**") ? MarkdownSpanKind.Bold
                : part.StartsWith('`') ? MarkdownSpanKind.Code
                : MarkdownSpanKind.Link;
            spans.Add(new MarkdownSpan(m.Index, m.Length, kind));
            pos = m.Index + m.Length;
        }
        if (pos < line.Length)
            spans.Add(new MarkdownSpan(pos, line.Length - pos, MarkdownSpanKind.Normal));
    }

    public static void Highlight(RichTextBlock rtb, string content, bool isDarkTheme = true)
    {
        rtb.Blocks.Clear();
        if (string.IsNullOrEmpty(content)) return;

        bool inFence = false;
        var spans = new List<MarkdownSpan>();

        var lines = content.Split('\n');
        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd('\r');
            var paragraph = new Paragraph
            {
                Margin = new Thickness(0, 1, 0, 1),
                FontFamily = MonoFont
            };

            bool wasFence = inFence || IsFenceLine(line);
            TokenizeLine(line, ref inFence, spans);

            bool hasText = false;
            foreach (var span in spans)
            {
                if (span.Length == 0) continue;
                hasText = true;
                var run = new Run
                {
                    Text = line.Substring(span.Start, span.Length),
                    Foreground = GetBrush(span.Kind, isDarkTheme),
                    FontFamily = MonoFont
                };
                int weight = GetWeight(span.Kind);
                if (weight == 700) run.FontWeight = FontWeights.Bold;
                else if (weight == 600) run.FontWeight = FontWeights.SemiBold;
                if (span.Kind == MarkdownSpanKind.Link) run.TextDecorations = TextDecorations.Underline;
                paragraph.Inlines.Add(run);
            }

            // An empty line still needs a run (keeps its height)
            if (!hasText)
                paragraph.Inlines.Add(new Run { Text = wasFence ? string.Empty : " ", FontFamily = MonoFont });

            rtb.Blocks.Add(paragraph);
        }
    }
}
