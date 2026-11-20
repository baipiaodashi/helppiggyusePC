using System.ComponentModel;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PiggyHelper.Services.Clips;

namespace PiggyHelper.Windows;

/// <summary>剪贴板小窗口和剪贴板页面里的一张卡片。</summary>
public sealed class ClipCardVm : INotifyPropertyChanged
{
    private const double ImageMaxWidth = 330, ImageMaxHeight = 150;
    private static readonly Dictionary<string, BitmapSource?> ThumbCache = new();
    private static readonly LinkedList<string> ThumbOrder = new();

    private readonly ClipboardStore _store;
    private bool _revealed, _thumbRequested;
    private ImageSource? _thumb;
    private string? _preview;

    public ClipItem Item { get; }

    public ClipCardVm(ClipItem item, ClipboardStore store)
    {
        Item = item;
        _store = store;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed(params string[] names)
    {
        foreach (var n in names) PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    public string Id => Item.Id;
    public bool IsText => Item.Kind == ClipKind.Text;
    public bool IsImage => Item.Kind == ClipKind.Image;
    public bool IsFiles => Item.Kind == ClipKind.Files;

    // ---------- 文字 ----------
    public bool Masked => Item.Sensitive && !_revealed;

    /// <summary>敏感内容：临时显示出来（关掉小窗口后又会打码）。</summary>
    public bool Revealed
    {
        get => _revealed;
        set { if (_revealed == value) return; _revealed = value; _preview = null; Changed(nameof(Revealed), nameof(Masked), nameof(Preview)); }
    }

    public string Preview => _preview ??= Item.Kind switch
    {
        ClipKind.Text => Masked ? ClipText.Mask(Item.Text) : ClipText.Preview(Item.Text, 400),
        _ => "",
    };

    public string SensitiveNote => "可能是密码或证件号，已打码";

    public string? SubKind => Item.SubKind;

    /// <summary>网址、邮箱、颜色、路径的小标签。</summary>
    public string? Badge => Item.Kind == ClipKind.Text && !Masked ? Item.SubKind switch
    {
        "link" => "网址",
        "email" => "邮箱",
        "color" => "颜色",
        "path" => "路径",
        _ => null,
    } : null;

    public Brush? ColorSwatch
    {
        get
        {
            if (Item.SubKind != "color") return null;
            try
            {
                var t = Item.Text.Trim();
                if (t.StartsWith("rgb", StringComparison.OrdinalIgnoreCase))
                {
                    var nums = t[(t.IndexOf('(') + 1)..t.IndexOf(')')].Split(',').Select(x => x.Trim()).ToArray();
                    byte r = byte.Parse(nums[0]), g = byte.Parse(nums[1]), b = byte.Parse(nums[2]);
                    byte a = nums.Length > 3 ? (byte)Math.Clamp(double.Parse(nums[3], System.Globalization.CultureInfo.InvariantCulture) * 255, 0, 255) : (byte)255;
                    var br = new SolidColorBrush(Color.FromArgb(a, r, g, b));
                    br.Freeze();
                    return br;
                }
                var brush = (Brush)new BrushConverter().ConvertFromInvariantString(t)!;
                brush.Freeze();
                return brush;
            }
            catch { return null; }
        }
    }

    /// <summary>带格式（网页/Word）的文字，或者文件：可以"只粘贴文字"。</summary>
    public bool CanPastePlain => (Item.Kind == ClipKind.Text && (Item.Html != null || Item.Rtf != null)) || Item.Kind == ClipKind.Files;
    public string PlainTip => Item.Kind == ClipKind.Files ? "粘贴文件的位置（文字）" : "只粘贴文字，不带字体颜色";

    public bool CanOpen => Item.Kind switch
    {
        ClipKind.Text => !Masked && Item.SubKind is "link" or "path" or "email",
        _ => true,
    };

    public string OpenTip => Item.Kind switch
    {
        ClipKind.Image => "用看图软件打开",
        ClipKind.Files => "打开所在文件夹",
        _ => Item.SubKind switch { "link" => "在浏览器里打开", "email" => "写邮件", _ => "打开所在文件夹" },
    };

    // ---------- 图片 ----------
    public ImageSource? Thumb
    {
        get
        {
            if (Item.Kind == ClipKind.Image && !_thumbRequested)
            {
                _thumbRequested = true;
                LoadThumb();
            }
            return _thumb;
        }
    }

    public double ImageWidth => Fit().W;
    public double ImageHeight => Fit().H;
    public string ImageInfo => $"{Item.Width} × {Item.Height}   {ClipText.SizeText(Item.Size)}";

    private (double W, double H) Fit()
    {
        if (Item.Width <= 0 || Item.Height <= 0) return (ImageMaxWidth, 90);
        double s = Math.Min(1, Math.Min(ImageMaxWidth / Item.Width, ImageMaxHeight / Item.Height));
        return (Math.Max(24, Item.Width * s), Math.Max(24, Item.Height * s));
    }

    private async void LoadThumb()
    {
        var path = _store.ThumbPath(Item);
        if (path == null) return;
        if (ThumbCache.TryGetValue(path, out var cached)) { _thumb = cached; Changed(nameof(Thumb)); return; }
        int decodeWidth = (int)Math.Min(Item.Width > 0 ? Item.Width : 660, 660);
        var bmp = await Task.Run(() => LoadBitmap(path, decodeWidth));
        ThumbCache[path] = bmp;
        ThumbOrder.AddLast(path);
        while (ThumbOrder.Count > 80)
        {
            ThumbCache.Remove(ThumbOrder.First!.Value);
            ThumbOrder.RemoveFirst();
        }
        _thumb = bmp;
        Changed(nameof(Thumb));
    }

    private static BitmapSource? LoadBitmap(string path, int decodeWidth)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.UriSource = new Uri(path);
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bi.DecodePixelWidth = decodeWidth;
            bi.EndInit();
            bi.Freeze();
            return bi;
        }
        catch { return null; }
    }

