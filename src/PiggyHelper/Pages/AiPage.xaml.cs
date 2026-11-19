using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using PiggyHelper.Controls;
using PiggyHelper.Services;

namespace PiggyHelper.Pages;

public partial class AiPage : UserControl
{
    private readonly MainWindow _main;
    private readonly List<ChatMessage> _history = new();
    private CancellationTokenSource? _cts;
    private DateTime _lastRender;

    private static readonly string[] Suggestions =
    {
        "电脑突然没声音了怎么办？",
        "怎么把手机里的照片传到电脑上？",
        "C 盘红了，空间不够怎么办？",
        "电脑越来越卡，有什么办法？",
        "怎么截一张长长的网页截图？",
        "下载软件时怎么避免装上一堆广告软件？",
    };

    public AiPage(MainWindow main, string? question = null)
    {
        _main = main;
        InitializeComponent();
        SetupPanel.Saved += () => { UpdateMode(); _main.ShowToast("设置好啦！现在可以向小猪提问了。"); };
        UpdateMode();
        AddWelcome();
        Loaded += (_, _) =>
        {
            if (App.AutoAction == "demo") AddDemo();
            else if (!string.IsNullOrWhiteSpace(question))
            {
                if (AiService.IsConfigured) _ = SendAsync(question);
                else Input.Text = question; // 设置好 Key 之后可以直接发送
            }
            Input.Focus();
        };
        Unloaded += (_, _) => _cts?.Cancel();
    }

    private void UpdateMode()
    {
        bool ready = AiService.IsConfigured || App.AutoAction == "demo";
        if (App.AutoAction == "setup") ready = false;
        SetupView.Visibility = ready ? Visibility.Collapsed : Visibility.Visible;
        ChatScroll.Visibility = InputArea.Visibility = ready ? Visibility.Visible : Visibility.Collapsed;
        NewChatButton.Visibility = ready ? Visibility.Visible : Visibility.Collapsed;
        var p = AiService.CurrentProvider;
        ProviderText.Text = ready ? $"正在使用：{p.Name}" : "还没设置";
    }

    private void OnSettingsClick(object sender, RoutedEventArgs e) => _main.Navigate("settings");

    private void OnNewChatClick(object sender, RoutedEventArgs e)
    {
        _cts?.Cancel();
        _history.Clear();
        ChatPanel.Children.Clear();
        AddWelcome();
    }

