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
    private AiPage? _aiPage;
    private TaskCompletionSource<bool>? _dialog;

    public MainWindow()
    {
        InitializeComponent();
        VersionText.Text = "v" + App.VersionText;
        _toastTimer.Tick += (_, _) => HideToast();
        StateChanged += (_, _) => UpdateMaximizeState();
        SideCard.SizeChanged += (_, _) => FitSidebar();
        Closing += OnWindowClosing;
        NavHome.IsChecked = true;
        PreviewKeyDown += (_, e) =>
        {
            if (_dialog != null && e.Key == System.Windows.Input.Key.Escape) { CloseDialog(false); e.Handled = true; }
        };
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

    // ---------- 侧边栏：窗口矮的时候把上面的小猪变小 ----------
    private void FitSidebar()
    {
        // 卡片上下各留 20 的边，底部版本号大约 20 高，每个菜单按钮 62 高
        double room = SideCard.ActualHeight - 40 - 22 - NavList.Children.Count * 62;
        if (room <= 0) return;
        bool full = room >= 196, mid = room >= 128, small = room >= 70;
        SideMascot.Width = SideMascot.Height = full ? 104 : mid ? 76 : 56;
        SideTitle.Visibility = mid ? Visibility.Visible : Visibility.Collapsed;
        SideSlogan.Visibility = full ? Visibility.Visible : Visibility.Collapsed;
        SideTop.Visibility = small ? Visibility.Visible : Visibility.Collapsed;
        SideTop.Margin = new Thickness(0, 0, 0, full ? 22 : mid ? 14 : 10);
    }

    // ---------- 页面切换 ----------
    private static string KeyOf(RadioButton rb) => rb.Name switch
    {
        nameof(NavNavigator) => "navigator",
        nameof(NavCleanup) => "cleanup",
        nameof(NavShortcuts) => "shortcuts",
        nameof(NavClipboard) => "clipboard",
        nameof(NavAi) => "ai",
        nameof(NavSettings) => "settings",
        _ => "home",
    };

    private void OnSideNavChecked(object sender, RoutedEventArgs e)
    {
        if (_suppressNav) return;
        if (sender is RadioButton rb) ShowPage(KeyOf(rb), null);
    }

    /// <summary>跳转到某个页面（query 用于把搜索词带过去）。key 可以带子页面，比如 cleanup:big。</summary>
    public void Navigate(string key, string? query = null)
    {
        _suppressNav = true;
        var target = key.Split(':')[0] switch
        {
            "navigator" => NavNavigator,
            "cleanup" => NavCleanup,
            "shortcuts" => NavShortcuts,
            "clipboard" => NavClipboard,
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
        var parts = key.Split(':', 2);
        var sub = parts.Length > 1 ? parts[1] : null;
        UserControl page = parts[0] switch
        {
            "navigator" => new NavigatorPage(this, query),
            "cleanup" => new CleanupPage(this, sub),
            "shortcuts" => new ShortcutsPage(this),
            "clipboard" => new ClipboardPage(this),
            // AI 页面一直留着：切到别的页面再回来，聊天记录和正在做的事都还在
            "ai" => _aiPage ??= new AiPage(this),
            "settings" => new SettingsPage(this),
            _ => new HomePage(this),
        };
        if (page == _aiPage && !string.IsNullOrWhiteSpace(query)) _aiPage.Ask(query);
        PageHost.Content = page;
        page.Opacity = 0;
        page.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(180)));
    }

    /// <summary>小猪退出时关掉后台的 AI 引擎。</summary>
    public void ShutdownAgents() => _aiPage?.Shutdown();

    // ---------- 确认框 ----------

    /// <summary>
    /// 弹出一个盖住整个窗口的确认框。checkText 不为空时，要先勾选才能点确定。
    /// danger = true 时确定按钮是红色的。
    /// </summary>
    public Task<bool> ShowConfirmAsync(string title, string intro, IEnumerable<string> points, string okText,
        string? checkText = null, bool danger = false, string? tag = null, string pose = "think", string? cancelText = "再想想")
    {
        _dialog?.TrySetResult(false);
        _dialog = new TaskCompletionSource<bool>();
        DialogTitle.Text = title;
        DialogIntro.Text = intro;
        DialogIntro.Visibility = string.IsNullOrEmpty(intro) ? Visibility.Collapsed : Visibility.Visible;
        DialogPig.Pose = pose;
        DialogTag.Visibility = tag == null ? Visibility.Collapsed : Visibility.Visible;
        DialogTagText.Text = tag ?? "";
        DialogPoints.Children.Clear();
        foreach (var p in points)
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            var dot = new Border
            {
                Width = 8, Height = 8, CornerRadius = new CornerRadius(4), Margin = new Thickness(4, 8, 12, 0), VerticalAlignment = VerticalAlignment.Top,
                Background = danger ? new System.Windows.Media.SolidColorBrush(Services.Palette.Parse("#E5534B")) : (System.Windows.Media.Brush)FindResource("PinkBrush"),
            };
            DockPanel.SetDock(dot, Dock.Left);
            row.Children.Add(dot);
            row.Children.Add(new TextBlock { Text = p, FontSize = 14, LineHeight = 21, TextWrapping = TextWrapping.Wrap });
            DialogPoints.Children.Add(row);
        }
        DialogCheck.IsChecked = false;
        DialogCheckRow.Visibility = checkText == null ? Visibility.Collapsed : Visibility.Visible;
        DialogCheckText.Text = checkText ?? "";
        DialogOk.Content = okText;
        DialogCancel.Content = cancelText ?? "";
        DialogCancel.Visibility = cancelText == null ? Visibility.Collapsed : Visibility.Visible;
        DialogOk.IsEnabled = checkText == null;
        if (danger)
        {
            DialogOk.Background = new System.Windows.Media.SolidColorBrush(Services.Palette.Parse("#E5534B"));
            DialogOk.BorderBrush = new System.Windows.Media.SolidColorBrush(Services.Palette.Parse("#B93A33"));
        }
        else
        {
            DialogOk.ClearValue(BackgroundProperty);
            DialogOk.ClearValue(BorderBrushProperty);
        }

        DialogLayer.Visibility = Visibility.Visible;
        DialogLayer.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)));
        var pop = new DoubleAnimation(0.92, 1, TimeSpan.FromMilliseconds(240)) { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.5 } };
        DialogScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, pop);
        DialogScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, pop);
        return _dialog.Task;
    }

    private void CloseDialog(bool result)
    {
        var tcs = _dialog;
        _dialog = null;
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(140));
        fade.Completed += (_, _) => { if (_dialog == null) DialogLayer.Visibility = Visibility.Collapsed; };
        DialogLayer.BeginAnimation(OpacityProperty, fade);
        tcs?.TrySetResult(result);
    }

    private void OnDialogOk(object sender, RoutedEventArgs e) => CloseDialog(true);
    private void OnDialogCancel(object sender, RoutedEventArgs e) => CloseDialog(false);
    private void OnDialogCheckChanged(object sender, RoutedEventArgs e) => DialogOk.IsEnabled = DialogCheck.IsChecked == true;

    private void OnDialogCheckRowClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject d && IsInside(d, DialogCheck)) return; // 点的就是勾选框本身
        DialogCheck.IsChecked = DialogCheck.IsChecked != true;
    }

    private static bool IsInside(DependencyObject d, DependencyObject parent)
    {
        for (var x = d; x != null; x = System.Windows.Media.VisualTreeHelper.GetParent(x))
            if (x == parent) return true;
        return false;
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
