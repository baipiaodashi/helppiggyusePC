using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using PiggyHelper.Services;

namespace PiggyHelper.Windows;

/// <summary>按住修饰键时，在屏幕下方弹出的快捷键提示卡。不会抢走焦点，也不挡鼠标。</summary>
public partial class ShortcutOverlay : Window
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;

    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

    private static ShortcutOverlay? _shared;
    /// <summary>整个程序共用一个提示卡窗口。</summary>
    public static ShortcutOverlay Shared => _shared ??= new ShortcutOverlay();

    // Ctrl / Alt / Win 三张列表提前做好，弹出时只切换显示，不用临时排版
    private readonly Dictionary<string, ItemsControl> _lists = new();
    private string? _current;
    private bool _hiding;

    public ShortcutOverlay()
    {
        InitializeComponent();
        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            SetWindowLong(hwnd, GWL_EXSTYLE, GetWindowLong(hwnd, GWL_EXSTYLE) | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
        };
        foreach (var mod in new[] { "Ctrl", "Alt", "Win" })
        {
            var list = new ItemsControl
            {
                Focusable = false,
                ItemsPanel = List.ItemsPanel,
                ItemTemplate = List.ItemTemplate,
                ItemsSource = ShortcutCatalog.ForModifier(mod),
                Visibility = Visibility.Collapsed,
            };
            _lists[mod] = list;
            ListHost.Children.Add(list);
        }
    }

    /// <summary>程序启动后先在屏幕外悄悄显示一次，把窗口和三张列表都准备好，第一次按住时就不会卡一下。</summary>
    public void Prewarm()
    {
        if (IsVisible) return;
        Left = -32000; Top = -32000;
        Root.Opacity = 0;
        Show();
        foreach (var mod in _lists.Keys)
        {
            Select(mod);
            UpdateLayout();
        }
        Hide();
    }

    public void ShowFor(string modifier)
    {
        _hiding = false;
        bool wasVisible = IsVisible && Root.Opacity > 0.01;
        if (_current != modifier || !IsVisible)
        {
            Select(modifier);
            FitAndPlace();
        }
        if (!IsVisible) Show();
        if (wasVisible) { Root.BeginAnimation(OpacityProperty, Anim(Root.Opacity, 1, 120)); return; }

        Root.BeginAnimation(OpacityProperty, Anim(0, 1, 150, new QuadraticEase { EasingMode = EasingMode.EaseOut }));
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        Zoom.BeginAnimation(ScaleTransform.ScaleXProperty, Anim(0.94, 1, 220, ease));
        Zoom.BeginAnimation(ScaleTransform.ScaleYProperty, Anim(0.94, 1, 220, ease));
        Shift.BeginAnimation(TranslateTransform.YProperty, Anim(16, 0, 220, ease));
    }

    public void HideCard()
    {
        if (!IsVisible || _hiding) return;
        _hiding = true;
        var fade = Anim(Root.Opacity, 0, 110, new QuadraticEase { EasingMode = EasingMode.EaseIn });
        fade.Completed += (_, _) => { if (_hiding) { Hide(); _hiding = false; } };
        Root.BeginAnimation(OpacityProperty, fade);
        Shift.BeginAnimation(TranslateTransform.YProperty, Anim(0, 8, 110));
    }

    private void Select(string modifier)
    {
        if (!_lists.ContainsKey(modifier)) modifier = "Ctrl";
        _current = modifier;
        KeyName.Text = $" {modifier} ";
        foreach (var (k, v) in _lists) v.Visibility = k == modifier ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>先量好卡片大小，再把窗口放到屏幕下方正中间，避免弹出后窗口又跳一下。</summary>
    private void FitAndPlace()
    {
        Root.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var size = Root.DesiredSize;
        Width = Math.Ceiling(size.Width);
        Height = Math.Ceiling(size.Height);
        var area = SystemParameters.WorkArea;
        Left = area.Left + (area.Width - Width) / 2;
        Top = area.Bottom - Height - 4;
    }

    private static DoubleAnimation Anim(double from, double to, int ms, IEasingFunction? ease = null)
    {
        var a = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(ms)) { EasingFunction = ease };
        Timeline.SetDesiredFrameRate(a, 60);
        return a;
    }
}
