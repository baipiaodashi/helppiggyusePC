using System.Windows.Threading;

namespace PiggyHelper.Services.Clips;

/// <summary>
/// 把小猪剪贴板的各部分接起来：监听 → 仓库 →（Win+V）弹窗。
/// 程序启动时 <see cref="Start"/>，设置改了 <see cref="ApplySettings"/>，退出时 <see cref="Stop"/>。
/// </summary>
public sealed class ClipboardFeature
{
    /// <summary>正在运行的那一个（设置页、托盘菜单要用）。</summary>
    public static ClipboardFeature? Current { get; private set; }

    public ClipboardStore Store { get; }
    public ClipboardMonitor Monitor { get; }

    private readonly Dispatcher _ui;
    private readonly DispatcherTimer _hourly;

    /// <summary>有新复制的内容记下来了（界面线程）。</summary>
    public event Action<ClipItem>? Captured;

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
        _hourly = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromHours(1) };
        _hourly.Tick += (_, _) => Store.Prune();
    }

    public static (int, int) CurrentLimits() =>
        (Math.Clamp(S.ClipboardMaxItems, 20, 5000), Math.Max(0, S.ClipboardKeepDays));

    public void Start()
    {
        Current = this;
        ApplySettings();
        _hourly.Start();
        Store.CleanupOrphansInBackground();
    }

    /// <summary>设置改了以后调用：开关监听、按新的条数和天数清理。</summary>
    public void ApplySettings()
    {
        if (S.ClipboardEnabled) Monitor.Start();
        else Monitor.Stop();
        Store.Prune();
    }

    public void Stop()
    {
        _hourly.Stop();
        Monitor.Stop();
        Store.Flush();
        if (Current == this) Current = null;
    }

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
