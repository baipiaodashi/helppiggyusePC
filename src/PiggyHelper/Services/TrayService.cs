using System.Windows;
using Forms = System.Windows.Forms;

namespace PiggyHelper.Services;

/// <summary>右下角托盘图标：关掉窗口后小猪继续在后台帮你看快捷键。</summary>
public sealed class TrayService : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly Forms.ToolStripMenuItem _popupItem, _clipItem;

    public event Action? OpenRequested;
    /// <summary>托盘菜单里点了"打开小猪剪贴板"。</summary>
    public event Action? ClipboardRequested;
    public event Action? ExitRequested;

    public TrayService()
    {
        var stream = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/piggy.ico"))?.Stream;
        _icon = new Forms.NotifyIcon
        {
            Icon = stream != null ? new System.Drawing.Icon(stream) : System.Drawing.SystemIcons.Application,
            Text = "小猪电脑助手",
            Visible = true,
        };
        _icon.DoubleClick += (_, _) => OpenRequested?.Invoke();
        _icon.MouseClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) OpenRequested?.Invoke(); };

        var menu = new Forms.ContextMenuStrip { Font = new System.Drawing.Font("Microsoft YaHei UI", 10f) };
        menu.Items.Add("打开小猪", null, (_, _) => OpenRequested?.Invoke());
        _clipItem = new Forms.ToolStripMenuItem("打开小猪剪贴板", null, (_, _) => ClipboardRequested?.Invoke());
        menu.Items.Add(_clipItem);
        _popupItem = new Forms.ToolStripMenuItem("按住 Ctrl 弹出快捷键提示") { CheckOnClick = true };
        _popupItem.CheckedChanged += (_, _) =>
        {
            if (SettingsService.Current.ShortcutPopup == _popupItem.Checked) return;
            SettingsService.Current.ShortcutPopup = _popupItem.Checked;
            SettingsService.Save();
        };
        menu.Items.Add(_popupItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("退出小猪", null, (_, _) => ExitRequested?.Invoke());
        menu.Opening += (_, _) =>
        {
            _popupItem.Checked = SettingsService.Current.ShortcutPopup;
            _clipItem.Text = SettingsService.Current.ClipboardWinV ? "打开小猪剪贴板（Win+V）" : "打开小猪剪贴板";
        };
        _icon.ContextMenuStrip = menu;
    }

    public void ShowTip(string title, string text)
    {
        try { _icon.ShowBalloonTip(5000, title, text, Forms.ToolTipIcon.Info); } catch { }
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
