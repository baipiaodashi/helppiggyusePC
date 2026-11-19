using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using PiggyHelper.Services;
using PiggyHelper.Windows;

namespace PiggyHelper.Pages;

public partial class ShortcutsPage : UserControl
{
    private readonly MainWindow _main;
    private string _category = ShortcutCatalog.All;
    private bool _ready;
    private ShortcutOverlay? _preview;

    public ShortcutsPage(MainWindow main)
    {
        _main = main;
        InitializeComponent();
        PopupSwitch.IsChecked = SettingsService.Current.ShortcutPopup;
        CategoryBar.ItemsSource = ShortcutCatalog.Categories;
        _ready = true;
        Loaded += (_, _) =>
        {
            if (CategoryBar.ItemContainerGenerator.ContainerFromIndex(0) is DependencyObject cp && FirstRadio(cp) is RadioButton rb)
                rb.IsChecked = true;
            Refresh();
        };
        Unloaded += (_, _) => _preview?.Close();
    }

    private static RadioButton? FirstRadio(DependencyObject parent)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is RadioButton rb) return rb;
            if (FirstRadio(child) is { } found) return found;
        }
        return null;
    }

    private void OnSwitchChanged(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        SettingsService.Current.ShortcutPopup = PopupSwitch.IsChecked == true;
        SettingsService.Save();
        _main.ShowToast(PopupSwitch.IsChecked == true
            ? "已打开：在任何地方按住 Ctrl、Alt 或 Win 一秒钟，就能看到快捷键提示。"
            : "已关闭按住提示卡。想用的时候再回来打开。");
    }

    /// <summary>演示一下提示卡长什么样（4 秒后自动消失）。</summary>
    private void OnTryClick(object sender, RoutedEventArgs e)
    {
        _preview ??= new ShortcutOverlay();
        _preview.ShowFor("Ctrl");
        var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        t.Tick += (_, _) => { t.Stop(); _preview?.HideCard(); };
        t.Start();
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
        var list = ShortcutCatalog.Search(SearchInput.Text, _category).ToList();
        ResultList.ItemsSource = list;
        EmptyState.Visibility = list.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
}
