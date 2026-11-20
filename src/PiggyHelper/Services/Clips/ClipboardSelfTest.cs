using System.Collections.Specialized;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PiggyHelper.Services.Clips;

/// <summary>
/// 自动测试（CI 上运行 PiggyHelper.exe --clipboard-selftest 报告.txt）：
/// 真的往系统剪贴板里放文字、网页格式、图片、文件，看小猪能不能正确记下来、该跳过的跳过、放回去时不重复记。
/// 用临时文件夹，不碰真实的剪贴板记录。
/// </summary>
public static class ClipboardSelfTest
{
    private static readonly StringBuilder Report = new();
    private static int _fails;

    public static async Task<int> RunAsync(string reportPath)
    {
        var dir = Path.Combine(Path.GetTempPath(), "PiggyClipTest-" + Guid.NewGuid().ToString("N")[..8]);
        Log($"小猪剪贴板自检  {DateTime.Now:yyyy-MM-dd HH:mm:ss}  临时文件夹 {dir}");
        ClipboardFeature? feature = null;
        try
        {
            feature = new ClipboardFeature(dir);
            var store = feature.Store;
            Check("开始监听剪贴板", feature.Monitor.Start());
            await Task.Delay(300);

            // 1. 普通文字
            var hello = "小猪测试 hello " + Guid.NewGuid().ToString("N")[..6];
            SetText(hello);
            var textItem = await WaitFor(store, x => x.Text == hello);
            Check("记下文字", textItem != null && textItem.Kind == ClipKind.Text, textItem?.SourceName);

            // 2. 再复制一次同样的：不重复，次数 +1
            await Task.Delay(200);
            SetText("中间插一条 " + Guid.NewGuid().ToString("N")[..4]);
            await WaitFor(store, x => x.Text.StartsWith("中间插一条"));
            int before = store.History.Count;
            SetText(hello);
            await WaitUntil(() => store.History.Items[0].Text == hello);
            Check("同样的内容只留一条并挪到最上面",
                store.History.Count == before && store.History.Items[0].Text == hello && textItem?.UseCount == 2,
                $"条数 {before}→{store.History.Count}，次数 {textItem?.UseCount}");

            // 3. 带网页格式的中文
            var html = new DataObject();
            html.SetData(DataFormats.UnicodeText, "红色的字");
            html.SetData(DataFormats.Html, CfHtml("<span style=\"color:red\">红色的字</span>"));
            SetData(html);
            var htmlItem = await WaitFor(store, x => x.Text == "红色的字");
            Check("网页格式里的中文不乱码", htmlItem?.Html?.Contains("红色的字") == true, htmlItem?.Html == null ? "没有网页格式" : null);

            // 4. 网址
            SetText("https://www.bilibili.com/video/BV1GJ411x7h7");
            var link = await WaitFor(store, x => x.Text.StartsWith("https://www.bilibili.com"));
            Check("认出网址", link?.SubKind == "link", link?.SubKind);

            // 5. 图片（像 QQ/微信截图那样放 DIB 位图）
            var bmp = TestBitmap(64, 48, 0);
            SetData(ImageData(bmp, png: false));
            var img = await WaitFor(store, x => x.Kind == ClipKind.Image && x.Width == 64);
            Check("记下图片（位图）", img != null && img.Height == 48 && File.Exists(store.ImagePath(img)), img == null ? "没记下" : $"{img.Width}×{img.Height}");
            if (img != null) Check("图片颜色正确、不透明", PixelOk(store.ImagePath(img)!));

            // 6. 只有 PNG 格式的图片（像截图工具、浏览器那样）
            var bmp2 = TestBitmap(120, 80, 1);
            SetData(ImageData(bmp2, png: true, onlyPng: true));
            var img2 = await WaitFor(store, x => x.Kind == ClipKind.Image && x.Width == 120);
            Check("记下图片（PNG）", img2 != null && img2.Height == 80);

            // 7. 大图有缩略图
            var big = TestBitmap(1600, 900, 2);
            SetData(ImageData(big, png: true));
            var bigItem = await WaitFor(store, x => x.Kind == ClipKind.Image && x.Width == 1600, 8000);
            Check("大图片生成缩略图", bigItem != null && File.Exists(Path.Combine(store.ThumbsDir, bigItem.Image!)));

            // 8. 文件
            var f1 = Path.Combine(dir, "作业 1.txt");
            var f2 = Path.Combine(dir, "作业 2.txt");
            File.WriteAllText(f1, "1");
            File.WriteAllText(f2, "2");
            var drop = new StringCollection { f1, f2 };
            var fileData = new DataObject();
            fileData.SetFileDropList(drop);
            SetData(fileData);
            var files = await WaitFor(store, x => x.Kind == ClipKind.Files);
            Check("记下文件", files?.Files?.Count == 2, files == null ? "没记下" : $"{files.Files?.Count} 个");

            // 9. 软件要求不记录的，不记
            int count = store.History.Count;
            var secret1 = new DataObject();
            secret1.SetData(DataFormats.UnicodeText, "密码管理器里的东西 A");
            secret1.SetData("ExcludeClipboardContentFromMonitorProcessing", new MemoryStream(new byte[4]));
            SetData(secret1);
            await Task.Delay(700);
            var secret2 = new DataObject();
            secret2.SetData(DataFormats.UnicodeText, "不进历史的东西 B");
            secret2.SetData("CanIncludeInClipboardHistory", new MemoryStream(BitConverter.GetBytes(0)));
            SetData(secret2);
            await Task.Delay(700);
            Check("软件说\"别记我\"就不记", store.History.Count == count,
                $"多了 {store.History.Count - count} 条");

            // 10. 像密钥的内容：记下但打码，硬盘上加密
            var key = "sk-" + Guid.NewGuid().ToString("N");
            SetText(key);
            var keyItem = await WaitFor(store, x => x.Text == key);
            Check("认出密钥并打码", keyItem?.Sensitive == true);

            // 11. 放回剪贴板（粘贴用）时不会又记一条
            await CheckWriteBack(store, textItem, "文字");
            await CheckWriteBack(store, img, "图片");
            await CheckWriteBack(store, files, "文件");
            if (textItem != null)
            {
                var plainOk = ClipboardWriter.Write(htmlItem ?? textItem, store.ImagesDir, plain: true) == ClipboardWriter.Result.Ok;
                await Task.Delay(300);
                Check("纯文字粘贴不带格式", plainOk && !System.Windows.Clipboard.ContainsData(DataFormats.Html));
            }

            // 12. 保存到硬盘再读回来
            store.SetFavorite(img?.Id ?? "", true);
            store.Flush();
            var json = File.ReadAllText(Path.Combine(dir, "history.json"), Encoding.UTF8);
            Check("密钥在硬盘上是加密的", !json.Contains(key));
            var again = new ClipboardStore(dir) { Limits = () => (300, 30) };
            again.Load();
            Check("关掉再打开，记录都还在", again.History.Count == store.History.Count,
                $"{store.History.Count} → {again.History.Count}");
            Check("加密的内容能解开", again.History.Items.Any(x => x.Text == key && x.Sensitive));
            Check("收藏也记住了", again.History.Items.Any(x => x.Favorite && x.Kind == ClipKind.Image));

            // 13. 条数上限：收藏的不删
            store.Limits = () => (3, 30);
            store.Prune();
            Check("超过条数会删旧的，收藏的留着",
                store.History.Items.Count(x => !x.Favorite) == 3 && store.History.Items.Any(x => x.Favorite),
                $"剩 {store.History.Count} 条");

            // 14. 设置里不记录的软件
            Check("按软件名跳过", SourceApps.IsIgnored(@"C:\Program Files\KeePass\KeePass.exe", AppSettings.DefaultIgnoreApps)
                && !SourceApps.IsIgnored(@"C:\Windows\notepad.exe", AppSettings.DefaultIgnoreApps));

            // 15. Win+V 小窗口
            await PanelTests(feature);
        }
        catch (Exception ex)
        {
            Fail("自检崩溃", ex.ToString());
        }
        finally
        {
            try { feature?.Stop(); } catch { }
            try { KeyboardHook.Shared.Stop(); } catch { }
            try { var log = Path.Combine(dir, "log.txt"); if (File.Exists(log)) Log("\n---- 剪贴板日志 ----\n" + File.ReadAllText(log)); } catch { }
            Log(_fails == 0 ? "\n全部通过" : $"\n失败 {_fails} 项");
            try { File.WriteAllText(reportPath, Report.ToString(), Encoding.UTF8); } catch { }
            try { Directory.Delete(dir, true); } catch { }
        }
        return _fails == 0 ? 0 : 1;
    }

