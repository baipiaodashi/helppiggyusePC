using System.IO;
using System.Windows.Media.Imaging;
using PiggyHelper.Controls;

namespace PiggyHelper.Services.Clips;

/// <summary>截图测试用的示例记录（放在临时文件夹里，不碰真实记录）。</summary>
public static class ClipDemo
{
    public static void Fill(ClipboardStore store)
    {
        var now = DateTime.Now;
        string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        string notepad = Path.Combine(win, "System32", "notepad.exe");
        string explorer = Path.Combine(win, "explorer.exe");
        string edge = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Microsoft\Edge\Application\msedge.exe");
        string paint = Path.Combine(win, "System32", "mspaint.exe");

        var items = new List<ClipItem>();
        ClipItem Text(string text, string source, string? path, int minutesAgo, int uses = 1)
        {
            var it = new ClipItem
            {
                Kind = ClipKind.Text,
                Text = text,
                SubKind = ClipText.Classify(text),
                Sensitive = SensitiveDetector.IsSensitive(text),
                Hash = ClipText.Hash(ClipKind.Text, text),
                Size = text.Length * 2,
                SourceName = source,
                SourcePath = path,
                CreatedAt = now.AddMinutes(-minutesAgo - 30),
                UsedAt = now.AddMinutes(-minutesAgo),
                UseCount = uses,
            };
            items.Add(it);
            return it;
        }

        Text("明天下午 3 点在 302 教室开班会，记得带上学生证和笔记本～", "微信", null, 0);
        var link = Text("https://www.bilibili.com/video/BV1GJ411x7h7", "Edge 浏览器", edge, 3, 4);
        link.Favorite = true;

        // 图片：用小猪欢呼的图当示例
        try
        {
            var png = ClipboardReader.EncodePng((BitmapSource)PiggyMascot.Load("cheer"));
            var dec = BitmapDecoder.Create(new MemoryStream(png), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var px = ClipboardReader.ToBgra(dec.Frames[0], out _);
            int w = dec.Frames[0].PixelWidth, h = dec.Frames[0].PixelHeight;
            var hash = ClipboardReader.HashPixels(w, h, px);
            ClipboardReader.WriteFileSafely(Path.Combine(store.ImagesDir, hash[..40] + ".png"), png);
            items.Add(new ClipItem
            {
                Kind = ClipKind.Image, Image = hash[..40] + ".png", Width = w, Height = h,
                Hash = ClipText.Hash(ClipKind.Image, hash), Size = png.Length,
                SourceName = "画图", SourcePath = paint, CreatedAt = now.AddMinutes(-12), UsedAt = now.AddMinutes(-12),
            });
        }
        catch { }

        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var files = new List<string> { Path.Combine(desktop, "数学作业.docx"), Path.Combine(desktop, "英语作文.pdf"), Path.Combine(desktop, "物理实验报告.xlsx") };
        items.Add(new ClipItem
        {
            Kind = ClipKind.Files, Files = files, Text = string.Join("\n", files), Hash = ClipText.Hash(ClipKind.Files, string.Join("\n", files)),
            SourceName = "文件资源管理器", SourcePath = explorer, CreatedAt = now.AddMinutes(-40), UsedAt = now.AddMinutes(-40),
        });
        Text("WiFi密码：Piggy2026!", "记事本", notepad, 65);
        Text("#FF7EA8", "记事本", notepad, 90);
        Text("xiaoming2008@qq.com", "QQ", null, 60 * 5);
        Text(@"D:\学习资料\期末复习", "文件资源管理器", explorer, 60 * 20);
        Text("第三章 牛顿运动定律\n1. 惯性：物体保持原来运动状态的性质\n2. F = ma，力是改变物体运动状态的原因\n3. 作用力与反作用力大小相等、方向相反",
            "Word", null, 60 * 26);

        store.History.Load(items);
    }
}
