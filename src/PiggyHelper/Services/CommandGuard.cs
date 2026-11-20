using System.Text.RegularExpressions;

namespace PiggyHelper.Services;

/// <summary>命令的危险程度。</summary>
public enum CommandRisk
{
    /// <summary>只是查看信息。</summary>
    Ok,
    /// <summary>会改动电脑（删文件、关程序、改设置、关机……），要特别提醒。</summary>
    Warn,
    /// <summary>高危，直接拦下，不给运行按钮。</summary>
    Block,
}

public sealed record GuardResult(CommandRisk Risk, string Reason);

/// <summary>
/// 给 AI 想运行的命令判断风险，用来在卡片上提醒（高级模式只提醒、不拦）。
/// 宁可多提醒，不能漏提醒——用户是电脑小白，看不懂命令。
/// </summary>
public static class CommandGuard
{
    private const RegexOptions Opt = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    /// <summary>把一个或几个命令名变成"独立的词"的正则（前后不能紧挨字母、数字、- 或 $）。</summary>
    private static string Word(string words) => $@"(?<![\w\-$.])(?:{words})(?![\w\-])";

    // ---------- 直接拦下 ----------
    private static readonly (Regex Pattern, string Reason)[] BlockRules =
    {
        // 小猪自己的引擎和安全闸门：AI 不许碰
        (new(@"piggyhelper[\\/](?:harness|dsh-home|settings\.json)|piggy-gate|cordis\.patch", Opt), "会改动小猪自己的安全设置"),
        (new($@"{Word("format")}(?:\.com)?\s+[a-z]:", Opt), "会格式化磁盘，里面的东西会全部没有"),
        (new(Word("format-volume|clear-disk|initialize-disk|remove-partition|set-partition|resize-partition|new-partition|diskpart|mountvol"), Opt), "会改动硬盘分区，可能把整个盘的数据弄没"),
        (new(Word("bcdedit|bootrec|bcdboot|bootsect"), Opt), "会改动开机引导，电脑可能开不了机"),
        (new(Word("vssadmin|wbadmin|disable-computerrestore") + "|shadowcopy", Opt), "会删除系统还原点或备份"),
        (new(Word("cipher") + @"\s+/w|" + Word("sdelete"), Opt), "会彻底擦除文件，找不回来"),

        // 注册表：读可以，改不行
        (new($@"{Word("reg(?:\\.exe)?")}\s+(?:add|delete|import|load|unload|copy|restore|save)\b|{Word("regedit|regedt32|regini")}", Opt), "会修改注册表，改错了软件或系统可能出问题"),
        (new($@"{Word("set-itemproperty|new-itemproperty|remove-itemproperty|rename-itemproperty|clear-itemproperty|copy-itemproperty|move-itemproperty|sp|rp")}", Opt), "会修改注册表或文件属性"),
        (new($@"{Word("new-item|ni|set-item|si|remove-item|ri|rm|del|erase|rd|rmdir|clear-item|cli|move-item|mi|copy-item|cpi|rename-item|rni")}[^|;]*(?:hk(?:lm|cu|cr|u|cc)\b|hkey_|registry::)", Opt), "会修改注册表，改错了软件或系统可能出问题"),

        // 安全软件和防火墙
        (new(Word("set-mppreference|add-mppreference|remove-mppreference|uninstall-windowsfeature") + "|disablerealtimemonitoring|exclusionpath", Opt), "会关掉或绕过 Windows 自带的杀毒保护"),
        (new($@"{Word("netsh")}[^|;]*firewall[^|;]*(?:\boff\b|disable|delete)|{Word("set-netfirewallprofile")}[^|;]*-enabled\s*(?:\$?false|0)|{Word("remove-netfirewallrule|disable-netfirewallrule")}", Opt), "会关掉防火墙，电脑更容易被攻击"),
        (new($@"(?:{Word("sc(?:\\.exe)?")}\s+(?:stop|config|delete)|{Word("stop-service|set-service|spsv")})[^|;]*(?:windefend|mpssvc|wscsvc|securityhealthservice|wuauserv|bfe)\b", Opt), "会停掉杀毒、防火墙或系统更新服务"),

        // 下载并运行、混淆
        (new(Word("invoke-expression|iex"), Opt), "会把一段文字当成命令运行，常被病毒利用"),
        (new(@"(?<![\w])-e(?:c|nc|nco|ncod|ncode|ncoded|ncodedc|ncodedco|ncodedcom|ncodedcomm|ncodedcomma|ncodedcomman|ncodedcommand)?\s+[a-z0-9+/=]{16,}|frombase64string", Opt), "命令被加密藏起来了，看不出它要做什么"),
        (new($@"downloadstring|downloadfile|downloaddata|{Word("start-bitstransfer|bitsadmin")}|{Word("certutil(?:\\.exe)?")}[^|;]*-(?:urlcache|decode)|-outfile\b|\s-o(?:ut)?\s+\S+\.(?:exe|msi|ps1|bat|cmd|vbs|scr|dll)\b", Opt), "会从网上下载文件，可能下到病毒"),
        (new($@"{Word("wscript|cscript|mshta|rundll32|regsvr32|installutil|msbuild|wmic")}|\.(?:ps1|bat|cmd|vbs|js|hta|scr)(?=[\s""';|)]|$)", Opt), "会运行脚本或系统组件，可能被用来干坏事"),

        // 账户、权限、提权、开机自启
        (new($@"{Word("net(?:\\.exe)?")}\s+(?:user|localgroup)\b|{Word("new-localuser|remove-localuser|set-localuser|enable-localuser|disable-localuser|add-localgroupmember|remove-localgroupmember")}", Opt), "会添加或修改电脑账户"),
        (new(Word("takeown|icacls|cacls|xcacls"), Opt), "会改动文件的权限"),
        (new(@"-verb\s+[""']?runas|" + Word("runas"), Opt), "会以管理员身份运行，权限太大"),
        (new($@"{Word("schtasks(?:\\.exe)?")}[^|;]*/(?:create|change|delete)|{Word("register-scheduledtask|new-scheduledtask|unregister-scheduledtask|set-scheduledtask")}|{Word("sc(?:\\.exe)?")}\s+create|{Word("new-service")}|currentversion\\run", Opt), "会设置开机自动运行或计划任务"),
        (new(Word("set-executionpolicy"), Opt), "会降低系统的安全设置"),

        // 关键系统进程
        (new($@"{Word("stop-process|kill|spps|taskkill(?:\\.exe)?|tskill")}[^|;]*\b(?:csrss|winlogon|lsass|wininit|smss|services|svchost|system|dwm|fontdrvhost|msmpeng)\b", Opt), "会关掉 Windows 自己必须的进程，电脑会蓝屏或死机"),
    };

