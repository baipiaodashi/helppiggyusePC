using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace PiggyHelper.Controls;

/// <summary>把 AI 回答里常见的 Markdown（标题、加粗、列表、代码）简单排版成好看的文字。</summary>
public static class MiniMarkdown
{
    private static readonly Regex Inline = new(@"(\*\*(.+?)\*\*|`([^`]+)`)", RegexOptions.Compiled);

    public static void Render(Panel target, string markdown, Brush text, Brush accent)
    {
        target.Children.Clear();
        var lines = markdown.Replace("\r", "").Split('\n');
        bool inCode = false;
        var code = new List<string>();

        foreach (var raw in lines)
        {
            var line = raw.TrimEnd();
            if (line.TrimStart().StartsWith("```"))
            {
                if (inCode) { target.Children.Add(CodeBlock(string.Join("\n", code))); code.Clear(); }
                inCode = !inCode;
                continue;
            }
            if (inCode) { code.Add(raw); continue; }
            if (line.Length == 0) continue;
            if (Regex.IsMatch(line, @"^\s*(-{3,}|\*{3,})\s*$")) continue; // 分割线

            var tb = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 14.5, LineHeight = 23, Foreground = text, Margin = new Thickness(0, 0, 0, 6) };
            var m = Regex.Match(line, @"^\s*(#{1,6})\s+(.*)$");
            if (m.Success)
            {
                tb.FontSize = 16;
                tb.FontWeight = FontWeights.Bold;
                tb.Margin = new Thickness(0, 6, 0, 6);
                AddInlines(tb, m.Groups[2].Value, accent);
            }
            else if ((m = Regex.Match(line, @"^(\s*)[-*+]\s+(.*)$")).Success)
            {
                tb.Margin = new Thickness(8 + m.Groups[1].Value.Length * 6, 0, 0, 4);
                tb.Inlines.Add(new Run("•  ") { Foreground = accent, FontWeight = FontWeights.Bold });
                AddInlines(tb, m.Groups[2].Value, accent);
            }
            else if ((m = Regex.Match(line, @"^(\s*)(\d+)[.、)]\s*(.*)$")).Success)
            {
                tb.Margin = new Thickness(4 + m.Groups[1].Value.Length * 6, 0, 0, 4);
                tb.Inlines.Add(new Run(m.Groups[2].Value + ". ") { Foreground = accent, FontWeight = FontWeights.Bold });
                AddInlines(tb, m.Groups[3].Value, accent);
            }
            else if (line.StartsWith(">"))
            {
                tb.Foreground = (Brush)Application.Current.FindResource("SubTextBrush");
                AddInlines(tb, line.TrimStart('>', ' '), accent);
            }
            else
            {
                AddInlines(tb, line, accent);
            }
            target.Children.Add(tb);
        }
        if (inCode && code.Count > 0) target.Children.Add(CodeBlock(string.Join("\n", code)));
    }

    private static void AddInlines(TextBlock tb, string text, Brush accent)
    {
        int pos = 0;
        foreach (Match m in Inline.Matches(text))
        {
            if (m.Index > pos) tb.Inlines.Add(new Run(text[pos..m.Index]));
            if (m.Groups[2].Success)
                tb.Inlines.Add(new Run(m.Groups[2].Value) { FontWeight = FontWeights.Bold });
            else
                tb.Inlines.Add(new Run(" " + m.Groups[3].Value + " ")
                {
                    FontFamily = new FontFamily("Consolas, Microsoft YaHei UI"),
                    Background = (Brush)Application.Current.FindResource("PinkSoftBrush"),
                    Foreground = accent,
                });
            pos = m.Index + m.Length;
        }
        if (pos < text.Length) tb.Inlines.Add(new Run(text[pos..]));
    }

    private static Border CodeBlock(string code) => new()
    {
        Background = new SolidColorBrush(Color.FromRgb(0x3E, 0x2D, 0x3C)),
        CornerRadius = new CornerRadius(12),
        Padding = new Thickness(14, 10, 14, 10),
        Margin = new Thickness(0, 4, 0, 10),
        Child = new TextBlock
        {
            Text = code,
            FontFamily = new FontFamily("Consolas, Microsoft YaHei UI"),
            FontSize = 13,
            Foreground = Brushes.White,
            TextWrapping = TextWrapping.Wrap,
        },
    };
}
