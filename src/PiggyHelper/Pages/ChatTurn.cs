using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using PiggyHelper.Controls;
using PiggyHelper.Services;

namespace PiggyHelper.Pages;

/// <summary>
/// 小猪的一次回答：文字、"小猪看了看……"的小标签、需要你点头的卡片，按出现的顺序排在一个气泡里。
/// </summary>
public sealed class ChatTurn
{
    private readonly FrameworkElement _res;
    private StackPanel? _segment;
    private readonly StringBuilder _segmentText = new();
    private DateTime _lastRender;

    public StackPanel Flow { get; } = new();
    public WrapPanel Actions { get; } = new() { Margin = new Thickness(0, 8, 0, 0), Visibility = Visibility.Collapsed };
    public StackPanel Footer { get; } = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(-10, 4, 0, -6), Visibility = Visibility.Collapsed };
    public FrameworkElement Root { get; }

    /// <summary>这次回答里 AI 说过的全部文字。</summary>
    public StringBuilder AllText { get; } = new();

    public ChatTurn(FrameworkElement resources)
    {
        _res = resources;
        var inner = new StackPanel();
        inner.Children.Add(Flow);
        inner.Children.Add(Actions);
        inner.Children.Add(Footer);

        var card = new Grid { Margin = new Thickness(0, 6, 60, 10), HorizontalAlignment = HorizontalAlignment.Left, MaxWidth = 760 };
        card.Children.Add(new Border { CornerRadius = new CornerRadius(6, 22, 22, 22), Background = Brushes.White, Effect = (System.Windows.Media.Effects.Effect)Res("SmallShadow") });
        card.Children.Add(new Border { CornerRadius = new CornerRadius(6, 22, 22, 22), Padding = new Thickness(18, 14, 18, 12), Child = inner });

        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.Children.Add(new PiggyMascot { Pose = "face", Animated = false, Width = 44, Height = 44, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 4, 10, 0) });
        Grid.SetColumn(card, 1);
        row.Children.Add(card);
        Root = row;
    }

    private object Res(string key) => _res.FindResource(key);
    public Brush TextBrush => (Brush)Res("TextBrush");
    public Brush SubBrush => (Brush)Res("SubTextBrush");
    public Brush Accent => (Brush)Res("PinkDeepBrush");

    // ---------- 文字 ----------

    /// <summary>"小猪正在思考……"这样的灰色占位文字（有新内容时会被替换掉）。</summary>
    public void Placeholder(string text)
    {
        EnsureSegment();
        MiniMarkdown.Render(_segment!, text, SubBrush, Accent);
    }

    /// <summary>流式追加文字（AI 一边想一边写）。</summary>
    public void Append(string delta)
    {
        if (string.IsNullOrEmpty(delta)) return;
        EnsureSegment();
        _segmentText.Append(delta);
        AllText.Append(delta);
        if ((DateTime.Now - _lastRender).TotalMilliseconds < 60) return;
        RenderSegment();
    }

    /// <summary>把这一段文字设成 full（用于"整段重画"的流式回调）。</summary>
    public void SetSegment(string full)
    {
        EnsureSegment();
        var add = full.Length >= _segmentText.Length ? full[_segmentText.Length..] : "";
        AllText.Append(add);
        _segmentText.Clear().Append(full);
        if ((DateTime.Now - _lastRender).TotalMilliseconds < 60) return;
        RenderSegment();
    }

    public string SegmentText => _segmentText.ToString();

    public void RenderSegment(string? overrideText = null)
    {
        if (_segment == null) return;
        _lastRender = DateTime.Now;
        var text = overrideText ?? AiService.ExtractActions(_segmentText.ToString()).Text;
        if (text.Trim().Length == 0) { _segment.Children.Clear(); return; }
        MiniMarkdown.Render(_segment, text, TextBrush, Accent);
    }

    /// <summary>结束当前这段文字（后面要插卡片了）。</summary>
    public void EndSegment()
    {
        if (_segment == null) return;
        RenderSegment();
        if (_segment.Children.Count == 0) Flow.Children.Remove(_segment);
        if (AllText.Length > 0 && !AllText.ToString().EndsWith("\n")) AllText.Append('\n');
        _segment = null;
        _segmentText.Clear();
    }

    private void EnsureSegment()
    {
        if (_segment != null) return;
        _segment = new StackPanel { Margin = new Thickness(0, Flow.Children.Count == 0 ? 0 : 6, 0, 2) };
        Flow.Children.Add(_segment);
    }

    public void Error(string message, Action? fix = null, string fixText = "去检查 AI 设置 →")
    {
        EndSegment();
        var panel = new StackPanel { Margin = new Thickness(0, Flow.Children.Count == 0 ? 0 : 8, 0, 0) };
        panel.Children.Add(new TextBlock
        {
            Text = "😥 " + message,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 14,
            LineHeight = 22,
            Foreground = new SolidColorBrush(Palette.Parse("#D9534F")),
        });
        if (fix != null)
        {
            var btn = new Button { Style = (Style)Res("LinkButton"), Content = fixText, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(-10, 6, 0, 0) };
            btn.Click += (_, _) => fix();
            panel.Children.Add(btn);
        }
        Flow.Children.Add(panel);
    }

    /// <summary>一条浅色的提示（比如"这个模型不支持动手，先用普通聊天回答"）。</summary>
    public void Notice(string text, string color = "#7451DD", string bg = "#F0E9FF")
    {
        EndSegment();
        Flow.Children.Add(new Border
        {
            Background = new SolidColorBrush(Palette.Parse(bg)),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(12, 7, 12, 7),
            Margin = new Thickness(0, Flow.Children.Count == 0 ? 0 : 8, 0, 6),
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = new TextBlock { Text = text, FontSize = 12.5, LineHeight = 19, TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Palette.Parse(color)) },
        });
    }

    // ---------- "小猪看了看……"小标签 ----------

    public sealed class Chip
    {
        public required TextBlock Label { get; init; }
        public required TextBlock Icon { get; init; }
        public required ToggleButton Toggle { get; init; }
        public required TextBox Details { get; init; }

        public void Done(string text, string details, bool ok = true)
        {
            Label.Text = text;
            Icon.Text = ok ? "" : "";
            Icon.Foreground = new SolidColorBrush(Palette.Parse(ok ? "#3FA66B" : "#D9534F"));
            Details.Text = details;
            Toggle.IsEnabled = details.Length > 0;
        }
    }

    public Chip AddChip(string runningText)
    {
        EndSegment();
        var icon = new TextBlock { Text = "", FontFamily = (FontFamily)Res("IconFont"), FontSize = 12, Foreground = Accent, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 7, 0) };
        var label = new TextBlock { Text = runningText, FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = SubBrush, VerticalAlignment = VerticalAlignment.Center };
        var arrow = new TextBlock { Text = "", FontFamily = (FontFamily)Res("IconFont"), FontSize = 9, Foreground = SubBrush, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 1, 0, 0) };
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(icon);
        content.Children.Add(label);
        content.Children.Add(arrow);
        var toggle = new ToggleButton
        {
            Content = content,
            IsEnabled = false,
            Cursor = System.Windows.Input.Cursors.Hand,
            Focusable = false,
            HorizontalAlignment = HorizontalAlignment.Left,
            Template = ChipTemplate(),
            ToolTip = "点一下看看小猪查到了什么",
        };
        var details = MonoBox("");
        details.Visibility = Visibility.Collapsed;
        details.Margin = new Thickness(0, 6, 0, 2);
        toggle.Checked += (_, _) => details.Visibility = Visibility.Visible;
        toggle.Unchecked += (_, _) => details.Visibility = Visibility.Collapsed;
        var box = new StackPanel { Margin = new Thickness(0, Flow.Children.Count == 0 ? 0 : 6, 0, 4) };
        box.Children.Add(toggle);
        box.Children.Add(details);
        Flow.Children.Add(box);
        return new Chip { Label = label, Icon = icon, Toggle = toggle, Details = details };
    }

    private static ControlTemplate ChipTemplate()
    {
        var t = new ControlTemplate(typeof(ToggleButton));
        var bd = new FrameworkElementFactory(typeof(Border));
        bd.SetValue(Border.BackgroundProperty, new SolidColorBrush(Palette.Parse("#F7F1F5")));
        bd.SetValue(Border.CornerRadiusProperty, new CornerRadius(12));
        bd.SetValue(Border.PaddingProperty, new Thickness(11, 6, 11, 6));
        var cp = new FrameworkElementFactory(typeof(ContentPresenter));
        bd.AppendChild(cp);
        t.VisualTree = bd;
        return t;
    }

    public TextBox MonoBox(string text) => new()
    {
        Text = text,
        IsReadOnly = true,
        TextWrapping = TextWrapping.Wrap,
        FontFamily = new FontFamily("Cascadia Mono, Consolas, Microsoft YaHei UI"),
        FontSize = 12.5,
        Background = new SolidColorBrush(Palette.Parse("#FBF7FA")),
        Foreground = TextBrush,
        BorderBrush = (Brush)Res("LineBrush"),
        BorderThickness = new Thickness(1.5),
        Padding = new Thickness(10, 8, 10, 8),
        MaxHeight = 220,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
    };

    // ---------- 需要点头的卡片 ----------

    public ActionCardView AddCard(string icon, string title, string detail, GuardResult risk, string raw, string rawLabel)
    {
        EndSegment();
        var card = new ActionCardView(this, icon, title, detail, risk, raw, rawLabel);
        card.Root.Margin = new Thickness(0, Flow.Children.Count == 0 ? 2 : 8, 0, 6);
        Flow.Children.Add(card.Root);
        return card;
    }

    public Style ButtonStyle(string key) => (Style)Res(key);
    public FontFamily IconFont => (FontFamily)Res("IconFont");
    public Brush LineBrush => (Brush)Res("LineBrush");
}

