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

    private static readonly SolidColorBrush HeadingBrush = new(Color.FromArgb(255, 76, 201, 240)); // #4CC9F0
    private static readonly SolidColorBrush BoldBrush = new(Color.FromArgb(255, 255, 209, 102));   // #FFD166
    private static readonly SolidColorBrush CodeBrush = new(Color.FromArgb(255, 144, 190, 109));   // #90BE6D
    private static readonly SolidColorBrush ListBrush = new(Color.FromArgb(255, 244, 162, 97));    // #F4A261
    private static readonly SolidColorBrush LinkBrush = new(Color.FromArgb(255, 122, 162, 247));   // #7AA2F7

    private static readonly SolidColorBrush DarkNormalTextBrush = new(Color.FromArgb(255, 235, 235, 235)); // #EBEBEB Crisp White
    private static readonly SolidColorBrush LightNormalTextBrush = new(Color.FromArgb(255, 30, 30, 30));    // #1E1E1E Dark Gray

    private static readonly Regex HeadingRegex = new(@"^#{1,6}\s", RegexOptions.Compiled);
    private static readonly Regex ListRegex = new(@"^(\s*(?:[-*+]|\d+\.)\s)(.*)$", RegexOptions.Compiled);
    private static readonly Regex InlineRegex = new(@"(\*\*[^*\n]+\*\*|`[^`\n]+`|\[[^\]\n]+\]\([^)\n]+\))", RegexOptions.Compiled);

    public static void Highlight(RichTextBlock rtb, string content, bool isDarkTheme = true)
    {
        rtb.Blocks.Clear();
        if (string.IsNullOrEmpty(content)) return;

        bool inFence = false;
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
                    Foreground = CodeBrush,
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
                    Foreground = CodeBrush,
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
                    Foreground = HeadingBrush,
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
                    Foreground = ListBrush,
                    FontWeight = FontWeights.SemiBold,
                    FontFamily = MonoFont
                });
                remainder = listMatch.Groups[2].Value;
            }

            // Parse inline formatting
            ParseInlines(paragraph, remainder, defaultBrush);

            rtb.Blocks.Add(paragraph);
        }
    }

    private static void ParseInlines(Paragraph paragraph, string text, Brush defaultBrush)
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
                    Foreground = BoldBrush,
                    FontWeight = FontWeights.Bold,
                    FontFamily = MonoFont
                });
            }
            else if (part.StartsWith('`') && part.EndsWith('`') && part.Length >= 2)
            {
                paragraph.Inlines.Add(new Run
                {
                    Text = part,
                    Foreground = CodeBrush,
                    FontFamily = MonoFont
                });
            }
            else if (part.StartsWith('[') && part.Contains("](") && part.EndsWith(')'))
            {
                paragraph.Inlines.Add(new Run
                {
                    Text = part,
                    Foreground = LinkBrush,
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
