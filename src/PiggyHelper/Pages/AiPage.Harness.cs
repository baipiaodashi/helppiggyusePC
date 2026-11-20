using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PiggyHelper.Services;
using PiggyHelper.Services.Harness;

namespace PiggyHelper.Pages;

/// <summary>高级模式：后台运行 DeepSeek Harness，界面上每一步都用小猪自己的卡片来确认。</summary>
public partial class AiPage
{
    private AcpClient? _acp;
    private string? _acpSignature;
    private bool _newHarnessSession;
    private ChatTurn? _harnessTurn;
    private volatile string? _fatalMessage;
    private readonly Dictionary<string, ActionCardView> _harnessCards = new();

    private void UpdateEngineStatus()
    {
        var dir = HarnessEngine.FindEngine();
        EngineInfoButton.Visibility = dir == null ? Visibility.Collapsed : Visibility.Visible;
        EngineStatus.Text = "按 Harness 原本的方式运行：AI 会直接动手，每一步都显示在卡片上。引擎自带沙盒，只能改你自己的文件夹，要改别的地方时会先问你。" +
                            (dir == null && App.AutoAction is not ("advanced-demo") ? "第一次使用要先下载引擎。" : "");
    }

    private async void OnEngineInfoClick(object sender, RoutedEventArgs e)
    {
        var dir = HarnessEngine.FindEngine();
        if (dir == null) return;
        var info = HarnessEngine.EngineInfo(dir) ?? "DeepSeek Harness";
        var remove = await _main.ShowConfirmAsync("高级模式引擎", "小猪的高级模式用的是 DeepSeek 开源的 Harness 引擎，在后台运行，不会弹出黑色的命令行窗口。",
            new[]
            {
                "版本：" + info.Replace("\r", "").Replace("\n", "，"),
                "放在：" + dir,
                "AI 干活的文件夹：" + HarnessEngine.WorkspaceDir,
                "开源协议：MIT。你的 API Key 只放在这台电脑上，不会写进引擎的文件里；引擎的遥测上报已关闭。",
            },
            "知道了", pose: "search", cancelText: null);
        _ = remove;
    }

    private async Task RunHarnessAsync(ChatTurn turn, string question, CancellationToken ct)
    {
        var dir = HarnessEngine.FindEngine() ?? await InstallEngineAsync(turn, ct);
        var provider = AiService.CurrentProvider;
        var key = SettingsService.GetApiKey();
        if (string.IsNullOrEmpty(key)) throw new AiException("还没有填写 API Key，先去设置一下吧。");
        var cfg = HarnessEngine.ProviderConfig(provider);
        var signature = $"{dir}|{cfg}|{key.GetHashCode()}";

        if (_acp == null || !_acp.IsRunning || _acpSignature != signature)
        {
            if (_acp != null) { var old = _acp; _acp = null; await old.DisposeAsync(); }
            turn.Placeholder("小猪正在启动高级模式的引擎，第一次会慢一点……");
            HarnessEngine.PrepareHome(cfg.BaseUrl, cfg.Model, cfg.ContextWindow, cfg.MaxTokens);
            var client = new AcpClient();
            WireClient(client);
            _fatalMessage = null;
            try
            {
                await client.StartAsync(dir, key, ct);
            }
            catch (OperationCanceledException) { await client.DisposeAsync(); throw; }
            catch (AiException) { await client.DisposeAsync(); throw; }
            catch (Exception ex)
            {
                await client.DisposeAsync();
                throw new AiException("高级模式的引擎没能启动：" + (_fatalMessage ?? ex.Message) + "\n可以点右上角“引擎信息”看看，或者先切回“安全助手”。");
            }
            _acp = client;
            _acpSignature = signature;
            _newHarnessSession = false;
        }
        else if (_newHarnessSession)
        {
            await _acp.NewSessionAsync(ct);
            _newHarnessSession = false;
        }

        turn.Placeholder("小猪正在思考……");
        _harnessCards.Clear();
        _harnessChips.Clear();
        _harnessTurn = turn;
        string stop;
        try
        {
            stop = await _acp.PromptAsync(question, ct);
        }
        catch (IOException)
        {
            _acp = null;
            throw new AiException(_fatalMessage ?? "高级模式的引擎意外停了，再发一次试试。");
        }
        finally
        {
            await Dispatcher.InvokeAsync(() => { }); // 等引擎发来的文字都画上去
            _harnessTurn = null;
        }
        if (stop == "max_tokens") turn.Notice("AI 这次写得太长被截断了，可以让它“接着说”。");
        else if (stop == "max_turn_requests") turn.Notice("这件事步骤太多，小猪先停一下。想继续的话，跟它说“继续”。");
        else if (stop == "refusal") turn.Notice("AI 拒绝了这个请求。");
        if (turn.AllText.Length == 0 && turn.Flow.Children.Count <= 1) turn.SetSegment("好的。");
        FinishTurn(turn);
    }

