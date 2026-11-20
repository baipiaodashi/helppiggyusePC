using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Windows.Threading;

namespace PiggyHelper.Services.Clips;

/// <summary>
/// 小猪剪贴板的"仓库"：记录列表 + 保存到硬盘 + 图片文件 + 按设置清理旧记录。
/// 位置：%LOCALAPPDATA%\PiggyHelper\Clipboard（history.json、images、thumbs）。
/// 只在界面线程上用；写硬盘放到后台，不卡界面。
/// </summary>
public sealed class ClipboardStore
{
    public static string DefaultDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PiggyHelper", "Clipboard");

    private static readonly JsonSerializerOptions Json = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly ClipHistory _history = new();
    private readonly DispatcherTimer _saveTimer;
    private readonly object _writeGate = new();
    private long _version, _written;

    public string Dir { get; }
    public string ImagesDir { get; }
    public string ThumbsDir { get; }
    private string FilePath => Path.Combine(Dir, "history.json");

    public ClipHistory History => _history;

    /// <summary>记录有变化（新增、删除、收藏……）。在界面线程上触发。</summary>
    public event Action? Changed;

    /// <summary>最多留几条（不算收藏）、留几天（0 = 一直留着）。默认从设置里读。</summary>
    public Func<(int MaxItems, int KeepDays)> Limits { get; set; } = () => (300, 30);