    // ---------- 删除 / 改动文件的命令 ----------
    private static readonly Regex DeleteVerb = new(
        Word("remove-item|ri|rm|del|erase|rd|rmdir|clear-content|clc|clear-item|cli|move-item|mi|move|rename-item|rni|ren|set-content|out-file|add-content|ac|copy-item|cpi|copy|xcopy|robocopy") +
        @"|\]::(?:delete\w*|move\w*|copy\w*|writeall\w*|replace)\(|\.delete\(\)|>{1,2}", Opt);

    private static readonly Regex SystemPath = new(
        @"(?:[a-z]:)?\\windows(?:\\|\s|$|[""';|)])|system32|syswow64|\\program files|programdata|\$env:(?:windir|systemroot|programfiles|programdata|systemdrive)|%(?:windir|systemroot|programfiles|programfiles\(x86\)|programdata|systemdrive)%|\\boot\\|bootmgr|pagefile\.sys|hiberfil\.sys|swapfile\.sys|\$recycle\.bin|system volume information|\\recovery(?:\\|\s|$)|\\efi\\" +
        // 自检时在 Linux / macOS 上跑：那边的系统文件夹
        @"|(?<![\w.~])/(?:etc|bin|sbin|usr|boot|lib|lib64|sys|proc|System|Library)(?=/|\s|$|[""';|)])",
        Opt);

    /// <summary>整个盘（C:\、D:\*）。</summary>
    private static readonly Regex DriveRoot = new(@"(?<![\w\\])[a-z]:\\?(?:\*(?:\.\*)?)?(?=$|[\s""';|)])|(?<![\w])\\\*|(?<![\w$)])rm\s+(?:-[a-z-]+\s+)*/\*?(?=$|[\s;|])", Opt);

    /// <summary>整个用户文件夹（C:\Users\xxx 本身，或者 ~）。</summary>
    private static readonly Regex ProfileRoot = new(
        @"\$env:userprofile(?=$|[\s""';|)])|%userprofile%(?=$|[\s""';|)])|\$home(?=$|[\s""';|)])|(?<![\w\\])~(?:\\|/)?(?=$|[\s""';|)])|[a-z]:\\users(?:\\[^\\\s""';|)]+)?\\?(?=$|[\s""';|)])|(?:\\|\$env:userprofile\\|%userprofile%\\)(?:documents|desktop|pictures|appdata|downloads|videos|music|onedrive)\\?(?=$|[\s""';|)])",
        Opt);

