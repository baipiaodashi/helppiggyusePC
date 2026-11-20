namespace PiggyHelper.Services;

/// <summary>键帽上的一块：要么是一个按键，要么是中间的"+"。</summary>
public sealed record KeyToken(string Text, bool IsPlus);

/// <summary>一个快捷键。Pop 表示按住第一个键时，会出现在弹出的提示卡上。</summary>
public sealed record Shortcut(string Keys, string Desc, string Category, string? Tip = null, bool Pop = false, bool Popular = false)
{
    /// <summary>拆成键帽，比如 "Ctrl+Shift+T" → [Ctrl] + [Shift] + [T]。</summary>
    public IReadOnlyList<KeyToken> Tokens
    {
        get
        {
            var list = new List<KeyToken>();
            var parts = Keys.Split('+');
            for (int i = 0; i < parts.Length; i++)
            {
                if (i > 0) list.Add(new KeyToken("+", true));
                list.Add(new KeyToken(parts[i], false));
            }
            return list;
        }
    }

    public string FirstKey => Keys.Split('+')[0];
    public bool HasTip => !string.IsNullOrEmpty(Tip);
}

public static class ShortcutCatalog
{
    public const string All = "全部";
    public const string Common = "最常用";
    public const string Window = "窗口和桌面";
    public const string Capture = "截图录屏";
    public const string Typing = "打字和文档";
    public const string Browser = "浏览器";
    public const string Files = "文件管理";
    public const string SystemCat = "系统";

    public static readonly string[] Categories = { All, Common, Window, Capture, Typing, Browser, Files, SystemCat };

