using System.Windows.Threading;

namespace PiggyHelper.Services.Clips;

/// <summary>
/// 小猪剪贴板的按键：
/// · Win+V：拦下来（Windows 自带的剪贴板就不会弹出来），换成打开小猪剪贴板；
/// · 小窗口开着、光标还在原来的软件里时：↑↓←→、回车、Esc、PageUp/PageDown、Home/End 交给小窗口用；
/// · 这时按了别的键（比如继续打字），就把小窗口收起来，按键照常给原来的软件。
/// <see cref="Intercept"/> 在键盘监听线程上运行，要很快返回。
/// </summary>
public sealed class ClipboardHotkeys
{
    public const int VK_V = 0x56, VK_MASK = 0xE8;
    public const int VK_RETURN = 0x0D, VK_ESCAPE = 0x1B, VK_PRIOR = 0x21, VK_NEXT = 0x22, VK_END = 0x23, VK_HOME = 0x24;
    public const int VK_LEFT = 0x25, VK_UP = 0x26, VK_RIGHT = 0x27, VK_DOWN = 0x28;
    private const int VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_MENU = 0x12, VK_LWIN = 0x5B, VK_RWIN = 0x5C;

    private readonly Dispatcher _ui;
    // 被小猪用掉的键（按下时记一下，松开时也要一起拦下），只在监听线程上用
    private readonly Dictionary<int, long> _held = new();

    /// <summary>接管 Win+V。</summary>
    public volatile bool WinVEnabled;

    /// <summary>小窗口开着，而且光标还在原来的软件里：方向键、回车、Esc 归小窗口。</summary>
    public volatile bool Navigating;

    /// <summary>按了 Win+V（界面线程）。</summary>
    public event Action? WinV;

    /// <summary>小窗口的导航键（界面线程）：键码、有没有按着 Shift。</summary>
    public event Action<int, bool>? NavKey;

    /// <summary>小窗口开着时用户按了别的键（界面线程）。</summary>
    public event Action? TypedElsewhere;

    public ClipboardHotkeys(Dispatcher ui) => _ui = ui;

    public bool Intercept(int vk, bool down)
    {
        long now = Environment.TickCount64;
        if (!down) return _held.Remove(vk);

        // 按住不放时系统会一直重复发"按下"：拦下的键继续拦，方向键继续移动
        if (_held.TryGetValue(vk, out long last))
        {
            if (now - last < 1500)
            {
                _held[vk] = now;
                if (Navigating && vk is VK_UP or VK_DOWN or VK_PRIOR or VK_NEXT)
                {
                    bool shift = Native.IsDown(VK_SHIFT);
                    _ui.BeginInvoke(() => NavKey?.Invoke(vk, shift));
                }
                return true;
            }
            _held.Remove(vk);   // 很久以前的记录（松开的消息没收到），当作新按下
        }

        bool win = Native.IsDown(VK_LWIN) || Native.IsDown(VK_RWIN);
        bool ctrl = Native.IsDown(VK_CONTROL), alt = Native.IsDown(VK_MENU);

        if (vk == VK_V && win && WinVEnabled && !ctrl && !alt && !Native.IsDown(VK_SHIFT))
        {
            _held[vk] = now;
            // 补按一个没用的键：松开 Win 时开始菜单就不会弹出来
            Native.Send(Native.Key(VK_MASK, false), Native.Key(VK_MASK, true));
            _ui.BeginInvoke(() => WinV?.Invoke());
            return true;
        }

        if (!Navigating || IsModifier(vk)) return false;

        if (IsNavKey(vk) && !ctrl && !alt && !win)
        {
            _held[vk] = now;
            bool shift = Native.IsDown(VK_SHIFT);
            _ui.BeginInvoke(() => NavKey?.Invoke(vk, shift));
            return true;
        }

        _ui.BeginInvoke(() => TypedElsewhere?.Invoke());
        return false;
    }

    public static bool IsNavKey(int vk) =>
        vk is VK_UP or VK_DOWN or VK_LEFT or VK_RIGHT or VK_RETURN or VK_ESCAPE or VK_PRIOR or VK_NEXT or VK_HOME or VK_END;

    private static bool IsModifier(int vk) =>
        vk is VK_SHIFT or VK_CONTROL or VK_MENU or VK_LWIN or VK_RWIN or 0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5
            or 0x14 /* CapsLock */ or 0x90 /* NumLock */ or 0x15 /* 输入法切换 */ or 0xE5 /* 输入法处理中 */ or VK_MASK;
}