    // ---------- 文件 ----------
    public int FileCount => Item.Files?.Count ?? 0;

    public string FilesTitle
    {
        get
        {
            var first = Item.Files?.FirstOrDefault() ?? "";
            var name = Path.GetFileName(first.TrimEnd('\\'));
            if (string.IsNullOrEmpty(name)) name = first;
            return FileCount > 1 ? $"{name}  等 {FileCount} 个" : name;
        }
    }

    public string FilesSub
    {
        get
        {
            var first = Item.Files?.FirstOrDefault() ?? "";
            try { return Path.GetDirectoryName(first.TrimEnd('\\')) ?? first; } catch { return first; }
        }
    }

    public string FilesGlyph
    {
        get
        {
            var first = Item.Files?.FirstOrDefault();
            if (FileCount > 1) return "";
            try { if (first != null && Directory.Exists(first)) return ""; } catch { }
            var ext = Path.GetExtension(first ?? "").ToLowerInvariant();
            return ext switch
            {
                ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".webp" or ".heic" => "",
                ".mp4" or ".mkv" or ".avi" or ".mov" or ".wmv" => "",
                ".mp3" or ".wav" or ".flac" or ".m4a" or ".aac" => "",
                ".zip" or ".rar" or ".7z" => "",
                _ => "",
            };
        }
    }

    // ---------- 来源和时间 ----------
    public string SourceName => string.IsNullOrEmpty(Item.SourceName) ? "复制" : Item.SourceName;
    public ImageSource? SourceIcon => AppIcons.Get(Item.SourcePath);
    public bool HasSourceIcon => SourceIcon != null;
    public string TimeText => ClipText.Ago(Item.UsedAt, DateTime.Now);

    public bool Favorite => Item.Favorite;

    public void Refresh() => Changed(nameof(Favorite), nameof(TimeText));
}