    public ClipboardStore(string dir)
    {
        Dir = dir;
        ImagesDir = Path.Combine(dir, "images");
        ThumbsDir = Path.Combine(dir, "thumbs");
        Directory.CreateDirectory(ImagesDir);
        Directory.CreateDirectory(ThumbsDir);
        _saveTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(600) };
        _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); SaveInBackground(); };
    }

    // ---------- 读 ----------
    public void Load()
    {
        var items = TryLoad(FilePath) ?? TryLoad(FilePath + ".bak") ?? new List<ClipItem>();
        _history.Load(items);
        var removed = _history.Prune(Limits().MaxItems, Limits().KeepDays, DateTime.Now);
        DeleteFilesOf(removed);
        ClipLog.Write($"读取记录 {_history.Count} 条");
    }

    private static List<ClipItem>? TryLoad(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var file = JsonSerializer.Deserialize<StoreFile>(File.ReadAllText(path, Encoding.UTF8), Json);
            if (file?.Items == null) return null;
            var list = new List<ClipItem>();
            foreach (var it in file.Items)
            {
                if (it.Secret != null && !Reveal(it)) continue;   // 换了电脑或用户，解不开就算了
                if (string.IsNullOrEmpty(it.Hash)) continue;
                list.Add(it);
            }
            return list;
        }
        catch (Exception ex)
        {
            ClipLog.Error("读取记录 " + Path.GetFileName(path), ex);
            return null;
        }
    }

    // ---------- 改 ----------
    /// <summary>新复制了一条：加进列表（重复的会挪到最上面），然后按设置清理。</summary>
    public ClipItem Add(ClipItem item)
    {
        var (actual, _) = _history.Add(item, DateTime.Now);
        var (max, days) = Limits();
        DeleteFilesOf(_history.Prune(max, days, DateTime.Now));
        Touched();
        return actual;
    }

    /// <summary>粘贴了一次：挪到最上面。</summary>
    public void Touch(string id)
    {
        if (_history.Touch(id, DateTime.Now)) Touched();
    }

    public void Remove(string id)
    {
        var it = _history.Remove(id);
        if (it == null) return;
        DeleteFilesOf(new[] { it });
        Touched();
    }

    public void SetFavorite(string id, bool favorite)
    {
        if (_history.SetFavorite(id, favorite)) Touched();
    }

    /// <summary>清空（收藏的默认留着）。</summary>
    public int Clear(bool keepFavorites = true)
    {
        var removed = _history.Clear(keepFavorites);
        DeleteFilesOf(removed);
        Touched();
        return removed.Count;
    }

    /// <summary>设置改了条数/天数，或者定时检查时调用。</summary>
    public void Prune()
    {
        var (max, days) = Limits();
        var removed = _history.Prune(max, days, DateTime.Now);
        if (removed.Count == 0) return;
        DeleteFilesOf(removed);
        Touched();
    }

    public string? ImagePath(ClipItem it) => it.Image == null ? null : Path.Combine(ImagesDir, it.Image);

    /// <summary>列表里显示用的小图：大图有缩略图就用缩略图。</summary>
    public string? ThumbPath(ClipItem it)
    {
        if (it.Image == null) return null;
        var thumb = Path.Combine(ThumbsDir, it.Image);
        return File.Exists(thumb) ? thumb : Path.Combine(ImagesDir, it.Image);
    }

    private void Touched()
    {
        Changed?.Invoke();
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void DeleteFilesOf(IEnumerable<ClipItem> removed)
    {
        var names = removed.Where(x => x.Image != null).Select(x => x.Image!).ToList();
        if (names.Count == 0) return;
        var inUse = _history.ImagesInUse();
        names.RemoveAll(inUse.Contains);
        if (names.Count == 0) return;
        Task.Run(() =>
        {
            foreach (var n in names)
            {
                try { File.Delete(Path.Combine(ImagesDir, n)); } catch { }
                try { File.Delete(Path.Combine(ThumbsDir, n)); } catch { }
            }
        });
    }

    /// <summary>删掉没有记录在用的图片（比如上次没保存完就关机留下的）。只删 2 分钟前的，免得删到正在写的新图。</summary>
    public void CleanupOrphansInBackground()
    {
        var inUse = _history.ImagesInUse();
        Task.Run(() =>
        {
            int n = 0;
            var cutoff = DateTime.Now.AddMinutes(-2);
            foreach (var dir in new[] { ImagesDir, ThumbsDir })
            {
                try
                {
                    foreach (var f in Directory.EnumerateFiles(dir))
                    {
                        var name = Path.GetFileName(f);
                        if (inUse.Contains(name)) continue;
                        try
                        {
                            if (File.GetLastWriteTime(f) > cutoff) continue;
                            File.Delete(f);
                            n++;
                        }
                        catch { }
                    }
                }
                catch { }
            }
            if (n > 0) ClipLog.Write($"删掉没用的图片 {n} 个");
        });
    }

    /// <summary>占用空间（字节），在后台算。</summary>
    public Task<long> UsageBytesAsync() => Task.Run(() =>
    {
        long total = 0;
        try
        {
            foreach (var f in Directory.EnumerateFiles(Dir, "*", SearchOption.AllDirectories))
                try { total += new FileInfo(f).Length; } catch { }
        }
        catch { }
        return total;
    });

    // ---------- 存 ----------
    /// <summary>在界面线程上拍个快照（复制每条记录），真正写硬盘放到后台。</summary>
    private void SaveInBackground()
    {
        var snapshot = Snapshot();
        Task.Run(() => Write(snapshot.Version, snapshot.Items));
    }

    /// <summary>马上保存（退出时用），会等写完。</summary>
    public void Flush()
    {
        _saveTimer.Stop();
        var snapshot = Snapshot();
        Write(snapshot.Version, snapshot.Items);
    }

    private (long Version, List<ClipItem> Items) Snapshot()
    {
        var items = _history.Items.Select(x => x.Clone()).ToList();
        return (Interlocked.Increment(ref _version), items);
    }

    private void Write(long version, List<ClipItem> items)
    {
        lock (_writeGate)
        {
            if (version <= _written) return;   // 已经写过更新的版本了
            try
            {
                foreach (var it in items) if (it.Sensitive) Hide(it);
                var bytes = JsonSerializer.SerializeToUtf8Bytes(new StoreFile { Version = 1, Items = items }, Json);
                Directory.CreateDirectory(Dir);
                var tmp = FilePath + ".tmp";
                using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    fs.Write(bytes);
                    fs.Flush(true);
                }
                if (File.Exists(FilePath))
                {
                    try { File.Replace(tmp, FilePath, FilePath + ".bak", true); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
                    {
                        File.Copy(tmp, FilePath, true);
                        File.Delete(tmp);
                    }
                }
                else File.Move(tmp, FilePath);
                _written = version;
            }
            catch (Exception ex) { ClipLog.Error("保存记录", ex); }
        }
    }

    // ---------- 敏感内容加密（Windows 自带的 DPAPI，只有当前电脑的当前用户能解开） ----------
    private static void Hide(ClipItem it)
    {
        var plain = JsonSerializer.SerializeToUtf8Bytes(new SecretPart { Text = it.Text, Html = it.Html, Rtf = it.Rtf });
        it.Secret = Convert.ToBase64String(ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser));
        it.Text = "";
        it.Html = null;
        it.Rtf = null;
    }

    private static bool Reveal(ClipItem it)
    {
        try
        {
            var plain = ProtectedData.Unprotect(Convert.FromBase64String(it.Secret!), null, DataProtectionScope.CurrentUser);
            var part = JsonSerializer.Deserialize<SecretPart>(plain);
            if (part == null) return false;
            it.Text = part.Text ?? "";
            it.Html = part.Html;
            it.Rtf = part.Rtf;
            it.Secret = null;
            return true;
        }
        catch { return false; }
    }

    private sealed class StoreFile
    {
        public int Version { get; set; } = 1;
        public List<ClipItem>? Items { get; set; }
    }

    private sealed class SecretPart
    {
        public string? Text { get; set; }
        public string? Html { get; set; }
        public string? Rtf { get; set; }
    }
}
