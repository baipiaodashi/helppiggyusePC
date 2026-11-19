using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PiggyHelper.Services;

namespace PiggyHelper.Pages;

public partial class CleanupPage : UserControl
{
    private readonly MainWindow _main;
    private readonly List<CleanItem> _items = CleanupService.CreateItems();
    private readonly ObservableCollection<BigFile> _bigFiles = new();
    private bool _scanned;
    private bool _working;

    public CleanupPage(MainWindow main)
    {
        _main = main;
        InitializeComponent();
        ItemsList.ItemsSource = _items;
        foreach (var item in _items) item.PropertyChanged += OnItemChanged;
        BigList.ItemsSource = _bigFiles;

        if (!CleanupService.IsAdmin())
        {
            foreach (var item in _items.Where(i => i.NeedsAdmin))
            {
                item.IsChecked = false;
                item.Status = "需要管理员权限";
            }
            AdminBanner.Visibility = Visibility.Visible;
        }
        UpdateDisk();

        Loaded += (_, _) =>
        {
            if (App.AutoAction == "scan") OnActionClick(this, new RoutedEventArgs());
            if (App.AutoAction == "big") { TabBig.IsChecked = true; OnFindClick(this, new RoutedEventArgs()); }
        };
    }

    // ---------- 标签切换 ----------
    private void OnTabChecked(object sender, RoutedEventArgs e)
    {
        if (JunkPanel == null || BigPanel == null) return; // 初始化中
        bool big = TabBig.IsChecked == true;
        JunkPanel.Visibility = big ? Visibility.Collapsed : Visibility.Visible;
        BigPanel.Visibility = big ? Visibility.Visible : Visibility.Collapsed;
    }

    // ---------- C 盘空间 ----------
    private void UpdateDisk()
    {
        try
        {
            var sys = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
            var d = new DriveInfo(sys);
            long used = d.TotalSize - d.AvailableFreeSpace;
            double pct = d.TotalSize > 0 ? used * 100.0 / d.TotalSize : 0;
            DiskTitle.Text = $"{sys.TrimEnd('\\')} 盘空间（系统盘）";
            DiskBar.Value = pct;
            DiskBar.Foreground = new SolidColorBrush(pct >= 90 ? Palette.Parse("#FF6B6B") : pct >= 75 ? Palette.Parse("#FFB443") : Palette.Parse("#45C08D"));
            string mood = pct >= 90 ? "快满了，赶紧清理一下吧！" : pct >= 75 ? "有点挤，可以清理一下。" : "空间还挺充足。";
            DiskText.Text = $"已用 {Format.Bytes(used)}，共 {Format.Bytes(d.TotalSize)}，还剩 {Format.Bytes(d.AvailableFreeSpace)}。{mood}";
        }
        catch
        {
            DiskText.Text = "读取不到磁盘信息。";
        }
    }

    // ---------- 扫描 / 清理 ----------
    private async void OnActionClick(object sender, RoutedEventArgs e)
    {
        if (_working) return;
        _working = true;
        ActionButton.IsEnabled = false;
        try
        {
            if (!_scanned) await ScanAsync();
            else await CleanAsync();
        }
        finally
        {
            _working = false;
            ActionButton.IsEnabled = true;
        }
    }

    private async Task ScanAsync()
    {
        DoneBanner.Visibility = Visibility.Collapsed;
        ActionButton.Content = "正在扫描…";
        bool admin = CleanupService.IsAdmin();
        foreach (var item in _items)
        {
            item.Busy = true;
            item.Status = "正在扫描…";
            item.Size = -1;
        }
        foreach (var item in _items)
        {
            long size = await Task.Run(() => CleanupService.Measure(item));
            item.Size = size;
            item.Busy = false;
            if (item.NeedsAdmin && !admin) item.Status = "需要管理员权限";
            else item.Status = size > 0 ? "" : "很干净";
        }
        long total = _items.Where(i => i.IsChecked && i.Size > 0).Sum(i => i.Size);
        _scanned = true;
        ActionButton.Content = total > 0 ? $"一键清理 {Format.Bytes(total)}" : "重新扫描";
        if (total == 0) _scanned = false;
    }