    public static readonly IReadOnlyList<Shortcut> Items = new List<Shortcut>
    {
        // ---------- 最常用 ----------
        new("Ctrl+C", "复制", Common, "先选中文字或文件，再按它。", Pop: true, Popular: true),
        new("Ctrl+V", "粘贴", Common, "把刚才复制的东西放到这里。", Pop: true, Popular: true),
        new("Ctrl+X", "剪切", Common, "像复制，但原来的会被拿走。", Pop: true, Popular: true),
        new("Ctrl+Z", "撤销", Common, "后悔药！删错、改错都能退回去。", Pop: true, Popular: true),
        new("Ctrl+Y", "恢复（重做）", Common, "撤销多了，再按它找回来。"),
        new("Ctrl+A", "全选", Common, "一下子选中所有文字或文件。", Pop: true, Popular: true),
        new("Ctrl+S", "保存", Common, "写作业时记得常按，防止断电白写。", Pop: true, Popular: true),
        new("Ctrl+F", "查找", Common, "在网页、文档里找某个词。", Pop: true, Popular: true),
        new("Ctrl+P", "打印", Common),
        new("Ctrl+N", "新建（新窗口 / 新文档）", Common),
        new("Ctrl+O", "打开文件", Common),
        new("Ctrl+W", "关闭当前标签页或文档", Common, Pop: true),
        new("Ctrl+滚轮", "放大 / 缩小", Common, "按住 Ctrl 再滚动鼠标滚轮，网页字太小就用它。", Pop: true, Popular: true),

        // ---------- 窗口和桌面 ----------
        new("Win+D", "一下子回到桌面", Window, "再按一次，窗口又都回来了。", Pop: true, Popular: true),
        new("Alt+Tab", "在打开的窗口之间切换", Window, "按住 Alt，多按几下 Tab 选择窗口。", Pop: true, Popular: true),
        new("Alt+F4", "关闭当前窗口", Window, "在桌面上按，会弹出关机菜单。", Pop: true, Popular: true),
        new("Win+Tab", "查看所有窗口和虚拟桌面", Window, Pop: true),
        new("Win+↑", "窗口最大化", Window, Pop: true),
        new("Win+↓", "窗口还原 / 最小化", Window),
        new("Win+←", "窗口贴到屏幕左半边", Window, "配合 Win+→ 可以左右分屏，一边看网课一边记笔记。", Pop: true),
        new("Win+→", "窗口贴到屏幕右半边", Window),
        new("Win+M", "最小化所有窗口", Window),
        new("Win+1", "打开任务栏上第 1 个程序", Window, "数字几就是第几个，Win+2、Win+3 以此类推。"),
        new("Win+Ctrl+D", "新建一个虚拟桌面", Window, "相当于多一块干净的桌面，Win+Ctrl+←/→ 来回切换。"),

        // ---------- 截图录屏 ----------
        new("Win+Shift+S", "截图（框选区域）", Capture, "截完自动复制，到微信里按 Ctrl+V 就能发出去。", Pop: true, Popular: true),
        new("PrtSc", "截取整个屏幕到剪贴板", Capture, "键盘右上角的 PrtSc / PrtScn 键。"),
        new("Win+PrtSc", "全屏截图并自动保存", Capture, "保存在“图片”文件夹的“屏幕截图”里。"),
        new("Alt+PrtSc", "只截当前窗口", Capture, Pop: true),
        new("Win+Alt+R", "开始 / 停止录屏", Capture, "录下当前窗口的画面，视频保存在“视频”文件夹。"),
        new("Win+G", "打开游戏栏（也能录屏）", Capture),

        // ---------- 打字和文档 ----------
        new("Win+空格", "切换输入法", Typing, Pop: true),
        new("Shift", "中文 / 英文切换", Typing, "微软拼音、搜狗等大多数输入法都这样切换。"),
        new("Win+.", "打开表情符号面板", Typing, "可以输入 😀 这样的表情和特殊符号。", Pop: true),
        new("Win+V", "剪贴板：找回复制过的内容", Typing, "复制过的文字、图片、文件都在这里，↑↓ 选一条，回车就粘贴。在小猪的“剪贴板”页可以换回 Windows 自带的。", Pop: true, Popular: true),
        new("Ctrl+B", "文字加粗", Typing, "Word、WPS 里都能用。", Pop: true),
        new("Ctrl+I", "文字倾斜", Typing),
        new("Ctrl+U", "文字加下划线", Typing),
        new("Home", "光标跳到这一行开头", Typing, "End 是跳到这一行末尾。"),
        new("Ctrl+Backspace", "删除光标前的一个词", Typing),
        new("Ctrl+Shift+V", "粘贴纯文字（去掉格式）", Typing, "从网页复制到 Word 时很好用（部分软件支持）。"),

        // ---------- 浏览器 ----------
        new("Ctrl+T", "打开新标签页", Browser, Pop: true),
        new("Ctrl+Shift+T", "找回刚关掉的网页", Browser, "手滑关错网页时的救星。", Pop: true, Popular: true),
        new("Ctrl+Tab", "切换到下一个标签页", Browser, Pop: true),
        new("Ctrl+L", "选中地址栏", Browser, "然后直接输入网址或要搜索的内容。"),
        new("F5", "刷新网页", Browser, "网页卡住、没加载出来时按一下。"),
        new("Ctrl+D", "收藏当前网页", Browser),
        new("Ctrl+H", "查看历史记录", Browser),
        new("Ctrl+J", "查看下载的文件", Browser, "找不到刚下载的东西就按它。"),
        new("Ctrl+Shift+N", "打开无痕窗口", Browser, "Chrome / Edge 用这个，Firefox 是 Ctrl+Shift+P。不会留下浏览记录。"),
        new("Ctrl+0", "网页恢复原来大小", Browser, "放大缩小乱了按这个。"),
        new("Alt+←", "网页后退", Browser, Pop: true),

        // ---------- 文件管理 ----------
        new("Win+E", "打开“此电脑”", Files, "找文件、看 U 盘就靠它。", Pop: true, Popular: true),
        new("F2", "给选中的文件改名字", Files),
        new("Delete", "删除到回收站", Files, "删错了可以去回收站还原。"),
        new("Shift+Delete", "彻底删除（不进回收站）", Files, "小心！这样删掉就找不回来了。"),
        new("Ctrl+Shift+N", "新建文件夹", Files, "在文件夹窗口里按。"),
        new("Alt+Enter", "查看文件属性（大小、位置）", Files, Pop: true),
        new("Alt+↑", "返回上一级文件夹", Files, Pop: true),
        new("Ctrl+单击", "挑着选多个文件", Files),
        new("Shift+单击", "连续选中一排文件", Files),

        // ---------- 系统 ----------
        new("Win+L", "马上锁屏", SystemCat, "离开座位时按一下，保护隐私。", Pop: true, Popular: true),
        new("Win+I", "打开设置", SystemCat, Pop: true),
        new("Win+A", "快速设置（WiFi、音量、亮度）", SystemCat, Pop: true),
        new("Win+X", "打开快捷菜单", SystemCat, "里面有设备管理器、关机等常用功能。"),
        new("Win+R", "打开“运行”", SystemCat),
        new("Ctrl+Shift+Esc", "打开任务管理器", SystemCat, "软件卡死了，用它强制关掉。", Pop: true, Popular: true),
        new("Ctrl+Alt+Delete", "安全选项（锁定、注销、任务管理器）", SystemCat, "电脑完全卡住时试试它。"),
        new("Win+P", "连接投影仪 / 第二块屏幕", SystemCat, "上台演讲接投影仪时用。"),
        new("Win+H", "语音输入（说话打字）", SystemCat),
        new("Win+Ctrl+Shift+B", "屏幕黑了、卡住时重启显卡", SystemCat, "按下后屏幕会闪一下，不会丢失任何东西。"),
    };

    public static IEnumerable<Shortcut> Search(string? query, string category)
    {
        IEnumerable<Shortcut> src = category switch
        {
            All => Items,
            Common => Items.Where(s => s.Popular),
            _ => Items.Where(s => s.Category == category),
        };
        if (string.IsNullOrWhiteSpace(query)) return src;
        var q = query.Trim().ToLowerInvariant().Replace("怎么", "").Replace("如何", "").Replace("快捷键", "");
        if (q.Length == 0) return src;
        return src.Where(s =>
            s.Keys.ToLowerInvariant().Contains(q) ||
            s.Desc.ToLowerInvariant().Contains(q) ||
            (s.Tip?.ToLowerInvariant().Contains(q) ?? false));
    }

    /// <summary>按住某个键时，提示卡上要显示的快捷键。</summary>
    public static IReadOnlyList<Shortcut> ForModifier(string modifier) =>
        Items.Where(s => s.Pop && s.FirstKey == modifier)
             .GroupBy(s => s.Keys).Select(g => g.First())
             .Take(12).ToList();
}
