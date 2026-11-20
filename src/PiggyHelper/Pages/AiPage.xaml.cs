using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using PiggyHelper.Services;

namespace PiggyHelper.Pages;

public partial class AiPage : UserControl
{
    private readonly MainWindow _main;
    private readonly List<ChatMessage> _history = new();
    private CancellationTokenSource? _cts;
    private bool _firstLoad = true;
    private bool _modeReady;
    /// <summary>这些平台已经确认不支持工具调用（本次运行期间不再尝试）。</summary>
    private readonly HashSet<string> _noTools = new();

    private static readonly string[] Suggestions =
    {
        "电脑突然没声音了怎么办？",
        "怎么把手机里的照片传到电脑上？",
        "C 盘红了，空间不够怎么办？",
        "电脑越来越卡，有什么办法？",
        "怎么截一张长长的网页截图？",
        "下载软件时怎么避免装上一堆广告软件？",
    };

    private static readonly string[] AgentSuggestions =
    {
        "帮我看看电脑配置怎么样",
        "C 盘还剩多少空间？有多少垃圾？",
        "网页打不开了，帮我查查网络",
        "电脑好卡，看看是什么在占内存",
        "帮我打开调节音量的设置",
    };

    private static readonly string[] AdvancedSuggestions =
    {
        "看看我的桌面上有哪些文件",
        "帮我查一下这台电脑的 IP 地址",
        "下载文件夹里最大的 5 个文件是什么？",
        "帮我在桌面建一个“作业”文件夹",
    };

    private bool Advanced => ModeAdvanced.IsChecked == true;
    private bool Busy => _cts != null;

    public AiPage(MainWindow main)
    {
        _main = main;
        InitializeComponent();
        SetupPanel.Saved += () => { UpdateMode(); _main.ShowToast("设置好啦！现在可以向小猪提问了。"); };
        var demoAdvanced = App.AutoAction is "advanced-demo" or "engine-card";
        (SettingsService.Current.AgentMode == "advanced" || demoAdvanced ? ModeAdvanced : ModeSafe).IsChecked = true;
        _modeReady = true;
        UpdateModeUi();
        UpdateMode();
        AddWelcome();
        Loaded += (_, _) =>
        {
            UpdateMode(); // 可能刚在设置里换了 AI 平台
            if (_firstLoad)
            {
                _firstLoad = false;
                switch (App.AutoAction)
                {
                    case "demo": AddDemo(); break;
                    case "agent-demo": AddAgentDemo(); break;
                    case "advanced-demo": AddAdvancedDemo(); break;
                    case "engine-card": AddEngineDemo(); break;
                    case "advanced-dialog": ModeSafe.IsChecked = true; Dispatcher.BeginInvoke(() => ModeAdvanced.IsChecked = true); break;
                }
            }
            Input.Focus();
        };
    }

    /// <summary>从首页搜索框等地方带着问题过来。</summary>
    public void Ask(string question)
    {
        if (Busy) { Input.Text = question; return; }
        if (AiService.IsConfigured) _ = SendAsync(question);
        else Input.Text = question; // 设置好 Key 之后可以直接发送
    }

    /// <summary>小猪退出时调用：关掉后台引擎。</summary>
    public void Shutdown()
    {
        _cts?.Cancel();
        _acp?.Kill();
    }

    private void UpdateMode()
    {
        bool demo = App.AutoAction is "demo" or "agent-demo" or "advanced-demo" or "engine-card" or "advanced-dialog";
        bool ready = AiService.IsConfigured || demo;
        if (App.AutoAction == "setup") ready = false;
        SetupView.Visibility = ready ? Visibility.Collapsed : Visibility.Visible;
        ChatScroll.Visibility = InputArea.Visibility = ModeBar.Visibility = ready ? Visibility.Visible : Visibility.Collapsed;
        NewChatButton.Visibility = ready ? Visibility.Visible : Visibility.Collapsed;
        var p = AiService.CurrentProvider;
        ProviderText.Text = ready ? $"正在使用：{p.Name}" : "还没设置";
    }

    private void UpdateModeUi()
    {
        AdvancedBanner.Visibility = Advanced ? Visibility.Visible : Visibility.Collapsed;
        ModeHint.Text = Advanced
            ? "原版 Harness：AI 能直接运行命令、整理文件。"
            : "AI 能帮你查电脑情况、打开设置，不会改动任何东西。";
        Subtitle.Text = Advanced
            ? "告诉小猪你想做什么，它会直接动手，每一步都显示给你看。"
            : "像聊天一样问电脑问题，小猪会查查你的电脑，再一步一步教你。";
        Input.Tag = Advanced
            ? "告诉小猪要做什么，比如：帮我看看桌面上有哪些文件（Enter 发送）"
            : "输入你的问题，比如：电脑没声音了怎么办？（Enter 发送，Shift+Enter 换行）";
        UpdateEngineStatus();
    }

