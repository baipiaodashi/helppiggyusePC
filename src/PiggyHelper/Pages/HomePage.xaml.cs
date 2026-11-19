using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PiggyHelper.Services;

namespace PiggyHelper.Pages;

public partial class HomePage : UserControl
{
    private readonly MainWindow _main;

    public HomePage(MainWindow main)
    {
        _main = main;
        InitializeComponent();
        Greeting.Text = GreetingForNow() + "，我是小猪！";
        FavoriteList.ItemsSource = NavCatalog.Items.Where(i => i.Favorite).ToList();
        TipText.Text = DailyTips.Random();
    }

    private static string GreetingForNow()
    {
        var h = DateTime.Now.Hour;
        return h switch
        {
            < 5 => "夜深啦",
            < 11 => "早上好",
            < 13 => "中午好",
            < 18 => "下午好",
            _ => "晚上好",
        };
    }

    private void OnSearchKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) DoSearch();
    }

    private void OnSearchClick(object sender, RoutedEventArgs e) => DoSearch();

    private void DoSearch() => _main.Navigate("navigator", SearchInput.Text);

    private void OnMoreClick(object sender, RoutedEventArgs e) => _main.Navigate("navigator");

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