    private void WireClient(AcpClient client)
    {
        client.TextChunk += t => Dispatcher.BeginInvoke(() =>
        {
            if (_acp != client && _acp != null) return;
            _harnessTurn?.Append(t);
            ChatScroll.ScrollToEnd();
        });
        client.ToolCallChanged += call => Dispatcher.BeginInvoke(() => OnHarnessToolCall(call));
        client.PermissionHandler = (call, _) => Dispatcher.InvokeAsync(() => AskHarnessAsync(call)).Task.Unwrap();
        client.Fatal += msg =>
        {
            _fatalMessage = msg;
            Dispatcher.BeginInvoke(() =>
            {
                if (_acp == client) _acp = null;
                CancelPendingCards();
                if (_harnessTurn == null && !Busy) AddTurn().Error(msg);
            });
        };
    }

    private static (string Icon, string RawLabel) ToolLook(string tool) => tool switch
    {
        "pwsh" or "bash" => ("\uE756", "查看原始命令"),
        "write" or "edit" => ("\uE70F", "查看要写入的内容"),
        "read" or "read_image" => ("\uE8A5", "查看详细参数"),
        "glob" or "grep" => ("\uE721", "查看详细参数"),
        "web_fetch" or "web_search" => ("\uE774", "查看详细参数"),
        _ => ("\uE90F", "查看详细参数"),
    };

    /// <summary>只看不改的工具：用一个小标签显示，不占地方。</summary>
    private static bool IsQuietTool(string tool) => tool is "read" or "read_image" or "glob" or "grep" or "web_fetch" or "web_search" or "working_directory"
        or "list_agents" or "send_message" or "wait_agent" or "create_goal" or "get_goal" or "update_goal" or "job_list" or "job_output" or "skill";

    private readonly Dictionary<string, ChatTurn.Chip> _harnessChips = new();

    /// <summary>
    /// 引擎自己要问的权限（比如 AI 申请用更大的权限运行）：在这一步的卡片上显示"同意 / 不同意"。
    /// 小猪不额外拦截，也不额外询问。
    /// </summary>
    private async Task<bool> AskHarnessAsync(HarnessToolCall call)
    {
        var turn = _harnessTurn;
        if (turn == null || !Busy) return false;
        if (!_harnessCards.TryGetValue(call.Id, out var view))
        {
            _harnessChips.Remove(call.Id);
            view = AddHarnessCard(turn, call);
        }
        view.SetStatus("Harness 想先问问你：这一步可以做吗？", "#A36200", "\uE9CE");
        ChatScroll.ScrollToEnd();
        Track(view);
        var ok = await view.AskAsync();
        return ok && Busy;
    }

    private ActionCardView AddHarnessCard(ChatTurn turn, HarnessToolCall call)
    {
        var card = ActionDescriber.Describe(call.ToolName, call.Input);
        // 风险只是提个醒，不拦
        var risk = card.Risk.Risk == CommandRisk.Block ? new GuardResult(CommandRisk.Warn, card.Risk.Reason) : card.Risk;
        var (icon, rawLabel) = ToolLook(call.ToolName);
        var view = turn.AddCard(icon, card.Title, card.Detail, risk, card.Raw, rawLabel);
        _harnessCards[call.Id] = view;
        return view;
    }

