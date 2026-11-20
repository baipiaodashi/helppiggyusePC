using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using PiggyHelper.Services;
using PiggyHelper.Services.Clips;

namespace PiggyHelper.Windows;

/// <summary>
/// 小猪剪贴板小窗口（Win+V）。
/// 平时不抢焦点：光标留在原来打字的地方，方向键、回车、Esc 由键盘监听转给这里，点卡片或按回车就粘贴过去。
/// 点了搜索框才会真正"激活"窗口（这样才能用输入法打中文），粘贴前再把焦点还给原来的软件。
/// </summary>
public partial class ClipboardPanel : Window
{
    private const double ShadowMargin = 20, DefaultHeight = 620, MinHeightLogical = 360;

    private readonly ClipboardStore _store;
    private readonly ClipboardHotkeys _hotkeys;
    private readonly Dictionary<string, ClipCardVm> _vms = new();
    private readonly DispatcherTimer _searchTimer, _fgTimer, _toastTimer, _hideTimer;
    private readonly RadioButton[] _tabs;
    private ClipFilter _filter = ClipFilter.All;
    private List<ClipCardVm> _cards = new();
    private IntPtr _hwnd, _target, _home;
    private bool _editing, _detached, _refreshQueued;

    /// <summary>点了设置按钮：打开小猪主窗口的剪贴板页。</summary>
    public event Action? OpenSettingsRequested;

    /// <summary>粘贴完成（自检用）。</summary>
    public event Action<ClipItem>? Pasted;

    public bool Pinned => PinButton.IsChecked == true;
    public bool IsEditing => _editing;
    public IReadOnlyList<ClipCardVm> Cards => _cards;
    public int SelectedIndex => List.SelectedIndex;
    public IntPtr Target => _target;

    /// <summary>没有要粘贴的地方（从托盘菜单、剪贴板页的按钮打开）：点卡片只复制，用户自己去按 Ctrl+V。</summary>
    public bool CopyOnly => _target == IntPtr.Zero;

    /// <summary>用户换到别的软件时自动收起（自检时关掉，免得 CI 机器上前台窗口乱跳）。</summary>
    public bool FollowForeground { get; set; } = true;

