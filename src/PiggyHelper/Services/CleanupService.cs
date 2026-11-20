using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace PiggyHelper.Services;

/// <summary>一个可以清理的项目（比如"浏览器缓存"），带通俗易懂的解释。</summary>
public sealed class CleanItem : INotifyPropertyChanged
{
    public required string Title { get; init; }
    public required string Icon { get; init; }
    public required string Color { get; init; }
    /// <summary>这是什么（大白话）。</summary>
    public required string What { get; init; }
    /// <summary>删了会怎样（大白话）。</summary>
    public required string Effect { get; init; }
    /// <summary>清理前需要注意的事，比如"先退出微信"。</summary>
    public string? Notice { get; init; }
    public bool NeedsAdmin { get; init; }
    /// <summary>只清理多少小时以前的文件（避免删掉别的软件正在用的临时文件）。</summary>
    public double MinAgeHours { get; init; }
    public bool IsRecycleBin { get; init; }
    public Func<IEnumerable<string>> Roots { get; init; } = () => Array.Empty<string>();

    public System.Windows.Media.Brush IconBrush => Palette.For(Color).Icon;
    public System.Windows.Media.Brush TintBrush => Palette.For(Color).Tint;
    public System.Windows.Media.Brush SolidBrush => Palette.For(Color).Solid;
    public bool HasNotice => !string.IsNullOrEmpty(Notice);

    private bool _isChecked = true;
    public bool IsChecked { get => _isChecked; set => Set(ref _isChecked, value); }

    private long _size = -1;
    /// <summary>扫描到的大小（字节），-1 表示还没扫描。</summary>
    public long Size
    {
        get => _size;
        set { if (Set(ref _size, value)) OnPropertyChanged(nameof(SizeText)); }
    }

    public string SizeText => _size <= 0 ? "" : Format.Bytes(_size);

    private string _status = "等待扫描";
    public string Status { get => _status; set => Set(ref _status, value); }

    private bool _busy;
    public bool Busy { get => _busy; set => Set(ref _busy, value); }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    private bool Set<T>(ref T field, T value, [CallerMemberName] string? n = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(n);
        return true;
    }
}

public sealed record CleanResult(long FreedBytes, int Deleted, int Skipped);

public static class Format
{
    public static string Bytes(long b)
    {
        if (b < 1024) return b + " B";
        double v = b;
        string[] units = { "KB", "MB", "GB", "TB" };
        int i = -1;
        do { v /= 1024; i++; } while (v >= 1024 && i < units.Length - 1);
        return v >= 100 ? $"{v:0} {units[i]}" : v >= 10 ? $"{v:0.#} {units[i]}" : $"{v:0.##} {units[i]}";
    }
}