    private void OnHarnessToolCall(HarnessToolCall call)
    {
        var turn = _harnessTurn;
        if (call.ToolName == "todo_write" || string.IsNullOrEmpty(call.ToolName)) return;

        if (IsQuietTool(call.ToolName) && !_harnessCards.ContainsKey(call.Id))
        {
            if (!_harnessChips.TryGetValue(call.Id, out var chip))
            {
                if (turn == null || call.Input == null) return;
                var desc = ActionDescriber.Describe(call.ToolName, call.Input);
                chip = turn.AddChip("小猪正在" + desc.Title + "……");
                _harnessChips[call.Id] = chip;
                ChatScroll.ScrollToEnd();
            }
            if (call.Status is "completed" or "failed")
            {
                var desc = ActionDescriber.Describe(call.ToolName, call.Input);
                var what = string.IsNullOrWhiteSpace(desc.Detail) ? desc.Title : desc.Title + "：" + desc.Detail;
                if (what.Length > 60) what = what[..60] + "…";
                var output = call.Output.Length > 6000 ? call.Output[..6000] + "\n……" : call.Output;
                chip.Done((call.Status == "completed" ? "" : "没成功：") + what, output.Trim(), call.Status == "completed");
            }
            return;
        }

        if (!_harnessCards.TryGetValue(call.Id, out var view))
        {
            if (turn == null || call.Input == null) return;
            view = AddHarnessCard(turn, call);
            ChatScroll.ScrollToEnd();
        }
        if (call.Status == "completed")
        {
            view.SetStatus("做完了", "#2E8550", "\uE73E");
            view.ShowOutput(call.Output, false);
        }
        else if (call.Status == "failed")
        {
            if (call.Decided && !call.Allowed) view.SetStatus("你没有同意，这一步没有做", "#8C7A84", "\uE711");
            else
            {
                view.SetStatus("没有成功，AI 会看看出了什么问题", "#C9302C", "\uE711");
                view.ShowOutput(call.Output, true);
            }
        }
        else if (!view.IsWaiting) view.SetStatus("正在做……", "#7451DD", "\uE768");
    }

    // ---------- 下载引擎 ----------

    private sealed class EngineCard
    {
        public required Border Root { get; init; }
        public required StackPanel Buttons { get; init; }
        public required ProgressBar Bar { get; init; }
        public required TextBlock Status { get; init; }
    }

    private EngineCard AddEngineCard(ChatTurn turn)
    {
        turn.EndSegment();
        var body = new StackPanel();
        var head = new DockPanel();
        var pig = new Controls.PiggyMascot { Pose = "search", Animated = false, Width = 56, Height = 56, Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Top };
        DockPanel.SetDock(pig, Dock.Left);
        head.Children.Add(pig);
        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        titles.Children.Add(new TextBlock { Text = "第一次用高级模式，要先下载引擎", FontSize = 15.5, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap });
        titles.Children.Add(new TextBlock
        {
            Text = "引擎是 DeepSeek 开源的 Harness，从小猪的 GitHub 发布页下载，只需要下载一次。文件有几百 MB，最好连着 Wi-Fi。",
            FontSize = 13, LineHeight = 19, TextWrapping = TextWrapping.Wrap, Foreground = (Brush)FindResource("SubTextBrush"), Margin = new Thickness(0, 4, 0, 0),
        });
        head.Children.Add(titles);
        body.Children.Add(head);
        var bar = new ProgressBar { Style = (Style)FindResource("CandyProgress"), Minimum = 0, Maximum = 1, Margin = new Thickness(70, 12, 0, 0), Visibility = Visibility.Collapsed };
        var status = new TextBlock { FontSize = 12.5, Foreground = (Brush)FindResource("SubTextBrush"), Margin = new Thickness(70, 6, 0, 0), TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(70, 12, 0, 0) };
        body.Children.Add(bar);
        body.Children.Add(status);
        body.Children.Add(buttons);
        var root = new Border
        {
            Background = new SolidColorBrush(Palette.Parse("#FFF8FB")),
            BorderBrush = (Brush)FindResource("PinkLightBrush"),
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(18),
            Padding = new Thickness(16, 14, 16, 14),
            Margin = new Thickness(0, 2, 0, 6),
            MinWidth = 420,
            Child = body,
        };
        turn.Flow.Children.Add(root);
        ChatScroll.ScrollToEnd();
        return new EngineCard { Root = root, Buttons = buttons, Bar = bar, Status = status };
    }

