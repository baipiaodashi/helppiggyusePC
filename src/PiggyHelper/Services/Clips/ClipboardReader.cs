using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PiggyHelper.Services.Clips;

/// <summary>
/// 把系统剪贴板里现在的东西读出来，变成一条记录。只在剪贴板监听线程（STA）上用。
/// 读的顺序：文件 → 文字（连同网页/Word 格式）→ 图片。Excel 复制表格时会同时带文字和图片，先看文字才不会存成一张图。
/// </summary>
internal static class ClipboardReader
{
    public const int MaxTextChars = 2_000_000;
    public const int MaxRichChars = 2_000_000;
    public const long MaxPngBytes = 30L * 1024 * 1024;
    public const long MaxPixels = 40_000_000;
    public const int ThumbSize = 640;

    private static readonly uint FmtExclude = Native.RegisterClipboardFormat("ExcludeClipboardContentFromMonitorProcessing");
    private static readonly uint FmtViewerIgnore = Native.RegisterClipboardFormat("Clipboard Viewer Ignore");
    private const string CanIncludeFormat = "CanIncludeInClipboardHistory";

    /// <summary>密码管理器等软件会给剪贴板打上"别记我"的标记，看到就跳过。</summary>
    public static string? PrivacyMark()
    {
        if (FmtExclude != 0 && Native.IsClipboardFormatAvailable(FmtExclude)) return "软件要求不记录";
        if (FmtViewerIgnore != 0 && Native.IsClipboardFormatAvailable(FmtViewerIgnore)) return "软件要求不记录";
        return null;
    }