/// <summary>"这一步要做什么"的卡片：大白话说明、风险标签、折叠的原始命令、同意/不同意。</summary>
public sealed class ActionCardView
{
    private readonly ChatTurn _turn;
    private readonly StackPanel _body = new();
    private readonly StackPanel _buttons = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
    private readonly TextBlock _status = new() { FontSize = 13, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0), Visibility = Visibility.Collapsed };
    private readonly Border _frame;
    private StackPanel? _output;
    private TaskCompletionSource<bool>? _decision;

    public FrameworkElement Root => _frame;
    public GuardResult Risk { get; }

    public ActionCardView(ChatTurn turn, string icon, string title, string detail, GuardResult risk, string raw, string rawLabel)
    {
        _turn = turn;
        Risk = risk;
        var (bg, border, tagBg, tagFg, tagIcon, tagText) = risk.Risk switch
        {
            CommandRisk.Block => ("#FFF3F2", "#F5B5B0", "#FFE1DE", "#C9302C", "", "小猪拦下了"),
            CommandRisk.Warn => ("#FFFBF2", "#F7D79B", "#FFF0CC", "#A36200", "", "会改动电脑"),
            _ => ("#F6FBF7", "#BFE3CB", "#E1F4E7", "#2E8550", "", "比较安全"),
        };
        _frame = new Border
        {
            Background = new SolidColorBrush(Palette.Parse(bg)),
            BorderBrush = new SolidColorBrush(Palette.Parse(border)),
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(18),
            Padding = new Thickness(16, 13, 16, 14),
            MinWidth = 360,
            Child = _body,
        };

        // 第一行：图标 + 标题 + 风险标签
        var head = new DockPanel();
        var iconBox = new Border
        {
            Width = 34, Height = 34, CornerRadius = new CornerRadius(11), Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Top,
            Background = new SolidColorBrush(Palette.Parse(tagBg)),
            Child = new TextBlock { Text = icon, FontFamily = turn.IconFont, FontSize = 16, Foreground = new SolidColorBrush(Palette.Parse(tagFg)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        DockPanel.SetDock(iconBox, Dock.Left);
        head.Children.Add(iconBox);
        var tag = new Border
        {
            Background = new SolidColorBrush(Palette.Parse(tagBg)), CornerRadius = new CornerRadius(9), Padding = new Thickness(8, 3, 9, 3),
            VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(10, 2, 0, 0),
        };
        var tagRow = new StackPanel { Orientation = Orientation.Horizontal };
        tagRow.Children.Add(new TextBlock { Text = tagIcon, FontFamily = turn.IconFont, FontSize = 11, Foreground = new SolidColorBrush(Palette.Parse(tagFg)), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 5, 0) });
        tagRow.Children.Add(new TextBlock { Text = tagText, FontSize = 12, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Palette.Parse(tagFg)), VerticalAlignment = VerticalAlignment.Center });
        tag.Child = tagRow;
        DockPanel.SetDock(tag, Dock.Right);
        head.Children.Add(tag);
        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        titles.Children.Add(new TextBlock { Text = title, FontSize = 15, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap, Foreground = turn.TextBrush });
        if (!string.IsNullOrWhiteSpace(detail))
            titles.Children.Add(new TextBlock { Text = detail, FontSize = 13.5, LineHeight = 20, TextWrapping = TextWrapping.Wrap, Foreground = turn.TextBrush, Margin = new Thickness(0, 3, 0, 0) });
        head.Children.Add(titles);
        _body.Children.Add(head);

        // 风险说明
        _body.Children.Add(new TextBlock
        {
            Text = risk.Reason,
            FontSize = 12.5,
            LineHeight = 19,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Palette.Parse(tagFg)),
            Margin = new Thickness(46, 8, 0, 0),
        });

        // 折叠的原始内容
        if (!string.IsNullOrWhiteSpace(raw)) _body.Children.Add(Collapsible(rawLabel, raw, new Thickness(36, 4, 0, 0)));

        _body.Children.Add(_status);
        _body.Children.Add(_buttons);
        _buttons.Margin = new Thickness(46, 12, 0, 0);
        _status.Margin = new Thickness(46, 10, 0, 0);
    }

    private FrameworkElement Collapsible(string label, string text, Thickness margin)
    {
        var panel = new StackPanel { Margin = margin };
        var arrow = new TextBlock { Text = "", FontFamily = _turn.IconFont, FontSize = 10, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 1, 6, 0) };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(arrow);
        row.Children.Add(new TextBlock { Text = label, FontSize = 12.5 });
        var btn = new Button { Style = _turn.ButtonStyle("LinkButton"), Content = row, HorizontalAlignment = HorizontalAlignment.Left, Foreground = _turn.SubBrush };
        var box = _turn.MonoBox(text);
        box.Visibility = Visibility.Collapsed;
        box.Margin = new Thickness(10, 2, 0, 4);
        btn.Click += (_, _) =>
        {
            bool show = box.Visibility != Visibility.Visible;
            box.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            arrow.Text = show ? "" : "";
        };
        panel.Children.Add(btn);
        panel.Children.Add(box);
        return panel;
    }

    /// <summary>显示"同意 / 不同意"按钮，等用户选。</summary>
    public Task<bool> AskAsync(string allowText = "同意", string denyText = "不同意")
    {
        _decision = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _buttons.Children.Clear();
        var ok = new Button { Style = _turn.ButtonStyle("PillButton"), Content = allowText, Height = 42, FontSize = 14.5, MinWidth = 104, Padding = new Thickness(18, 0, 18, 0), Margin = new Thickness(0, 0, 10, 0) };
        if (Risk.Risk == CommandRisk.Warn)
        {
            ok.Background = new SolidColorBrush(Palette.Parse("#F2A33A"));
            ok.BorderBrush = new SolidColorBrush(Palette.Parse("#C77F1C"));
        }
        var no = new Button { Style = _turn.ButtonStyle("GhostButton"), Content = denyText, Height = 40, FontSize = 14.5, MinWidth = 96, Padding = new Thickness(16, 0, 16, 0) };
        ok.Click += (_, _) => Decide(true);
        no.Click += (_, _) => Decide(false);
        _buttons.Children.Add(ok);
        _buttons.Children.Add(no);
        _buttons.Visibility = Visibility.Visible;
        return _decision.Task;
    }

    public bool IsWaiting => _decision is { Task.IsCompleted: false };

    /// <summary>做出决定（按钮、停止、或小猪自动拒绝）。</summary>
    public void Decide(bool allow, string? why = null)
    {
        _buttons.Children.Clear();
        _buttons.Visibility = Visibility.Collapsed;
        if (allow) SetStatus(why ?? "你同意了，正在做……", "#2E8550", "");
        else SetStatus(why ?? "你没有同意，这一步不会做。", "#8C7A84", "");
        _decision?.TrySetResult(allow);
    }

    public void SetStatus(string text, string color, string? icon = null)
    {
        _status.Inlines.Clear();
        if (icon != null) _status.Inlines.Add(new System.Windows.Documents.Run(icon + "  ") { FontFamily = _turn.IconFont, FontSize = 11.5 });
        _status.Inlines.Add(new System.Windows.Documents.Run(text));
        _status.Foreground = new SolidColorBrush(Palette.Parse(color));
        _status.Visibility = Visibility.Visible;
    }

    /// <summary>做完以后显示结果（折叠起来）。</summary>
    public void ShowOutput(string output, bool failed)
    {
        if (_output != null) _body.Children.Remove(_output);
        if (string.IsNullOrWhiteSpace(output)) return;
        var text = output.Length > 6000 ? output[..6000] + "\n……（后面还有，省略了）" : output;
        _output = new StackPanel();
        _output.Children.Add(Collapsible(failed ? "查看出错信息" : "查看运行结果", text.TrimEnd(), new Thickness(36, 2, 0, 0)));
        _body.Children.Add(_output);
    }
}
