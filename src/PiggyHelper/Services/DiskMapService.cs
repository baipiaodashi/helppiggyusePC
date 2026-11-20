using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;

namespace PiggyHelper.Services;

/// <summary>能不能自己动手删。</summary>
public enum DiskLevel { Safe, Careful, Never }

/// <summary>C 盘里的一"块"地方，比如桌面、已安装的软件、Windows 系统。只用来解释和打开文件夹，不提供删除。</summary>
public sealed class DiskArea : INotifyPropertyChanged
{
    public required string Title { get; init; }
    public required string Icon { get; init; }
    public required string Color { get; init; }
    public required DiskLevel Level { get; init; }
    public required string What { get; init; }
    public required string HowTo { get; init; }
    /// <summary>这一块包含的文件夹（第一个存在的会被"打开文件夹"打开）。</summary>
    public List<string> Paths { get; init; } = new();
    /// <summary>计算大小时要跳过的子文件夹（避免和别的块重复计算）。</summary>
    public List<string> Exclude { get; init; } = new();
    /// <summary>C 盘根目录下要计算大小的单个文件（比如 pagefile.sys）。</summary>
    public List<string> Files { get; init; } = new();
    public bool CanOpen { get; init; } = true;
    public string? ActionText { get; init; }
    /// <summary>按钮要打开的地址；"#junk" 表示切换到垃圾清理。</summary>
    public string? ActionTarget { get; init; }
    public bool IsRecycleBin { get; init; }
    public bool IsOther { get; init; }

    public Brush IconBrush => Palette.For(Color).Icon;
    public Brush TintBrush => Palette.For(Color).Tint;
    public Brush SolidBrush => Palette.For(Color).Solid;

    public string LevelText => Level switch
    {
        DiskLevel.Safe => "可以自己整理",
        DiskLevel.Careful => "要用对方法",
        _ => "千万别动",
    };
    public Brush LevelBg => Frozen(Level switch { DiskLevel.Safe => "#DDF5EA", DiskLevel.Careful => "#FFF0D4", _ => "#FFE1E4" });
    public Brush LevelFg => Frozen(Level switch { DiskLevel.Safe => "#2E9E70", DiskLevel.Careful => "#B87610", _ => "#D6455A" });

    public bool HasAction => !string.IsNullOrEmpty(ActionText);
    public bool ShowOpen => CanOpen && OpenPath != null;
    public string? OpenPath => IsRecycleBin ? "shell:RecycleBinFolder" : Paths.FirstOrDefault(Directory.Exists);

    /// <summary>不在 C 盘（比如桌面被挪到了 D 盘）时的提示。</summary>
    public string? ElsewhereNote { get; set; }
    public bool HasElsewhere => !string.IsNullOrEmpty(ElsewhereNote);

    private long _size = -1;
    /// <summary>-1 表示还在计算。</summary>
    public long Size
    {
        get => _size;
        set { _size = value; Changed(nameof(Size)); Changed(nameof(SizeText)); }
    }
    public string SizeText => _size < 0 ? "计算中…" : Format.Bytes(_size);

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private static Brush Frozen(string hex)
    {
        var b = new SolidColorBrush(Palette.Parse(hex));
        b.Freeze();
        return b;
    }
}

public static class DiskMapService
{
    public static string SystemDrive => Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";

