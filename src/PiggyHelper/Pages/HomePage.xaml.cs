using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PiggyHelper.Services;

namespace PiggyHelper.Pages;

public partial class HomePage : UserControl
{
    private readonly MainWindow _main;

    private static readonly string[] Bubbles =
    {
        "今天也要元气满满！", "有问题尽管问～", "C 盘还好吗？", "试试按住 Ctrl 一秒！", "小猪随时待命！", "试试按 Win+V！",
    };

    public HomePage(MainWindow main)
    {
        _main = main;
        InitializeComponent();
        var now = DateTime.Now;
        Greeting.Text = GreetingFor(now.Hour) + "，我是小猪！";
        DateText.Text = now.ToString("M月d日 dddd", CultureInfo.GetCultureInfo("zh-CN"));
        Bubble.Text = Bubbles[Random.Shared.Next(Bubbles.Length)];
        FavoriteList.ItemsSource = NavCatalog.Items.Where(i => i.Favorite).ToList();
        TipText.Text = DailyTips.Random();
    }

    private static string GreetingFor(int h) => h switch
    {
        < 5 => "夜深啦",
        < 11 => "早上好",
        < 13 => "中午好",
        < 18 => "下午好",
        _ => "晚上好",
    };

    private void OnSearchKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) DoSearch();
    }

    private void OnSearchClick(object sender, RoutedEventArgs e) => DoSearch();

    private void DoSearch() => _main.Navigate("navigator", SearchInput.Text);

    private void OnMoreClick(object sender, RoutedEventArgs e) => _main.Navigate("navigator");

    private void OnFeatureClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is string key) _main.Navigate(key);
    }

    private void OnNextTipClick(object sender, RoutedEventArgs e)
    {
        var old = TipText.Text;
        string next;
        do next = DailyTips.Random(); while (next == old);
        TipText.Text = next;
    }

    private void OnTileClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is NavItem item) NavigatorPage.OpenItem(_main, item);
    }
}
