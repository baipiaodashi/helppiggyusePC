using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace PiggyHelper.Services;

/// <summary>
/// 全局键盘监听：只用来判断"是不是一直按着 Ctrl / Alt / Win"。
/// 不记录、不拦截、不上传任何按键，每个按键都原样交还给系统。
/// </summary>
public sealed class KeyboardHook : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100, WM_KEYUP = 0x0101, WM_SYSKEYDOWN = 0x0104, WM_SYSKEYUP = 0x0105;

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    private readonly LowLevelKeyboardProc _proc; // 必须保存引用，否则会被垃圾回收导致崩溃
    private IntPtr _hook;

    /// <summary>参数：虚拟键码，是否按下。</summary>
    public event Action<int, bool>? KeyChanged;

    public KeyboardHook()
    {
        _proc = HookProc;
    }

    public bool Start()
    {
        if (_hook != IntPtr.Zero) return true;
        using var module = Process.GetCurrentProcess().MainModule;
        _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(module?.ModuleName), 0);
        return _hook != IntPtr.Zero;
    }

    public void Stop()
    {
        if (_hook == IntPtr.Zero) return;
        UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
    }

    private IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int msg = wParam.ToInt32();
            int vk = Marshal.ReadInt32(lParam);
            try
            {
                if (msg is WM_KEYDOWN or WM_SYSKEYDOWN) KeyChanged?.Invoke(vk, true);
                else if (msg is WM_KEYUP or WM_SYSKEYUP) KeyChanged?.Invoke(vk, false);
            }
            catch { /* 绝不能让异常影响用户打字 */ }
        }
        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    public void Dispose() => Stop();
}

/// <summary>判断"单独按住某个修饰键超过 N 秒"，然后通知显示提示卡。</summary>
public sealed class HoldDetector : IDisposable
{
    private readonly KeyboardHook _hook = new();
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Normal);
    private string? _holding;   // 正在按住的修饰键："Ctrl" / "Alt" / "Win"
    private bool _shown;

    public event Action<string>? HoldStarted;
    public event Action? HoldEnded;

    public HoldDetector()
    {
        _hook.KeyChanged += OnKey;
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

    public bool Start() => _hook.Start();
    public void Stop() { _hook.Stop(); Cancel(); }

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
