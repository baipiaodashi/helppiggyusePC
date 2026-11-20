using System.IO;
using System.Text;

namespace PiggyHelper.Services.Clips;

/// <summary>
/// 剪贴板功能的小日志（%LOCALAPPDATA%\PiggyHelper\Clipboard\log.txt），出问题时方便排查。
/// 只记"发生了什么"（种类、大小、出错原因），绝不记复制的内容本身。
/// </summary>
public static class ClipLog
{
    private const long MaxBytes = 256 * 1024;
    private static readonly object Gate = new();
    private static string? _path;

    public static void Init(string dir)
    {
        lock (Gate) _path = Path.Combine(dir, "log.txt");
    }

    public static void Write(string message)
    {
        lock (Gate)
        {
            if (_path == null) return;
            try
            {
                var fi = new FileInfo(_path);
                if (fi.Exists && fi.Length > MaxBytes)
                {
                    var old = Path.Combine(fi.DirectoryName!, "log.old.txt");
                    File.Copy(_path, old, true);
                    File.Delete(_path);
                }
                File.AppendAllText(_path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}\r\n", Encoding.UTF8);
            }
            catch { }
        }
    }

    public static void Error(string where, Exception ex) =>
        Write($"[错误] {where}: {ex.GetType().Name} {ex.Message}");
}
