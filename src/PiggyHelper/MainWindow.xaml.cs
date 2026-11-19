using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using PiggyHelper.Pages;

namespace PiggyHelper;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromSeconds(6) };
    private bool _suppressNav;

    public MainWindow()
    {
        InitializeComponent();
        _toastTimer.Tick += (_, _) => HideToast();
        NavHome.IsChecked = true;
    }

    private void OnSideNavChecked(object sender, RoutedEventArgs e)
    {
        if (_suppressNav) return;
        if (sender is RadioButton { Tag: string key }) ShowPage(key, null);
    }

    /// <summary>跳转到某个页面（query 用于把首页搜索词带到功能导航）。</summary>
    public void Navigate(string key, string? query = null)
    {
        _suppressNav = true;
        var target = key switch
        {
            "navigator" => NavNavigator,
            "cleanup" => NavCleanup,
            "shortcuts" => NavShortcuts,
            "ai" => NavAi,
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
            "cleanup" => new ComingSoonPage("一键清理",
                "帮你清理临时文件、浏览器缓存、微信/QQ 缓存，让 C 盘重新变得宽敞。聊天文件会单独列出来，由你自己决定删不删。"),
            "shortcuts" => new ComingSoonPage("快捷键指南",
                "可以搜索的快捷键大全；按住 Ctrl 一秒钟，还会弹出常用快捷键的小卡片。"),
            "ai" => new ComingSoonPage("AI 问答",
                "填入自己的 DeepSeek 或通义千问 API Key，就能用大白话问电脑问题。"),
            _ => new HomePage(this),
        };
        PageHost.Content = page;
    }

    public void ShowToast(string message)
    {
        ToastText.Text = message;
        Toast.Visibility = Visibility.Visible;
        Toast.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(200)));
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
