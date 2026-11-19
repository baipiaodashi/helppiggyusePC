using System.Windows;
using System.Windows.Controls;
using PiggyHelper.Services;

namespace PiggyHelper.Pages;

public partial class NavigatorPage : UserControl
{
    private readonly MainWindow _main;
    private string _category = NavCatalog.All;
    private bool _ready;

    public NavigatorPage(MainWindow main, string? query)
    {
        _main = main;
        InitializeComponent();
        CategoryBar.ItemsSource = NavCatalog.Categories;
        SearchInput.Text = query ?? "";
        _ready = true;
        Loaded += (_, _) =>
        {
            // 默认选中"全部"
            if (CategoryBar.ItemContainerGenerator.ContainerFromIndex(0) is ContentPresenter cp &&
                VisualTreeHelperFirstRadio(cp) is RadioButton rb)
            {
                rb.IsChecked = true;
            }
            Refresh();
            SearchInput.Focus();
            SearchInput.CaretIndex = SearchInput.Text.Length;
        };
    }

    private static RadioButton? VisualTreeHelperFirstRadio(DependencyObject parent)
    {
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is RadioButton rb) return rb;
            var found = VisualTreeHelperFirstRadio(child);
            if (found != null) return found;
        }
        return null;
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e)
    {
        if (_ready) Refresh();
    }

    private void OnCategoryChecked(object sender, RoutedEventArgs e)
    {
        if ((sender as RadioButton)?.Content is string c)
        {
            _category = c;
            if (_ready) Refresh();
        }
    }

    private void Refresh()
    {
        var results = NavCatalog.Search(SearchInput.Text, _category).ToList();
        ResultList.ItemsSource = results;
        EmptyState.Visibility = results.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnAskAiClick(object sender, RoutedEventArgs e) => _main.Navigate("ai", SearchInput.Text);

    private void OnTileClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is NavItem item) OpenItem(_main, item);
    }

    /// <summary>打开一项，并在底部用小气泡告诉用户接下来怎么做。</summary>
    public static void OpenItem(MainWindow main, NavItem item)
    {
        if (Launcher.Open(item, out var error))
        {
            main.ShowToast($"已经帮你打开“{item.Title}”。{item.Tip}");
        }
        else
        {
            main.ShowToast($"打开“{item.Title}”失败了，可能是系统版本不支持。（{error}）");
        }
    }
}