    private async void OnModeChecked(object sender, RoutedEventArgs e)
    {
        if (!_modeReady) return;
        if (sender == ModeAdvanced && SettingsService.Current.AgentMode != "advanced")
        {
            if (Busy) { ModeSafe.IsChecked = true; _main.ShowToast("等小猪这次回答完，再切换模式吧。"); return; }
            var ok = await _main.ShowConfirmAsync(
                "要打开高级模式吗？",
                "高级模式按 DeepSeek Harness 原本的方式运行，让 AI 在你的电脑上真的动手：运行命令、读写文件。它很能干，但也可能做错事。",
                new[]
                {
                    "AI 会直接动手，不会每一步都问你。它做的每一步都会显示成一张卡片，可以随时点“停止”。",
                    "引擎自带沙盒：只能改你自己的文件夹（桌面、文档、下载……）；要改别的地方，会先弹出卡片问你。",
                    "AI 可能改错、删错你自己的文件（比如作业）。重要的文件，最好先复制一份。",
                    "第一次使用要下载引擎（三百多 MB），只需要下载一次。",
                },
                "我知道了，打开高级模式",
                checkText: "我已了解风险",
                danger: true, tag: "有风险的功能");
            if (!ok)
            {
                _modeReady = false;
                ModeSafe.IsChecked = true;
                _modeReady = true;
                return;
            }
            SettingsService.Current.AgentMode = "advanced";
            SettingsService.Save();
            UpdateModeUi();
            AddSystemNote("已切换到高级模式：AI 会直接动手，每一步都会显示出来，随时可以点“停止”。");
            return;
        }
        if (sender == ModeSafe && SettingsService.Current.AgentMode != "safe")
        {
            if (Busy) { _cts?.Cancel(); }
            SettingsService.Current.AgentMode = "safe";
            SettingsService.Save();
            UpdateModeUi();
            AddSystemNote("已切换回安全助手：小猪只会查看电脑情况、帮你打开设置，不会改动任何东西。");
            return;
        }
        UpdateModeUi();
    }

    private void OnSettingsClick(object sender, RoutedEventArgs e) => _main.Navigate("settings");

    private void OnNewChatClick(object sender, RoutedEventArgs e)
    {
        _cts?.Cancel();
        _history.Clear();
        _newHarnessSession = true;
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
        if (Busy) return;
        AddUser(question);
        var turn = AddTurn();
        turn.Placeholder("小猪正在思考……");

        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        SendButton.Content = "停止";
        try
        {
            if (Advanced) await RunHarnessAsync(turn, question, ct);
            else await RunSafeAgentAsync(turn, question, ct);
        }
        catch (OperationCanceledException)
        {
            turn.Notice("（已停止）", "#8C7A84", "#F4F0F2");
        }
        catch (AiException ex)
        {
            turn.Error(ex.Message, () => _main.Navigate("settings"));
        }
        catch (Exception ex)
        {
            turn.Error("出了点小问题：" + ex.Message);
        }
        finally
        {
            CancelPendingCards();
            _cts?.Dispose();
            _cts = null;
            SendButton.Content = "发送";
            ChatScroll.ScrollToEnd();
        }
    }

    /// <summary>回答结束：画完最后一段文字，加上"打开某功能"按钮和"复制回答"。</summary>
    private void FinishTurn(ChatTurn turn)
    {
        turn.EndSegment();
        var (text, names) = AiService.ExtractActions(turn.AllText.ToString());
        foreach (var name in names) AddAction(turn.Actions, name);
        if (text.Trim().Length > 0)
        {
            var copy = new Button { Style = (Style)FindResource("LinkButton"), Content = "复制回答", FontSize = 12 };
            copy.Click += (_, _) =>
            {
                try { Clipboard.SetText(text.Trim()); _main.ShowToast("已复制，可以按 Ctrl+V 粘贴到别的地方。"); } catch { }
            };
            turn.Footer.Children.Clear();
            turn.Footer.Children.Add(copy);
            turn.Footer.Visibility = Visibility.Visible;
        }
    }

    private void RememberTurn(string question, string answer)
    {
        _history.Add(new ChatMessage("user", question));
        _history.Add(new ChatMessage("assistant", answer.Length > 0 ? answer : "（好的）"));
        if (_history.Count > 16) _history.RemoveRange(0, _history.Count - 16);
    }

