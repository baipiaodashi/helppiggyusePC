using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PiggyHelper.Services.Clips;

/// <summary>认出"从哪个软件复制的"：找到软件的 exe，再起个大家看得懂的名字（微信、Edge 浏览器……）。</summary>
public static class SourceApps
{
    private static readonly uint OwnPid = (uint)Environment.ProcessId;
    private static readonly ConcurrentDictionary<string, string> NameCache = new(StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<string, string> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        ["weixin.exe"] = "微信", ["wechat.exe"] = "微信", ["wechatappex.exe"] = "微信",
        ["qq.exe"] = "QQ", ["qqnt.exe"] = "QQ", ["tim.exe"] = "TIM",
        ["msedge.exe"] = "Edge 浏览器", ["chrome.exe"] = "Chrome 浏览器", ["firefox.exe"] = "火狐浏览器",
        ["360se.exe"] = "360 浏览器", ["360chrome.exe"] = "360 极速浏览器", ["qqbrowser.exe"] = "QQ 浏览器",
        ["sogouexplorer.exe"] = "搜狗浏览器", ["2345explorer.exe"] = "2345 浏览器", ["quark.exe"] = "夸克",
        ["explorer.exe"] = "文件资源管理器", ["notepad.exe"] = "记事本", ["mspaint.exe"] = "画图",
        ["snippingtool.exe"] = "截图工具", ["screenclippinghost.exe"] = "截图工具", ["screensketch.exe"] = "截图工具",
        ["winword.exe"] = "Word", ["excel.exe"] = "Excel", ["powerpnt.exe"] = "PowerPoint", ["onenote.exe"] = "OneNote",
        ["outlook.exe"] = "Outlook", ["olk.exe"] = "Outlook",
        ["wps.exe"] = "WPS 文字", ["et.exe"] = "WPS 表格", ["wpp.exe"] = "WPS 演示", ["wpspdf.exe"] = "WPS PDF",
        ["dingtalk.exe"] = "钉钉", ["feishu.exe"] = "飞书", ["lark.exe"] = "飞书", ["wemeetapp.exe"] = "腾讯会议",
        ["code.exe"] = "VS Code", ["cmd.exe"] = "命令提示符", ["windowsterminal.exe"] = "终端", ["powershell.exe"] = "PowerShell",
        ["acrord32.exe"] = "Adobe Reader", ["acrobat.exe"] = "Adobe Acrobat", ["photoshop.exe"] = "Photoshop",
        ["baidunetdisk.exe"] = "百度网盘", ["cloudmusic.exe"] = "网易云音乐", ["qqmusic.exe"] = "QQ 音乐",
        ["douyin.exe"] = "抖音", ["bilibili.exe"] = "哔哩哔哩", ["xmind.exe"] = "XMind",
        ["applicationframehost.exe"] = "Windows 应用", ["textinputhost.exe"] = "Windows 输入法",
    };

    /// <summary>
    /// 刚复制完时调用：先看剪贴板归哪个窗口，拿不到就看最前面的窗口。
    /// 返回 exe 完整路径和名字；什么都拿不到时两个都是 null。
    /// </summary>
    public static (string? Path, string? Name) Current()
    {
        try
        {
            var path = Native.ProcessPathOf(Native.GetClipboardOwner(), out uint pid);
            if (path == null || IsSystemHost(path))
            {
                var fg = Native.ProcessPathOf(Native.GetForegroundWindow(), out uint fgPid);
                if (fg != null) { path = fg; pid = fgPid; }
            }
            if (path == null) return (null, null);
            return (path, pid == OwnPid ? "小猪电脑助手" : NameOf(path));
        }
        catch { return (null, null); }
    }

    /// <summary>"svchost""rdpclip" 这类后台程序替别人放的剪贴板，看前台窗口更准。</summary>
    private static bool IsSystemHost(string path)
    {
        var f = System.IO.Path.GetFileName(path);
        return f.Equals("svchost.exe", StringComparison.OrdinalIgnoreCase)
            || f.Equals("rdpclip.exe", StringComparison.OrdinalIgnoreCase)
            || f.Equals("dwm.exe", StringComparison.OrdinalIgnoreCase);
    }

    public static string NameOf(string path) => NameCache.GetOrAdd(path, p =>
    {
        var file = System.IO.Path.GetFileName(p);
        if (Known.TryGetValue(file, out var known)) return known;
        try
        {
            var info = FileVersionInfo.GetVersionInfo(p);
            var desc = info.FileDescription?.Trim();
            if (!string.IsNullOrEmpty(desc) && desc.Length <= 24) return desc;
            var product = info.ProductName?.Trim();
            if (!string.IsNullOrEmpty(product) && product.Length <= 24) return product;
        }
        catch { }
        return System.IO.Path.GetFileNameWithoutExtension(p);
    });

    /// <summary>设置里的"不记录这些软件"：按 exe 文件名比较。</summary>
    public static bool IsIgnored(string? path, IEnumerable<string>? ignoreList)
    {
        if (path == null || ignoreList == null) return false;
        var file = System.IO.Path.GetFileName(path);
        foreach (var x in ignoreList)
        {
            var name = x.Trim();
            if (name.Length == 0) continue;
            if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name += ".exe";
            if (file.Equals(name, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }
}

/// <summary>软件图标（列表里显示"来自哪个软件"），按 exe 路径缓存。只在界面线程用。</summary>
public static class AppIcons
{
    private static readonly Dictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static ImageSource? Get(string? exePath)
    {
        if (string.IsNullOrEmpty(exePath)) return null;
        if (Cache.TryGetValue(exePath, out var img)) return img;
        img = null;
        try
        {
            if (File.Exists(exePath))
            {
                using var icon = System.Drawing.Icon.ExtractAssociatedIcon(exePath);
                if (icon != null)
                {
                    var src = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    src.Freeze();
                    img = src;
                }
            }
        }
        catch { }
        if (Cache.Count > 200) Cache.Clear();
        Cache[exePath] = img;
        return img;
    }
}
