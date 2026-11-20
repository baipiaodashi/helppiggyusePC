using PiggyHelper.Services.Clips;
using Xunit;

public class ClipTextTests
{
    [Theory]
    [InlineData("https://www.bilibili.com/video/BV1xx", "link")]
    [InlineData("  http://example.com  ", "link")]
    [InlineData("www.baidu.com", "link")]
    [InlineData("ftp://files.example.org/a.zip", "link")]
    [InlineData("xiaoming@qq.com", "email")]
    [InlineData("a.b-c+d@mail.example.edu.cn", "email")]
    [InlineData("#FF8FA3", "color")]
    [InlineData("#fff", "color")]
    [InlineData("rgb(255, 143, 163)", "color")]
    [InlineData("rgba(0,0,0,0.5)", "color")]
    [InlineData(@"C:\Users\xiaoming\Desktop\作业.docx", "path")]
    [InlineData(@"\\server\share\folder", "path")]
    public void Classify_recognizes(string text, string expected) => Assert.Equal(expected, ClipText.Classify(text));

    [Theory]
    [InlineData("")]
    [InlineData("你好呀")]
    [InlineData("https://a.com\nhttps://b.com")]
    [InlineData("看看这个 https://a.com")]
    [InlineData("#ggg")]
    [InlineData("#12345")]
    [InlineData("hello@")]
    [InlineData(@"C:\a|b")]
    [InlineData("www.")]
    public void Classify_plain_text_is_null(string text) => Assert.Null(ClipText.Classify(text));

    [Fact]
    public void Hash_is_stable_and_kind_specific()
    {
        var a = ClipText.Hash(ClipKind.Text, "abc");
        Assert.Equal(a, ClipText.Hash(ClipKind.Text, "abc"));
        Assert.NotEqual(a, ClipText.Hash(ClipKind.Files, "abc"));
        Assert.NotEqual(a, ClipText.Hash(ClipKind.Text, "abd"));
        Assert.Equal(64, a.Length);
        Assert.Equal(a.ToLowerInvariant(), a);
    }

    [Fact]
    public void Preview_collapses_whitespace_and_blank_lines()
    {
        Assert.Equal("a b\nc", ClipText.Preview("  a \t  b \r\n\r\n\n   c  \n"));
        Assert.Equal("第一行\n第二行", ClipText.Preview("第一行\r\n\r\n第二行"));
    }

    [Fact]
    public void Preview_truncates_with_ellipsis()
    {
        var p = ClipText.Preview(new string('x', 500), 10);
        Assert.Equal(new string('x', 10) + "…", p);
        Assert.Equal("abc", ClipText.Preview("abc", 3));
    }

    [Fact]
    public void Mask_shows_only_first_two_chars()
    {
        var m = ClipText.Mask("sk-1234567890abcdef");
        Assert.StartsWith("sk", m);
        Assert.DoesNotContain("123", m);
        Assert.Equal(new string('•', 8), ClipText.Mask("1234"));
        Assert.Equal("ab" + new string('•', 6), ClipText.Mask("abcde"));
    }

    [Fact]
    public void Ago_reads_like_a_person()
    {
        var now = new DateTime(2026, 11, 20, 15, 0, 0);
        Assert.Equal("刚刚", ClipText.Ago(now.AddSeconds(-20), now));
        Assert.Equal("5 分钟前", ClipText.Ago(now.AddMinutes(-5), now));
        Assert.Equal("3 小时前", ClipText.Ago(now.AddHours(-3), now));
        Assert.Equal("昨天 21:30", ClipText.Ago(new DateTime(2026, 11, 19, 21, 30, 0), now));
        Assert.Equal("11月3日", ClipText.Ago(new DateTime(2026, 11, 3, 8, 0, 0), now));
        Assert.Equal("2025年12月31日", ClipText.Ago(new DateTime(2025, 12, 31, 8, 0, 0), now));
    }

    [Theory]
    [InlineData(512, "512 B")]
    [InlineData(2048, "2 KB")]
    [InlineData(3565158, "3.4 MB")]
    public void SizeText_formats(long bytes, string expected) => Assert.Equal(expected, ClipText.SizeText(bytes));
}