    /// <summary>小窗口：打开不抢焦点、方向键选择、回车粘贴到原来的输入框、继续打字自动收起、Win+V 能打开。</summary>
    private static async Task PanelTests(ClipboardFeature feature)
    {
        var store = feature.Store;
        feature.Start(hotkeys: true);
        Check("键盘监听启动", KeyboardHook.Shared.IsRunning);

        // 一个带输入框的测试窗口，假装是用户正在打字的软件
        var box = new System.Windows.Controls.TextBox { FontSize = 16 };
        var win = new Window { Title = "小猪剪贴板自检 - 输入框", Width = 420, Height = 160, Left = 600, Top = 300, Content = box, Topmost = true };
        win.Show();
        win.Activate();
        box.Focus();
        await Task.Delay(400);
        var winHwnd = new System.Windows.Interop.WindowInteropHelper(win).Handle;
        bool foreground = Native.GetForegroundWindow() == winHwnd;
        Log("INFO 测试窗口在最前面：" + (foreground ? "是" : "否（CI 机器可能不让程序抢到前台，下面依赖它的项目只记 WARN）"));

        var pasteText = "粘贴测试 " + Guid.NewGuid().ToString("N")[..6];
        SetText(pasteText);
        await WaitFor(store, x => x.Text == pasteText);
        // 粘贴测试要等剪贴板换成别的内容，才能看出确实是小猪放回去的
        SetText("别的内容 " + Guid.NewGuid().ToString("N")[..4]);
        await WaitUntil(() => store.History.Items[0].Text.StartsWith("别的内容"));

        var panel = feature.Panel;
        panel.FollowForeground = false;
        feature.ShowPanel(winHwnd);
        await Task.Delay(300);
        Check("小窗口能打开，列表和记录一致", panel.IsVisible && panel.Cards.Count == store.History.Count,
            $"显示 {panel.IsVisible}，卡片 {panel.Cards.Count} 张，记录 {store.History.Count} 条");
        if (foreground) Check("打开小窗口不抢焦点", Native.GetForegroundWindow() == winHwnd);
        Check("打开时选中第一条", panel.SelectedIndex == 0);

        // 方向键（模拟键盘监听转过来的按键）
        bool swallowed = feature.Hotkeys.Intercept(ClipboardHotkeys.VK_DOWN, true);
        bool upSwallowed = feature.Hotkeys.Intercept(ClipboardHotkeys.VK_DOWN, false);
        await Task.Delay(150);
        Check("↓ 键归小窗口用，并选中第二条", swallowed && upSwallowed && panel.SelectedIndex == 1, $"选中第 {panel.SelectedIndex + 1} 条");
        feature.Hotkeys.Intercept(ClipboardHotkeys.VK_RIGHT, true);
        feature.Hotkeys.Intercept(ClipboardHotkeys.VK_RIGHT, false);
        await Task.Delay(150);
        Check("→ 键切换到\"收藏\"", panel.Cards.All(c => c.Favorite) && panel.Cards.Count == store.History.Items.Count(x => x.Favorite));
        feature.Hotkeys.Intercept(ClipboardHotkeys.VK_LEFT, true);
        feature.Hotkeys.Intercept(ClipboardHotkeys.VK_LEFT, false);
        await Task.Delay(150);

        // 继续打字：小窗口收起，按键照常给原来的软件
        bool typedSwallowed = feature.Hotkeys.Intercept(0x41, true);
        feature.Hotkeys.Intercept(0x41, false);
        await Task.Delay(150);
        Check("小窗口开着时继续打字：按键不拦，小窗口收起", !typedSwallowed && !panel.IsVisible);

        // Esc 关闭
        feature.ShowPanel(winHwnd);
        await Task.Delay(200);
        feature.Hotkeys.Intercept(ClipboardHotkeys.VK_ESCAPE, true);
        feature.Hotkeys.Intercept(ClipboardHotkeys.VK_ESCAPE, false);
        await Task.Delay(150);
        Check("Esc 关闭小窗口", !panel.IsVisible);
        Check("小窗口关着时方向键不拦", !feature.Hotkeys.Intercept(ClipboardHotkeys.VK_DOWN, true) & !feature.Hotkeys.Intercept(ClipboardHotkeys.VK_DOWN, false));

        // 回车粘贴到原来的输入框
        win.Activate();
        box.Focus();
        await Task.Delay(300);
        feature.ShowPanel(winHwnd);
        await Task.Delay(200);
        int idx = panel.Cards.ToList().FindIndex(c => c.Item.Text == pasteText);
        for (int i = 0; i < idx; i++)
        {
            feature.Hotkeys.Intercept(ClipboardHotkeys.VK_DOWN, true);
            feature.Hotkeys.Intercept(ClipboardHotkeys.VK_DOWN, false);
        }
        await Task.Delay(150);
        ClipItem? pasted = null;
        panel.Pasted += it => pasted = it;
        feature.Hotkeys.Intercept(ClipboardHotkeys.VK_RETURN, true);
        feature.Hotkeys.Intercept(ClipboardHotkeys.VK_RETURN, false);
        await WaitUntil(() => pasted != null, 2000);
        Check("回车：把选中的那条放进剪贴板、收起小窗口", pasted?.Text == pasteText && !panel.IsVisible
            && System.Windows.Clipboard.GetText() == pasteText, pasted == null ? "没粘贴" : null);
        Check("粘贴过的挪到最上面", store.History.Items[0].Text == pasteText);
        await WaitUntil(() => box.Text.Contains(pasteText), 1500);
        Soft("模拟 Ctrl+V，文字进了原来的输入框", box.Text.Contains(pasteText), foreground, $"输入框里是「{box.Text}」");

        // 从托盘菜单 / 剪贴板页打开（没有要粘贴的地方）：回车只复制，提示一下再收起
        feature.ShowPanel(IntPtr.Zero);
        await Task.Delay(200);
        int ti = panel.Cards.ToList().FindIndex(1, c => c.Item.Kind == ClipKind.Text && !c.Item.Sensitive);
        for (int i = 0; i < ti; i++)
        {
            feature.Hotkeys.Intercept(ClipboardHotkeys.VK_DOWN, true);
            feature.Hotkeys.Intercept(ClipboardHotkeys.VK_DOWN, false);
        }
        await Task.Delay(150);
        var expect = ti > 0 && panel.SelectedIndex == ti ? panel.Cards[ti].Item : null;
        string boxBefore = box.Text;
        pasted = null;
        feature.Hotkeys.Intercept(ClipboardHotkeys.VK_RETURN, true);
        feature.Hotkeys.Intercept(ClipboardHotkeys.VK_RETURN, false);
        await WaitUntil(() => pasted != null, 2000);
        Check("只复制模式：回车把那条放进剪贴板，小窗口先留着显示提示", expect != null && pasted == expect && panel.IsVisible && panel.CopyOnly
            && System.Windows.Clipboard.GetText() == expect.Text, expect == null ? "没找到能选的文字" : pasted == null ? "没复制" : $"小窗口 {panel.IsVisible}");
        await WaitUntil(() => !panel.IsVisible, 3000);
        await Task.Delay(200);
        Check("只复制模式：提示完自动收起，不往别的软件里粘贴", !panel.IsVisible && box.Text == boxBefore, $"输入框里是「{box.Text}」");

        // 真的按 Win+V（模拟按键，不带小猪的记号）
        box.Clear();
        win.Activate();
        await Task.Delay(300);
        Native.Send(Native.Key(0x5B, false, IntPtr.Zero), Native.Key(0x56, false, IntPtr.Zero));
        await Task.Delay(60);
        Native.Send(Native.Key(0x56, true, IntPtr.Zero), Native.Key(0x5B, true, IntPtr.Zero));
        await WaitUntil(() => panel.IsVisible, 1500);
        bool opened = panel.IsVisible;
        Soft("按 Win+V 打开小猪剪贴板", opened, true, "没有打开（这台机器可能不支持模拟按键）");
        await Task.Delay(200);
        Soft("Win+V 的 V 没有漏到输入框里", !box.Text.Contains('v') && !box.Text.Contains('V'), opened, $"输入框里是「{box.Text}」");
        if (opened)
        {
            Native.Send(Native.Key(0x5B, false, IntPtr.Zero), Native.Key(0x56, false, IntPtr.Zero));
            await Task.Delay(60);
            Native.Send(Native.Key(0x56, true, IntPtr.Zero), Native.Key(0x5B, true, IntPtr.Zero));
            await WaitUntil(() => !panel.IsVisible, 1500);
            Soft("再按一次 Win+V 收起", !panel.IsVisible, true, "没有收起");
        }
        panel.HidePanel();
        win.Close();
    }

