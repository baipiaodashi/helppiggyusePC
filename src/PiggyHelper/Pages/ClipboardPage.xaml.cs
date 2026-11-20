using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using PiggyHelper.Services;
using PiggyHelper.Services.Clips;

namespace PiggyHelper.Pages;

/// <summary>主窗口里的"剪贴板"页：介绍 Win+V、开关、记多少、哪些软件不记、清空记录。</summary>
public partial class ClipboardPage : UserControl
{
    /// <summary>一个软件小胶囊：exe 文件名、显示的名字、图标。</summary>
    public sealed record AppChip(string File, string Name, ImageSource? Icon);

    private readonly MainWindow _main;
    private readonly ClipboardFeature? _feature;
    private readonly DispatcherTimer _statsTimer;
    private RadioButton? _maxChecked, _keepChecked;
    private bool _ready, _listening;

    private static AppSettings S => SettingsService.Current;

    public ClipboardPage(MainWindow main)
    {
        _main = main;
        _feature = ClipboardFeature.Current;
        InitializeComponent();

        EnabledSwitch.IsChecked = S.ClipboardEnabled;
        WinVSwitch.IsChecked = S.ClipboardWinV;
        SensitiveSwitch.IsChecked = S.ClipboardSkipSensitive;
        _maxChecked = new[] { Max100, Max300, Max1000 }.FirstOrDefault(r => (string)r.Tag == S.ClipboardMaxItems.ToString());
        _keepChecked = new[] { Keep1, Keep7, Keep30, Keep0 }.FirstOrDefault(r => (string)r.Tag == S.ClipboardKeepDays.ToString());
        if (_maxChecked != null) _maxChecked.IsChecked = true;
        if (_keepChecked != null) _keepChecked.IsChecked = true;
        DirText.Text = _feature?.Store.Dir ?? ClipboardStore.DefaultDir;

        _statsTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        _statsTimer.Tick += (_, _) => { _statsTimer.Stop(); UpdateStats(); };
        Loaded += (_, _) =>
        {
            if (_feature != null && !_listening) { _feature.Store.Changed += OnStoreChanged; _listening = true; }
            UpdateStats();
            // 自动测试截图用：滚到最下面 / 弹出清空确认框
            if (App.AutoAction == "bottom") Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => Scroller.ScrollToEnd());
            else if (App.AutoAction == "clear") Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => OnClearClick(this, new RoutedEventArgs()));
        };
        Unloaded += (_, _) =>
        {
            if (_feature != null && _listening) { _feature.Store.Changed -= OnStoreChanged; _listening = false; }
            _statsTimer.Stop();
        };

        UpdateStatus();
        RefreshApps();
        _ready = true;
    }

    // ---------- 状态和统计 ----------

    private void UpdateStatus()
    {
        string text, bg, fg;
        string? warnTitle = null, warnText = null;
        if (_feature == null)
        {
            (text, bg, fg) = ("没有在运行", "#FDE3E1", "#C9302C");
            warnTitle = "小猪剪贴板没能启动";
            warnText = "可以退出小猪（右键右下角托盘里的小猪图标，选“退出小猪”），再重新打开试试。";
            HeroText.Text = "平时复制的文字、图片、文件，小猪都会记下来。在任何软件里打字时按 Win+V，光标旁边就会弹出小窗口，点一下就粘贴进去。";
        }
        else if (S.ClipboardWinV && _feature.HotkeysFailed)
        {
            (text, bg, fg) = (S.ClipboardEnabled ? "正在记录 · Win+V 没接上" : "暂停记录 · Win+V 没接上", "#FFF0D1", "#B7791F");
            warnTitle = "Win+V 暂时用不了";
            warnText = "小猪没能开始监听键盘（可能被安全软件拦住了），按 Win+V 打开的还是 Windows 自带的剪贴板。"
                + "复制的内容照样会记下来，可以点上面的按钮，或者点右下角托盘里的小猪图标打开小猪剪贴板。";
            HeroText.Text = "点“现在打开看看”，选一条就能复制，再到要用的地方按 Ctrl+V 粘贴。";
        }
        else
        {
            if (!S.ClipboardEnabled) (text, bg, fg) = ("暂停记录中", "#EFE8EE", "#85738A");
            else if (!S.ClipboardWinV) (text, bg, fg) = ("正在记录 · Win+V 给了 Windows", "#DDF5E8", "#23845C");
            else (text, bg, fg) = ("正在记录", "#DDF5E8", "#23845C");
            HeroText.Text = !S.ClipboardWinV
                ? "现在按 Win+V 打开的是 Windows 自带的剪贴板。想换成小猪的，打开下面的“按 Win+V 打开小猪剪贴板”；也可以点右下角托盘里的小猪图标打开。"
                : !S.ClipboardEnabled
                    ? "现在新复制的内容先不记。已经记下的照样能按 Win+V 打开，点一下就粘贴。"
                    : "在任何软件里打字时按一下，光标旁边就会弹出小窗口。用 ↑ ↓ 选一条，按回车或者用鼠标点一下，就粘贴进去了。";
        }
        StatusText.Text = text;
        StatusPill.Background = new SolidColorBrush(Palette.Parse(bg));
        StatusText.Foreground = StatusDot.Fill = new SolidColorBrush(Palette.Parse(fg));
        WarnCard.Visibility = warnTitle == null ? Visibility.Collapsed : Visibility.Visible;
        WarnTitle.Text = warnTitle ?? "";
        WarnText.Text = warnText ?? "";
    }

    private void OnStoreChanged()
    {
        _statsTimer.Stop();
        _statsTimer.Start();
    }

    private async void UpdateStats()
    {
        if (_feature == null)
        {
            CountText.Text = "记了 0 条";
            FavText.Text = "收藏 0 条";
            UsageText.Text = "占用 0 KB";
            ClearButton.IsEnabled = false;
            return;
        }
        var items = _feature.Store.History.Items;
        int fav = items.Count(x => x.Favorite);
        CountText.Text = $"记了 {items.Count} 条";
        FavText.Text = $"收藏 {fav} 条";
        ClearButton.IsEnabled = items.Count > fav;
        RefreshRecent();
        long bytes = await _feature.Store.UsageBytesAsync();
        UsageText.Text = "占用 " + FormatSize(bytes);
    }

    private static string FormatSize(long b) =>
        b <= 0 ? "0 KB" : b < 1024 * 1024 ? $"{(b + 1023) / 1024} KB" : $"{b / 1024.0 / 1024.0:0.#} MB";

    // ---------- 按钮 ----------

    private void OnOpenNowClick(object sender, RoutedEventArgs e)
    {
        if (_feature == null)
        {
            _main.ShowToast("小猪剪贴板没有在运行，退出小猪再重新打开试试。");
            return;
        }
        // 从这里打开时没有要粘贴的地方：点一条只复制
        _feature.ShowPanel(IntPtr.Zero);
    }

    private void OnOpenFolderClick(object sender, RoutedEventArgs e)
    {
        var dir = _feature?.Store.Dir ?? ClipboardStore.DefaultDir;
        try
        {
            Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
        }
        catch (Exception ex) { _main.ShowToast("打不开这个文件夹：" + ex.Message); }
    }

    private async void OnClearClick(object sender, RoutedEventArgs e)
    {
        if (_feature == null) return;
        var items = _feature.Store.History.Items;
        int fav = items.Count(x => x.Favorite), n = items.Count - fav;
        if (n == 0)
        {
            _main.ShowToast("没有可以清空的记录。收藏的内容不会被清空。");
            return;
        }
        var points = new List<string> { $"删掉 {n} 条记录，连同里面的图片" };
        if (fav > 0) points.Add($"收藏的 {fav} 条会留着");
        points.Add("删掉以后找不回来");
        bool ok = await _main.ShowConfirmAsync("清空剪贴板记录？", "小猪记下的复制内容会被删掉。", points, "清空", danger: true, pose: "think");
        if (!ok) return;
        int removed = _feature.Store.Clear(keepFavorites: true);
        _main.ShowToast(fav > 0 ? $"清空好了，删掉了 {removed} 条，收藏的 {fav} 条还在。" : $"清空好了，删掉了 {removed} 条。");
        UpdateStats();
    }

    // ---------- 开关 ----------

    private void OnSwitchChanged(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        S.ClipboardEnabled = EnabledSwitch.IsChecked == true;
        S.ClipboardWinV = WinVSwitch.IsChecked == true;
        S.ClipboardSkipSensitive = SensitiveSwitch.IsChecked == true;
        SettingsService.Save();
        _feature?.ApplySettings();
        UpdateStatus();
        if (sender == EnabledSwitch)
            _main.ShowToast(S.ClipboardEnabled ? "好的，接着记复制的内容。" : "暂停了：新复制的内容先不记，已经记下的还能用。");
        else if (sender == WinVSwitch)
            _main.ShowToast(S.ClipboardWinV
                ? (_feature?.HotkeysFailed == true ? "设置好了，不过键盘监听没能启动，Win+V 暂时还是 Windows 自带的。" : "好了，现在按 Win+V 打开的是小猪剪贴板。")
                : "好了，Win+V 换回 Windows 自带的剪贴板了。");
        else if (sender == SensitiveSwitch)
            _main.ShowToast(S.ClipboardSkipSensitive ? "好的，像密码的内容以后直接不记。" : "好的，像密码的内容会记下来，但打码显示、加密保存。");
    }

    private async void OnLimitChecked(object sender, RoutedEventArgs e)
    {
        if (!_ready || sender is not RadioButton rb || rb.Tag is not string t) return;
        bool isMax = rb.GroupName == "ClipMax";
        int value = int.Parse(t);
        int max = isMax ? value : S.ClipboardMaxItems;
        int days = isMax ? S.ClipboardKeepDays : value;
        int n = _feature?.Store.History.CountPrunable(Math.Clamp(max, 20, 5000), Math.Max(0, days), DateTime.Now) ?? 0;
        if (n > 0)
        {
            string title = isMax ? $"改成最多记 {value} 条？" : $"改成留 {value} 天？";
            string intro = isMax
                ? $"现在记得比较多，最早的 {n} 条会被删掉。"
                : $"超过 {value} 天没用过的 {n} 条会被删掉。";
            bool ok = await _main.ShowConfirmAsync(title, intro, new[] { "收藏的不会删", "删掉以后找不回来" }, "删掉旧的", danger: true, pose: "think");
            if (!ok)
            {
                _ready = false;
                var prev = isMax ? _maxChecked : _keepChecked;
                if (prev != null) prev.IsChecked = true;
                else rb.IsChecked = false;
                _ready = true;
                return;
            }
        }
        if (isMax) { S.ClipboardMaxItems = value; _maxChecked = rb; }
        else { S.ClipboardKeepDays = value; _keepChecked = rb; }
        SettingsService.Save();
        _feature?.ApplySettings();   // 里面会按新设置清理
        if (n > 0) _main.ShowToast($"删掉了 {n} 条旧记录，收藏的都还在。");
        UpdateStats();
    }

    // ---------- 不记录的软件 ----------

    private void RefreshApps()
    {
        var known = KnownPaths();
        IgnoreList.ItemsSource = S.ClipboardIgnoreApps.Select(f => ChipFor(f, known)).ToList();
        IgnoreEmpty.Visibility = S.ClipboardIgnoreApps.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        RefreshRecent();
    }

    /// <summary>exe 文件名 → 完整路径（从记录里找，用来显示中文名字和图标）。</summary>
    private Dictionary<string, string> KnownPaths()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (_feature != null)
            foreach (var it in _feature.Store.History.Items)
                if (!string.IsNullOrEmpty(it.SourcePath)) map.TryAdd(Path.GetFileName(it.SourcePath), it.SourcePath);
        return map;
    }

    private static AppChip ChipFor(string file, Dictionary<string, string> known)
    {
        var f = Norm(file);
        known.TryGetValue(f, out var path);
        var name = path != null ? SourceApps.NameOf(path) : Path.GetFileNameWithoutExtension(f);
        return new AppChip(f, name, AppIcons.Get(path));
    }

    /// <summary>最近复制过的软件（还没在"不记"名单里的），最多 8 个。</summary>
    private void RefreshRecent()
    {
        var list = new List<AppChip>();
        if (_feature != null)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var own = Environment.ProcessPath;
            foreach (var it in _feature.Store.History.Items)
            {
                var p = it.SourcePath;
                if (string.IsNullOrEmpty(p)) continue;
                var file = Path.GetFileName(p);
                if (!seen.Add(file)) continue;
                if (own != null && string.Equals(p, own, StringComparison.OrdinalIgnoreCase)) continue;
                if (SourceApps.IsIgnored(p, S.ClipboardIgnoreApps)) continue;
                list.Add(new AppChip(file, string.IsNullOrEmpty(it.SourceName) ? SourceApps.NameOf(p) : it.SourceName, AppIcons.Get(p)));
                if (list.Count >= 8) break;
            }
        }
        RecentList.ItemsSource = list;
        RecentBlock.Visibility = list.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static string Norm(string x)
    {
        x = x.Trim();
        return x.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? x : x + ".exe";
    }

    private void OnRemoveIgnoreClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not AppChip chip) return;
        S.ClipboardIgnoreApps.RemoveAll(x => string.Equals(Norm(x), chip.File, StringComparison.OrdinalIgnoreCase));
        SettingsService.Save();
        RefreshApps();
        _main.ShowToast($"好的，以后在“{chip.Name}”里复制的内容也会记下来。");
    }

    private void OnAddRecentClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is AppChip chip) AddIgnore(chip.File, chip.Name);
    }

    private void OnPickAppClick(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选一个软件（桌面上的快捷方式也行）：在它里面复制的内容，小猪不记",
            Filter = "软件或快捷方式 (*.exe; *.lnk)|*.exe;*.lnk",
            DereferenceLinks = true,
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        };
        if (dlg.ShowDialog(Window.GetWindow(this)) != true) return;
        var path = dlg.FileName;
        if (!path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            _main.ShowToast("这个不是软件。请选软件本身（.exe），或者桌面上软件的快捷方式。");
            return;
        }
        AddIgnore(Path.GetFileName(path), SourceApps.NameOf(path));
    }

    private void AddIgnore(string file, string name)
    {
        file = Norm(file);
        if (!S.ClipboardIgnoreApps.Any(x => string.Equals(Norm(x), file, StringComparison.OrdinalIgnoreCase)))
            S.ClipboardIgnoreApps.Add(file);
        SettingsService.Save();
        RefreshApps();
        _main.ShowToast($"好的，以后在“{name}”里复制的内容小猪不记了。已经记下的还在，不想要可以在小窗口里删掉。");
    }
}