    // ---------- 提醒（不拦） ----------
    private static readonly (Regex Pattern, string Reason)[] WarnRules =
    {
        (new(Word("stop-process|kill|spps|taskkill(?:\\.exe)?|tskill"), Opt), "会强制关掉正在运行的程序，没保存的内容会丢"),
        (new(Word("shutdown(?:\\.exe)?|restart-computer|stop-computer|logoff"), Opt), "会关机、重启或注销，先保存好手上的东西"),
        (new(Word("winget|choco|scoop|pip|npm|msiexec|install-package|uninstall-package|install-module|add-appxpackage|remove-appxpackage"), Opt), "会安装或卸载软件"),
        (new(Word("netsh|ipconfig") + @"[^|;]*(?:set|add|delete|reset|/release|/renew|/flushdns)|" + Word("set-netipaddress|new-netipaddress|set-dnsclientserveraddress|disable-netadapter|enable-netadapter|restart-netadapter|set-netadapter"), Opt), "会改动网络设置，可能暂时断网"),
        (new(Word("stop-service|start-service|restart-service|set-service|spsv|sasv") + "|" + Word("sc(?:\\.exe)?") + @"\s+(?:start|stop|config|pause|continue|failure)", Opt), "会启动或停止系统服务"),
        (new(Word("setx") + @"|\]::setenvironmentvariable|" + Word("set-date|set-timezone|set-culture|set-winsystemlocale|set-winuserlanguagelist"), Opt), "会改动系统设置"),
        (new(Word("sfc|dism|chkdsk|defrag|optimize-volume|repair-volume"), Opt), "会检查或修复系统，可能要很久"),
        (new(Word("start-process|saps|start|invoke-item|ii"), Opt), "会打开一个程序或文件"),
    };

    public static GuardResult Check(string? command)
    {
        if (string.IsNullOrWhiteSpace(command)) return new(CommandRisk.Block, "命令是空的");
        if (command.Length > 1500) return new(CommandRisk.Block, "命令太长了，看不清它要做什么");

        // 去掉常见的"障眼法"：PowerShell 的反引号、cmd 的 ^、引号、多余空格
        var flat = Normalize(command);

        foreach (var (pattern, reason) in BlockRules)
            if (pattern.IsMatch(flat)) return new(CommandRisk.Block, reason);

        if (DeleteVerb.IsMatch(flat))
        {
            if (SystemPath.IsMatch(flat)) return new(CommandRisk.Block, "会删除或改动系统文件，电脑可能坏掉");
            if (DriveRoot.IsMatch(flat)) return new(CommandRisk.Block, "会删除或改动整个磁盘里的东西");
            if (ProfileRoot.IsMatch(flat)) return new(CommandRisk.Block, "会删除或改动你的整个用户文件夹");
            return new(CommandRisk.Warn, "会删除、移动或改写文件，删掉的东西可能找不回来");
        }

        foreach (var (pattern, reason) in WarnRules)
            if (pattern.IsMatch(flat)) return new(CommandRisk.Warn, reason);

        return new(CommandRisk.Ok, "只是查看信息，不会改动电脑");
    }

    /// <summary>检查 AI 想写入、修改的文件路径（写文件、改文件这类工具用）。</summary>
    public static GuardResult CheckWritePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return new(CommandRisk.Block, "没有说清楚要改哪个文件");
        var flat = Normalize(path) + " ";
        if (SystemPath.IsMatch(flat)) return new(CommandRisk.Block, "要改的是系统文件，电脑可能坏掉");
        if (Regex.IsMatch(flat, @"piggyhelper[\\/](?:harness|dsh-home|settings\.json)|piggy-gate|cordis\.patch", Opt)) return new(CommandRisk.Block, "要改的是小猪自己的安全设置");
        if (DriveRoot.IsMatch(Normalize(path))) return new(CommandRisk.Block, "要改的是整个磁盘");
        if (Regex.IsMatch(flat, @"^\\\\[.?]\\|\\appdata\\roaming\\microsoft\\windows\\start menu\\programs\\startup", Opt))
            return new(CommandRisk.Block, "要改的地方会影响开机启动或系统设备");
        if (Regex.IsMatch(flat, @"\.(?:exe|dll|sys|bat|cmd|ps1|vbs|js|hta|scr|msi|lnk|reg)\s", Opt))
            return new(CommandRisk.Block, "要新建或修改的是程序、脚本或快捷方式，可能被用来干坏事");
        return new(CommandRisk.Warn, "会新建或修改一个文件");
    }

    public static string Normalize(string command)
    {
        var s = command.Replace("`", "").Replace("^", "").Replace("\"", " ").Replace("'", " ");
        s = s.Replace('/', '/').Replace('\u3000', ' ');
        s = Regex.Replace(s, @"\s+", " ");
        return s.Trim();
    }
}