    private void OnItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(CleanItem.IsChecked) || !_scanned || _working) return;
        long total = _items.Where(i => i.IsChecked && i.Size > 0).Sum(i => i.Size);
        ActionButton.Content = total > 0 ? $"一键清理 {Format.Bytes(total)}" : "请先勾选项目";
    }

    private async Task CleanAsync()
    {
        var targets = _items.Where(i => i.IsChecked && i.Size > 0).ToList();
        if (targets.Count == 0)
        {
            _main.ShowToast("先勾选要清理的项目吧～");
            return;
        }
        ActionButton.Content = "正在清理…";
        long freed = 0;
        int skipped = 0;
        foreach (var item in targets)
        {
            item.Busy = true;
            item.Status = "正在清理…";
            var r = await Task.Run(() => CleanupService.Clean(item));
            freed += r.FreedBytes;
            skipped += r.Skipped;
            item.Size = Math.Max(0, item.Size - r.FreedBytes);
            item.Busy = false;
            item.Status = r.Skipped > 0 ? $"已清理，{r.Skipped} 个正在使用的文件已跳过" : "已清理";
        }
        DoneTitle.Text = freed > 0 ? $"清理完成！帮你腾出了 {Format.Bytes(freed)} 空间" : "清理完成！";
        DoneDetail.Text = skipped > 0
            ? $"有 {skipped} 个文件正在被软件使用，小猪自动跳过了它们，不会出问题。关掉相关软件后再清理一次可以清得更干净。"
            : "电脑变得更清爽啦，记得隔一段时间再来清理一次哦。";
        DoneBanner.Visibility = Visibility.Visible;
        UpdateDisk();
        _scanned = false;
        ActionButton.Content = "重新扫描";
    }

    private void OnOpenRecycleBinClick(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe", "shell:RecycleBinFolder") { UseShellExecute = true }); }
        catch { }
    }

    private void OnRunAsAdminClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (exe == null) return;
            App.ReleaseSingleInstance();
            Process.Start(new ProcessStartInfo(exe, "--page cleanup") { UseShellExecute = true, Verb = "runas" });
            App.ExitApp();
        }
        catch (System.ComponentModel.Win32Exception)
        {
            App.ClaimSingleInstance();
            _main.ShowToast("没关系，不用管理员权限也能清理其他项目。");
        }
    }

    // ---------- 大文件 ----------
    private async void OnFindClick(object sender, RoutedEventArgs e)
    {
        FindButton.IsEnabled = false;
        FindButton.Content = "正在查找…";
        BigStatus.Text = "正在翻找各个文件夹，文件多的话需要一会儿……";
        BigEmptyText.Text = "小猪正在努力翻找……";
        foreach (var f in _bigFiles) f.PropertyChanged -= OnBigFileChanged;
        _bigFiles.Clear();
        var list = await Task.Run(() => BigFileService.Find());
        foreach (var f in list)
        {
            f.PropertyChanged += OnBigFileChanged;
            _bigFiles.Add(f);
        }
        BigStatus.Text = list.Count == 0
            ? "没有找到 20MB 以上的大文件，你的电脑很整洁！"
            : $"找到 {list.Count} 个大文件，一共 {Format.Bytes(list.Sum(f => f.Size))}。从大到小排好了。";
        BigBar.Visibility = list.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        BigEmpty.Visibility = list.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
        BigEmptyText.Text = "没有找到大文件，你的电脑很整洁！";
        FindButton.Content = "重新查找";
        FindButton.IsEnabled = true;
        UpdateSelection();
    }

    private void OnBigFileChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BigFile.IsChecked)) UpdateSelection();
    }

    private void UpdateSelection()
    {
        var sel = _bigFiles.Where(f => f.IsChecked).ToList();
        SelectionText.Text = sel.Count == 0 ? "还没有选择文件" : $"已选 {sel.Count} 个，共 {Format.Bytes(sel.Sum(f => f.Size))}";
        RecycleButton.IsEnabled = sel.Count > 0;
    }

    private void OnUnselectAllClick(object sender, RoutedEventArgs e)
    {
        foreach (var f in _bigFiles) f.IsChecked = false;
    }

    private void OnRevealClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not BigFile f) return;
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{f.Path}\"") { UseShellExecute = true }); }
        catch { }
    }

    private async void OnRecycleClick(object sender, RoutedEventArgs e)
    {
        var sel = _bigFiles.Where(f => f.IsChecked).ToList();
        if (sel.Count == 0) return;
        RecycleButton.IsEnabled = false;
        var done = await Task.Run(() => sel.Where(f => RecycleBin.MoveToRecycleBin(f.Path)).ToList());
        foreach (var f in done)
        {
            f.PropertyChanged -= OnBigFileChanged;
            _bigFiles.Remove(f);
        }
        if (_bigFiles.Count == 0)
        {
            BigBar.Visibility = Visibility.Collapsed;
            BigEmpty.Visibility = Visibility.Visible;
            BigEmptyText.Text = "全部整理完啦！";
        }
        int failed = sel.Count - done.Count;
        _main.ShowToast(done.Count > 0
            ? $"已把 {done.Count} 个文件（{Format.Bytes(done.Sum(f => f.Size))}）移到回收站。后悔了可以去回收站还原。" +
              (failed > 0 ? $"另有 {failed} 个文件正在使用，没能移动。" : "")
            : "文件可能正在被使用，没能移动。先关掉打开它的软件再试试。");
        UpdateSelection();
        UpdateDisk();
    }
}
