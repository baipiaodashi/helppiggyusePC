using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace PiggyHelper.Services;

/// <summary>
/// 全局键盘监听。放在单独的线程上：小猪界面忙的时候，别的软件里打字也不会变卡。
/// 用来：① 判断"是不是一直按着 Ctrl / Alt / Win"（快捷键提示卡）；
///       ② 小猪剪贴板：拦下 Win+V，以及剪贴板小窗口开着时的方向键、回车、Esc。
/// 除了上面这几个键，其余按键都原样交还给系统。不记录、不上传任何按键。
/// </summary>
public sealed class KeyboardHook : IDisposable
{
    /// <summary>整个程序共用一个监听。</summary>
    public static KeyboardHook Shared { get; } = new();

    private const int WH_KEYBOARD_LL = 13, WH_MOUSE_LL = 14;
    private const int WM_KEYDOWN = 0x0100, WM_KEYUP = 0x0101, WM_SYSKEYDOWN = 0x0104, WM_SYSKEYUP = 0x0105;
    private const int WM_LBUTTONDOWN = 0x0201, WM_RBUTTONDOWN = 0x0204, WM_MBUTTONDOWN = 0x0207, WM_XBUTTONDOWN = 0x020B;
    private const uint WM_QUIT = 0x0012, WM_APP = 0x8000;
    private const uint MsgMouseOn = WM_APP + 1, MsgMouseOff = WM_APP + 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT { public uint vkCode, scanCode, flags, time; public IntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT { public int x, y; public uint mouseData, flags, time; public IntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public int x, y; }

    private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, HookProc proc, IntPtr hMod, uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? name);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out MSG msg, IntPtr hwnd, uint min, uint max);

    [DllImport("user32.dll")]
    private static extern bool PeekMessage(out MSG msg, IntPtr hwnd, uint min, uint max, uint remove);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostThreadMessage(uint threadId, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    /// <summary>小猪自己模拟的按键带的记号（"PIGG"），监听时直接放过。</summary>
    public static readonly IntPtr OwnInputTag = new(0x50494747);

    // 两个回调必须一直留着引用，否则会被垃圾回收导致崩溃
    private readonly HookProc _keyProc, _mouseProc;
    private IntPtr _keyHook, _mouseHook;
    private Thread? _thread;
    private uint _threadId;
    private Dispatcher? _ui;

    /// <summary>参数：虚拟键码，是否按下。在界面线程上触发。</summary>
    public event Action<int, bool>? KeyChanged;

    /// <summary>鼠标按下（屏幕坐标，物理像素）。只有调用 <see cref="WatchMouse"/>(true) 以后才有，在界面线程上触发。</summary>
    public event Action<int, int>? MouseDown;

    /// <summary>
    /// 在监听线程上直接调用，参数：虚拟键码，是否按下；返回 true 表示这个键被小猪用掉了，不再传给别的软件。
    /// 必须很快返回，不能碰界面。
    /// </summary>
    public Func<int, bool, bool>? Interceptor { get; set; }

    public bool IsRunning => _keyHook != IntPtr.Zero;

    public KeyboardHook()
    {
        _keyProc = KeyProc;
        _mouseProc = MouseProc;
    }

    /// <summary>在界面线程上调用（事件会回到这个线程）。</summary>
    public bool Start()
    {
        if (_thread != null) return IsRunning;
        _ui = Dispatcher.CurrentDispatcher;
        using var ready = new ManualResetEventSlim();
        _thread = new Thread(() =>
        {
            _threadId = GetCurrentThreadId();
            PeekMessage(out _, IntPtr.Zero, 0, 0, 0);   // 先建好消息队列，别的线程才能发消息过来
            _keyHook = SetWindowsHookEx(WH_KEYBOARD_LL, _keyProc, GetModuleHandle(null), 0);
            ready.Set();
            if (_keyHook == IntPtr.Zero) return;
            while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
            {
                if (msg.hwnd != IntPtr.Zero) continue;
                if (msg.message == MsgMouseOn && _mouseHook == IntPtr.Zero)
                    _mouseHook = SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, GetModuleHandle(null), 0);
                else if (msg.message == MsgMouseOff) RemoveMouseHook();
            }
            RemoveMouseHook();
            UnhookWindowsHookEx(_keyHook);
            _keyHook = IntPtr.Zero;
        })
        { IsBackground = true, Name = "PiggyKeyboard", Priority = ThreadPriority.AboveNormal };
        _thread.Start();
        ready.Wait(3000);
        if (!IsRunning) _thread = null;
        return IsRunning;
    }

    public void Stop()
    {
        var t = _thread;
        if (t == null) return;
        _thread = null;
        PostThreadMessage(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        t.Join(2000);
    }

    /// <summary>需要知道"点没点到窗口外面"时打开鼠标监听，用完关掉。</summary>
    public void WatchMouse(bool on)
    {
        if (_thread != null) PostThreadMessage(_threadId, on ? MsgMouseOn : MsgMouseOff, IntPtr.Zero, IntPtr.Zero);
    }

    private void RemoveMouseHook()
    {
        if (_mouseHook == IntPtr.Zero) return;
        UnhookWindowsHookEx(_mouseHook);
        _mouseHook = IntPtr.Zero;
    }

    private IntPtr KeyProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            try
            {
                var k = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                int msg = wParam.ToInt32();
                bool down = msg is WM_KEYDOWN or WM_SYSKEYDOWN;
                if ((down || msg is WM_KEYUP or WM_SYSKEYUP) && k.dwExtraInfo != OwnInputTag)
                {
                    int vk = (int)k.vkCode;
                    bool swallow = Interceptor?.Invoke(vk, down) == true;
                    if (KeyChanged != null) _ui?.BeginInvoke(() => KeyChanged?.Invoke(vk, down));
                    if (swallow) return new IntPtr(1);
                }
            }
            catch { /* 绝不能让异常影响用户打字 */ }
        }
        return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }

    private IntPtr MouseProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            try
            {
                int msg = wParam.ToInt32();
                if ((msg is WM_LBUTTONDOWN or WM_RBUTTONDOWN or WM_MBUTTONDOWN or WM_XBUTTONDOWN) && MouseDown != null)
                {
                    var m = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                    if (m.dwExtraInfo != OwnInputTag) _ui?.BeginInvoke(() => MouseDown?.Invoke(m.x, m.y));
                }
            }
            catch { }
        }
        return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }

    public void Dispose() => Stop();
}

