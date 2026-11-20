using System.Collections.Specialized;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Imaging;

namespace PiggyHelper.Services.Clips;

/// <summary>
/// 记住"这次剪贴板变化是小猪自己写的"，监听线程看到就跳过，不会把粘贴操作又记成一条新复制。
/// 两道保险：剪贴板的序号，以及刚写进去的内容指纹（3 秒内有效）。
/// </summary>
public static class ClipboardGuard
{
    private static readonly object Gate = new();
    private static uint _ownSeq;
    private static readonly List<(string Hash, DateTime Until)> Expected = new();

    /// <summary>写之前先登记指纹：写的过程中监听线程就可能读到，到时候也认得出来。</summary>
    public static void Expect(string hash)
    {
        lock (Gate)
        {
            var now = DateTime.UtcNow;
            Expected.RemoveAll(x => x.Until < now);
            Expected.Add((hash, now.AddSeconds(3)));
        }
    }

    /// <summary>写完以后记下剪贴板序号。</summary>
    public static void MarkOwnSequence()
    {
        lock (Gate) _ownSeq = Native.GetClipboardSequenceNumber();
    }

    /// <summary>剪贴板序号还是小猪写完时的那个：说明没人动过，不用读。</summary>
    public static bool IsOwnSequence(uint seq)
    {
        lock (Gate) return _ownSeq != 0 && seq == _ownSeq;
    }

    /// <summary>读出来的内容正是小猪刚写进去的。</summary>
    public static bool IsExpected(string hash)
    {
        lock (Gate)
        {
            var now = DateTime.UtcNow;
            return Expected.Any(x => x.Hash == hash && x.Until >= now);
        }
    }
}

/// <summary>把一条记录放回系统剪贴板（粘贴前用）。只在界面线程用。</summary>
public static class ClipboardWriter
{
    public enum Result { Ok, Missing, Failed }

    /// <param name="plain">只放纯文字（不带字体颜色；文件记录会变成路径文字）。</param>
    public static Result Write(ClipItem item, string imagesDir, bool plain = false)
    {
        DataObject data;
        string guardHash = item.Hash;
        switch (item.Kind)
        {
            case ClipKind.Text:
                data = new DataObject();
                data.SetData(DataFormats.UnicodeText, item.Text);
                if (!plain)
                {
                    if (!string.IsNullOrEmpty(item.Html)) data.SetData(DataFormats.Html, item.Html);
                    if (!string.IsNullOrEmpty(item.Rtf)) data.SetData(DataFormats.Rtf, item.Rtf);
                }
                break;

            case ClipKind.Files:
                var files = (item.Files ?? item.Text.Split('\n').ToList()).Where(f => File.Exists(f) || Directory.Exists(f)).ToList();
                if (files.Count == 0) return Result.Missing;
                data = new DataObject();
                if (plain)
                {
                    var text = string.Join(Environment.NewLine, files);
                    data.SetData(DataFormats.UnicodeText, text);
                    guardHash = ClipText.Hash(ClipKind.Text, text);
                }
                else
                {
                    var list = new StringCollection();
                    list.AddRange(files.ToArray());
                    data.SetFileDropList(list);
                    // 告诉资源管理器这是"复制"不是"剪切"，粘贴后原文件还在
                    data.SetData("Preferred DropEffect", new MemoryStream(BitConverter.GetBytes(1)));
                    if (files.Count != (item.Files?.Count ?? files.Count))
                        guardHash = ClipText.Hash(ClipKind.Files, string.Join("\n", files));
                }
                break;

            case ClipKind.Image:
                var path = item.Image == null ? null : Path.Combine(imagesDir, item.Image);
                if (path == null || !File.Exists(path)) return Result.Missing;
                byte[] png;
                try { png = File.ReadAllBytes(path); }
                catch { return Result.Missing; }
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.StreamSource = new MemoryStream(png);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bmp.Freeze();
                data = new DataObject();
                data.SetImage(bmp);
                data.SetData("PNG", new MemoryStream(png));   // 支持 PNG 的软件能保留透明背景
                break;

            default:
                return Result.Failed;
        }

        ClipboardGuard.Expect(guardHash);
        for (int attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                System.Windows.Clipboard.SetDataObject(data, true);
                ClipboardGuard.MarkOwnSequence();
                return Result.Ok;
            }
            catch (ExternalException) { Thread.Sleep(40 + attempt * 40); }
            catch (Exception ex) { ClipLog.Error("写剪贴板", ex); return Result.Failed; }
        }
        ClipLog.Write("写剪贴板失败：一直被别的软件占着");
        return Result.Failed;
    }

    /// <summary>模拟按一下 Ctrl+V。用户还按着 Shift 的话先松开它，免得变成 Ctrl+Shift+V。</summary>
    public static void SendPasteKeys()
    {
        var keys = new List<Native.INPUT>();
        foreach (int vk in new[] { 0xA0, 0xA1 })
            if (Native.IsDown(vk)) keys.Add(Native.Key((ushort)vk, true));
        keys.Add(Native.Key(0x11, false));
        keys.Add(Native.Key(0x56, false));
        keys.Add(Native.Key(0x56, true));
        keys.Add(Native.Key(0x11, true));
        Native.Send(keys.ToArray());
    }
}