    private static string Profile => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    private static string Docs => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    private static string Local => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid id, uint flags, IntPtr token, out IntPtr path);

    /// <summary>"下载"文件夹的真实位置（用户可能把它挪到了别的盘）。</summary>
    public static string Downloads
    {
        get
        {
            try
            {
                if (SHGetKnownFolderPath(new Guid("374DE290-123F-4565-9164-39C4925E467B"), 0, IntPtr.Zero, out var p) == 0)
                {
                    var s = Marshal.PtrToStringUni(p);
                    Marshal.FreeCoTaskMem(p);
                    if (!string.IsNullOrEmpty(s)) return s;
                }
            }
            catch { }
            return Path.Combine(Profile, "Downloads");
        }
    }

    public static List<DiskArea> CreateAreas()
    {
        var chat = CleanupService.WeChatRoots().Concat(CleanupService.XWeChatRoots()).Concat(CleanupService.QQRoots()).ToList();
        var win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var programs = Path.Combine(Local, "Programs");
        var appData = Path.Combine(Profile, "AppData");

        return new List<DiskArea>
        {
            // ---------- 可以自己整理 ----------
            new()
            {
                Title = "桌面", Icon = "\uE7F4", Color = "#5A9CF2", Level = DiskLevel.Safe,
                Paths = { Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory) },
                What = "放在桌面上的文件，其实都存在 C 盘里。桌面东西越多，C 盘越挤。",
                HowTo = "不用的文件拖进回收站；要留的作业、照片，挪到 D 盘的文件夹里保存。",
            },
            new()
            {
                Title = "下载", Icon = "\uE896", Color = "#45C08D", Level = DiskLevel.Safe,
                Paths = { Downloads },
                What = "浏览器和软件下载的东西默认都放在这里，常常堆着很多早就用不上的安装包和视频。",
                HowTo = "打开后按大小排序。软件安装包（.exe、.msi）装好以后就没用了，看过的视频、用完的资料也可以删。",
            },
            new()
            {
                Title = "文档", Icon = "\uE8A5", Color = "#9A7BF2", Level = DiskLevel.Safe,
                Paths = { Docs }, Exclude = chat,
                What = "你保存的 Word、PPT、作业一般在这里。有些软件和游戏也会在这里放设置和存档。",
                HowTo = "自己的文件挑着删。看到不认识的文件夹（名字是某个软件或游戏）先别删，可能是它的存档。",
            },
            new()
            {
                Title = "图片", Icon = "\uEB9F", Color = "#FF8A7A", Level = DiskLevel.Safe,
                Paths = { Environment.GetFolderPath(Environment.SpecialFolder.MyPictures) },
                What = "照片、截图（Win+PrtSc 的截图在\u201C屏幕截图\u201D文件夹）都在这里。",
                HowTo = "重要的照片先备份到网盘或 U 盘，再删 C 盘上的。重复的、模糊的截图可以直接删。",
            },
            new()
            {
                Title = "视频和音乐", Icon = "\uE714", Color = "#F59E3D", Level = DiskLevel.Safe,
                Paths = { Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), Environment.GetFolderPath(Environment.SpecialFolder.MyMusic) },
                What = "录屏（Win+Alt+R）、下载的视频和歌曲在这里。视频文件通常很大。",
                HowTo = "看过的视频、录屏可以删；想留的挪到 D 盘或 U 盘。",
            },
            new()
            {
                Title = "回收站", Icon = "\uE74D", Color = "#8E9AAF", Level = DiskLevel.Safe, IsRecycleBin = true,
                What = "你删掉的文件其实先放到了这里，还在占地方，后悔了可以还原。",
                HowTo = "打开看看，有要的就右键\u201C还原\u201D；确认都不要了，就在\u201C垃圾清理\u201D里勾上\u201C回收站\u201D一起清掉。",
                ActionText = "去垃圾清理", ActionTarget = "#junk",
            },

            // ---------- 要用对方法 ----------
            new()
            {
                Title = "微信、QQ 聊天文件", Icon = "\uE8BD", Color = "#2DBE60", Level = DiskLevel.Careful,
                Paths = chat,
                What = "聊天里收到的图片、视频、文件和聊天记录都存在这里，聊得越多越大，常常有好几 GB。",
                HowTo = "别直接删文件夹，可能把聊天记录弄坏。在微信里清理最安全：左下角 \u2261 \u2192 设置 \u2192 存储空间（或\u201C文件管理\u201D）\u2192 挑着清理。QQ 在 设置 \u2192 存储管理。也可以用小猪的\u201C大文件整理\u201D挑大文件。",
                ActionText = "去大文件整理", ActionTarget = "#big",
            },
            new()
            {
                Title = "已安装的软件", Icon = "\uE71D", Color = "#3EB6E8", Level = DiskLevel.Careful,
                Paths = new[] { pf, pf86, programs }.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                What = "电脑上装的软件（浏览器、WPS、游戏……）都放在 Program Files 这些文件夹里。",
                HowTo = "千万不要直接删软件的文件夹！删一半软件会坏掉，还会留下垃圾。正确做法：打开\u201C安装的应用\u201D，找到不用的软件，点\u201C卸载\u201D。",
                ActionText = "打开卸载软件页面", ActionTarget = "ms-settings:appsfeatures",
            },
            new()
            {
                Title = "软件数据（AppData）", Icon = "\uE8B7", Color = "#E57FB0", Level = DiskLevel.Careful,
                Paths = { appData }, Exclude = { programs },
                What = "一个隐藏文件夹，放着各个软件的设置、登录信息和缓存。",
                HowTo = "别手动删里面的东西，不然软件可能要重新登录或者打不开。里面的缓存用小猪的\u201C垃圾清理\u201D就能安全清掉。",
                ActionText = "去垃圾清理", ActionTarget = "#junk",
            },

            // ---------- 千万别动 ----------
            new()
            {
                Title = "Windows 系统", Icon = "\uE770", Color = "#E5578A", Level = DiskLevel.Never, CanOpen = false,
                Paths = { win },
                What = "Windows 系统本身就住在这里，开机、上网、显示画面都靠它。",
                HowTo = "千万别动！删错一个文件，电脑就可能开不了机。想给系统瘦身，用\u201C垃圾清理\u201D里的\u201C系统更新残留\u201D。",
            },
            new()
            {
                Title = "系统隐藏文件", Icon = "\uE72E", Color = "#B07CE8", Level = DiskLevel.Never, CanOpen = false,
                Paths = { Path.Combine(SystemDrive, "ProgramData") },
                Files = { "pagefile.sys", "hiberfil.sys", "swapfile.sys" },
                What = "虚拟内存（内存不够时借 C 盘来用）、休眠文件，以及所有软件共用的数据（ProgramData）。",
                HowTo = "这些都由 Windows 自动管理，不要手动删。",
            },
            new()
            {
                Title = "其他", Icon = "\uE712", Color = "#A9A2B0", Level = DiskLevel.Never, CanOpen = false, IsOther = true,
                What = "其他用户的文件、系统还原点和一些零碎的系统文件。",
                HowTo = "一般不用管。",
            },
        };
    }

    /// <summary>这个文件夹在不在系统盘上。</summary>
    public static bool OnSystemDrive(string path) =>
        string.Equals(Path.GetPathRoot(path), SystemDrive, StringComparison.OrdinalIgnoreCase);

    private static readonly EnumerationOptions Deep = new()
    {
        RecurseSubdirectories = false,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint,
        ReturnSpecialDirectories = false,
    };

    /// <summary>算出一块地方占了多少（只算在 C 盘上的部分）。只读，不改动任何文件。</summary>
    public static long Measure(DiskArea area, CancellationToken ct)
    {
        if (area.IsRecycleBin) return Math.Max(0, RecycleBin.QuerySize());
        long total = 0;
        var exclude = new HashSet<string>(area.Exclude.Select(Norm), StringComparer.OrdinalIgnoreCase);
        foreach (var p in area.Paths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrEmpty(p) || !Directory.Exists(p) || !OnSystemDrive(p)) continue;
            total += DirSize(p, exclude, ct);
        }
        foreach (var f in area.Files)
        {
            try
            {
                var fi = new DirectoryInfo(SystemDrive).EnumerateFiles(f, new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = 0 }).FirstOrDefault();
                if (fi != null) total += fi.Length;
            }
            catch { }
        }
        return total;
    }

    private static string Norm(string p) => p.TrimEnd('\\', '/');

    /// <summary>一层一层往下数文件大小（不跟随快捷方式/链接，进不去的文件夹就跳过）。</summary>
    private static long DirSize(string root, HashSet<string> exclude, CancellationToken ct)
    {
        long total = 0;
        var stack = new Stack<string>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var dir = stack.Pop();
            try
            {
                var info = new DirectoryInfo(dir);
                foreach (var fi in info.EnumerateFiles("*", Deep))
                {
                    try { total += fi.Length; } catch { }
                }
                foreach (var di in info.EnumerateDirectories("*", Deep))
                {
                    if (exclude.Count > 0 && exclude.Contains(Norm(di.FullName))) continue;
                    stack.Push(di.FullName);
                }
            }
            catch { /* 没权限或者正在被删除，跳过 */ }
        }
        return total;
    }
}
