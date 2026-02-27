using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace meshIt.Controls;

/// <summary>
/// Parses markdown-like text into WPF inlines for rich text rendering.
/// Supports **bold**, *italic*, `code`, and ```code blocks```.
/// </summary>
public static class RichTextParser
{
    private static readonly Regex BoldRegex = new(@"\*\*(.+?)\*\*", RegexOptions.Compiled);
    private static readonly Regex ItalicRegex = new(@"\*(.+?)\*", RegexOptions.Compiled);
    private static readonly Regex CodeBlockRegex = new(@"```(.+?)```", RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex InlineCodeRegex = new(@"`(.+?)`", RegexOptions.Compiled);

    /// <summary>
    /// Parse message text and add styled inlines to a TextBlock.
    /// </summary>
    public static void ApplyRichText(TextBlock textBlock, string text, bool isSent)
    {
        textBlock.Inlines.Clear();

        // Simple pass: handle code blocks first, then bold, italic, inline code
        var remaining = text;

        // Replace code blocks
        remaining = CodeBlockRegex.Replace(remaining, match =>
        {
            return $"\x01CODE:{match.Groups[1].Value}\x01";
        });

        // Replace inline code
        remaining = InlineCodeRegex.Replace(remaining, match =>
        {
            return $"\x02INLINE:{match.Groups[1].Value}\x02";
        });

        // Replace bold
        remaining = BoldRegex.Replace(remaining, match =>
        {
            return $"\x03BOLD:{match.Groups[1].Value}\x03";
        });

        // Replace italic
        remaining = ItalicRegex.Replace(remaining, match =>
        {
            return $"\x04ITALIC:{match.Groups[1].Value}\x04";
        });

        // Now parse and create inlines
        int i = 0;
        while (i < remaining.Length)
        {
            if (remaining[i] == '\x01')
            {
                var end = remaining.IndexOf('\x01', i + 1);
                if (end > i)
                {
                    var content = remaining[(i + 6)..end]; // skip "CODE:"
                    textBlock.Inlines.Add(new Run(content)
                    {
                        FontFamily = new FontFamily("Consolas"),
                        Background = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
                        Foreground = new SolidColorBrush(Color.FromRgb(200, 200, 200))
                    });
                    i = end + 1;
                    continue;
                }
            }
            else if (remaining[i] == '\x02')
            {
                var end = remaining.IndexOf('\x02', i + 1);
                if (end > i)
                {
                    var content = remaining[(i + 8)..end]; // skip "INLINE:"
                    textBlock.Inlines.Add(new Run(content)
                    {
                        FontFamily = new FontFamily("Consolas"),
                        Background = new SolidColorBrush(Color.FromArgb(40, 124, 77, 255)),
                        FontSize = 12
                    });
                    i = end + 1;
                    continue;
                }
            }
            else if (remaining[i] == '\x03')
            {
                var end = remaining.IndexOf('\x03', i + 1);
                if (end > i)
                {
                    var content = remaining[(i + 6)..end]; // skip "BOLD:"
                    textBlock.Inlines.Add(new Bold(new Run(content)));
                    i = end + 1;
                    continue;
                }
            }
            else if (remaining[i] == '\x04')
            {
                var end = remaining.IndexOf('\x04', i + 1);
                if (end > i)
                {
                    var content = remaining[(i + 8)..end]; // skip "ITALIC:"
                    textBlock.Inlines.Add(new Italic(new Run(content)));
                    i = end + 1;
                    continue;
                }
            }

            // Regular text — collect until next special char
            var nextSpecial = remaining.IndexOfAny(new[] { '\x01', '\x02', '\x03', '\x04' }, i);
            if (nextSpecial < 0) nextSpecial = remaining.Length;
            textBlock.Inlines.Add(new Run(remaining[i..nextSpecial]));
            i = nextSpecial;
        }
    }
}
