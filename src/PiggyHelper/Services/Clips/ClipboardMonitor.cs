using System.Windows.Interop;
using System.Windows.Threading;

namespace PiggyHelper.Services.Clips;

/// <summary>
/// 在自己的后台线程上听"剪贴板变了"的通知，读出新内容交给 <see cref="Captured"/>。
/// 用单独的线程，是为了读大图片、等别的软件松开剪贴板时，不会让小猪的界面卡住。
/// </summary>
public sealed class ClipboardMonitor : IDisposable
{
    private readonly string _imagesDir, _thumbsDir;
    private Thread? _thread;
    private Dispatcher? _dispatcher;
    private HwndSource? _window;
    private DispatcherTimer? _debounce;
    private uint _lastSeq;
    private bool _busy, _again;
    private (string? Path, string? Name) _source;

    /// <summary>读到一条新内容（在监听线程上触发）。</summary>
    public event Action<ClipItem>? Captured;

    /// <summary>判断要不要跳过这个软件（在监听线程上调用）。</summary>
    public Func<string?, bool>? IgnoreSource { get; set; }

    public bool IsRunning => _thread != null;

    public ClipboardMonitor(string imagesDir, string thumbsDir)
    {
        _imagesDir = imagesDir;
        _thumbsDir = thumbsDir;
    }

    public bool Start()
    {
        if (_thread != null) return true;
        bool ok = false;
        using var ready = new ManualResetEventSlim();
        _thread = new Thread(() =>
        {
            try
            {
                _dispatcher = Dispatcher.CurrentDispatcher;
                _debounce = new DispatcherTimer(DispatcherPriority.Normal, _dispatcher) { Interval = TimeSpan.FromMilliseconds(70) };
                _debounce.Tick += (_, _) => { _debounce.Stop(); Process(); };
                var p = new HwndSourceParameters("PiggyClipboardMonitor") { ParentWindow = Native.HWND_MESSAGE, WindowStyle = 0 };
                _window = new HwndSource(p);
                _window.AddHook(WndProc);
                ok = Native.AddClipboardFormatListener(_window.Handle);
                _lastSeq = Native.GetClipboardSequenceNumber();   // 启动前就在剪贴板里的东西不算新复制
            }
            catch (Exception ex) { ClipLog.Error("启动剪贴板监听", ex); }
            finally { ready.Set(); }
            if (ok) Dispatcher.Run();
        })
        { IsBackground = true, Name = "PiggyClipboard" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        ready.Wait(5000);
        if (!ok)
        {
            ClipLog.Write("剪贴板监听没能启动");
            _thread = null;
            return false;
        }
        ClipLog.Write("剪贴板监听已启动");
        return true;
    }

    public void Stop()
    {
        var t = _thread;
        if (t == null) return;
        _thread = null;
        try
        {
            _dispatcher?.Invoke(() =>
            {
                _debounce?.Stop();
                if (_window != null)
                {
                    Native.RemoveClipboardFormatListener(_window.Handle);
                    _window.Dispose();
                    _window = null;
                }
            }, DispatcherPriority.Send, CancellationToken.None, TimeSpan.FromSeconds(3));
            _dispatcher?.InvokeShutdown();
        }
        catch { }
        t.Join(3000);
        _dispatcher = null;
        ClipLog.Write("剪贴板监听已停止");
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Native.WM_CLIPBOARDUPDATE)
        {
            handled = true;
            // 复制的那一刻看是哪个软件（等一会儿用户可能已经切走了）
            if (!ClipboardGuard.IsOwnSequence(Native.GetClipboardSequenceNumber()))
                _source = SourceApps.Current();
            _debounce?.Stop();
            _debounce?.Start();   // 很多软件一次复制会连着改好几次剪贴板，等它们改完再读
        }
        return IntPtr.Zero;
    }

    private void Process()
    {
        if (_busy) { _again = true; return; }   // 读的过程中系统可能又通知，读完再来一次
        _busy = true;
        try
        {
            uint seq = Native.GetClipboardSequenceNumber();
            if (seq == _lastSeq) return;
            _lastSeq = seq;
            if (ClipboardGuard.IsOwnSequence(seq)) return;

            var source = _source;
            _source = default;
            if (IgnoreSource?.Invoke(source.Path) == true)
            {
                ClipLog.Write("跳过：来自设置里不记录的软件");
                return;
            }
            var privacy = ClipboardReader.PrivacyMark();
            if (privacy != null) { ClipLog.Write("跳过：" + privacy); return; }

            var item = ClipboardReader.Read(_imagesDir, _thumbsDir, out var note);
            if (item == null) { ClipLog.Write("跳过：" + note); return; }
            if (ClipboardGuard.IsExpected(item.Hash)) return;   // 小猪自己刚放进去的

            item.SourcePath = source.Path;
            item.SourceName = source.Name;
            ClipLog.Write($"记录：{note}，来自 {source.Name ?? "未知"}");
            Captured?.Invoke(item);
        }
        catch (Exception ex) { ClipLog.Error("读剪贴板", ex); }
        finally
        {
            _busy = false;
            if (_again)
            {
                _again = false;
                _debounce?.Stop();
                _debounce?.Start();
            }
        }
    }

    public void Dispose() => Stop();
}