    /// <summary>依赖"真的能模拟按键、窗口能到最前面"的检查：CI 机器上做不到时只记 WARN，不算失败。</summary>
    private static void Soft(string name, bool ok, bool required, string? detail)
    {
        if (ok) Log("PASS " + name);
        else if (required && Environment.GetEnvironmentVariable("PIGGY_STRICT_INPUT") == "1") Fail(name, detail);
        else Log("WARN " + name + (detail == null ? "" : "：" + detail));
    }

    private static async Task CheckWriteBack(ClipboardStore store, ClipItem? item, string what)
    {
        if (item == null) { Fail($"放回{what}", "前面没记下来"); return; }
        int count = store.History.Count, uses = item.UseCount;
        var top = store.History.Items[0];
        var r = ClipboardWriter.Write(item, store.ImagesDir);
        await Task.Delay(700);
        bool contentOk = item.Kind switch
        {
            ClipKind.Text => System.Windows.Clipboard.GetText() == item.Text,
            ClipKind.Image => System.Windows.Clipboard.ContainsImage(),
            ClipKind.Files => System.Windows.Clipboard.GetFileDropList().Count == item.Files?.Count,
            _ => false,
        };
        Check($"放回{what}：剪贴板里是对的、不重复记", r == ClipboardWriter.Result.Ok && contentOk
            && store.History.Count == count && item.UseCount == uses && store.History.Items[0] == top,
            $"结果 {r}，内容{(contentOk ? "对" : "不对")}，条数 {count}→{store.History.Count}，次数 {uses}→{item.UseCount}");
    }