public class SensitiveDetectorTests
{
    [Theory]
    [InlineData("sk-1a2b3c4d5e6f7a8b9c0d1e2f3a4b5c6d", "密钥")]
    [InlineData("我的 key 是 sk-proj-Ab3dEf5hIj7lMn9pQr1tUv2xYz4", "密钥")]
    [InlineData("ghp_abcdefghijklmnopqrstuvwxyz0123456789", "密钥")]
    [InlineData("AKIAIOSFODNN7EXAMPLE", "密钥")]
    [InlineData("AIzaSyA1234567890abcdefghijklmnopqrstuv", "密钥")]
    [InlineData("-----BEGIN RSA PRIVATE KEY-----\nMIIEow...", "密钥")]
    [InlineData("-----BEGIN OPENSSH PRIVATE KEY-----", "密钥")]
    [InlineData("eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0.dozjgNryP4J3jVmNHl0w5N_XgL0n3I9PlFUP0THsR8U", "密钥")]
    [InlineData("WiFi密码：Piggy2026!", "密码")]
    [InlineData("QQ 密码是 abc12345", "密码")]
    [InlineData("登录密码 qwerty123", "密码")]
    [InlineData("password: hunter22", "密码")]
    [InlineData("API_KEY = \"abcd1234efgh\"", "密码")]
    [InlineData("11010519491231002X", "身份证号")]
    [InlineData("身份证号码：11010519491231002x", "身份证号")]
    [InlineData("4111111111111111", "银行卡号")]
    [InlineData("4111 1111 1111 1111", "银行卡号")]
    [InlineData("银行卡号: 6222 0000 0000 0000 4", "银行卡号")]
    public void Detects(string text, string reason) => Assert.Equal(reason, SensitiveDetector.Check(text));

    [Theory]
    [InlineData("今天的作业是第三单元的练习题")]
    [InlineData("https://www.bilibili.com/video/BV1GJ411x7h7")]
    [InlineData("13812345678")]
    [InlineData("忘记密码：请点击这里找回")]
    [InlineData("Forgot your password? Click here")]
    [InlineData("sk-learn is a python library")]
    [InlineData("task-abcdefghijklmnopqrstuvwxyz")]
    [InlineData("110105194912310021")]            // 校验码不对
    [InlineData("110105194913310026")]            // 13 月不存在
    [InlineData("4111111111111112")]              // Luhn 不对
    [InlineData("1111111111111111")]              // 一串同样的数字
    [InlineData("202611201234567890")]            // 订单号
    [InlineData("")]
    [InlineData("   ")]
    public void Ignores(string text) => Assert.Null(SensitiveDetector.Check(text));

    [Fact]
    public void Chinese_id_checksum()
    {
        Assert.True(SensitiveDetector.IsChineseId("11010519491231002X"));
        Assert.False(SensitiveDetector.IsChineseId("11010519491231002Y"));
        Assert.False(SensitiveDetector.IsChineseId("1101051949123100"));
    }

    [Fact]
    public void Bank_card_luhn()
    {
        Assert.True(SensitiveDetector.IsBankCard("4111-1111-1111-1111"));
        Assert.True(SensitiveDetector.IsBankCard("5500 0000 0000 0004"));
        Assert.False(SensitiveDetector.IsBankCard("4111 1111 1111"));
        Assert.False(SensitiveDetector.IsBankCard("4111a111111111111"));
    }

    [Fact]
    public void Very_long_text_is_fast()
    {
        var big = string.Concat(Enumerable.Repeat("这是一段很长的文章，里面没有密码。abc 123 ", 50000));
        var sw = System.Diagnostics.Stopwatch.StartNew();
        Assert.Null(SensitiveDetector.Check(big));
        Assert.True(sw.ElapsedMilliseconds < 2000);
    }
}

public class ClipHistoryTests
{
    private static readonly DateTime T0 = new(2026, 11, 20, 10, 0, 0);

    private static ClipItem Text(string s) => new() { Kind = ClipKind.Text, Text = s, Hash = ClipText.Hash(ClipKind.Text, s) };

    [Fact]
    public void New_items_go_to_top()
    {
        var h = new ClipHistory();
        h.Add(Text("a"), T0);
        h.Add(Text("b"), T0.AddMinutes(1));
        Assert.Equal(new[] { "b", "a" }, h.Items.Select(x => x.Text));
    }

    [Fact]
    public void Same_content_is_kept_once_and_moves_up()
    {
        var h = new ClipHistory();
        var (first, isNew1) = h.Add(Text("a"), T0);
        h.Add(Text("b"), T0.AddMinutes(1));
        var again = Text("a");
        again.Html = "<b>a</b>";
        again.SourceName = "微信";
        again.SourcePath = @"C:\Program Files\Tencent\WeChat\WeChat.exe";
        var (item, isNew2) = h.Add(again, T0.AddMinutes(2));

        Assert.True(isNew1);
        Assert.False(isNew2);
        Assert.Same(first, item);
        Assert.Equal(2, h.Count);
        Assert.Equal("a", h.Items[0].Text);
        Assert.Equal(2, item.UseCount);
        Assert.Equal("<b>a</b>", item.Html);
        Assert.Equal("微信", item.SourceName);
        Assert.Equal(T0, item.CreatedAt);
        Assert.Equal(T0.AddMinutes(2), item.UsedAt);
    }

