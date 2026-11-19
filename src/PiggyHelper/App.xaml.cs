using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PiggyHelper.Services;
using PiggyHelper.Windows;

namespace PiggyHelper;

public partial class App : Application
{
    public static string VersionText =>
        System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";

    /// <summary>自动测试用：打开页面后自动执行的动作（scan = 自动扫描，big = 查找大文件，demo = AI 示例对话）。</summary>
    public static string? AutoAction { get; private set; }

    public static TrayService? Tray { get; private set; }
    public static bool IsExiting { get; private set; }

    private static Mutex? _instanceMutex;
    private static EventWaitHandle? _showSignal;
    private HoldDetector? _hold;
    private ShortcutOverlay? _overlay;
    private MainWindow? _main;

    // 自动测试用：--screenshot 文件路径 会在界面画好后保存截图并退出
    private string? _screenshotPath;
    private double _screenshotDelay = 3;

    // ---------- 只允许开一个小猪 ----------
    /// <summary>占用"只能开一个小猪"的锁。已经有一个在运行时返回 false。</summary>
    public static bool ClaimSingleInstance()
    {
        _instanceMutex ??= new Mutex(false, "PiggyHelper.SingleInstance");
        try { return _instanceMutex.WaitOne(0); }
        catch (AbandonedMutexException) { return true; }
    }

    public static void ReleaseSingleInstance()
    {
        try { _instanceMutex?.ReleaseMutex(); } catch { }
    }

    public static void ExitApp()
    {
        IsExiting = true;
        Current.Shutdown();
    }

    private void OnStartup(object sender, StartupEventArgs e)
    {
        string? startPage = null, query = null;
        bool minimized = false;
        for (int i = 0; i < e.Args.Length; i++)
        {
            string? next = i + 1 < e.Args.Length ? e.Args[i + 1] : null;
            switch (e.Args[i])
            {
                case "--page": startPage = next; break;
                case "--query": query = next; break;
                case "--screenshot": _screenshotPath = next; break;
                case "--auto": AutoAction = next; break;
                case "--delay" when next != null:
                    _screenshotDelay = double.Parse(next, System.Globalization.CultureInfo.InvariantCulture); break;
                case "--minimized": minimized = true; break;
            }
        }

        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        bool smokeTest = _screenshotPath != null;

        if (!smokeTest)
        {
            if (!ClaimSingleInstance())
            {
                // 已经开着一个小猪了：叫它出来，自己退出
                try { EventWaitHandle.OpenExisting("PiggyHelper.Show").Set(); } catch { }
                Shutdown();
                return;
            }
            StartShowListener();

            Tray = new TrayService();
            Tray.OpenRequested += ShowMainWindow;
            Tray.ExitRequested += ExitApp;

            _overlay = new ShortcutOverlay();
            _hold = new HoldDetector();
            _hold.HoldStarted += mod => _overlay.ShowFor(mod);
            _hold.HoldEnded += () => _overlay.HideCard();
            _hold.Start();
        }

        _main = new MainWindow();
        if (!minimized) _main.Show();
        if (startPage != null && startPage != "overlay") _main.Navigate(startPage, query);

        if (smokeTest)
        {
            Window target = _main;
            if (startPage == "overlay")
            {
                _overlay = new ShortcutOverlay();
                _overlay.ShowFor("Ctrl");
                target = _overlay;
            }
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(_screenshotDelay) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                SaveScreenshot(target, _screenshotPath!);
                ExitApp();
            };
            timer.Start();
        }
    }

    /// <summary>别的小猪进程想启动时，会通过这个信号让我们把窗口叫出来。</summary>
    private void StartShowListener()
    {
        _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, "PiggyHelper.Show");
        var t = new Thread(() =>
        {
            while (!IsExiting)
            {
                try
                {
                    if (_showSignal.WaitOne(1000)) Dispatcher.BeginInvoke(ShowMainWindow);
                }
                catch { return; }
            }
        }) { IsBackground = true, Name = "ShowListener" };
        t.Start();
    }

    public void ShowMainWindow()
    {
        _main ??= new MainWindow();
        if (!_main.IsVisible) _main.Show();
        if (_main.WindowState == WindowState.Minimized) _main.WindowState = WindowState.Normal;
        _main.Activate();
        _main.Topmost = true;  // 确保跳到最前面
        _main.Topmost = false;
        _main.Focus();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        IsExiting = true;
        _hold?.Dispose();
        Tray?.Dispose();
        ReleaseSingleInstance();
        base.OnExit(e);
    }

    private static void SaveScreenshot(Window window, string path)
    {
        FrameworkElement root = window.Content is FrameworkElement fe && window is MainWindow ? fe : window;
        double w = root.ActualWidth, h = root.ActualHeight;
        var dpi = VisualTreeHelper.GetDpi(window);
        var bmp = new RenderTargetBitmap(
            (int)Math.Ceiling(w * dpi.DpiScaleX), (int)Math.Ceiling(h * dpi.DpiScaleY),
            dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        // 先铺一层背景色，再直接画界面（不缩放）
        var bg = new DrawingVisual();
        using (var dc = bg.RenderOpen())
        {
            Brush back = window is MainWindow ? window.Background : new SolidColorBrush(Color.FromRgb(0xE4, 0xDC, 0xE2));
            dc.DrawRectangle(back, null, new Rect(0, 0, w, h));
        }
        bmp.Render(bg);
        bmp.Render(root);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bmp));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var fs = File.Create(path);
        encoder.Save(fs);
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        if (_screenshotPath != null)
        {
            // 自动测试时不弹窗，直接报错退出
            Console.Error.WriteLine(e.Exception);
            File.WriteAllText(_screenshotPath + ".error.txt", e.Exception.ToString());
            e.Handled = true;
            Shutdown(1);
            return;
        }
        MessageBox.Show("哎呀，小猪遇到了一点问题：\n\n" + e.Exception.Message,
            "小猪电脑助手", MessageBoxButton.OK, MessageBoxImage.Warning);
        e.Handled = true;
    }
}
