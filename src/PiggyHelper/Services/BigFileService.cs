using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;

namespace PiggyHelper.Services;

/// <summary>大文件列表里的一个文件。</summary>
public sealed class BigFile : INotifyPropertyChanged
{
    public required string Path { get; init; }
    public required long Size { get; init; }
    public required DateTime Modified { get; init; }
    /// <summary>来源，比如"微信收到的文件"。</summary>
    public required string Source { get; init; }

    public string Name => System.IO.Path.GetFileName(Path);
    public string SizeText => Format.Bytes(Size);
    public string Detail => $"{Source} · {Modified:yyyy-MM-dd}";

    public string Kind => System.IO.Path.GetExtension(Path).ToLowerInvariant() switch
    {
        ".mp4" or ".mov" or ".avi" or ".mkv" or ".flv" or ".wmv" => "视频",
        ".jpg" or ".jpeg" or ".png" or ".gif" or ".heic" or ".bmp" or ".webp" => "图片",
        ".zip" or ".rar" or ".7z" or ".tar" or ".gz" => "压缩包",
        ".exe" or ".msi" => "安装包",
        ".iso" => "光盘镜像",
        ".pdf" or ".doc" or ".docx" or ".ppt" or ".pptx" or ".xls" or ".xlsx" => "文档",
        ".mp3" or ".wav" or ".flac" or ".m4a" => "音频",
        _ => "文件",
    };

    public string Icon => Kind switch
    {
        "视频" => "\uE714",
        "图片" => "\uE91B",
        "压缩包" => "\uE8B7",
        "安装包" => "\uE7B8",
        "光盘镜像" => "\uE958",
        "文档" => "\uE8A5",
        "音频" => "\uE8D6",
        _ => "\uE7C3",
    };

    public string Color => Kind switch
    {
        "视频" => "#FF8E8E",
        "图片" => "#7FD1AE",
        "压缩包" => "#FFB86B",
        "安装包" => "#9AA5FF",
        "光盘镜像" => "#B79CFF",
        "文档" => "#7DB7FF",
        "音频" => "#FF9DBA",
        _ => "#C0AEBA",
    };

    /// <summary>给小白的一句提示。</summary>
    public string Hint => Kind switch
    {
        "安装包" => "软件装好以后，安装包就可以删了",
        "压缩包" => "如果已经解压过，压缩包可以删掉",
        "光盘镜像" => "系统或软件的安装镜像，装完一般就不需要了",
        _ => "",
    };

    public bool HasHint => Hint.Length > 0;
    public System.Windows.Media.Brush IconBrush => Palette.For(Color).Icon;

    private bool _isChecked;
    public bool IsChecked
    {
        get => _isChecked;
        set { if (_isChecked != value) { _isChecked = value; OnPropertyChanged(); } }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

public static class BigFileService
{
    public const long MinSize = 20L * 1024 * 1024; // 20 MB 以上才算大文件

    private static readonly EnumerationOptions Deep = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System | FileAttributes.Hidden,
    };

    private static string Documents => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    private static string UserProfile => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// <summary>要找大文件的地方：微信/QQ 收到的文件、下载文件夹、桌面。</summary>
    private static IEnumerable<(string Dir, string Source)> Places()
    {
        foreach (var root in CleanupService.WeChatRoots())
        foreach (var acc in SafeSubDirs(root))
        {
            yield return (System.IO.Path.Combine(acc, "FileStorage", "File"), "微信收到的文件");
            yield return (System.IO.Path.Combine(acc, "FileStorage", "Video"), "微信里的视频");
            yield return (System.IO.Path.Combine(acc, "FileStorage", "MsgAttach"), "微信聊天图片和附件");
        }
        foreach (var root in CleanupService.XWeChatRoots())
        foreach (var acc in SafeSubDirs(root))
        {
            yield return (System.IO.Path.Combine(acc, "msg", "file"), "微信收到的文件");
            yield return (System.IO.Path.Combine(acc, "msg", "video"), "微信里的视频");
        }
        foreach (var root in CleanupService.QQRoots())
        foreach (var acc in SafeSubDirs(root))
        {
            yield return (System.IO.Path.Combine(acc, "FileRecv"), "QQ 收到的文件");
            yield return (System.IO.Path.Combine(acc, "nt_qq", "nt_data", "File"), "QQ 收到的文件");
            yield return (System.IO.Path.Combine(acc, "nt_qq", "nt_data", "Video"), "QQ 里的视频");
        }
        yield return (System.IO.Path.Combine(UserProfile, "Downloads"), "下载文件夹");
        yield return (Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "桌面");
    }

    private static IEnumerable<string> SafeSubDirs(string dir)
    {
        try { return Directory.Exists(dir) ? Directory.GetDirectories(dir) : Array.Empty<string>(); }
        catch { return Array.Empty<string>(); }
    }

    public static List<BigFile> Find(int max = 200, CancellationToken ct = default)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<BigFile>();
        foreach (var (dir, source) in Places())
        {
            if (!Directory.Exists(dir)) continue;
            try
            {
                foreach (var f in new DirectoryInfo(dir).EnumerateFiles("*", Deep))
                {
                    ct.ThrowIfCancellationRequested();
                    long len;
                    try { len = f.Length; } catch { continue; }
                    if (len < MinSize || !seen.Add(f.FullName)) continue;
                    list.Add(new BigFile { Path = f.FullName, Size = len, Modified = f.LastWriteTime, Source = source });
                }
            }
            catch (OperationCanceledException) { throw; }
            catch { }
        }
        return list.OrderByDescending(f => f.Size).Take(max).ToList();
    }
}