public static class CleanupService
{
    private static readonly EnumerationOptions Deep = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System,
    };

    private static readonly EnumerationOptions Shallow = new()
    {
        RecurseSubdirectories = false,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint,
    };

    public static bool IsAdmin()
    {
        try
        {
            using var id = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch { return false; }
    }

    // ---------- 各种路径 ----------
    private static string Local => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    private static string Roaming => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    private static string Documents => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    private static string WinDir => Environment.GetFolderPath(Environment.SpecialFolder.Windows);
    private static string ProgramData => Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

    /// <summary>列出某个文件夹下的子文件夹（不存在就返回空）。</summary>
    private static IEnumerable<string> SubDirs(string dir)
    {
        if (!Directory.Exists(dir)) yield break;
        string[] subs;
        try { subs = Directory.GetDirectories(dir, "*", Shallow); }
        catch { yield break; }
        foreach (var s in subs) yield return s;
    }

    /// <summary>微信的"WeChat Files"文件夹，用户可能改过位置。</summary>
    public static IEnumerable<string> WeChatRoots()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Path.Combine(Documents, "WeChat Files") };
        try
        {
            var ini = Path.Combine(Roaming, "Tencent", "WeChat", "All Users", "config", "3ebffe94.ini");
            if (File.Exists(ini))
            {
                var p = File.ReadAllText(ini).Trim();
                if (p.Length > 3 && !p.StartsWith("MyDocument", StringComparison.OrdinalIgnoreCase))
                    set.Add(Path.Combine(p, "WeChat Files"));
            }
        }
        catch { /* 读不到就用默认位置 */ }
        return set.Where(Directory.Exists);
    }

    /// <summary>新版微信（4.x）的 xwechat_files 文件夹。</summary>
    public static IEnumerable<string> XWeChatRoots()
    {
        var p = Path.Combine(Documents, "xwechat_files");
        return Directory.Exists(p) ? new[] { p } : Array.Empty<string>();
    }

    /// <summary>每个微信账号的文件夹（排除 All Users、Applet 这类公共文件夹）。</summary>
    private static IEnumerable<string> WeChatAccounts() =>
        WeChatRoots().SelectMany(SubDirs).Where(d =>
        {
            var n = Path.GetFileName(d);
            return !n.Equals("All Users", StringComparison.OrdinalIgnoreCase) && !n.Equals("Applet", StringComparison.OrdinalIgnoreCase);
        });

    private static IEnumerable<string> XWeChatAccounts() => XWeChatRoots().SelectMany(SubDirs);

    public static IEnumerable<string> QQRoots()
    {
        var p = Path.Combine(Documents, "Tencent Files");
        return Directory.Exists(p) ? new[] { p } : Array.Empty<string>();
    }

    private static IEnumerable<string> QQAccounts() => QQRoots().SelectMany(SubDirs);

    /// <summary>Chrome / Edge 这类浏览器：每个用户资料文件夹下的缓存。</summary>
    private static IEnumerable<string> ChromiumCaches(string userData)
    {
        if (!Directory.Exists(userData)) yield break;
        foreach (var profile in SubDirs(userData))
        {
            var name = Path.GetFileName(profile);
            if (!(name == "Default" || name.StartsWith("Profile", StringComparison.OrdinalIgnoreCase) || name == "Guest Profile")) continue;
            yield return Path.Combine(profile, "Cache");
            yield return Path.Combine(profile, "Code Cache");
            yield return Path.Combine(profile, "GPUCache");
            yield return Path.Combine(profile, "Service Worker", "CacheStorage");
        }
        yield return Path.Combine(userData, "ShaderCache");
        yield return Path.Combine(userData, "GrShaderCache");
    }

    // ---------- 清理项目清单（每一项都配上大白话解释） ----------
    public static List<CleanItem> CreateItems() => new()
    {
        new CleanItem
        {
            Title = "系统临时文件", Icon = "\uE8B7", Color = "#7DB7FF",
            What = "软件运行时临时放的“草稿纸”，用完以后常常忘了扔，越攒越多。",
            Effect = "不会影响任何软件和你的文件。正在被使用的、以及最近两小时内刚生成的，都会自动跳过。",
            MinAgeHours = 2,
            Roots = () => new[] { Path.GetTempPath(), Path.Combine(WinDir, "Temp") },
        },
        new CleanItem
        {
            Title = "浏览器缓存", Icon = "\uE774", Color = "#FFB86B",
            What = "浏览器为了下次打开网页更快，悄悄存下的图片和网页碎片。",
            Effect = "网页第一次打开会稍慢一点。不会退出登录，收藏夹、密码、历史记录都不受影响。",
            Notice = "先关掉浏览器，能清得更干净。",
            Roots = () => ChromiumCaches(Path.Combine(Local, "Microsoft", "Edge", "User Data"))
                .Concat(ChromiumCaches(Path.Combine(Local, "Google", "Chrome", "User Data")))
                .Concat(ChromiumCaches(Path.Combine(Local, "Tencent", "QQBrowser", "User Data")))
                .Concat(ChromiumCaches(Path.Combine(Roaming, "360se6", "User Data")))
                .Concat(ChromiumCaches(Path.Combine(Local, "360Chrome", "Chrome", "User Data")))
                .Concat(SubDirs(Path.Combine(Local, "Mozilla", "Firefox", "Profiles")).Select(p => Path.Combine(p, "cache2"))),
        },
        new CleanItem
        {
            Title = "微信缓存", Icon = "\uE8BD", Color = "#5FCB8E",
            What = "微信为了看图更快存下的缩略图、小程序安装包和运行日志。",
            Effect = "不会删除聊天记录，也不会删除别人发给你的照片、视频和文件。小程序下次打开会重新下载。",
            Notice = "请先退出微信再清理。",
            Roots = () => WeChatAccounts().Select(a => Path.Combine(a, "FileStorage", "Cache"))
                .Concat(WeChatRoots().Select(r => Path.Combine(r, "Applet")))
                .Concat(XWeChatAccounts().Select(a => Path.Combine(a, "cache")))
                .Concat(XWeChatAccounts().Select(a => Path.Combine(a, "temp")))
                .Concat(new[]
                {
                    Path.Combine(Roaming, "Tencent", "WeChat", "log"),
                    Path.Combine(Roaming, "Tencent", "xwechat", "log"),
                }),
        },
        new CleanItem
        {
            Title = "QQ 缓存", Icon = "\uE8BD", Color = "#6FB6F2",
            What = "QQ 运行时产生的临时文件、图片缓存和界面缓存。",
            Effect = "不会删除聊天记录和你收到的文件。",
            Notice = "请先退出 QQ 再清理。",
            Roots = () => QQAccounts().Select(a => Path.Combine(a, "nt_qq", "nt_temp"))
                .Concat(new[]
                {
                    Path.Combine(Roaming, "QQ", "Cache"),
                    Path.Combine(Roaming, "QQ", "Code Cache"),
                    Path.Combine(Roaming, "QQ", "GPUCache"),
                    Path.Combine(Roaming, "Tencent", "QQ", "Temp"),
                }),
        },
        new CleanItem
        {
            Title = "显卡和游戏缓存", Icon = "\uE7FC", Color = "#B79CFF",
            What = "显卡为了让游戏和视频更流畅，提前存下的画面数据。",
            Effect = "游戏第一次加载可能会稍慢一下，之后会自动重新生成。",
            Roots = () => new[]
            {
                Path.Combine(Local, "D3DSCache"),
                Path.Combine(Local, "NVIDIA", "DXCache"),
                Path.Combine(Local, "NVIDIA", "GLCache"),
                Path.Combine(Local, "AMD", "DxCache"),
                Path.Combine(Local, "AMD", "DxcCache"),
            },
        },
        new CleanItem
        {
            Title = "崩溃报告和日志", Icon = "\uE7BA", Color = "#FF8E8E",
            What = "软件出错或崩溃时，Windows 自动写下的“事故报告”，普通人用不上。",
            Effect = "完全不影响使用。",
            Roots = () => new[]
            {
                Path.Combine(Local, "CrashDumps"),
                Path.Combine(Local, "Microsoft", "Windows", "WER", "ReportArchive"),
                Path.Combine(Local, "Microsoft", "Windows", "WER", "ReportQueue"),
                Path.Combine(ProgramData, "Microsoft", "Windows", "WER", "ReportArchive"),
                Path.Combine(ProgramData, "Microsoft", "Windows", "WER", "ReportQueue"),
            },
        },
        new CleanItem
        {
            Title = "系统更新残留", Icon = "\uE895", Color = "#6FC3DF",
            What = "Windows 更新时下载的安装包。更新装好以后，这些安装包就没用了。",
            Effect = "不会影响已经装好的更新。如果正在更新，请等更新结束再清理。",
            NeedsAdmin = true,
            Roots = () => new[] { Path.Combine(WinDir, "SoftwareDistribution", "Download") },
        },
        new CleanItem
        {
            Title = "回收站", Icon = "\uE74D", Color = "#FF9DBA",
            What = "你之前删掉的文件，其实都还暂时躺在回收站里占地方。",
            Effect = "清空后这些文件就真的找不回来了！不确定的话，先点“看一看”打开回收站检查。",
            IsRecycleBin = true,
            IsChecked = false,
        },
    };

    // ---------- 扫描 ----------
    public static long Measure(CleanItem item, CancellationToken ct = default)
    {
        if (item.IsRecycleBin) return RecycleBin.QuerySize();
        long total = 0;
        foreach (var root in SafeRoots(item))
        {
            try
            {
                foreach (var f in new DirectoryInfo(root).EnumerateFiles("*", Deep))
                {
                    ct.ThrowIfCancellationRequested();
                    if (!ShouldTouch(item, f)) continue;
                    try { total += f.Length; } catch { }
                }
            }
            catch (OperationCanceledException) { throw; }
            catch { /* 没权限的文件夹跳过 */ }
        }
        return total;
    }

    // ---------- 清理 ----------
    public static CleanResult Clean(CleanItem item, CancellationToken ct = default)
    {
        if (item.IsRecycleBin)
        {
            long size = RecycleBin.QuerySize();
            return RecycleBin.Empty() ? new CleanResult(size, 1, 0) : new CleanResult(0, 0, 1);
        }

        long freed = 0;
        int deleted = 0, skipped = 0;
        foreach (var root in SafeRoots(item))
        {
            var rootFull = Path.GetFullPath(root).TrimEnd('\\') + "\\";
            List<FileInfo> files;
            try { files = new DirectoryInfo(root).EnumerateFiles("*", Deep).ToList(); }
            catch { continue; }

            foreach (var f in files)
            {
                ct.ThrowIfCancellationRequested();
                // 安全检查：只删清单文件夹里面的东西
                if (!f.FullName.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase)) continue;
                if (!ShouldTouch(item, f)) continue;
                try
                {
                    long len = f.Length;
                    if (f.IsReadOnly) f.IsReadOnly = false;
                    f.Delete();
                    freed += len;
                    deleted++;
                }
                catch { skipped++; } // 正在使用或没权限，跳过
            }

            // 删掉清空后的子文件夹（保留最外层文件夹本身）
            try
            {
                var dirs = new DirectoryInfo(root).EnumerateDirectories("*", Deep)
                    .Select(d => d.FullName)
                    .OrderByDescending(p => p.Length)
                    .ToList();
                foreach (var d in dirs)
                {
                    if (!d.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase)) continue;
                    if ((d + "\\").StartsWith(DotNetExtractDir, StringComparison.OrdinalIgnoreCase)) continue;
                    try { if (!Directory.EnumerateFileSystemEntries(d).Any()) Directory.Delete(d); } catch { }
                }
            }
            catch { }
        }
        return new CleanResult(freed, deleted, skipped);
    }

    /// <summary>
    /// 不碰的文件：.NET 程序（包括小猪自己）解压在临时文件夹里的运行库，删了会导致软件打不开；
    /// 以及太新的文件（可能正在被安装程序等使用）。
    /// </summary>
    private static readonly string DotNetExtractDir = Path.Combine(Path.GetTempPath(), ".net") + "\\";

    private static bool ShouldTouch(CleanItem item, FileInfo f)
    {
        if (f.FullName.StartsWith(DotNetExtractDir, StringComparison.OrdinalIgnoreCase)) return false;
        if (item.MinAgeHours > 0)
        {
            try
            {
                var newest = f.LastWriteTimeUtc > f.CreationTimeUtc ? f.LastWriteTimeUtc : f.CreationTimeUtc;
                if ((DateTime.UtcNow - newest).TotalHours < item.MinAgeHours) return false;
            }
            catch { return false; }
        }
        return true;
    }

    /// <summary>去重并过滤掉不存在或看起来不安全的路径（比如整个盘、用户主目录）。</summary>
    private static IEnumerable<string> SafeRoots(CleanItem item)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile).TrimEnd('\\');
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        IEnumerable<string> roots;
        try { roots = item.Roots().ToList(); }
        catch { yield break; }
        foreach (var r in roots)
        {
            string full;
            try { full = Path.GetFullPath(r).TrimEnd('\\'); }
            catch { continue; }
            if (full.Length <= 3) continue;                                        // 整个盘
            if (full.Equals(home, StringComparison.OrdinalIgnoreCase)) continue;   // 用户主目录
            if (full.Equals(WinDir.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) continue;
            if (full.Equals(Documents.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) continue;
            if (!Directory.Exists(full)) continue;
            if (seen.Add(full)) yield return full;
        }
    }
}

