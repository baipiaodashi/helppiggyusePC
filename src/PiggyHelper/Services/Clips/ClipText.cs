using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace PiggyHelper.Services.Clips;

/// <summary>剪贴板文字的小工具：识别网址/邮箱/颜色/路径、算指纹、做预览和打码、显示"几分钟前"。</summary>
public static class ClipText
{
    private const RegexOptions Opt = RegexOptions.CultureInvariant;

    private static readonly Regex UrlRe = new(@"^(?:(?:https?|ftp)://\S+|www\.[^\s.]+\.\S+)$", Opt | RegexOptions.IgnoreCase);
    private static readonly Regex EmailRe = new(@"^[A-Za-z0-9._%+\-]+@[A-Za-z0-9\-]+(?:\.[A-Za-z0-9\-]+)+$", Opt);
    private static readonly Regex HexColorRe = new(@"^#(?:[0-9a-f]{3}|[0-9a-f]{4}|[0-9a-f]{6}|[0-9a-f]{8})$", Opt | RegexOptions.IgnoreCase);
    private static readonly Regex RgbColorRe = new(@"^rgba?\(\s*\d{1,3}\s*,\s*\d{1,3}\s*,\s*\d{1,3}\s*(?:,\s*(?:\d*\.)?\d+\s*)?\)$", Opt | RegexOptions.IgnoreCase);
    private static readonly Regex PathRe = new(@"^(?:[A-Za-z]:\\|\\\\[^\\\s]+\\)[^<>""|?*\r\n]*$", Opt);

    /// <summary>识别一段文字是不是网址、邮箱、颜色或文件路径；都不是就返回 null。</summary>
    public static string? Classify(string text)
    {
        var t = text.Trim();
        if (t.Length == 0 || t.Length > 2048 || t.Contains('\n')) return null;
        if (UrlRe.IsMatch(t)) return "link";
        if (EmailRe.IsMatch(t)) return "email";
        if (HexColorRe.IsMatch(t) || RgbColorRe.IsMatch(t)) return "color";
        if (PathRe.IsMatch(t)) return "path";
        return null;
    }

    /// <summary>内容指纹（SHA-256）。种类不同的内容不会算成同一个指纹。</summary>
    public static string Hash(ClipKind kind, string content)
    {
        var bytes = Encoding.UTF8.GetBytes(kind + "\n" + content);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    public static string HashBytes(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    /// <summary>列表里显示的预览：连续的空白和空行合并，太长的截断加省略号。</summary>
    public static string Preview(string text, int maxChars = 300)
    {
        var sb = new StringBuilder(Math.Min(text.Length, maxChars) + 2);
        bool pendingSpace = false, pendingLine = false, truncated = false;
        foreach (char ch in text)
        {
            if (ch == '\n')
            {
                if (sb.Length > 0) pendingLine = true;
                pendingSpace = false;
                continue;
            }
            if (ch == '\r') continue;
            if (char.IsWhiteSpace(ch))
            {
                if (sb.Length > 0 && !pendingLine) pendingSpace = true;
                continue;
            }
            if (sb.Length >= maxChars) { truncated = true; break; }
            if (pendingLine) sb.Append('\n');
            else if (pendingSpace) sb.Append(' ');
            pendingLine = pendingSpace = false;
            sb.Append(ch);
        }
        if (truncated) sb.Append('…');
        return sb.ToString();
    }

    /// <summary>敏感内容打码：只露出开头两个字，其余用圆点代替。</summary>
    public static string Mask(string text)
    {
        var t = text.Trim();
        int dots = Math.Clamp(t.Length - 2, 6, 12);
        if (t.Length <= 4) return new string('•', 8);
        var head = t[..2].Replace('\r', ' ').Replace('\n', ' ');
        return head + new string('•', dots);
    }

    /// <summary>"刚刚""5 分钟前""昨天 21:30""11月3日"。</summary>
    public static string Ago(DateTime then, DateTime now)
    {
        var d = now - then;
        if (d.TotalSeconds < 60) return "刚刚";
        if (d.TotalMinutes < 60) return $"{(int)d.TotalMinutes} 分钟前";
        if (then.Date == now.Date) return $"{(int)d.TotalHours} 小时前";
        if (then.Date == now.Date.AddDays(-1)) return "昨天 " + then.ToString("HH:mm");
        if (then.Year == now.Year) return $"{then.Month}月{then.Day}日";
        return $"{then.Year}年{then.Month}月{then.Day}日";
    }

    /// <summary>"512 KB""3.4 MB"。</summary>
    public static string SizeText(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0} KB",
        < 1024L * 1024 * 1024 => $"{bytes / 1024.0 / 1024:0.0} MB",
        _ => $"{bytes / 1024.0 / 1024 / 1024:0.00} GB",
    };
}