/// <summary>判断"单独按住某个修饰键超过 N 秒"，然后通知显示提示卡。</summary>
public sealed class HoldDetector : IDisposable
{
    private readonly KeyboardHook _hook = KeyboardHook.Shared;
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Normal);
    private string? _holding;   // 正在按住的修饰键："Ctrl" / "Alt" / "Win"
    private bool _shown, _listening;

    public event Action<string>? HoldStarted;
    public event Action? HoldEnded;

    public HoldDetector()
    {
        _timer.Tick += (_, _) =>
        {
            _timer.Stop();
            if (_holding != null && Allowed(_holding))
            {
                _shown = true;
                HoldStarted?.Invoke(_holding);
            }
        };
    }

    public bool Start()
    {
        if (!_listening)
        {
            _hook.KeyChanged += OnKey;
            _listening = true;
        }
        return _hook.Start();
    }

    public void Stop()
    {
        if (_listening)
        {
            _hook.KeyChanged -= OnKey;
            _listening = false;
        }
        Cancel();
    }

    private static string? ModifierOf(int vk) => vk switch
    {
        0x11 or 0xA2 or 0xA3 => "Ctrl",
        0x12 or 0xA4 or 0xA5 => "Alt",
        0x5B or 0x5C => "Win",
        _ => null,
    };

    private static bool Allowed(string mod)
    {
        var s = SettingsService.Current;
        return s.ShortcutPopup && mod switch
        {
            "Ctrl" => s.PopupCtrl,
            "Alt" => s.PopupAlt,
            "Win" => s.PopupWin,
            _ => false,
        };
    }

    private void OnKey(int vk, bool down)
    {
        var mod = ModifierOf(vk);
        if (down)
        {
            if (mod != null && _holding == null && !_shown)
            {
                _holding = mod;
                _timer.Interval = TimeSpan.FromSeconds(Math.Clamp(SettingsService.Current.HoldSeconds, 0.3, 5));
                _timer.Start();
            }
            else if (mod != null && mod == _holding)
            {
                // 按住不放时系统会重复发送按下事件，忽略
            }
            else
            {
                Cancel(); // 按了别的键，说明在用快捷键，不打扰
            }
        }
        else if (mod != null && mod == _holding)
        {
            Cancel();
        }
        else if (mod != null && _shown)
        {
            Cancel();
        }
    }

    private void Cancel()
    {
        _timer.Stop();
        _holding = null;
        if (_shown)
        {
            _shown = false;
            HoldEnded?.Invoke();
        }
    }

    public void Dispose() => Stop();
}
