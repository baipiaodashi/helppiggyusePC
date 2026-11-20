using System.Windows.Threading;
using PiggyHelper.Windows;

namespace PiggyHelper.Services.Clips;

/// <summary>
/// 把小猪剪贴板的各部分接起来：监听剪贴板 → 仓库 → Win+V 小窗口。
/// 程序启动时 <see cref="Start"/>，设置改了 <see cref="ApplySettings"/>，退出时 <see cref="Stop"/>。
/// </summary>
public sealed class ClipboardFeature
{
    /// <summary>正在运行的那一个（设置页、托盘菜单要用）。</summary>
    public static ClipboardFeature? Current { get; private set; }

    public ClipboardStore Store { get; }
    public ClipboardMonitor Monitor { get; }
    public ClipboardHotkeys Hotkeys { get; }

    private readonly Dispatcher _ui;
    private readonly DispatcherTimer _hourly;
    private readonly Func<int, bool, bool> _intercept;
    private ClipboardPanel? _panel;
    private bool _started;

    /// <summary>有新复制的内容记下来了（界面线程）。</summary>
    public event Action<ClipItem>? Captured;

    /// <summary>小窗口里点了"设置"：打开主窗口的剪贴板页。</summary>
    public event Action? OpenManagerRequested;

    private static AppSettings S => SettingsService.Current;

    public ClipboardFeature(string? dir = null)
    {
        _ui = Dispatcher.CurrentDispatcher;
        Store = new ClipboardStore(dir ?? ClipboardStore.DefaultDir) { Limits = CurrentLimits };
        ClipLog.Init(Store.Dir);
        Store.Load();
        Monitor = new ClipboardMonitor(Store.ImagesDir, Store.ThumbsDir)
        {
            IgnoreSource = path => SourceApps.IsIgnored(path, S.ClipboardIgnoreApps),
        };
        Monitor.Captured += item => _ui.BeginInvoke(() => OnCaptured(item));
        Hotkeys = new ClipboardHotkeys(_ui);
        _intercept = Hotkeys.Intercept;
        Hotkeys.WinV += () => TogglePanel(Native.GetForegroundWindow());
        Hotkeys.NavKey += (vk, shift) => _panel?.OnNavKey(vk, shift);
        Hotkeys.TypedElsewhere += () => _panel?.OnTypedElsewhere();
        _hourly = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromHours(1) };
        _hourly.Tick += (_, _) => Store.Prune();
    }

    public static (int, int) CurrentLimits() =>
        (Math.Clamp(S.ClipboardMaxItems, 20, 5000), Math.Max(0, S.ClipboardKeepDays));

    /// <summary>小窗口（第一次用到时才创建）。</summary>
    public ClipboardPanel Panel
    {
        get
        {
            if (_panel == null)
            {
                _panel = new ClipboardPanel(Store, Hotkeys);
                _panel.OpenSettingsRequested += () => OpenManagerRequested?.Invoke();
            }
            return _panel;
        }
    }

    /// <param name="hotkeys">接管 Win+V（自检时不接管）。</param>
    public void Start(bool hotkeys = true)
    {
        Current = this;
        _started = true;
        if (hotkeys)
        {
            var hook = KeyboardHook.Shared;
            hook.Interceptor = _intercept;
            hook.MouseDown += OnMouseDown;
            if (!hook.Start()) ClipLog.Write("键盘监听没能启动，Win+V 用不了");
        }
        ApplySettings();
        _hourly.Start();
        Store.CleanupOrphansInBackground();
    }

    /// <summary>设置改了以后调用：开关监听和 Win+V、按新的条数和天数清理。</summary>
    public void ApplySettings()
    {
        if (S.ClipboardEnabled) Monitor.Start();
        else Monitor.Stop();
        Hotkeys.WinVEnabled = _started && S.ClipboardEnabled && S.ClipboardWinV && KeyboardHook.Shared.Interceptor == _intercept;
        if (!Hotkeys.WinVEnabled) _panel?.HidePanel();
        Store.Prune();
    }

    /// <summary>程序空闲时先把小窗口准备好。</summary>
    public void Prewarm()
    {
        try { Panel.Prewarm(); } catch (Exception ex) { ClipLog.Error("准备小窗口", ex); }
    }

    /// <summary>打开 / 收起小窗口（Win+V、托盘菜单、剪贴板页的按钮）。</summary>
    public void TogglePanel(IntPtr target)
    {
        try { Panel.Toggle(target); }
        catch (Exception ex) { ClipLog.Error("打开小窗口", ex); }
    }

    public void ShowPanel(IntPtr target)
    {
        try { Panel.ShowAt(target); }
        catch (Exception ex) { ClipLog.Error("打开小窗口", ex); }
    }

    public void Stop()
    {
        _hourly.Stop();
        Hotkeys.WinVEnabled = false;
        Hotkeys.Navigating = false;
        if (KeyboardHook.Shared.Interceptor == _intercept) KeyboardHook.Shared.Interceptor = null;
        KeyboardHook.Shared.MouseDown -= OnMouseDown;
        try { _panel?.HidePanel(); } catch { }
        Monitor.Stop();
        Store.Flush();
        if (Current == this) Current = null;
    }

    private void OnMouseDown(int x, int y) => _panel?.OnGlobalMouseDown(x, y);

    private void OnCaptured(ClipItem item)
    {
        if (item.Sensitive && S.ClipboardSkipSensitive)
        {
            ClipLog.Write("跳过：像密码的内容（设置里选了不记）");
            return;
        }
        var actual = Store.Add(item);
        Captured?.Invoke(actual);
    }
}