    private async Task<string> InstallEngineAsync(ChatTurn turn, CancellationToken ct)
    {
        var card = AddEngineCard(turn);
        var choice = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var go = new Button { Style = (Style)FindResource("PillButton"), Content = "开始下载", Height = 42, FontSize = 14.5, MinWidth = 120, Margin = new Thickness(0, 0, 10, 0) };
        var no = new Button { Style = (Style)FindResource("GhostButton"), Content = "先不用了", Height = 40, FontSize = 14.5 };
        go.Click += (_, _) => choice.TrySetResult(true);
        no.Click += (_, _) => choice.TrySetResult(false);
        card.Buttons.Children.Add(go);
        card.Buttons.Children.Add(no);
        using (ct.Register(() => choice.TrySetCanceled()))
        {
            if (!await choice.Task)
            {
                card.Buttons.Visibility = Visibility.Collapsed;
                throw new AiException("好的，先不下载。想让小猪回答问题的话，可以先切回上面的“安全助手”。");
            }
        }
        card.Buttons.Visibility = Visibility.Collapsed;
        card.Bar.Visibility = card.Status.Visibility = Visibility.Visible;
        card.Bar.IsIndeterminate = true;
        card.Status.Text = "正在连接下载服务器……";
        try
        {
            await HarnessEngine.InstallAsync((done, total, stage) => Dispatcher.BeginInvoke(() =>
            {
                if (total > 0 && done < total)
                {
                    card.Bar.IsIndeterminate = false;
                    card.Bar.Value = (double)done / total;
                    card.Status.Text = $"{stage}  {Format.Bytes(done)} / {Format.Bytes(total)}";
                }
                else if (total < 0 && done > 0)
                {
                    card.Bar.IsIndeterminate = true;
                    card.Status.Text = $"{stage}  已下载 {Format.Bytes(done)}";
                }
                else
                {
                    card.Bar.IsIndeterminate = done != total;
                    if (done == total && total > 0) card.Bar.Value = 1;
                    card.Status.Text = stage;
                }
            }), ct);
        }
        catch (OperationCanceledException)
        {
            card.Status.Text = "已停止下载。";
            card.Bar.Visibility = Visibility.Collapsed;
            throw;
        }
        catch (Exception ex)
        {
            card.Bar.Visibility = Visibility.Collapsed;
            card.Status.Text = "下载没成功。";
            throw new AiException(ex.Message);
        }
        await Dispatcher.InvokeAsync(() => { });
        card.Bar.IsIndeterminate = false;
        card.Bar.Value = 1;
        card.Status.Text = "引擎装好啦！";
        UpdateEngineStatus();
        return HarnessEngine.FindEngine() ?? throw new AiException("引擎装好了，但是没找到，重新试一次吧。");
    }

    // ---------- 自动截图用的示例（不联网） ----------

    private void AddDemo()
    {
        AddUser("电脑突然没声音了怎么办？");
        var turn = AddTurn();
        turn.SetSegment(
            "别着急，按下面的顺序检查一下，大多数情况第 1、2 步就能解决：\n\n" +
            "1. 看看屏幕右下角的**喇叭图标**，如果上面有个 ✕，点一下取消静音。\n" +
            "2. 点开喇叭，把**音量滑块**往右拖大一点。\n" +
            "3. 如果插着耳机，拔下来试试；或者检查耳机线有没有插紧。\n" +
            "4. 还是不行的话，在声音设置里看看**输出设备**是不是选对了。\n\n" +
            "> 小提示：重启一下电脑，也能解决很多奇怪的问题哦。\n\n" +
            "[[打开:调节音量]]");
        FinishTurn(turn);
    }