    // ---------- 安全助手：小猪自己的白名单工具 ----------

    private async Task RunSafeAgentAsync(ChatTurn turn, string question, CancellationToken ct)
    {
        var provider = AiService.CurrentProvider;
        if (_noTools.Contains(provider.Id + "|" + provider.Model))
        {
            await PlainChatAsync(turn, question, ct);
            return;
        }

        var messages = new JsonArray { new JsonObject { ["role"] = "system", ["content"] = AiService.SystemPrompt() + AgentTools.SystemPromptAddon() } };
        foreach (var m in _history) messages.Add(new JsonObject { ["role"] = m.Role, ["content"] = m.Content });
        messages.Add(new JsonObject { ["role"] = "user", ["content"] = question });
        var tools = AgentTools.Definitions();
        var denied = new HashSet<string>();

        for (int round = 0; round < 8; round++)
        {
            bool last = round == 7; // 最后一轮不给工具，让 AI 直接总结
            RoundResult r;
            try
            {
                r = await AiService.ChatRoundAsync(messages, last ? null : tools,
                    full => Dispatcher.BeginInvoke(() => { turn.SetSegment(full); ChatScroll.ScrollToEnd(); }), ct);
            }
            catch (ToolsNotSupportedException)
            {
                _noTools.Add(provider.Id + "|" + provider.Model);
                turn.Notice($"“{provider.Name}”的这个模型不支持让小猪动手查电脑，这次先用普通聊天回答你。想用这个功能，可以在设置里换成 DeepSeek 或通义千问。");
                await PlainChatAsync(turn, question, ct);
                return;
            }
            await Dispatcher.InvokeAsync(() => { }); // 等最后一段文字画上去
            turn.SetSegment(r.Text);
            turn.EndSegment();
            if (r.ToolCalls.Count == 0) break;

            var assistant = new JsonObject
            {
                ["role"] = "assistant",
                ["content"] = r.Text.Length > 0 ? r.Text : null,
                ["tool_calls"] = new JsonArray(r.ToolCalls.Select(c => (JsonNode)new JsonObject
                {
                    ["id"] = c.Id,
                    ["type"] = "function",
                    ["function"] = new JsonObject { ["name"] = c.Name, ["arguments"] = c.Arguments },
                }).ToArray()),
            };
            messages.Add(assistant);
            foreach (var call in r.ToolCalls)
            {
                ct.ThrowIfCancellationRequested();
                var result = await RunSafeToolAsync(turn, call, denied, ct);
                messages.Add(new JsonObject { ["role"] = "tool", ["tool_call_id"] = call.Id, ["content"] = result });
            }
            ChatScroll.ScrollToEnd();
        }
        FinishTurn(turn);
        RememberTurn(question, AiService.ExtractActions(turn.AllText.ToString()).Text.Trim());
    }

    private async Task PlainChatAsync(ChatTurn turn, string question, CancellationToken ct)
    {
        var history = new List<ChatMessage>(_history) { new("user", question) };
        var answer = await AiService.AskAsync(history,
            partial => Dispatcher.BeginInvoke(() => { turn.SetSegment(partial); ChatScroll.ScrollToEnd(); }), ct);
        await Dispatcher.InvokeAsync(() => { });
        turn.SetSegment(answer);
        FinishTurn(turn);
        RememberTurn(question, AiService.ExtractActions(answer).Text.Trim());
    }