    public ClipboardPanel(ClipboardStore store, ClipboardHotkeys hotkeys)
    {
        InitializeComponent();
        _store = store;
        _hotkeys = hotkeys;
        _tabs = new[] { TabAll, TabFav, TabText, TabImage, TabFiles };

        _searchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        _searchTimer.Tick += (_, _) => { _searchTimer.Stop(); Refresh(resetSelection: true); };
        _fgTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _fgTimer.Tick += (_, _) => WatchForeground();
        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.6) };
        _toastTimer.Tick += (_, _) => { _toastTimer.Stop(); Toast.Visibility = Visibility.Collapsed; Hint.Visibility = Visibility.Visible; };
        _hideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.6) };
        _hideTimer.Tick += (_, _) => { _hideTimer.Stop(); if (!Pinned) HidePanel(); };

        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            int ex = Native.GetWindowLong(_hwnd, Native.GWL_EXSTYLE);
            Native.SetWindowLong(_hwnd, Native.GWL_EXSTYLE, ex | Native.WS_EX_NOACTIVATE | Native.WS_EX_TOOLWINDOW);
        };
        Deactivated += (_, _) =>
        {
            // 在搜索框里打字时点到了别处
            if (!_editing) return;
            if (Pinned) { LeaveEditMode(); _detached = true; UpdateNavigating(); }
            else HidePanel();
        };
        PreviewKeyDown += OnPreviewKeyDown;
        _store.Changed += OnStoreChanged;
    }

    // ---------- 打开 / 收起 ----------

    /// <summary>程序启动后先在屏幕外悄悄显示一次，第一次按 Win+V 时就不会卡一下。</summary>
    public void Prewarm()
    {
        if (IsVisible) return;
        Left = -32000;
        Top = -32000;
        Root.Opacity = 0;
        Show();
        Refresh(resetSelection: true);
        UpdateLayout();
        Hide();
    }

    /// <summary>
    /// 打开小窗口。target 是按 Win+V 时最前面的窗口（粘贴到那里）；
    /// IntPtr.Zero 表示没有要粘贴的地方，点卡片只复制。
    /// </summary>
    public void ShowAt(IntPtr target)
    {
        _target = target;
        _home = target != IntPtr.Zero ? target : Native.GetForegroundWindow();
        _detached = false;
        _hideTimer.Stop();
        if (_editing) LeaveEditMode();
        _searchTimer.Stop();
        if (Search.Text.Length > 0)
        {
            Search.Text = "";
            _searchTimer.Stop();
        }
        foreach (var vm in _vms.Values) vm.Revealed = false;
        Refresh(resetSelection: true);

        if (!IsVisible)
        {
            Root.Opacity = 0;
            Show();
        }
        bool above = Place(target);
        Animate(above);
        UpdateNavigating();
        KeyboardHook.Shared.WatchMouse(true);
        _fgTimer.Start();
    }

    public void Toggle(IntPtr target)
    {
        if (IsVisible && Root.Opacity > 0.01) HidePanel();
        else ShowAt(target);
    }

    public void HidePanel()
    {
        _fgTimer.Stop();
        _hideTimer.Stop();
        _hotkeys.Navigating = false;
        KeyboardHook.Shared.WatchMouse(false);
        if (_editing) LeaveEditMode();
        if (!IsVisible) return;
        Root.BeginAnimation(OpacityProperty, null);
        Root.Opacity = 0;
        Hide();
    }

    private void Animate(bool fromAbove)
    {
        var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(130)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
        var slide = new DoubleAnimation(fromAbove ? -10 : 10, 0, TimeSpan.FromMilliseconds(190)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        Timeline.SetDesiredFrameRate(fade, 60);
        Timeline.SetDesiredFrameRate(slide, 60);
        Root.BeginAnimation(OpacityProperty, fade);
        Shift.BeginAnimation(TranslateTransform.YProperty, slide);
    }

    private void UpdateNavigating() => _hotkeys.Navigating = IsVisible && !_editing && !_detached;

    // ---------- 放在哪里：光标下面，看不到光标就放在鼠标旁边 ----------

    /// <summary>返回 true 表示放在了光标上方（下面放不下）。</summary>
    private bool Place(IntPtr target)
    {
        var (anchor, top, bottom) = Anchor(target);
        var mon = Native.MonitorFromPoint(anchor, Native.MONITOR_DEFAULTTONEAREST);
        var mi = new Native.MONITORINFO { cbSize = Marshal.SizeOf<Native.MONITORINFO>() };
        Native.GetMonitorInfo(mon, ref mi);
        var work = mi.rcWork;
        double scale = 1;
        try { if (Native.GetDpiForMonitor(mon, 0, out uint dx, out _) == 0 && dx > 0) scale = dx / 96.0; } catch { }

        // 屏幕矮（比如 1366×768 放大 125%）时把窗口变矮一点
        double workH = (work.Bottom - work.Top) / scale;
        Height = Math.Max(MinHeightLogical, Math.Min(DefaultHeight, workH - 8));
        int w = (int)Math.Round(Width * scale), h = (int)Math.Round(Height * scale);
        int m = (int)Math.Round(ShadowMargin * scale), gap = (int)Math.Round(6 * scale);

        int x = anchor.X - m - (int)Math.Round(10 * scale);
        int y = bottom + gap - m;
        bool above = false;
        if (y + h - m > work.Bottom)
        {
            int up = top - gap - h + m;
            if (up + m >= work.Top) { y = up; above = true; }
            else y = work.Bottom - h + m;
        }
        x = Math.Clamp(x, work.Left - m, Math.Max(work.Left - m, work.Right - w + m));
        y = Math.Clamp(y, work.Top - m, Math.Max(work.Top - m, work.Bottom - h + m));
        Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, x, y, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);

        // 换到了缩放比例不同的屏幕：系统调整完大小以后再摆一次
        if (Math.Abs(VisualTreeHelper.GetDpi(this).DpiScaleX - scale) > 0.01)
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
            {
                if (IsVisible) Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, x, y, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
            });
        return above;
    }

    /// <summary>文字光标的位置（屏幕物理像素）：返回光标左下角，以及光标的上沿和下沿。</summary>
    private static (Native.POINT Pt, int Top, int Bottom) Anchor(IntPtr target)
    {
        try
        {
            if (target != IntPtr.Zero)
            {
                uint tid = Native.GetWindowThreadProcessId(target, out _);
                var gti = new Native.GUITHREADINFO { cbSize = Marshal.SizeOf<Native.GUITHREADINFO>() };
                if (Native.GetGUIThreadInfo(tid, ref gti) && gti.hwndCaret != IntPtr.Zero)
                {
                    var r = gti.rcCaret;
                    if (r.Bottom > r.Top && r.Bottom - r.Top < 400)
                    {
                        // 没有适配高分屏的老软件，坐标是按 100% 算的，要换算成真实像素
                        double s = 1;
                        uint wdpi = Native.GetDpiForWindow(gti.hwndCaret);
                        var mon = Native.MonitorFromPoint(CursorPos(), Native.MONITOR_DEFAULTTONEAREST);
                        if (Native.GetWindowRect(gti.hwndCaret, out var wr))
                            mon = Native.MonitorFromPoint(new Native.POINT { X = wr.Left + 1, Y = wr.Top + 1 }, Native.MONITOR_DEFAULTTONEAREST);
                        if (wdpi > 0 && Native.GetDpiForMonitor(mon, 0, out uint mdpi, out _) == 0 && mdpi > 0) s = (double)mdpi / wdpi;
                        var tl = new Native.POINT { X = (int)Math.Round(r.Left * s), Y = (int)Math.Round(r.Top * s) };
                        var bl = new Native.POINT { X = (int)Math.Round(r.Left * s), Y = (int)Math.Round(r.Bottom * s) };
                        if (Native.ClientToScreen(gti.hwndCaret, ref tl) && Native.ClientToScreen(gti.hwndCaret, ref bl))
                            return (bl, tl.Y, bl.Y);
                    }
                }
            }
        }
        catch { }
        var p = CursorPos();
        return (p, p.Y - 4, p.Y + 18);
    }

    private static Native.POINT CursorPos()
    {
        Native.GetCursorPos(out var p);
        return p;
    }

    // ---------- 列表 ----------

    private void OnStoreChanged()
    {
        if (!IsVisible) return;   // 下次打开时会整个刷新
        if (_refreshQueued) return;
        _refreshQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            _refreshQueued = false;
            Refresh(resetSelection: false, keepScroll: true);
        });
    }

    private void Refresh(bool resetSelection, bool keepScroll = false)
    {
        string? keepId = resetSelection ? null : (List.SelectedItem as ClipCardVm)?.Id;
        var sv = keepScroll ? FindScroll(List) : null;
        double offset = sv?.VerticalOffset ?? 0;

        var items = _store.History.Query(_filter, Search.Text.Trim());
        var cards = new List<ClipCardVm>(items.Count);
        foreach (var it in items)
        {
            if (!_vms.TryGetValue(it.Id, out var vm) || vm.Item != it)
            {
                vm = new ClipCardVm(it, _store);
                _vms[it.Id] = vm;
            }
            else vm.Refresh();
            cards.Add(vm);
        }
        if (_vms.Count > _store.History.Count + 300)
        {
            var alive = _store.History.Items.Select(x => x.Id).ToHashSet();
            foreach (var id in _vms.Keys.Where(k => !alive.Contains(k)).ToList()) _vms.Remove(id);
        }

        _cards = cards;
        List.ItemsSource = cards;
        int idx = 0;
        if (keepId != null)
        {
            idx = cards.FindIndex(c => c.Id == keepId);
            if (idx < 0) idx = 0;
        }
        List.SelectedIndex = cards.Count > 0 ? idx : -1;
        if (keepScroll && sv != null)
        {
            List.UpdateLayout();
            sv.ScrollToVerticalOffset(offset);
        }
        else if (List.SelectedItem != null) List.ScrollIntoView(List.SelectedItem);
        UpdateEmpty(cards.Count);
    }

    private void UpdateEmpty(int count)
    {
        Empty.Visibility = count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (count > 0) return;
        var q = Search.Text.Trim();
        if (q.Length > 0)
        {
            EmptyPig.Pose = "search";
            EmptyTitle.Text = $"没找到「{(q.Length > 12 ? q[..12] + "…" : q)}」";
            EmptyText.Text = "换个词试试，或者点上面的分类看看。";
        }
        else if (_store.History.Count == 0)
        {
            EmptyPig.Pose = "clip";
            EmptyTitle.Text = "还没有复制过东西";
            EmptyText.Text = "选中文字或图片，按 Ctrl+C 复制，就会出现在这里。下次按 Win+V，点一下就能粘贴。";
        }
        else if (_filter == ClipFilter.Favorite)
        {
            EmptyPig.Pose = "wave";
            EmptyTitle.Text = "还没有收藏";
            EmptyText.Text = "鼠标移到卡片上，点右上角的 ☆ 就能收藏。收藏的内容一直留着，清空时也不删。";
        }
        else
        {
            EmptyPig.Pose = "sleep";
            EmptyTitle.Text = "这一类还没有";
            EmptyText.Text = _filter switch
            {
                ClipFilter.Image => "截图或者在网页上复制图片，就会出现在这里。",
                ClipFilter.Files => "在文件夹里选中文件按 Ctrl+C，就会出现在这里。",
                _ => "复制一段文字，就会出现在这里。",
            };
        }
    }

    private static ScrollViewer? FindScroll(DependencyObject d)
    {
        if (d is ScrollViewer sv) return sv;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++)
        {
            var r = FindScroll(VisualTreeHelper.GetChild(d, i));
            if (r != null) return r;
        }
        return null;
    }

    private void MoveSelection(int delta)
    {
        if (_cards.Count == 0) return;
        int i = List.SelectedIndex < 0 ? 0 : Math.Clamp(List.SelectedIndex + delta, 0, _cards.Count - 1);
        Select(i);
    }

    private void Select(int i)
    {
        if (_cards.Count == 0) return;
        List.SelectedIndex = Math.Clamp(i, 0, _cards.Count - 1);
        List.ScrollIntoView(List.SelectedItem);
    }

    private void CycleTab(int delta)
    {
        int i = Array.FindIndex(_tabs, t => t.IsChecked == true);
        _tabs[(i + delta + _tabs.Length) % _tabs.Length].IsChecked = true;
    }

    private void OnTabChecked(object sender, RoutedEventArgs e)
    {
        if (_tabs == null) return;   // 窗口还在初始化
        _filter = sender switch
        {
            _ when sender == TabFav => ClipFilter.Favorite,
            _ when sender == TabText => ClipFilter.Text,
            _ when sender == TabImage => ClipFilter.Image,
            _ when sender == TabFiles => ClipFilter.Files,
            _ => ClipFilter.All,
        };
        Refresh(resetSelection: true);
    }

    /// <summary>自检和截图用：直接切换分类。</summary>
    public void SetFilter(ClipFilter f)
    {
        var tab = f switch
        {
            ClipFilter.Favorite => TabFav,
            ClipFilter.Text => TabText,
            ClipFilter.Image => TabImage,
            ClipFilter.Files => TabFiles,
            _ => TabAll,
        };
        if (tab.IsChecked == true) Refresh(resetSelection: true);
        else tab.IsChecked = true;
    }

    /// <summary>截图用：直接填搜索词。</summary>
    public void SetSearch(string text)
    {
        Search.Text = text;
        _searchTimer.Stop();
        Refresh(resetSelection: true);
    }

    // ---------- 键盘 ----------

    /// <summary>光标还在原来软件里时，键盘监听转过来的方向键、回车、Esc。</summary>
    public void OnNavKey(int vk, bool shift)
    {
        if (!IsVisible) return;
        switch (vk)
        {
            case ClipboardHotkeys.VK_UP: MoveSelection(-1); break;
            case ClipboardHotkeys.VK_DOWN: MoveSelection(1); break;
            case ClipboardHotkeys.VK_PRIOR: MoveSelection(-4); break;
            case ClipboardHotkeys.VK_NEXT: MoveSelection(4); break;
            case ClipboardHotkeys.VK_HOME: Select(0); break;
            case ClipboardHotkeys.VK_END: Select(_cards.Count - 1); break;
            case ClipboardHotkeys.VK_LEFT: CycleTab(-1); break;
            case ClipboardHotkeys.VK_RIGHT: CycleTab(1); break;
            case ClipboardHotkeys.VK_RETURN: PasteSelected(shift); break;
            case ClipboardHotkeys.VK_ESCAPE: HidePanel(); break;
        }
    }

    /// <summary>小窗口开着时，用户在原来的软件里继续打字了。</summary>
    public void OnTypedElsewhere()
    {
        if (!IsVisible || _editing) return;
        if (Pinned) { _detached = true; UpdateNavigating(); }
        else HidePanel();
    }

    /// <summary>在搜索框里打字时（窗口被激活了）的按键。</summary>
    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_editing) return;
        bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        switch (e.Key)   // 输入法正在打字时 e.Key 是 ImeProcessed，不会走到这里
        {
            case Key.Down: MoveSelection(1); e.Handled = true; break;
            case Key.Up: MoveSelection(-1); e.Handled = true; break;
            case Key.PageDown: MoveSelection(4); e.Handled = true; break;
            case Key.PageUp: MoveSelection(-4); e.Handled = true; break;
            case Key.Enter: PasteSelected(shift); e.Handled = true; break;
            case Key.Escape:
                if (Search.Text.Length > 0) Search.Text = "";
                else HidePanel();
                e.Handled = true;
                break;
        }
    }

    // ---------- 鼠标 ----------

    /// <summary>键盘监听发现鼠标在别处按下了（屏幕物理像素）。</summary>
    public void OnGlobalMouseDown(int x, int y)
    {
        if (!IsVisible || _hwnd == IntPtr.Zero) return;
        if (Native.GetWindowRect(_hwnd, out var r))
        {
            int m = (int)Math.Round(ShadowMargin * VisualTreeHelper.GetDpi(this).DpiScaleX);
            if (x >= r.Left + m && x < r.Right - m && y >= r.Top + m && y < r.Bottom - m) return;
        }
        if (Pinned)
        {
            _detached = true;
            if (_editing) LeaveEditMode();
            UpdateNavigating();
            return;
        }
        HidePanel();
    }

    private void WatchForeground()
    {
        if (!IsVisible) { _fgTimer.Stop(); return; }
        if (!FollowForeground) return;
        var fg = Native.GetForegroundWindow();
        if (fg == IntPtr.Zero || fg == _hwnd || fg == _home) return;
        if (_home != IntPtr.Zero && Native.GetAncestor(fg, Native.GA_ROOT) == Native.GetAncestor(_home, Native.GA_ROOT)) return;
        // 只复制的时候，小猪自己的窗口（托盘菜单、主窗口）跳到前面不算换了软件
        if (CopyOnly && IsOwnWindow(fg)) return;
        if (Pinned)
        {
            // 固定着：跟着用户换到新的软件，粘贴到那里
            _target = fg;
            _home = fg;
            _detached = true;
            UpdateNavigating();
            return;
        }
        HidePanel();
    }

    private static bool IsOwnWindow(IntPtr hwnd)
    {
        Native.GetWindowThreadProcessId(hwnd, out uint pid);
        return pid == (uint)Environment.ProcessId;
    }

    private static ClipCardVm? VmOf(object sender) => (sender as FrameworkElement)?.DataContext as ClipCardVm;

    private void OnCardClick(object sender, MouseButtonEventArgs e)
    {
        if (VmOf(sender) is { } vm) Paste(vm, plain: false);
    }

    private void OnPlainClick(object sender, RoutedEventArgs e)
    {
        if (VmOf(sender) is { } vm) Paste(vm, plain: true);
    }

    private void OnFavoriteClick(object sender, RoutedEventArgs e)
    {
        if (VmOf(sender) is not { } vm) return;
        _store.SetFavorite(vm.Id, !vm.Favorite);
        vm.Refresh();
        ShowToast(vm.Favorite ? "收藏好了，清空记录时也会留着" : "取消收藏了");
    }

    private void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if (VmOf(sender) is { } vm) _store.Remove(vm.Id);
    }

    private void OnRevealClick(object sender, RoutedEventArgs e)
    {
        if (VmOf(sender) is { } vm) vm.Revealed = !vm.Revealed;
    }

    private void OnOpenClick(object sender, RoutedEventArgs e)
    {
        if (VmOf(sender) is not { } vm) return;
        var it = vm.Item;
        try
        {
            switch (it.Kind)
            {
                case ClipKind.Image:
                    var img = _store.ImagePath(it);
                    if (img != null && File.Exists(img)) Shell(img);
                    else { ShowToast("这张图片的文件找不到了"); return; }
                    break;
                case ClipKind.Files:
                    var first = it.Files?.FirstOrDefault(f => File.Exists(f) || Directory.Exists(f));
                    if (first == null) { ShowToast("这些文件已经被移走或删掉了"); return; }
                    Process.Start("explorer.exe", $"/select,\"{first}\"");
                    break;
                default:
                    var t = it.Text.Trim();
                    if (it.SubKind == "link") Shell(t.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? "https://" + t : t);
                    else if (it.SubKind == "email") Shell("mailto:" + t);
                    else if (File.Exists(t)) Process.Start("explorer.exe", $"/select,\"{t}\"");
                    else if (Directory.Exists(t)) Shell(t);
                    else { ShowToast("这个位置已经不存在了"); return; }
                    break;
            }
            if (!Pinned) HidePanel();
        }
        catch (Exception ex) { ShowToast("打不开：" + ex.Message); }
    }

    private static void Shell(string target) => Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });

    private void OnPinClick(object sender, RoutedEventArgs e)
    {
        PinButton.Content = Pinned ? "" : "";
        ShowToast(Pinned ? "固定住了：粘贴以后小窗口不会收起" : "取消固定");
    }

    private void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        SettingsButton.IsChecked = false;
        HidePanel();
        OpenSettingsRequested?.Invoke();
    }

    // ---------- 搜索（要激活窗口才能打字） ----------

    private void OnSearchMouseDown(object sender, MouseButtonEventArgs e) => EnterEditMode();

    private void OnSearchChanged(object sender, TextChangedEventArgs e)
    {
        _searchTimer.Stop();
        _searchTimer.Start();
    }

    private void EnterEditMode()
    {
        if (_editing || _hwnd == IntPtr.Zero) return;
        _editing = true;
        UpdateNavigating();
        int ex = Native.GetWindowLong(_hwnd, Native.GWL_EXSTYLE);
        Native.SetWindowLong(_hwnd, Native.GWL_EXSTYLE, ex & ~Native.WS_EX_NOACTIVATE);
        Activate();
        Search.Focus();
        Keyboard.Focus(Search);
    }

    private void LeaveEditMode()
    {
        if (!_editing) return;
        _editing = false;
        if (_hwnd != IntPtr.Zero)
        {
            int ex = Native.GetWindowLong(_hwnd, Native.GWL_EXSTYLE);
            Native.SetWindowLong(_hwnd, Native.GWL_EXSTYLE, ex | Native.WS_EX_NOACTIVATE);
        }
        Keyboard.ClearFocus();
        UpdateNavigating();
    }

    // ---------- 粘贴 ----------

    public void PasteSelected(bool plain)
    {
        if (List.SelectedItem is ClipCardVm vm) Paste(vm, plain);
    }

    public async void Paste(ClipCardVm vm, bool plain)
    {
        var item = vm.Item;
        var result = ClipboardWriter.Write(item, _store.ImagesDir, plain);
        if (result != ClipboardWriter.Result.Ok)
        {
            ShowToast(result == ClipboardWriter.Result.Missing
                ? (item.Kind == ClipKind.Image ? "这张图片的文件找不到了，粘贴不了" : "这些文件已经被移走或删掉了，粘贴不了")
                : "剪贴板正被别的软件占着，请再试一次");
            return;
        }
        _store.Touch(item.Id);

        if (CopyOnly || !Native.IsWindow(_target))
        {
            // 没有要粘贴的地方：复制好就行，稍等一下再收起，让用户看到提示
            _target = IntPtr.Zero;
            if (_editing) LeaveEditMode();
            ShowToast("复制好了！到要用的地方按 Ctrl+V 就能粘贴");
            _hideTimer.Stop();
            if (!Pinned) _hideTimer.Start();
            ClipLog.Write($"复制：{item.Kind}{(plain ? "（纯文字）" : "")}");
            Pasted?.Invoke(item);
            return;
        }

        var target = _target;
        bool wasEditing = _editing;
        if (Pinned) { if (wasEditing) LeaveEditMode(); }
        else HidePanel();

        if (wasEditing && target != IntPtr.Zero && Native.IsWindow(target))
        {
            // 刚才在搜索框打字，焦点在小猪这里：先把焦点还给原来的软件
            Native.SetForegroundWindow(target);
            for (int i = 0; i < 20 && Native.GetForegroundWindow() != target; i++) await Task.Delay(20);
        }
        await Task.Delay(50);
        ClipboardWriter.SendPasteKeys();
        ClipLog.Write($"粘贴：{item.Kind}{(plain ? "（纯文字）" : "")}");
        Pasted?.Invoke(item);
    }

    // ---------- 底部提示 ----------

    public void ShowToast(string text)
    {
        Toast.Text = text;
        Toast.Visibility = Visibility.Visible;
        Hint.Visibility = Visibility.Hidden;
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    /// <summary>截图用：把面板放在指定的屏幕位置并显示（不依赖光标）。</summary>
    public void ShowForScreenshot()
    {
        _target = IntPtr.Zero;
        _home = IntPtr.Zero;
        Refresh(resetSelection: true);
        Left = 40;
        Top = 40;
        if (!IsVisible) Show();
        Root.BeginAnimation(OpacityProperty, null);
        Root.Opacity = 1;
    }
}
