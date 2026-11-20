using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using PiggyHelper.Services;

namespace PiggyHelper.Pages;

public partial class SettingsPage : UserControl
{
    private readonly MainWindow _main;
    private bool _ready;

    public SettingsPage(MainWindow main)
    {
        _main = main;
        InitializeComponent();
        var s = SettingsService.Current;
        PopupSwitch.IsChecked = s.ShortcutPopup;
        CtrlSwitch.IsChecked = s.PopupCtrl;
        AltSwitch.IsChecked = s.PopupAlt;
        WinSwitch.IsChecked = s.PopupWin;
        TraySwitch.IsChecked = s.CloseToTray;
        AutoStartSwitch.IsChecked = SettingsService.StartWithWindows;
        (s.HoldSeconds < 0.8 ? Hold06 : s.HoldSeconds > 1.2 ? Hold15 : Hold10).IsChecked = true;
        AboutTitle.Text = $"小猪电脑助手 v{App.VersionText}";
        AiPanel.Saved += () => _main.ShowToast("AI 设置好啦，去“AI 问答”里试试吧！");
        _ready = true;
    }

    private void OnChanged(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        var s = SettingsService.Current;
        s.ShortcutPopup = PopupSwitch.IsChecked == true;
        s.PopupCtrl = CtrlSwitch.IsChecked == true;
        s.PopupAlt = AltSwitch.IsChecked == true;
        s.PopupWin = WinSwitch.IsChecked == true;
        s.CloseToTray = TraySwitch.IsChecked == true;
        if (sender == AutoStartSwitch) SettingsService.StartWithWindows = AutoStartSwitch.IsChecked == true;
        SettingsService.Save();
    }

    private void OnHoldChecked(object sender, RoutedEventArgs e)
    {
        if (!_ready || (sender as RadioButton)?.Tag is not string t) return;
        SettingsService.Current.HoldSeconds = double.Parse(t, CultureInfo.InvariantCulture);
        SettingsService.Save();
    }

    private void OnClipboardClick(object sender, RoutedEventArgs e) => _main.Navigate("clipboard");

    private void OnUpdateClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("https://github.com/baipiaodashi/helppiggyusePC/releases/latest") { UseShellExecute = true });
        }
        catch { }
    }
}
