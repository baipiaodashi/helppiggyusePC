namespace PiggyHelper.Services;

/// <summary>功能导航里的一项：可以是系统设置、电脑自带工具，或一个网站。</summary>
public sealed record NavItem(
    string Title,
    string Category,
    string Icon,
    string Color,
    string Target,
    string Tip,
    string Keywords,
    string? Args = null,
    bool Favorite = false)
{
    /// <summary>卡片上显示的小字说明（网站显示"官方网站"字样）。</summary>
    public string Subtitle => Tip;
}

public static class NavCatalog
{
    public const string All = "全部";
    public const string Settings = "系统设置";
    public const string Tools = "电脑自带工具";
    public const string Study = "学习网站";
    public const string Software = "常用软件官网";
    public const string Ai = "AI 助手";

    public static readonly string[] Categories = { All, Settings, Tools, Study, Software, Ai };

    // 图标使用 Segoe MDL2 Assets / Segoe Fluent Icons 字体里的字符
    public static readonly IReadOnlyList<NavItem> Items = new List<NavItem>
    {
        // ---------- 系统设置 ----------
        new("调节音量", Settings, "\uE767", "#FF8E8E", "ms-settings:sound",
            "拖动“音量”滑块就能调大调小；右下角的喇叭图标也可以点开调节。",
            "声音 音量 喇叭 静音 大声 小声 没声音 扬声器 耳机 麦克风 话筒", Favorite: true),
        new("连接 WiFi", Settings, "\uE701", "#7DB7FF", "ms-settings:network-wifi",
            "打开 WiFi 开关，点“显示可用网络”，选你的网络并输入密码。",
            "wifi 无线 网络 上网 联网 断网 连不上 宽带 校园网", Favorite: true),
        new("蓝牙设备", Settings, "\uE702", "#6FC3DF", "ms-settings:bluetooth",
            "打开蓝牙开关，点“添加设备”，就能连蓝牙耳机、鼠标、音箱。",
            "蓝牙 耳机 音箱 无线鼠标 配对 airpods", Favorite: true),
        new("屏幕亮度和分辨率", Settings, "\uE706", "#FFC24B", "ms-settings:display",
            "在这里可以调亮度、分辨率，还能设置多个显示器。",
            "亮度 屏幕 显示 分辨率 太暗 太亮 显示器 投屏 第二个屏幕 缩放", Favorite: true),
        new("把字变大", Settings, "\uE8D2", "#B79CFF", "ms-settings:easeofaccess-display",
            "拖动“文本大小”滑块，点“应用”，全电脑的字都会变大。",
            "字体 字太小 字变大 放大 看不清 文字大小 缩放", Favorite: true),
        new("护眼模式", Settings, "\uE708", "#FFB86B", "ms-settings:nightlight",
            "打开“夜间模式”，屏幕会变成暖黄色，晚上看更舒服。",
            "护眼 夜间模式 夜灯 蓝光 眼睛累 暖色"),
        new("换桌面壁纸", Settings, "\uE91B", "#7FD1AE", "ms-settings:personalization-background",
            "选“图片”，再点“浏览照片”就能用自己的照片当壁纸。",
            "壁纸 桌面 背景 换图片 主题"),
        new("深色 / 浅色模式", Settings, "\uE790", "#9AA5FF", "ms-settings:personalization-colors",
            "在“选择模式”里可以切换深色或浅色界面。",
            "深色 黑色 暗黑 浅色 颜色 主题色"),
        new("卸载软件", Settings, "\uE74D", "#FF8E8E", "ms-settings:appsfeatures",
            "找到不想要的软件，点它（或右边的“…”），选“卸载”。",
            "卸载 删除软件 删软件 流氓软件 应用 程序", Favorite: true),
        new("开机自动启动的软件", Settings, "\uE7E8", "#FFB86B", "ms-settings:startupapps",
            "把不需要开机就运行的软件关掉，电脑开机会更快。",
            "开机 启动 自启动 开机慢 开机启动项"),
        new("默认浏览器和打开方式", Settings, "\uE774", "#7DB7FF", "ms-settings:defaultapps",
            "可以设置默认用哪个浏览器、用什么软件打开图片和视频。",
            "默认 浏览器 打开方式 默认应用 用什么打开"),
        new("存储空间", Settings, "\uEDA2", "#7FD1AE", "ms-settings:storagesense",
            "看看 C 盘被什么占满了，还能打开“存储感知”自动清理。",
            "存储 磁盘 c盘 空间不足 满了 硬盘 内存不够"),
        new("电源和睡眠", Settings, "\uE945", "#FFC24B", "ms-settings:powersleep",
            "设置多久不用就关屏幕、多久进入睡眠。",
            "电源 睡眠 休眠 关屏 待机 自动关机 省电 电池"),
        new("Windows 更新", Settings, "\uE895", "#6FC3DF", "ms-settings:windowsupdate",
            "检查并安装系统更新，让电脑更安全。",
            "更新 升级 补丁 系统更新 windows update"),
        new("打印机", Settings, "\uE749", "#B79CFF", "ms-settings:printers",
            "点“添加设备”连接打印机，或者查看打印任务。",
            "打印 打印机 扫描 扫描仪"),
        new("鼠标设置", Settings, "\uE962", "#9AA5FF", "ms-settings:mousetouchpad",
            "可以调鼠标速度、滚轮一次滚动多少行、左右键互换。",
            "鼠标 指针 光标 滚轮 速度 左键 右键 触摸板"),
        new("输入法和语言", Settings, "\uE765", "#FF8E8E", "ms-settings:regionlanguage",
            "添加或删除输入法，切换输入法可以按 Win + 空格。",
            "输入法 打字 拼音 中文 英文 语言 键盘 切换输入法"),
        new("日期和时间", Settings, "\uE787", "#7FD1AE", "ms-settings:dateandtime",
            "时间不对的话，打开“自动设置时间”就好。",
            "时间 日期 时钟 时区 时间不对"),
        new("登录密码和 PIN", Settings, "\uE72E", "#FFB86B", "ms-settings:signinoptions",
            "可以设置或修改开机密码、PIN 码。",
            "密码 开机密码 pin 锁屏密码 登录 指纹 人脸"),
        new("通知", Settings, "\uEA8F", "#6FC3DF", "ms-settings:notifications",
            "关掉烦人的软件通知弹窗。",
            "通知 弹窗 提醒 消息 打扰 勿扰"),
        new("移动热点", Settings, "\uE704", "#7DB7FF", "ms-settings:network-mobilehotspot",
            "把电脑的网络分享给手机用。",
            "热点 共享网络 手机上网 分享wifi"),
        new("飞行模式", Settings, "\uE709", "#9AA5FF", "ms-settings:network-airplanemode",
            "WiFi 突然全没了？看看是不是不小心开了飞行模式。",
            "飞行模式 没网 网络消失 wifi不见了"),
        new("摄像头权限", Settings, "\uE722", "#FF8E8E", "ms-settings:privacy-webcam",
            "上网课摄像头打不开？在这里允许软件使用摄像头。",
            "摄像头 相机 网课 视频通话 打不开摄像头 权限"),
        new("麦克风权限", Settings, "\uE720", "#B79CFF", "ms-settings:privacy-microphone",
            "别人听不到你说话？在这里允许软件使用麦克风。",
            "麦克风 话筒 听不到 说话 语音 权限"),
        new("这台电脑的配置", Settings, "\uE946", "#7FD1AE", "ms-settings:about",
            "查看电脑型号、处理器、内存大小和系统版本。",
            "配置 关于 型号 内存 处理器 cpu 系统版本 电脑信息"),

        // ---------- 电脑自带工具 ----------
        new("截图", Tools, "\uE8C6", "#FF7EA8", "ms-screenclip:",
            "拖动鼠标框选要截的区域，截完自动复制，按 Ctrl + V 粘贴。快捷键：Win + Shift + S。",
            "截图 截屏 抓图 屏幕截图 截个图", Favorite: true),
        new("任务管理器", Tools, "\uE7C4", "#FF8E8E", "taskmgr.exe",
            "软件卡死了？选中它，点“结束任务”。快捷键：Ctrl + Shift + Esc。",
            "任务管理器 卡死 没反应 结束程序 卡住 关不掉 内存占用"),
        new("此电脑（文件）", Tools, "\uE8B7", "#FFC24B", "explorer.exe",
            "查看电脑里所有的文件和 U 盘。快捷键：Win + E。",
            "文件 文件夹 此电脑 我的电脑 资源管理器 u盘 找文件", "shell:MyComputerFolder"),
        new("下载的文件", Tools, "\uE896", "#7DB7FF", "explorer.exe",
            "从网上下载的东西一般都在这里。",
            "下载 下载的文件 下载目录 找不到下载", "shell:Downloads"),
        new("回收站", Tools, "\uE74D", "#7FD1AE", "explorer.exe",
            "误删的文件可以在这里右键“还原”。",
            "回收站 误删 恢复文件 还原 删除的文件", "shell:RecycleBinFolder"),
        new("计算器", Tools, "\uE8EF", "#B79CFF", "calc.exe",
            "加减乘除，还有科学计算器模式。",
            "计算器 算数 计算"),
        new("记事本", Tools, "\uE70F", "#9AA5FF", "notepad.exe",
            "写点简单的文字笔记。",
            "记事本 笔记 写字 文本"),
        new("控制面板", Tools, "\uE713", "#6FC3DF", "control.exe",
            "老式的设置界面，有些高级设置在这里。",
            "控制面板 设置 老设置"),
        new("设备管理器", Tools, "\uE90F", "#FFB86B", "devmgmt.msc",
            "查看硬件和驱动，有黄色感叹号说明驱动有问题。",
            "设备管理器 驱动 硬件 感叹号 声卡 网卡 显卡"),

        // ---------- 学习网站 ----------
        new("国家智慧教育平台", Study, "\uE7BE", "#FF8E8E", "https://www.smartedu.cn/",
            "官方免费课程资源，从中小学到高校都有。",
            "学习 课程 网课 教育 智慧教育 中小学"),
        new("中国大学 MOOC", Study, "\uE7BE", "#7DB7FF", "https://www.icourse163.org/",
            "各大学的免费公开课。",
            "慕课 mooc 大学 公开课 网课 学习"),
        new("学堂在线", Study, "\uE7BE", "#7FD1AE", "https://www.xuetangx.com/",
            "清华大学发起的在线课程平台。",
            "学堂在线 清华 网课 学习 课程"),
        new("哔哩哔哩", Study, "\uE714", "#6FC3DF", "https://www.bilibili.com/",
            "有很多免费的教程视频，搜“电脑入门”试试。",
            "b站 哔哩哔哩 bilibili 视频 教程"),
        new("中国知网", Study, "\uE82D", "#B79CFF", "https://www.cnki.net/",
            "查论文、查资料。",
            "知网 论文 文献 资料 cnki"),
        new("有道翻译", Study, "\uE774", "#FFB86B", "https://fanyi.youdao.com/",
            "中英文互译，还能翻译整篇文档。",
            "翻译 英语 英文 单词 有道"),

        // ---------- 常用软件官网（官网下载，不会被捆绑安装） ----------
        new("微信电脑版", Software, "\uE8BD", "#7FD1AE", "https://pc.weixin.qq.com/",
            "官方网站，放心下载，不会被捆绑安装其他软件。",
            "微信 wechat 聊天 下载微信"),
        new("QQ", Software, "\uE8BD", "#7DB7FF", "https://im.qq.com/",
            "官方网站，放心下载。",
            "qq 腾讯 聊天 下载qq"),
        new("WPS Office", Software, "\uE8A5", "#FF8E8E", "https://www.wps.cn/",
            "写文档、做表格、做 PPT，官方网站下载。",
            "wps office word excel ppt 文档 表格 幻灯片 办公"),
        new("腾讯文档", Software, "\uE8A5", "#6FC3DF", "https://docs.qq.com/",
            "在线文档，和同学一起编辑，不用安装。",
            "腾讯文档 在线文档 协作 共享文档"),
        new("搜狗输入法", Software, "\uE765", "#FFB86B", "https://pinyin.sogou.com/",
            "官方网站下载，安装时注意取消勾选多余的软件。",
            "输入法 搜狗 打字 拼音"),
        new("7-Zip 解压软件", Software, "\uE8B7", "#9AA5FF", "https://www.7-zip.org/",
            "免费、无广告的解压缩软件，能打开 zip、rar、7z。",
            "解压 压缩 zip rar 7z 压缩包 打不开压缩包"),
        new("火绒安全", Software, "\uEA18", "#FF7EA8", "https://www.huorong.cn/",
            "轻量的安全软件，还能拦截弹窗广告。",
            "杀毒 安全 病毒 弹窗 广告 火绒"),

        // ---------- AI 助手 ----------
        new("DeepSeek", Ai, "\uE8F2", "#7DB7FF", "https://chat.deepseek.com/",
            "国产 AI，能回答问题、写作业思路、解释代码。",
            "ai 人工智能 deepseek 问答 聊天机器人"),
        new("通义千问", Ai, "\uE8F2", "#B79CFF", "https://tongyi.aliyun.com/",
            "阿里出品的 AI 助手。",
            "ai 人工智能 通义 千问 阿里"),
        new("豆包", Ai, "\uE8F2", "#FFB86B", "https://www.doubao.com/",
            "字节出品的 AI 助手。",
            "ai 人工智能 豆包 字节"),
        new("Kimi", Ai, "\uE8F2", "#7FD1AE", "https://www.kimi.com/",
            "擅长读长文章和文件的 AI 助手。",
            "ai 人工智能 kimi 月之暗面 读文件"),
    };

