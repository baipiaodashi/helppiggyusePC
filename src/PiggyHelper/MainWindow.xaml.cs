using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using PiggyHelper.Pages;

namespace PiggyHelper;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromSeconds(7) };
    private bool _suppressNav;

    public MainWindow()
    {
        InitializeComponent();
        VersionText.Text = "v" + App.VersionText;
        _toastTimer.Tick += (_, _) => HideToast();
        StateChanged += (_, _) => UpdateMaximizeState();
        Closing += OnWindowClosing;
        NavHome.IsChecked = true;
    }

    // ---------- 标题栏 ----------
    private void OnMinimizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximizeClick(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    /// <summary>点关闭时：默认缩到右下角托盘，按住 Ctrl 的提示卡继续可用。</summary>
    private void OnWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (App.IsExiting) return;
        var s = Services.SettingsService.Current;
        if (App.Tray != null && s.CloseToTray)
        {
            e.Cancel = true;
            Hide();
            if (!s.TrayTipShown)
            {
                App.Tray.ShowTip("小猪还在这里哦", "我躲到右下角的托盘里啦，按住 Ctrl 一秒还能看快捷键。想彻底退出，右键小猪图标选\u201C退出小猪\u201D。");
                s.TrayTipShown = true;
                Services.SettingsService.Save();
            }
            return;
        }
        App.ExitApp();
    }

    private void UpdateMaximizeState()
    {
        // 最大化时窗口会超出屏幕一圈，留出边距避免内容被裁掉
        RootGrid.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
        MaxButton.Content = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
        MaxButton.ToolTip = WindowState == WindowState.Maximized ? "还原" : "最大化";
    }

    // ---------- 页面切换 ----------
    private static string KeyOf(RadioButton rb) => rb.Name switch
    {
        nameof(NavNavigator) => "navigator",
        nameof(NavCleanup) => "cleanup",
        nameof(NavShortcuts) => "shortcuts",
        nameof(NavAi) => "ai",
        nameof(NavSettings) => "settings",
        _ => "home",
    };

    private void OnSideNavChecked(object sender, RoutedEventArgs e)
    {
        if (_suppressNav) return;
        if (sender is RadioButton rb) ShowPage(KeyOf(rb), null);
    }

    /// <summary>跳转到某个页面（query 用于把搜索词带过去）。</summary>
    public void Navigate(string key, string? query = null)
    {
        _suppressNav = true;
        var target = key switch
        {
            "navigator" => NavNavigator,
            "cleanup" => NavCleanup,
            "shortcuts" => NavShortcuts,
            "ai" => NavAi,
            "settings" => NavSettings,
            _ => NavHome,
        };
        target.IsChecked = true;
        _suppressNav = false;
        ShowPage(key, query);
    }

    private void ShowPage(string key, string? query)
    {
        UserControl page = key switch
        {
            "navigator" => new NavigatorPage(this, query),
            "cleanup" => new CleanupPage(this),
            "shortcuts" => new ShortcutsPage(this),
            "ai" => new ComingSoonPage("AI 问答", "填入自己的 API Key，就能用大白话问电脑问题。"),
            "settings" => new ComingSoonPage("设置", "提示卡开关、开机自启、AI 设置都在这里。"),
            _ => new HomePage(this),
        };
        PageHost.Content = page;
        page.Opacity = 0;
        page.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(180)));
    }

    // ---------- 底部气泡 ----------
    public void ShowToast(string message)
    {
        ToastText.Text = message;
        Toast.Visibility = Visibility.Visible;
        Toast.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(200)));
        ToastShift.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty,
            new DoubleAnimation(16, 0, TimeSpan.FromMilliseconds(240)) { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.4 } });
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    private void HideToast()
    {
        _toastTimer.Stop();
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(300));
        fade.Completed += (_, _) =>
        {
            if (Toast.Opacity == 0) Toast.Visibility = Visibility.Collapsed;
        };
        Toast.BeginAnimation(OpacityProperty, fade);
    }
}
