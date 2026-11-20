using System.Text.RegularExpressions;

namespace PiggyHelper.Services.Clips;

/// <summary>
/// 判断一段复制的文字像不像密码、密钥、身份证号、银行卡号。
/// 像的话，小猪剪贴板会打码显示，保存到硬盘时加密（也可以在设置里选"干脆不记"）。
/// 只看文字本身，不联网、不上传。
/// </summary>
public static class SensitiveDetector
{
    private const RegexOptions Opt = RegexOptions.CultureInvariant;
    private static readonly TimeSpan Limit = TimeSpan.FromMilliseconds(250);

    // 私钥文件
    private static readonly Regex PemRe = new(@"-----BEGIN (?:[A-Z0-9]+ )*PRIVATE KEY-----", Opt, Limit);

    // 各家网站的密钥：GitHub、OpenAI/DeepSeek 等 sk- 开头的、Slack、Google、GitLab、AWS
    private static readonly Regex TokenRe = new(
        @"(?<![A-Za-z0-9_])(?:gh[pousr]_[A-Za-z0-9]{36,}|github_pat_[A-Za-z0-9_]{50,}|xox[abprs]-[A-Za-z0-9-]{10,}" +
        @"|AIza[0-9A-Za-z_\-]{35}|glpat-[0-9A-Za-z_\-]{20,}|(?:AKIA|ASIA)[0-9A-Z]{16}(?![0-9A-Z]))", Opt, Limit);
    private static readonly Regex SkKeyRe = new(@"(?<![A-Za-z0-9_])sk-(?:proj-|ant-|or-v1-)?[A-Za-z0-9_\-]{20,}", Opt, Limit);

    // 登录凭证 JWT：三段 base64，前两段都以 eyJ 开头
    private static readonly Regex JwtRe = new(@"eyJ[A-Za-z0-9_\-]{10,}\.eyJ[A-Za-z0-9_\-]{10,}\.[A-Za-z0-9_\-]{10,}", Opt, Limit);

    // "密码：abc123""password = xxx"：中文标签后面可以跟冒号、"是"或空格，英文标签必须跟冒号或等号
    private static readonly Regex LabeledZhRe = new(
        @"(?:密码|口令|密钥|支付密码|登录密码)(?:\s*[:：=]\s*|\s*[是为][:：]?\s*|\s+)[!-~]{4,}", Opt, Limit);
    private static readonly Regex LabeledEnRe = new(
        @"(?<![A-Za-z])(?:password|passwd|pwd|passcode|secret|api[ _\-]?key|access[ _\-]?token)\s*[:：=]\s*[""']?[!-~]{4,}",
        Opt | RegexOptions.IgnoreCase, Limit);

    // 身份证号（整段就是号码，或者前面写着"身份证/证件号"）
    private static readonly Regex IdWholeRe = new(@"^\d{17}[\dXx]$", Opt, Limit);
    private static readonly Regex IdLabeledRe = new(@"(?:身份证|证件)[号码]{0,2}\s*[:：]?\s*(\d{17}[\dXx])(?!\d)", Opt, Limit);

    // 银行卡号（整段就是 16~19 位号码，或者前面写着"银行卡/卡号"）
    private static readonly Regex CardWholeRe = new(@"^[3-6]\d{3}(?:[ \-]?\d){12,15}$", Opt, Limit);
    private static readonly Regex CardLabeledRe = new(@"(?:银行卡|信用卡|储蓄卡|借记卡|卡号)[号码]{0,2}\s*[:：]?\s*(\d(?:[ \-]?\d){15,18})(?!\d)", Opt, Limit);

    private static readonly int[] IdWeights = { 7, 9, 10, 5, 8, 4, 2, 1, 6, 3, 7, 9, 10, 5, 8, 4, 2 };
    private const string IdCheckCodes = "10X98765432";

    /// <summary>像敏感内容就返回原因（"密钥""密码""身份证号""银行卡号"），否则返回 null。</summary>
    public static string? Check(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        // 特别长的文字（文章、代码文件）只看开头一段，避免卡顿
        var t = text.Length > 20000 ? text[..20000] : text;
        try
        {
            if (PemRe.IsMatch(t)) return "密钥";
            if (TokenRe.IsMatch(t)) return "密钥";
            foreach (Match m in SkKeyRe.Matches(t))
                if (CountDigits(m.Value) >= 3) return "密钥";
            if (JwtRe.IsMatch(t)) return "密钥";
            if (LabeledZhRe.IsMatch(t) || LabeledEnRe.IsMatch(t)) return "密码";

            var whole = t.Trim();
            if (IdWholeRe.IsMatch(whole) && IsChineseId(whole)) return "身份证号";
            foreach (Match m in IdLabeledRe.Matches(t))
                if (IsChineseId(m.Groups[1].Value)) return "身份证号";

            if (CardWholeRe.IsMatch(whole) && IsBankCard(whole)) return "银行卡号";
            foreach (Match m in CardLabeledRe.Matches(t))
                if (IsBankCard(m.Groups[1].Value)) return "银行卡号";
        }
        catch (RegexMatchTimeoutException) { }
        return null;
    }

    public static bool IsSensitive(string? text) => Check(text) != null;

    /// <summary>18 位身份证号：校验码对得上，出生日期也是真实的日子。</summary>
    public static bool IsChineseId(string id)
    {
        if (id.Length != 18) return false;
        int sum = 0;
        for (int i = 0; i < 17; i++)
        {
            if (id[i] is < '0' or > '9') return false;
            sum += (id[i] - '0') * IdWeights[i];
        }
        if (char.ToUpperInvariant(id[17]) != IdCheckCodes[sum % 11]) return false;
        int year = int.Parse(id.AsSpan(6, 4)), month = int.Parse(id.AsSpan(10, 2)), day = int.Parse(id.AsSpan(12, 2));
        if (year < 1900 || year > DateTime.Now.Year || month is < 1 or > 12) return false;
        return day >= 1 && day <= DateTime.DaysInMonth(year, month);
    }

    /// <summary>银行卡号：16~19 位，符合 Luhn 校验，而且不是一串同样的数字。</summary>
    public static bool IsBankCard(string raw)
    {
        Span<char> digits = stackalloc char[24];
        int n = 0;
        foreach (char c in raw)
        {
            if (c is ' ' or '-') continue;
            if (c is < '0' or > '9' || n >= digits.Length) return false;
            digits[n++] = c;
        }
        if (n is < 16 or > 19) return false;
        bool allSame = true;
        for (int i = 1; i < n; i++) if (digits[i] != digits[0]) { allSame = false; break; }
        if (allSame) return false;

        int total = 0;
        bool dbl = false;
        for (int i = n - 1; i >= 0; i--)
        {
            int d = digits[i] - '0';
            if (dbl) { d *= 2; if (d > 9) d -= 9; }
            total += d;
            dbl = !dbl;
        }
        return total % 10 == 0;
    }

    private static int CountDigits(string s)
    {
        int n = 0;
        foreach (char c in s) if (c is >= '0' and <= '9') n++;
        return n;
    }
}