    // ---------- 小工具 ----------
    private static void SetText(string text)
    {
        var d = new DataObject();
        d.SetData(DataFormats.UnicodeText, text);
        SetData(d);
    }

    private static void SetData(DataObject d)
    {
        for (int i = 0; ; i++)
        {
            try { System.Windows.Clipboard.SetDataObject(d, true); return; }
            catch (System.Runtime.InteropServices.ExternalException) when (i < 10) { Thread.Sleep(50); }
        }
    }

    private static async Task<ClipItem?> WaitFor(ClipboardStore store, Func<ClipItem, bool> match, int ms = 4000)
    {
        ClipItem? found = null;
        await WaitUntil(() => (found = store.History.Items.FirstOrDefault(match)) != null, ms);
        return found;
    }

    private static async Task WaitUntil(Func<bool> cond, int ms = 4000)
    {
        var until = DateTime.UtcNow.AddMilliseconds(ms);
        while (!cond() && DateTime.UtcNow < until) await Task.Delay(40);
    }

    private static BitmapSource TestBitmap(int w, int h, int seed)
    {
        var px = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = (y * w + x) * 4;
                px[i] = (byte)(x * 255 / w);          // B
                px[i + 1] = (byte)(y * 255 / h);      // G
                px[i + 2] = (byte)(200 + seed * 20);  // R
                px[i + 3] = 255;
            }
        var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, px, w * 4);
        bmp.Freeze();
        return bmp;
    }

    private static DataObject ImageData(BitmapSource bmp, bool png, bool onlyPng = false)
    {
        var d = new DataObject();
        if (!onlyPng) d.SetImage(bmp);
        if (png) d.SetData("PNG", new MemoryStream(ClipboardReader.EncodePng(bmp)));
        return d;
    }

    /// <summary>左上角应该是蓝 0、绿 0、红 200，完全不透明。</summary>
    private static bool PixelOk(string path)
    {
        try
        {
            var dec = BitmapDecoder.Create(new Uri(path), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var px = ClipboardReader.ToBgra(dec.Frames[0], out _);
            return px[0] < 8 && px[1] < 8 && Math.Abs(px[2] - 200) < 8 && px[3] == 255;
        }
        catch { return false; }
    }

    /// <summary>做一段 Windows 剪贴板要求的 HTML 格式（开头写清楚每一段在第几个字节）。</summary>
    public static string CfHtml(string fragment)
    {
        const string header = "Version:0.9\r\nStartHTML:{0:D10}\r\nEndHTML:{1:D10}\r\nStartFragment:{2:D10}\r\nEndFragment:{3:D10}\r\n";
        const string pre = "<html><body>\r\n<!--StartFragment-->";
        const string post = "<!--EndFragment-->\r\n</body></html>";
        int headerLen = Encoding.UTF8.GetByteCount(string.Format(header, 0, 0, 0, 0));
        int startHtml = headerLen;
        int startFrag = startHtml + Encoding.UTF8.GetByteCount(pre);
        int endFrag = startFrag + Encoding.UTF8.GetByteCount(fragment);
        int endHtml = endFrag + Encoding.UTF8.GetByteCount(post);
        return string.Format(header, startHtml, endHtml, startFrag, endFrag) + pre + fragment + post;
    }

    private static void Check(string name, bool ok, string? detail = null)
    {
        if (ok) Log("PASS " + name);
        else Fail(name, detail);
    }

    private static void Fail(string name, string? detail)
    {
        _fails++;
        Log("FAIL " + name + (string.IsNullOrEmpty(detail) ? "" : "：" + detail));
    }

    private static void Log(string line) => Report.AppendLine(line);
}
