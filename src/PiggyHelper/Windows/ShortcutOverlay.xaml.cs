using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
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

    public ShortcutOverlay()
    {
        InitializeComponent();
        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            SetWindowLong(hwnd, GWL_EXSTYLE, GetWindowLong(hwnd, GWL_EXSTYLE) | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
        };
        SizeChanged += (_, _) => PlaceOnScreen();
    }

    public void ShowFor(string modifier)
    {
        KeyName.Text = $" {modifier} ";
        List.ItemsSource = ShortcutCatalog.ForModifier(modifier);
        if (!IsVisible) Show();
        PlaceOnScreen();
        Root.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)));
        Shift.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty,
            new DoubleAnimation(24, 0, TimeSpan.FromMilliseconds(260)) { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.5 } });
    }

    public void HideCard()
    {
        if (IsVisible) Hide();
    }

    private void PlaceOnScreen()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Left + (area.Width - ActualWidth) / 2;
        Top = area.Bottom - ActualHeight - 12;
    }
}