    /// <summary>读剪贴板。拿到新内容返回记录；不该记或者是空的返回 null，原因写在 note 里（只用于日志）。</summary>
    public static ClipItem? Read(string imagesDir, string thumbsDir, out string note)
    {
        int[] waits = { 25, 60, 120 };
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                var data = System.Windows.Clipboard.GetDataObject();
                if (data == null) { note = "剪贴板是空的"; return null; }
                return ReadFrom(data, imagesDir, thumbsDir, out note);
            }
            catch (ExternalException) when (attempt < waits.Length)
            {
                // 别的软件正占着剪贴板，等一下再试
                Thread.Sleep(waits[attempt]);
            }
        }
    }

    private static ClipItem? ReadFrom(IDataObject data, string imagesDir, string thumbsDir, out string note)
    {
        if (data.GetDataPresent(CanIncludeFormat) && data.GetData(CanIncludeFormat) is MemoryStream ms && ms.Length >= 4)
        {
            var buf = new byte[4];
            ms.Position = 0;
            ms.ReadExactly(buf);
            if (BitConverter.ToInt32(buf) == 0) { note = "软件要求不进历史"; return null; }
        }

        // 1. 文件
        if (data.GetDataPresent(DataFormats.FileDrop) && data.GetData(DataFormats.FileDrop) is string[] drop)
        {
            var files = drop.Where(f => !string.IsNullOrWhiteSpace(f)).ToList();
            if (files.Count > 0)
            {
                var joined = string.Join("\n", files);
                note = $"文件 ×{files.Count}";
                return new ClipItem
                {
                    Kind = ClipKind.Files,
                    Text = joined,
                    Files = files,
                    Hash = ClipText.Hash(ClipKind.Files, joined),
                    Size = FilesSize(files),
                };
            }
        }

        // 2. 文字
        if (data.GetDataPresent(DataFormats.UnicodeText) && data.GetData(DataFormats.UnicodeText, true) is string text
            && !string.IsNullOrWhiteSpace(text))
        {
            if (text.Length > MaxTextChars) { note = $"文字太长（{text.Length} 字），不记录"; return null; }
            var html = TryString(data, DataFormats.Html);
            var rtf = TryString(data, DataFormats.Rtf);
            note = $"文字 {text.Length} 字" + (html != null ? " +网页格式" : "") + (rtf != null ? " +RTF" : "");
            return new ClipItem
            {
                Kind = ClipKind.Text,
                Text = text,
                Html = html,
                Rtf = rtf,
                SubKind = ClipText.Classify(text),
                Sensitive = SensitiveDetector.IsSensitive(text),
                Hash = ClipText.Hash(ClipKind.Text, text),
                Size = Encoding.UTF8.GetByteCount(text) + (html?.Length ?? 0) + (rtf?.Length ?? 0),
            };
        }

        // 3. 图片
        return ReadImage(data, imagesDir, thumbsDir, out note);
    }

    private static string? TryString(IDataObject data, string format)
    {
        try
        {
            if (!data.GetDataPresent(format)) return null;
            return data.GetData(format) is string s && s.Length > 0 && s.Length <= MaxRichChars ? s : null;
        }
        catch { return null; }
    }

    private static long FilesSize(List<string> files)
    {
        long total = 0;
        foreach (var f in files.Take(200))
        {
            try { var fi = new FileInfo(f); if (fi.Exists) total += fi.Length; } catch { }
        }
        return total;
    }

    // ---------- 图片 ----------
    private static ClipItem? ReadImage(IDataObject data, string imagesDir, string thumbsDir, out string note)
    {
        byte[]? png = null;
        BitmapSource? bmp = null;
        bool fromDib = false;

        foreach (var fmt in new[] { "PNG", "image/png" })
        {
            if (!data.GetDataPresent(fmt) || data.GetData(fmt) is not MemoryStream pngStream) continue;
            if (pngStream.Length > MaxPngBytes) { note = "图片太大，不记录"; return null; }
            png = pngStream.ToArray();
            try
            {
                var dec = BitmapDecoder.Create(new MemoryStream(png), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                bmp = dec.Frames[0];
                break;
            }
            catch { png = null; bmp = null; }
        }

        if (bmp == null && data.GetDataPresent(DataFormats.Dib) && data.GetData(DataFormats.Dib) is MemoryStream dib)
        {
            try { bmp = DecodeDib(dib.ToArray()); fromDib = bmp != null; } catch (Exception ex) { ClipLog.Error("读 DIB 图片", ex); }
        }

        if (bmp == null && data.GetDataPresent(DataFormats.Bitmap))
        {
            try { bmp = data.GetData(DataFormats.Bitmap) as BitmapSource; fromDib = bmp != null; } catch { }
        }

        if (bmp == null) { note = "没有能记录的内容"; return null; }

        int w = bmp.PixelWidth, h = bmp.PixelHeight;
        if (w <= 0 || h <= 0) { note = "图片是空的"; return null; }
        if ((long)w * h > MaxPixels) { note = $"图片太大（{w}×{h}），不记录"; return null; }

        var px = ToBgra(bmp, out bool fixedAlpha);
        var pixelHash = HashPixels(w, h, px);
        var name = pixelHash[..40] + ".png";
        var path = Path.Combine(imagesDir, name);

        if (!File.Exists(path))
        {
            if (png == null || fixedAlpha)
            {
                var clean = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, px, w * 4);
                png = EncodePng(clean);
            }
            WriteFileSafely(path, png);
            if (Math.Max(w, h) > ThumbSize)
            {
                try { WriteFileSafely(Path.Combine(thumbsDir, name), MakeThumb(png, w, h)); }
                catch (Exception ex) { ClipLog.Error("做缩略图", ex); }
            }
        }

        long size = 0;
        try { size = new FileInfo(path).Length; } catch { }
        note = $"图片 {w}×{h}" + (fromDib ? "（DIB）" : "（PNG）");
        return new ClipItem
        {
            Kind = ClipKind.Image,
            Image = name,
            Width = w,
            Height = h,
            Hash = ClipText.Hash(ClipKind.Image, pixelHash),
            Size = size,
        };
    }

    /// <summary>CF_DIB 前面补一个 BMP 文件头，交给 Windows 自带的 BMP 解码器。</summary>
    internal static BitmapSource? DecodeDib(byte[] dib)
    {
        if (dib.Length < 40) return null;
        int headerSize = BitConverter.ToInt32(dib, 0);
        if (headerSize < 40 || headerSize > dib.Length) return null;
        short bitCount = BitConverter.ToInt16(dib, 14);
        int compression = BitConverter.ToInt32(dib, 16);
        int clrUsed = BitConverter.ToInt32(dib, 32);
        int masks = headerSize == 40 ? compression switch { 3 => 12, 6 => 16, _ => 0 } : 0;
        int colors = clrUsed != 0 ? clrUsed : (bitCount is > 0 and <= 8 ? 1 << bitCount : 0);
        int offset = 14 + headerSize + masks + colors * 4;

        var file = new byte[14 + dib.Length];
        file[0] = (byte)'B';
        file[1] = (byte)'M';
        BitConverter.GetBytes(file.Length).CopyTo(file, 2);
        BitConverter.GetBytes(offset).CopyTo(file, 10);
        Buffer.BlockCopy(dib, 0, file, 14, dib.Length);
        var dec = new BmpBitmapDecoder(new MemoryStream(file), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        return dec.Frames[0];
    }

    /// <summary>转成 BGRA 像素。很多截图软件放进来的图透明度全是 0（其实是不透明的），这种情况改成不透明。</summary>
    internal static byte[] ToBgra(BitmapSource src, out bool fixedAlpha)
    {
        fixedAlpha = false;
        bool hadAlpha = src.Format == PixelFormats.Bgra32 || src.Format == PixelFormats.Pbgra32 || src.Format == PixelFormats.Prgba64
            || src.Format == PixelFormats.Rgba64 || src.Format == PixelFormats.Rgba128Float || src.Format == PixelFormats.Prgba128Float;
        BitmapSource s = src.Format == PixelFormats.Bgra32 ? src : new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
        int w = s.PixelWidth, h = s.PixelHeight, stride = w * 4;
        var px = new byte[(long)stride * h];
        s.CopyPixels(px, stride, 0);
        if (hadAlpha)
        {
            bool allZero = true;
            for (int i = 3; i < px.Length; i += 4) if (px[i] != 0) { allZero = false; break; }
            if (allZero)
            {
                for (int i = 3; i < px.Length; i += 4) px[i] = 255;
                fixedAlpha = true;
            }
        }
        return px;
    }

    internal static string HashPixels(int w, int h, byte[] px)
    {
        using var ih = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        ih.AppendData(BitConverter.GetBytes(w));
        ih.AppendData(BitConverter.GetBytes(h));
        ih.AppendData(px);
        return Convert.ToHexString(ih.GetHashAndReset()).ToLowerInvariant();
    }

    internal static byte[] EncodePng(BitmapSource src)
    {
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(src));
        using var ms = new MemoryStream();
        enc.Save(ms);
        return ms.ToArray();
    }

    private static byte[] MakeThumb(byte[] png, int w, int h)
    {
        var bi = new BitmapImage();
        bi.BeginInit();
        bi.StreamSource = new MemoryStream(png);
        bi.CacheOption = BitmapCacheOption.OnLoad;
        if (w >= h) bi.DecodePixelWidth = ThumbSize; else bi.DecodePixelHeight = ThumbSize;
        bi.EndInit();
        bi.Freeze();
        return EncodePng(bi);
    }

    internal static void WriteFileSafely(string path, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
        File.WriteAllBytes(tmp, bytes);
        try { File.Move(tmp, path, true); }
        catch { try { File.Delete(tmp); } catch { } throw; }
    }
}