/// <summary>回收站相关的系统调用。</summary>
public static class RecycleBin
{
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct SHQUERYRBINFO
    {
        public int cbSize;
        public long i64Size;
        public long i64NumItems;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHQueryRecycleBin(string? pszRootPath, ref SHQUERYRBINFO pSHQueryRBInfo);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHEmptyRecycleBin(IntPtr hwnd, string? pszRootPath, uint dwFlags);

    private const uint SHERB_NOCONFIRMATION = 0x1, SHERB_NOPROGRESSUI = 0x2, SHERB_NOSOUND = 0x4;

    public static long QuerySize()
    {
        try
        {
            var info = new SHQUERYRBINFO { cbSize = Marshal.SizeOf<SHQUERYRBINFO>() };
            return SHQueryRecycleBin(null, ref info) == 0 ? info.i64Size : 0;
        }
        catch { return 0; }
    }

    public static bool Empty()
    {
        try
        {
            int hr = SHEmptyRecycleBin(IntPtr.Zero, null, SHERB_NOCONFIRMATION | SHERB_NOPROGRESSUI | SHERB_NOSOUND);
            return hr == 0 || QuerySize() == 0;
        }
        catch { return false; }
    }

    // ---------- 把文件移到回收站（可以还原） ----------
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint wFunc;
        [MarshalAs(UnmanagedType.LPWStr)] public string pFrom;
        [MarshalAs(UnmanagedType.LPWStr)] public string? pTo;
        public ushort fFlags;
        [MarshalAs(UnmanagedType.Bool)] public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperation(ref SHFILEOPSTRUCT lpFileOp);

    private const uint FO_DELETE = 3;
    private const ushort FOF_SILENT = 0x4, FOF_NOCONFIRMATION = 0x10, FOF_ALLOWUNDO = 0x40, FOF_NOERRORUI = 0x400;

    /// <summary>把一个文件移到回收站，成功返回 true。</summary>
    public static bool MoveToRecycleBin(string path)
    {
        try
        {
            var op = new SHFILEOPSTRUCT
            {
                wFunc = FO_DELETE,
                pFrom = path + "\0\0",
                fFlags = (ushort)(FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI),
            };
            return SHFileOperation(ref op) == 0 && !op.fAnyOperationsAborted && !File.Exists(path);
        }
        catch { return false; }
    }
}
