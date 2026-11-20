using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PiggyHelper.Services;

namespace PiggyHelper.Controls;

/// <summary>AI 设置：选服务、领 Key、粘贴并测试。AI 问答页和设置页共用。</summary>
public partial class AiSetupPanel : UserControl
{
    private string _provider = SettingsService.Current.AiProvider;

    /// <summary>保存并测试成功后触发。</summary>
    public event Action? Saved;

    public AiSetupPanel()
    {
        InitializeComponent();
        ProviderBar.ItemsSource = AiService.Providers;
        CustomUrl.Text = SettingsService.Current.AiCustomUrl ?? "";
        CustomModel.Text = SettingsService.Current.AiCustomModel ?? "";
        if (SettingsService.GetApiKey() is { } key) KeyBox.Password = key;
        Loaded += (_, _) => SelectProvider(_provider);
    }

    private void SelectProvider(string id)
    {
        for (int i = 0; i < AiService.Providers.Count; i++)
        {
            if (ProviderBar.ItemContainerGenerator.ContainerFromIndex(i) is DependencyObject c && FindRadio(c) is RadioButton rb &&
                rb.Tag as string == id)
            {
                rb.IsChecked = true;
            }
        }
        UpdateProviderInfo();
    }

    private static RadioButton? FindRadio(DependencyObject parent)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is RadioButton rb) return rb;
            if (FindRadio(child) is { } f) return f;
        }
        return null;
    }

    private void OnProviderChecked(object sender, RoutedEventArgs e)
    {
        if ((sender as RadioButton)?.Tag is string id)
        {
            _provider = id;
            UpdateProviderInfo();
        }
    }

    private void UpdateProviderInfo()
    {
        var p = AiService.Providers.FirstOrDefault(x => x.Id == _provider) ?? AiService.Providers[0];
        ProviderIntro.Text = p.Intro;
        CustomPanel.Visibility = p.Id == "custom" ? Visibility.Visible : Visibility.Collapsed;
        KeyLinkButton.Visibility = string.IsNullOrEmpty(p.KeyUrl) ? Visibility.Collapsed : Visibility.Visible;
        KeyLinkButton.Content = $"去 {p.Name} 官网领取 Key →";
    }

    private void OnKeyLinkClick(object sender, RoutedEventArgs e)
    {
        var p = AiService.Providers.FirstOrDefault(x => x.Id == _provider);
        if (p == null || string.IsNullOrEmpty(p.KeyUrl)) return;
        try { Process.Start(new ProcessStartInfo(p.KeyUrl) { UseShellExecute = true }); } catch { }
    }

    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        var key = KeyBox.Password.Trim();
        if (key.Length < 8)
        {
            ShowStatus("Key 好像不完整，确认一下有没有复制全。", false);
            return;
        }
        var s = SettingsService.Current;
        s.AiProvider = _provider;
        s.AiCustomUrl = CustomUrl.Text.Trim();
        s.AiCustomModel = CustomModel.Text.Trim();
        SettingsService.SetApiKey(key); // 会顺便保存其他设置

        SaveButton.IsEnabled = false;
        ShowStatus("正在测试连接……", null);
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(40));
            await AiService.AskAsync(new[] { new ChatMessage("user", "你好，请只回复两个字：收到") }, _ => { }, cts.Token);
            ShowStatus("测试成功！AI 已经可以用啦 🎉", true);
            Saved?.Invoke();
        }
        catch (AiException ex)
        {
            ShowStatus(ex.Message, false);
        }
        catch (OperationCanceledException)
        {
            ShowStatus("测试超时了，可能是网络不好，Key 已经保存，可以稍后直接提问试试。", false);
        }
        catch (Exception ex)
        {
            ShowStatus("测试失败：" + ex.Message, false);
        }
        finally
        {
            SaveButton.IsEnabled = true;
        }
    }

    private void ShowStatus(string text, bool? ok)
    {
        StatusText.Text = text;
        StatusText.Foreground = ok switch
        {
            true => new SolidColorBrush(Palette.Parse("#2E9E70")),
            false => new SolidColorBrush(Palette.Parse("#E05A5A")),
            null => (Brush)FindResource("SubTextBrush"),
        };
    }
}