    private async Task<string> RunSafeToolAsync(ChatTurn turn, ToolCall call, HashSet<string> denied, CancellationToken ct)
    {
        var tool = AgentTools.Find(call.Name);
        if (tool == null) return $"Error: there is no tool named '{call.Name}'. Use only the tools you were given.";

        if (!tool.NeedsConfirm)
        {
            var chip = turn.AddChip("小猪正在" + tool.Label.Replace("了", "") + "……");
            ChatScroll.ScrollToEnd();
            try
            {
                var res = await AgentTools.RunReadOnlyAsync(call.Name, ct);
                chip.Done(res.ForUser, res.ForAi, res.Ok);
                return res.ForAi;
            }
            catch (OperationCanceledException) { chip.Done("已停止", "", false); throw; }
            catch (Exception ex)
            {
                chip.Done("没查到（" + ex.Message + "）", "", false);
                return "Error: " + ex.Message;
            }
        }

        // 需要用户点头的：打开设置 / 打开小猪的页面
        var (title, detail) = AgentTools.DescribeConfirm(call);
        string? target = null, page = null;
        NavItem? item = null;
        if (call.Name == "open_setting")
        {
            var n = AgentTools.ArgOf(call.Arguments, "name") ?? "";
            item = AgentTools.OpenableSettings.FirstOrDefault(i => i.Title == n);
            if (item == null) return $"Error: unknown setting '{n}'. Choose one of the names in the enum.";
            target = item.Title;
        }
        else if (call.Name == "open_xiaozhu_page")
        {
            var p = AgentTools.ArgOf(call.Arguments, "page") ?? "";
            if (!AgentTools.PiggyPages.TryGetValue(p, out var pg)) return $"Error: unknown page '{p}'.";
            page = pg.Page;
            target = pg.Title;
        }
        var key = call.Name + "|" + target;
        if (denied.Contains(key)) return "Error: the user already said no to this. Do not ask again; tell the user how to do it themselves.";

        var card = turn.AddCard(call.Name == "open_setting" ? "" : "", title, detail,
            new GuardResult(CommandRisk.Ok, "只是打开页面，不会改动任何东西。"), "", "");
        ChatScroll.ScrollToEnd();
        Track(card);
        bool allow = await card.AskAsync("打开", "不用了");
        ct.ThrowIfCancellationRequested();
        if (!allow)
        {
            denied.Add(key);
            return "The user chose not to open it. Do not try again; explain how they can do it themselves if needed.";
        }
        if (item != null)
        {
            var ok = Launcher.Open(item, out var error);
            card.Decide(true, ok ? $"已经打开“{item.Title}”。" + item.Tip : "没打开成功：" + error);
            return ok ? $"Opened '{item.Title}' for the user." : $"Error: failed to open ({error}).";
        }
        card.Decide(true, $"已经打开“{target}”，点左边的“AI 问答”就能回来。");
        var navPage = page!;
        _ = Dispatcher.BeginInvoke(() => _main.Navigate(navPage), System.Windows.Threading.DispatcherPriority.Background);
        return $"Opened Piggy's '{target}' page for the user.";
    }

    // ---------- 等待中的卡片（按"停止"时一起取消） ----------
    private readonly List<ActionCardView> _waitingCards = new();

    private void Track(ActionCardView card)
    {
        _waitingCards.RemoveAll(c => !c.IsWaiting);
        _waitingCards.Add(card);
    }

    private void CancelPendingCards()
    {
        foreach (var c in _waitingCards.Where(c => c.IsWaiting).ToList()) c.Decide(false, "已停止，这一步没有做。");
        _waitingCards.Clear();
    }

    // ---------- 气泡 ----------
    private Brush TextColor => (Brush)FindResource("TextBrush");
    private Brush Accent => (Brush)FindResource("PinkDeepBrush");

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

    private ChatTurn AddTurn()
    {
        var turn = new ChatTurn(this);
        ChatPanel.Children.Add(turn.Root);
        ChatScroll.ScrollToEnd();
        return turn;
    }

    private void AddSystemNote(string text)
    {
        ChatPanel.Children.Add(new Border
        {
            Background = new SolidColorBrush(Palette.Parse(Advanced ? "#FFF4E5" : "#EEF7F1")),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(14, 7, 14, 7),
            Margin = new Thickness(0, 8, 0, 8),
            HorizontalAlignment = HorizontalAlignment.Center,
            Child = new TextBlock
            {
                Text = text, FontSize = 12.5, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, MaxWidth = 560,
                Foreground = new SolidColorBrush(Palette.Parse(Advanced ? "#8A4B08" : "#2E8550")),
            },
        });
        ChatScroll.ScrollToEnd();
    }

    private void AddWelcome()
    {
        var turn = AddTurn();
        turn.SetSegment(Advanced
            ? "你好呀，我是小猪！🐷\n现在是**高级模式**：你说想做什么，我会直接在电脑上动手，做的每一步都会显示出来，想停就点“停止”。可以这样试试："
            : "你好呀，我是小猪！🐷\n电脑上遇到任何不会的，都可以直接问我，用大白话说就行。我还能帮你**看看电脑的情况**、**打开设置**。点一下就能问：");
        turn.RenderSegment();
        turn.EndSegment();
        turn.AllText.Clear();
        var actions = turn.Actions;
        actions.Visibility = Visibility.Visible;
        actions.Margin = new Thickness(0, 10, 0, 4);
        var list = Advanced ? AdvancedSuggestions : AgentSuggestions.Concat(Suggestions.Take(3));
        foreach (var s in list)
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
                if (Busy) return;
                if (!AiService.IsConfigured && App.AutoAction is not ("demo" or "agent-demo")) { UpdateMode(); return; }
                _ = SendAsync(s);
            };
            actions.Children.Add(chip);
        }
    }
}
