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

/// <summary>
/// Lightweight Markdown syntax highlighter for RichTextBlock preview.
/// Replicates DirSnap v1 Markdown syntax colors:
/// - Headings: #4CC9F0 (Sky Blue)
/// - Bold: #FFD166 (Warm Yellow)
/// - Code & Fences: #90BE6D (Soft Green)
/// - Lists: #F4A261 (Orange)
/// - Links: #7AA2F7 (Blue)
/// </summary>
public static class MarkdownHighlighter
{
    private static readonly FontFamily MonoFont = new("Consolas, Cascadia Code, Courier New");

    // Dark Theme Brushes (optimized for dark background #1F1F1F / #252526)
    private static readonly SolidColorBrush DarkHeadingBrush = new(Color.FromArgb(255, 76, 201, 240)); // #4CC9F0 Sky Blue
    private static readonly SolidColorBrush DarkBoldBrush = new(Color.FromArgb(255, 255, 209, 102));   // #FFD166 Warm Yellow
    private static readonly SolidColorBrush DarkCodeBrush = new(Color.FromArgb(255, 144, 190, 109));   // #90BE6D Soft Green
    private static readonly SolidColorBrush DarkListBrush = new(Color.FromArgb(255, 244, 162, 97));    // #F4A261 Orange
    private static readonly SolidColorBrush DarkLinkBrush = new(Color.FromArgb(255, 122, 162, 247));   // #7AA2F7 Blue
    private static readonly SolidColorBrush DarkNormalTextBrush = new(Color.FromArgb(255, 235, 235, 235)); // #EBEBEB Crisp White

    // Light Theme Brushes (high-contrast, rich tones on light gray background)
    private static readonly SolidColorBrush LightHeadingBrush = new(Color.FromArgb(255, 5, 80, 174));    // #0550AE Deep Cobalt / Azure
    private static readonly SolidColorBrush LightBoldBrush = new(Color.FromArgb(255, 161, 92, 0));       // #A15C00 Rich Dark Amber
    private static readonly SolidColorBrush LightCodeBrush = new(Color.FromArgb(255, 22, 101, 52));     // #166534 Deep Forest Green
    private static readonly SolidColorBrush LightListBrush = new(Color.FromArgb(255, 194, 65, 12));      // #C2410C Rich Terracotta / Rust
    private static readonly SolidColorBrush LightLinkBrush = new(Color.FromArgb(255, 9, 105, 218));     // #0969DA GitHub / Hyperlink Blue
    private static readonly SolidColorBrush LightNormalTextBrush = new(Color.FromArgb(255, 31, 35, 40)); // #1F2328 Deep Charcoal / Dark Gray

    private static readonly Regex HeadingRegex = new(@"^#{1,6}\s", RegexOptions.Compiled);
    private static readonly Regex ListRegex = new(@"^(\s*(?:[-*+]|\d+\.)\s)(.*)$", RegexOptions.Compiled);
    private static readonly Regex InlineRegex = new(@"(\*\*[^*\n]+\*\*|`[^`\n]+`|\[[^\]\n]+\]\([^)\n]+\))", RegexOptions.Compiled);

    public static void Highlight(RichTextBlock rtb, string content, bool isDarkTheme = true)
    {
        rtb.Blocks.Clear();
        if (string.IsNullOrEmpty(content)) return;

        bool inFence = false;
        var headingBrush = isDarkTheme ? DarkHeadingBrush : LightHeadingBrush;
        var boldBrush = isDarkTheme ? DarkBoldBrush : LightBoldBrush;
        var codeBrush = isDarkTheme ? DarkCodeBrush : LightCodeBrush;
        var listBrush = isDarkTheme ? DarkListBrush : LightListBrush;
        var linkBrush = isDarkTheme ? DarkLinkBrush : LightLinkBrush;
        var defaultBrush = isDarkTheme ? DarkNormalTextBrush : LightNormalTextBrush;

        var lines = content.Split('\n');
        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd('\r');
            var paragraph = new Paragraph
            {
                Margin = new Thickness(0, 1, 0, 1),
                FontFamily = MonoFont
            };

            // Fenced code block ```
            if (line.TrimStart().StartsWith("```"))
            {
                paragraph.Inlines.Add(new Run
                {
                    Text = line,
                    Foreground = codeBrush,
                    FontFamily = MonoFont
                });
                rtb.Blocks.Add(paragraph);
                inFence = !inFence;
                continue;
            }

            if (inFence)
            {
                paragraph.Inlines.Add(new Run
                {
                    Text = line,
                    Foreground = codeBrush,
                    FontFamily = MonoFont
                });
                rtb.Blocks.Add(paragraph);
                continue;
            }

            // Heading line
            if (HeadingRegex.IsMatch(line))
            {
                paragraph.Inlines.Add(new Run
                {
                    Text = line,
                    Foreground = headingBrush,
                    FontWeight = FontWeights.Bold,
                    FontFamily = MonoFont
                });
                rtb.Blocks.Add(paragraph);
                continue;
            }

            // List item
            var listMatch = ListRegex.Match(line);
            string remainder = line;
            if (listMatch.Success)
            {
                paragraph.Inlines.Add(new Run
                {
                    Text = listMatch.Groups[1].Value,
                    Foreground = listBrush,
                    FontWeight = FontWeights.SemiBold,
                    FontFamily = MonoFont
                });
                remainder = listMatch.Groups[2].Value;
            }

            // Parse inline formatting
            ParseInlines(paragraph, remainder, defaultBrush, boldBrush, codeBrush, linkBrush);

            rtb.Blocks.Add(paragraph);
        }
    }

    private static void ParseInlines(
        Paragraph paragraph,
        string text,
        Brush defaultBrush,
        Brush boldBrush,
        Brush codeBrush,
        Brush linkBrush)
    {
        if (string.IsNullOrEmpty(text))
        {
            paragraph.Inlines.Add(new Run { Text = " ", FontFamily = MonoFont });
            return;
        }

        var parts = InlineRegex.Split(text);
        foreach (var part in parts)
        {
            if (string.IsNullOrEmpty(part)) continue;

            if (part.StartsWith("**") && part.EndsWith("**") && part.Length >= 4)
            {
                paragraph.Inlines.Add(new Run
                {
                    Text = part,
                    Foreground = boldBrush,
                    FontWeight = FontWeights.Bold,
                    FontFamily = MonoFont
                });
            }
            else if (part.StartsWith('`') && part.EndsWith('`') && part.Length >= 2)
            {
                paragraph.Inlines.Add(new Run
                {
                    Text = part,
                    Foreground = codeBrush,
                    FontFamily = MonoFont
                });
            }
            else if (part.StartsWith('[') && part.Contains("](") && part.EndsWith(')'))
            {
                paragraph.Inlines.Add(new Run
                {
                    Text = part,
                    Foreground = linkBrush,
                    TextDecorations = TextDecorations.Underline,
                    FontFamily = MonoFont
                });
            }
            else
            {
                paragraph.Inlines.Add(new Run
                {
                    Text = part,
                    Foreground = defaultBrush,
                    FontFamily = MonoFont
                });
            }
        }
    }
}