    private static readonly string[] FillerWords =
    {
        "怎么样", "怎么", "如何", "我想", "我要", "想要", "在哪里", "在哪", "哪里", "打开", "一下", "帮我", "可以",
        "电脑", "的", "了", "吗", "呢", "？", "?", "，", ",", "。", " ",
    };

    /// <summary>用大白话搜索，比如"怎么调声音"也能找到"调节音量"。</summary>
    public static IEnumerable<NavItem> Search(string? query, string category = All)
    {
        var source = category == All ? Items : Items.Where(i => i.Category == category);
        if (string.IsNullOrWhiteSpace(query)) return source;

        var q = query.Trim().ToLowerInvariant();
        var core = q;
        foreach (var w in FillerWords) core = core.Replace(w, "");
        if (core.Length == 0) core = q.Replace(" ", "");

        return source
            .Select(i => (Item: i, Score: Score(i, core)))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .Select(x => x.Item);
    }

    private static int Score(NavItem item, string q)
    {
        var title = item.Title.ToLowerInvariant();
        if (title.Contains(q)) return 100;
        var keywords = item.Keywords.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        int best = 0;
        foreach (var k in keywords)
        {
            if (k == q) best = Math.Max(best, 90);
            else if (k.Contains(q)) best = Math.Max(best, 70);
            else if (q.Contains(k) && k.Length >= 2) best = Math.Max(best, 50 + k.Length);
        }
        if (best == 0 && q.Length >= 2 && item.Tip.ToLowerInvariant().Contains(q)) best = 20;
        if (best == 0 && q.Length >= 2 && title.Any(c => q.Contains(c) && c > 0x4E00))
        {
            // 单字模糊：标题里有和搜索词相同的汉字，只给低分（"调声"也能找到"调节音量"）
            int hit = title.Count(c => c > 0x4E00 && q.Contains(c));
            if (hit >= 2) best = 10 + hit;
        }
        return best;
    }
}