    [Fact]
    public void Touch_moves_to_top()
    {
        var h = new ClipHistory();
        var (a, _) = h.Add(Text("a"), T0);
        h.Add(Text("b"), T0.AddMinutes(1));
        Assert.True(h.Touch(a.Id, T0.AddMinutes(5)));
        Assert.Equal("a", h.Items[0].Text);
        Assert.Equal(2, a.UseCount);
        Assert.False(h.Touch("nope", T0));
    }

    [Fact]
    public void Prune_keeps_favorites_and_limits_count_and_age()
    {
        var h = new ClipHistory();
        for (int i = 0; i < 10; i++) h.Add(Text("t" + i), T0.AddDays(i));
        var oldest = h.Items[^1];
        h.SetFavorite(oldest.Id, true);

        Assert.Equal(4, h.CountPrunable(maxItems: 5, keepDays: 0, now: T0.AddDays(10)));
        Assert.Equal(0, h.CountPrunable(maxItems: 100, keepDays: 0, now: T0.AddDays(10)));
        Assert.Equal(10, h.Count);   // 只是数一数，没有删

        var removed = h.Prune(maxItems: 5, keepDays: 0, now: T0.AddDays(10));
        Assert.Equal(4, removed.Count);   // t1..t4（t0 收藏了）
        Assert.Equal(2, h.CountPrunable(maxItems: 100, keepDays: 3, now: T0.AddDays(10)));
        Assert.Equal(6, h.Count);
        Assert.Contains(h.Items, x => x.Text == "t0");
        Assert.DoesNotContain(h.Items, x => x.Text == "t4");

        removed = h.Prune(maxItems: 100, keepDays: 3, now: T0.AddDays(10));
        // 留下 t7 t8 t9（3 天内）+ 收藏的 t0
        Assert.Equal(new[] { "t9", "t8", "t7", "t0" }, h.Items.Select(x => x.Text));
        Assert.Equal(2, removed.Count);
    }

    [Fact]
    public void Clear_keeps_favorites_by_default()
    {
        var h = new ClipHistory();
        var (a, _) = h.Add(Text("a"), T0);
        h.Add(Text("b"), T0);
        h.SetFavorite(a.Id, true);
        var removed = h.Clear();
        Assert.Single(removed);
        Assert.Single(h.Items);
        Assert.Equal("a", h.Items[0].Text);
        h.Clear(keepFavorites: false);
        Assert.Equal(0, h.Count);
    }

    [Fact]
    public void Query_filters_and_searches()
    {
        var h = new ClipHistory();
        h.Add(Text("数学作业第三题"), T0);
        var link = Text("https://www.bilibili.com");
        link.SubKind = "link";
        h.Add(link, T0.AddMinutes(1));
        var img = new ClipItem { Kind = ClipKind.Image, Hash = "img1", Image = "img1.png", SourceName = "微信" };
        h.Add(img, T0.AddMinutes(2));
        var files = new ClipItem { Kind = ClipKind.Files, Text = @"D:\作业\数学.docx", Files = new() { @"D:\作业\数学.docx" }, Hash = "f1" };
        h.Add(files, T0.AddMinutes(3));
        h.SetFavorite(img.Id, true);

        Assert.Equal(4, h.Query(ClipFilter.All, null).Count);
        Assert.Single(h.Query(ClipFilter.Image, ""));
        Assert.Single(h.Query(ClipFilter.Favorite, null));
        Assert.Equal(2, h.Query(ClipFilter.Text, null).Count);
        Assert.Equal(2, h.Query(ClipFilter.All, "数学").Count);
        Assert.Single(h.Query(ClipFilter.All, "数学 作业 第三"));
        Assert.Single(h.Query(ClipFilter.All, "BILIBILI"));
        Assert.Single(h.Query(ClipFilter.All, "微信"));
        Assert.Single(h.Query(ClipFilter.All, "网址"));
        Assert.Single(h.Query(ClipFilter.All, "图片"));
        Assert.Empty(h.Query(ClipFilter.Text, "微信"));
        Assert.Equal(1, h.CountOf(ClipFilter.Files));
        Assert.Contains("img1.png", h.ImagesInUse());
    }

    [Fact]
    public void Load_sorts_and_drops_duplicates()
    {
        var a = Text("a"); a.UsedAt = T0;
        var b = Text("b"); b.UsedAt = T0.AddHours(1);
        var dup = Text("a"); dup.UsedAt = T0.AddMinutes(-5);
        var h = new ClipHistory();
        h.Load(new[] { a, b, dup });
        Assert.Equal(new[] { "b", "a" }, h.Items.Select(x => x.Text));
        Assert.Same(a, h.Items[1]);
    }

    [Fact]
    public void Remove_returns_item()
    {
        var h = new ClipHistory();
        var (a, _) = h.Add(Text("a"), T0);
        Assert.Same(a, h.Remove(a.Id));
        Assert.Null(h.Remove(a.Id));
        Assert.Equal(0, h.Count);
    }
}