    // ---------- 输入 ----------
    private void OnInputKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) == 0)
        {
            e.Handled = true;
            OnSendClick(sender, e);
        }
    }

    private void OnSendClick(object sender, RoutedEventArgs e)
    {
        if (_cts != null) { _cts.Cancel(); return; } // 正在回答时，按钮变成"停止"
        var q = Input.Text.Trim();
        if (q.Length == 0) return;
        Input.Text = "";
        _ = SendAsync(q);
    }

    private async Task SendAsync(string question)
    {
        AddUser(question);
        _history.Add(new ChatMessage("user", question));
        if (_history.Count > 16) _history.RemoveRange(0, _history.Count - 16);

        var (content, actions, footer) = AddAssistant();
        MiniMarkdown.Render(content, "小猪正在思考……", (Brush)FindResource("SubTextBrush"), Accent);

        _cts = new CancellationTokenSource();
        SendButton.Content = "停止";
        string answer = "";
        try
        {
            answer = await AiService.AskAsync(_history, partial =>
            {
                Dispatcher.BeginInvoke(() =>
                {
                    if ((DateTime.Now - _lastRender).TotalMilliseconds < 60) return;
                    _lastRender = DateTime.Now;
                    MiniMarkdown.Render(content, AiService.ExtractActions(partial).Text, TextColor, Accent);
                    ChatScroll.ScrollToEnd();
                });
            }, _cts.Token);
            Finish(content, actions, footer, answer);
            _history.Add(new ChatMessage("assistant", answer));
        }
        catch (OperationCanceledException)
        {
            var partial = AiService.ExtractActions(answer).Text;
            MiniMarkdown.Render(content, partial.Length > 0 ? partial : "（已停止）", TextColor, Accent);
        }
        catch (AiException ex)
        {
            ShowError(content, ex.Message);
        }
        catch (Exception ex)
        {
            ShowError(content, "出了点小问题：" + ex.Message);
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            SendButton.Content = "发送";
            ChatScroll.ScrollToEnd();
        }
    }

    private void Finish(StackPanel content, WrapPanel actions, StackPanel footer, string answer)
    {
        var (text, names) = AiService.ExtractActions(answer);
        MiniMarkdown.Render(content, text, TextColor, Accent);
        foreach (var name in names) AddAction(actions, name);
        footer.Visibility = Visibility.Visible;
        footer.Tag = text;
    }

    private void ShowError(StackPanel content, string message)
    {
        content.Children.Clear();
        content.Children.Add(new TextBlock
        {
            Text = "😥 " + message,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 14,
            LineHeight = 22,
            Foreground = new SolidColorBrush(Palette.Parse("#D9534F")),
        });
        var fix = new Button { Style = (Style)FindResource("LinkButton"), Content = "去检查 AI 设置 →", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(-10, 6, 0, 0) };
        fix.Click += (_, _) => _main.Navigate("settings");
        content.Children.Add(fix);
    }

    /// <summary>AI 建议的功能，变成一个可以直接点的按钮。</summary>
    private void AddAction(WrapPanel panel, string name)
    {
        string? page = name switch
        {
            "一键清理" => "cleanup",
            "快捷键指南" or "快捷键" => "shortcuts",
            _ => null,
        };
        var item = NavCatalog.Items.FirstOrDefault(i => i.Title == name);
        if (page == null && item == null) return;
        var btn = new Button
        {
            Style = (Style)FindResource("GhostButton"),
            Content = "👉 打开：" + name,
            Height = 42,
            FontSize = 14,
            Margin = new Thickness(0, 0, 10, 0),
        };
        btn.Click += (_, _) =>
        {
            if (page != null) _main.Navigate(page);
            else if (item != null) NavigatorPage.OpenItem(_main, item);
        };
        panel.Children.Add(btn);
        panel.Visibility = Visibility.Visible;
    }

    // ---------- 气泡 ----------
    private Brush TextColor => (Brush)FindResource("TextBrush");
    private Brush Accent => (Brush)FindResource("PinkDeepBrush");

    private void AddUser(string text)
    {
        var bubble = new Border
        {
            Background = (Brush)FindResource("PinkGradient"),
            CornerRadius = new CornerRadius(20, 20, 6, 20),
            Padding = new Thickness(16, 11, 16, 11),
            HorizontalAlignment = HorizontalAlignment.Right,
            MaxWidth = 560,
            Margin = new Thickness(80, 6, 0, 10),
            Child = new TextBlock { Text = text, Foreground = Brushes.White, FontSize = 14.5, LineHeight = 22, TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold },
        };
        ChatPanel.Children.Add(bubble);
        ChatScroll.ScrollToEnd();
    }

    private (StackPanel Content, WrapPanel Actions, StackPanel Footer) AddAssistant()
    {
        var content = new StackPanel();
        var actions = new WrapPanel { Margin = new Thickness(0, 8, 0, 0), Visibility = Visibility.Collapsed };
        var footer = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(-10, 4, 0, -6), Visibility = Visibility.Collapsed };
        var copy = new Button { Style = (Style)FindResource("LinkButton"), Content = "复制回答", FontSize = 12 };
        copy.Click += (_, _) =>
        {
            try { Clipboard.SetText(footer.Tag as string ?? ""); _main.ShowToast("已复制，可以按 Ctrl+V 粘贴到别的地方。"); } catch { }
        };
        footer.Children.Add(copy);

        var inner = new StackPanel();
        inner.Children.Add(content);
        inner.Children.Add(actions);
        inner.Children.Add(footer);

        var card = new Grid { Margin = new Thickness(0, 6, 60, 10), HorizontalAlignment = HorizontalAlignment.Left, MaxWidth = 720 };
        card.Children.Add(new Border { CornerRadius = new CornerRadius(6, 22, 22, 22), Background = Brushes.White, Effect = (System.Windows.Media.Effects.Effect)FindResource("SmallShadow") });
        card.Children.Add(new Border { CornerRadius = new CornerRadius(6, 22, 22, 22), Padding = new Thickness(18, 14, 18, 12), Child = inner });

        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var avatar = new PiggyMascot { Width = 44, Height = 44, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 4, 10, 0) };
        row.Children.Add(avatar);
        Grid.SetColumn(card, 1);
        row.Children.Add(card);
        ChatPanel.Children.Add(row);
        ChatScroll.ScrollToEnd();
        return (content, actions, footer);
    }

    private void AddWelcome()
    {
        var (content, actions, _) = AddAssistant();
        MiniMarkdown.Render(content, "你好呀，我是小猪！🐷\n电脑上遇到任何不会的，都可以直接问我，用大白话说就行。下面是大家常问的问题，点一下就能问：", TextColor, Accent);
        actions.Visibility = Visibility.Visible;
        actions.Margin = new Thickness(0, 10, 0, 4);
        foreach (var s in Suggestions)
        {
            var chip = new Button
            {
                Style = (Style)FindResource("GhostButton"),
                Content = s,
                Height = 40,
                FontSize = 13,
                Margin = new Thickness(0, 0, 8, 8),
            };
            chip.Click += (_, _) =>
            {
                if (_cts != null) return;
                if (!AiService.IsConfigured && App.AutoAction != "demo") { UpdateMode(); return; }
                _ = SendAsync(s);
            };
            actions.Children.Add(chip);
        }
    }

    /// <summary>自动截图测试用的示例对话（不联网）。</summary>
    private void AddDemo()
    {
        AddUser("电脑突然没声音了怎么办？");
        var (content, actions, footer) = AddAssistant();
        Finish(content, actions, footer,
            "别着急，按下面的顺序检查一下，大多数情况第 1、2 步就能解决：\n\n" +
            "1. 看看屏幕右下角的**喇叭图标**，如果上面有个 ✕，点一下取消静音。\n" +
            "2. 点开喇叭，把**音量滑块**往右拖大一点。\n" +
            "3. 如果插着耳机，拔下来试试；或者检查耳机线有没有插紧。\n" +
            "4. 还是不行的话，在声音设置里看看**输出设备**是不是选对了。\n\n" +
            "> 小提示：重启一下电脑，也能解决很多奇怪的问题哦。\n\n" +
            "[[打开:调节音量]]");
    }
}