    private void AddAgentDemo()
    {
        AddUser("电脑好卡，帮我看看是什么在占内存");
        var turn = AddTurn();
        turn.SetSegment("好的，我先看看你电脑的配置和正在运行的软件～");
        var c1 = turn.AddChip("");
        c1.Done("小猪看了看电脑配置", "Windows: Windows 11 家庭中文版 23H2 (build 22631), 64-bit\nCPU: 12th Gen Intel(R) Core(TM) i5-1240P (16 threads)\nMemory (RAM): 15.7 GB total, 2.1 GB free, 87% in use");
        var c2 = turn.AddChip("");
        c2.Done("小猪看了看正在运行的软件", "Using the most memory:\n- msedge (32 processes): 3.9 GB\n- WeChat: 1.1 GB\n- QQ: 820 MB");
        turn.SetSegment(
            "找到原因啦：你的内存一共 16 GB，现在已经用了 **87%**，主要是这几个在占：\n\n" +
            "1. **Edge 浏览器**开了很多网页，用了快 4 GB。把不看的网页关掉就能省下不少。\n" +
            "2. **微信**和 **QQ** 一共用了快 2 GB，不聊天的时候可以先退出一个。\n\n" +
            "想看得更清楚，我可以帮你打开“任务管理器”，里面能看到每个软件用了多少内存：");
        var card = turn.AddCard("\uE713", "打开“任务管理器”", "看看哪个软件占用了太多内存，卡住的软件可以在这里关掉。",
            new GuardResult(CommandRisk.Ok, "只是打开页面，不会改动任何东西。"), "", "");
        _ = card.AskAsync("打开", "不用了");
    }

    private void AddAdvancedDemo()
    {
        AddUser("下载文件夹里最大的 5 个文件是什么？");
        var turn = AddTurn();
        turn.SetSegment("好的，我用一条只看不改的命令，看看你的下载文件夹～");
        var cmd = "Get-ChildItem \"$HOME\\Downloads\" -File | Sort-Object Length -Descending | Select-Object -First 5 Name, @{n='MB';e={[math]::Round($_.Length/1MB)}}";
        var a = ActionDescriber.Describe("pwsh", new JsonObject { ["command"] = cmd, ["description"] = "列出下载文件夹里最大的 5 个文件（只是看看）" });
        var v1 = turn.AddCard("\uE756", a.Title, a.Detail, a.Risk, a.Raw, "查看原始命令");
        v1.Decide(true);
        v1.SetStatus("做完了", "#2E8550", "\uE73E");
        v1.ShowOutput("Name                         MB\n----                         --\n剪映安装包.exe               612\n期末复习资料.zip             388\nWeChatSetup.exe              196\n课堂录屏.mp4                 154\n论文初稿(1).docx              12", false);
        turn.SetSegment("最大的是 **剪映安装包.exe**（612 MB）。安装包装好软件以后就没用了，可以删掉。要我帮你把它移到回收站吗？");
        turn.EndSegment();

        AddUser("好，帮我把它移到回收站，再在 D 盘建一个“学习资料”文件夹");
        var t2 = AddTurn();
        t2.SetSegment("好的，先把安装包移到回收站（之后还能恢复）：");
        var b = ActionDescriber.Describe("pwsh", new JsonObject
        {
            ["command"] = "Add-Type -AssemblyName Microsoft.VisualBasic; [Microsoft.VisualBasic.FileIO.FileSystem]::DeleteFile(\"$HOME\\Downloads\\剪映安装包.exe\", 'OnlyErrorDialogs', 'SendToRecycleBin')",
            ["description"] = "把下载文件夹里的“剪映安装包.exe”移到回收站",
        });
        var v2 = t2.AddCard("\uE756", b.Title, b.Detail, b.Risk, b.Raw, "查看原始命令");
        v2.SetStatus("做完了", "#2E8550", "\uE73E");
        t2.SetSegment("D 盘在我的工作范围外面，需要你同意一下：");
        var c = ActionDescriber.Describe("pwsh", new JsonObject
        {
            ["command"] = "New-Item -ItemType Directory -Path 'D:\\学习资料'",
            ["description"] = "在 D 盘新建“学习资料”文件夹",
            ["sandbox_permissions"] = "danger-full-access",
            ["justification"] = "要在用户文件夹外面（D 盘）新建文件夹",
        });
        var v3 = t2.AddCard("\uE756", c.Title, c.Detail, c.Risk, c.Raw, "查看原始命令");
        v3.SetStatus("Harness 想先问问你：这一步可以做吗？", "#A36200", "\uE9CE");
        _ = v3.AskAsync();
    }

    private void AddEngineDemo()
    {
        AddUser("帮我在桌面建一个“作业”文件夹");
        var turn = AddTurn();
        var card = AddEngineCard(turn);
        card.Bar.Visibility = card.Status.Visibility = Visibility.Visible;
        card.Bar.Value = 0.42;
        card.Status.Text = "正在下载引擎……  148 MB / 352 MB";
    }
}
