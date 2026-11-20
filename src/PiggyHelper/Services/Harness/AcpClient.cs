using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using PiggyHelper.Services;

namespace PiggyHelper.Services.Harness;

/// <summary>Harness 想调用的一个工具（运行命令、读写文件、打开网页……）。</summary>
public sealed class HarnessToolCall
{
    public required string Id { get; init; }
    public string Title { get; set; } = "";
    public string Kind { get; set; } = "";
    public JsonObject? Input { get; set; }
    public string Status { get; set; } = "pending";
    public string Output { get; set; } = "";
    /// <summary>引擎问过用户、用户已经做了决定。</summary>
    public bool Decided { get; set; }
    public bool Allowed { get; set; }
    /// <summary>引擎为这一步问过几次。</summary>
    public int Asks { get; set; }

    /// <summary>工具名（Harness 的 title 就是工具名，比如 pwsh、read、write）。</summary>
    public string ToolName => Title;

    public string? Arg(string name) =>
        Input != null && Input.TryGetPropertyValue(name, out var v) && v is JsonValue jv && jv.TryGetValue<string>(out var s) ? s : null;
}

/// <summary>
/// 用 Agent Client Protocol（JSON-RPC，走标准输入输出）和后台的 DeepSeek Harness 对话。
/// 引擎跑在看不见的后台进程里，不会弹出命令行窗口；所有内容都由小猪的界面来画。
/// </summary>
public sealed class AcpClient : IAsyncDisposable
{
    private Process? _proc;
    private int _nextId = 1;
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonNode?>> _pending = new();
    private readonly ConcurrentDictionary<string, HarnessToolCall> _calls = new();
    private readonly StringBuilder _stderr = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private string? _sessionId;
    private bool _disposed;

    /// <summary>AI 说的话（一小段一小段地来）。</summary>
    public event Action<string>? TextChunk;
    /// <summary>AI 想调用一个工具（或者工具状态有变化）。</summary>
    public event Action<HarnessToolCall>? ToolCallChanged;
    /// <summary>引擎问："这一步可以做吗？" 返回 true 表示允许。</summary>
    public Func<HarnessToolCall, CancellationToken, Task<bool>>? PermissionHandler;
    /// <summary>引擎意外退出或者出了安全问题。</summary>
    public event Action<string>? Fatal;

    public bool IsRunning => !_disposed && _proc is { HasExited: false } && _sessionId != null;
    public string StderrTail { get { lock (_stderr) return _stderr.Length > 4000 ? _stderr.ToString(_stderr.Length - 4000, 4000) : _stderr.ToString(); } }

    /// <summary>启动引擎并新建一个对话。</summary>
    public async Task StartAsync(string engineDir, string apiKey, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(HarnessEngine.NodeExe(engineDir))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
            WorkingDirectory = HarnessEngine.WorkspaceDir,
        };
        psi.ArgumentList.Add(HarnessEngine.DshBin(engineDir));
        psi.ArgumentList.Add("--profile");
        psi.ArgumentList.Add("acp");
        psi.Environment["DSH_HOME"] = HarnessEngine.HomeDir;
        psi.Environment["PIGGY_AI_KEY"] = apiKey;
        psi.Environment["DSH_TELEMETRY_DISABLED"] = "1";
        psi.Environment["DSH_PERMISSION_MODE"] = "workspace-write";
        psi.Environment["NO_PROXY"] = "127.0.0.1,localhost";
        psi.Environment.Remove("NODE_OPTIONS");
        if (HarnessEngine.TempDir is { } tmp)
        {
            psi.Environment["TEMP"] = tmp;
            psi.Environment["TMP"] = tmp;
        }

        _proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        _proc.ErrorDataReceived += (_, e) =>
        {
            if (e.Data == null) return;
            lock (_stderr) _stderr.AppendLine(e.Data);
        };
        _proc.Exited += (_, _) =>
        {
            foreach (var p in _pending.Values) p.TrySetException(new IOException("引擎已经退出"));
            if (!_disposed) Fatal?.Invoke("高级模式的引擎意外退出了。" + LastErrorLine());
        };
        _proc.Start();
        ChildProcessJob.Add(_proc);
        _proc.BeginErrorReadLine();
        _ = Task.Run(ReadLoop);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(90)); // 第一次启动要加载很多文件
        await RequestAsync("initialize", new JsonObject
        {
            ["protocolVersion"] = 1,
            ["clientCapabilities"] = new JsonObject
            {
                ["fs"] = new JsonObject { ["readTextFile"] = false, ["writeTextFile"] = false },
                ["terminal"] = false,
            },
        }, timeout.Token);
        await NewSessionAsync(timeout.Token);
    }

    public async Task NewSessionAsync(CancellationToken ct)
    {
        var res = await RequestAsync("session/new", new JsonObject
        {
            ["cwd"] = HarnessEngine.WorkspaceDir,
            ["mcpServers"] = new JsonArray(),
        }, ct);
        _sessionId = res?["sessionId"]?.GetValue<string>() ?? throw new AiException("引擎没有创建对话。");
        _calls.Clear();
    }

    /// <summary>发一条消息给 AI，等它这一轮做完（返回停止原因）。</summary>
    public async Task<string> PromptAsync(string text, CancellationToken ct)
    {
        if (_sessionId == null) throw new InvalidOperationException("还没有开始对话");
        var sessionId = _sessionId;
        var task = RequestAsync("session/prompt", new JsonObject
        {
            ["sessionId"] = sessionId,
            ["prompt"] = new JsonArray { new JsonObject { ["type"] = "text", ["text"] = text } },
        }, CancellationToken.None);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using (ct.Register(() =>
        {
            try { _ = NotifyAsync("session/cancel", new JsonObject { ["sessionId"] = sessionId }); } catch { }
            cancelled.TrySetResult();
        }))
        {
            if (await Task.WhenAny(task, cancelled.Task) != task)
            {
                // 按了"停止"：给引擎几秒钟收尾，还不停就直接关掉（下次提问会重新启动）
                if (await Task.WhenAny(task, Task.Delay(8000)) != task) Kill();
                throw new OperationCanceledException(ct);
            }
        }
        var res = await task;
        return res?["stopReason"]?.GetValue<string>() ?? "end_turn";
    }

    // ---------- JSON-RPC ----------

    private async Task<JsonNode?> RequestAsync(string method, JsonObject @params, CancellationToken ct)
    {
        int id = Interlocked.Increment(ref _nextId);
        var tcs = new TaskCompletionSource<JsonNode?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = tcs;
        await SendAsync(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["method"] = method, ["params"] = @params });
        using (ct.Register(() => tcs.TrySetCanceled(ct)))
        {
            try { return await tcs.Task; }
            finally { _pending.TryRemove(id, out _); }
        }
    }

    private Task NotifyAsync(string method, JsonObject @params) =>
        SendAsync(new JsonObject { ["jsonrpc"] = "2.0", ["method"] = method, ["params"] = @params });

    private async Task SendAsync(JsonNode msg)
    {
        if (_proc == null || _proc.HasExited) throw new IOException("引擎没有在运行");
        var line = msg.ToJsonString();
        await _writeLock.WaitAsync();
        try
        {
            await _proc.StandardInput.WriteLineAsync(line);
            await _proc.StandardInput.FlushAsync();
        }
        finally { _writeLock.Release(); }
    }

    private async Task ReadLoop()
    {
        try
        {
            var reader = _proc!.StandardOutput;
            string? line;
            while ((line = await reader.ReadLineAsync()) != null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                JsonNode? msg;
                try { msg = JsonNode.Parse(line); } catch { continue; }
                if (msg is not JsonObject obj) continue;

                bool hasId = obj.TryGetPropertyValue("id", out var idNode) && idNode != null;
                var method = obj["method"]?.GetValue<string>();
                if (method != null && hasId) _ = HandleRequestAsync(idNode!.DeepClone(), method, obj["params"] as JsonObject);
                else if (method != null) HandleNotification(method, obj["params"] as JsonObject);
                else if (hasId && idNode is JsonValue v && v.TryGetValue<int>(out var id) && _pending.TryGetValue(id, out var tcs))
                {
                    if (obj["error"] is JsonObject err)
                        tcs.TrySetException(new AiException(FriendlyEngineError(err["message"]?.ToString() ?? "未知错误")));
                    else tcs.TrySetResult(obj["result"]);
                }
            }
        }
        catch { /* 进程退出 */ }
    }

    private void HandleNotification(string method, JsonObject? p)
    {
        if (method != "session/update" || p?["update"] is not JsonObject u) return;
        var kind = u["sessionUpdate"]?.GetValue<string>();
        switch (kind)
        {
            case "agent_message_chunk":
                if (u["content"]?["text"]?.GetValue<string>() is string t && t.Length > 0) TextChunk?.Invoke(t);
                break;
            case "tool_call":
            case "tool_call_update":
            {
                var id = u["toolCallId"]?.GetValue<string>();
                if (id == null) break;
                var call = _calls.GetOrAdd(id, _ => new HarnessToolCall { Id = id });
                if (u["title"]?.GetValue<string>() is string title) call.Title = title;
                if (u["kind"]?.GetValue<string>() is string k) call.Kind = k;
                if (u["rawInput"] is JsonObject input) call.Input = (JsonObject)input.DeepClone();
                if (u["status"]?.GetValue<string>() is string st) call.Status = st;
                if (u["content"] is JsonArray content)
                {
                    var sb = new StringBuilder();
                    foreach (var c in content)
                        if (c?["content"]?["text"]?.GetValue<string>() is string txt) sb.Append(txt);
                    if (sb.Length > 0) call.Output = sb.ToString();
                }
                ToolCallChanged?.Invoke(call);
                break;
            }
        }
    }

    private async Task HandleRequestAsync(JsonNode id, string method, JsonObject? p)
    {
        JsonObject response;
        if (method == "session/request_permission")
        {
            var callId = p?["toolCall"]?["toolCallId"]?.GetValue<string>() ?? "";
            var call = _calls.GetOrAdd(callId, _ => new HarnessToolCall { Id = callId });
            bool allow = false;
            call.Asks++;
            try
            {
                if (PermissionHandler != null) allow = await PermissionHandler(call, CancellationToken.None);
            }
            catch { allow = false; }
            call.Decided = true;
            call.Allowed = allow;
            response = new JsonObject
            {
                ["outcome"] = new JsonObject { ["outcome"] = "selected", ["optionId"] = allow ? "allow-once" : "reject-once" },
            };
            try { await SendAsync(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = response }); } catch { }
            return;
        }
        // 文件、终端等能力小猪不提供
        try
        {
            await SendAsync(new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = id,
                ["error"] = new JsonObject { ["code"] = -32601, ["message"] = "not supported by Piggy Helper" },
            });
        }
        catch { }
    }

    private void Abort(string reason)
    {
        if (_disposed) return;
        _disposed = true;
        try { _proc?.Kill(entireProcessTree: true); } catch { }
        Fatal?.Invoke(reason);
    }

    private string LastErrorLine()
    {
        var lines = StderrTail.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(l => !l.StartsWith("at ")).ToList();
        return lines.Count == 0 ? "" : "（" + lines[^1] + "）";
    }

    private static string FriendlyEngineError(string message)
    {
        var m = message.ToLowerInvariant();
        if (m.Contains("401") || m.Contains("unauthorized") || m.Contains("api key") || m.Contains("missing_credential"))
            return "API Key 不对或已经失效，去“设置”里检查一下。";
        if (m.Contains("402") || m.Contains("insufficient") || m.Contains("balance")) return "AI 账户的余额不足了，需要去平台充值。";
        if (m.Contains("429") || m.Contains("rate")) return "问得太快啦，休息一会儿再试。";
        if (m.Contains("tool") && (m.Contains("support") || m.Contains("invalid"))) return "这个 AI 模型不支持让小猪动手，换 DeepSeek 试试。";
        return "引擎出错了：" + (message.Length > 200 ? message[..200] + "…" : message);
    }

    /// <summary>马上关掉引擎（小猪退出时用）。</summary>
    public void Kill()
    {
        _disposed = true;
        try { if (_proc is { HasExited: false }) _proc.Kill(entireProcessTree: true); } catch { }
    }

    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        try
        {
            if (_proc is { HasExited: false })
            {
                try { _proc.StandardInput.Close(); } catch { }
                await Task.WhenAny(_proc.WaitForExitAsync(), Task.Delay(2000));
                if (!_proc.HasExited) _proc.Kill(entireProcessTree: true);
            }
        }
        catch { }
        _proc?.Dispose();
        _proc = null;
        _sessionId = null;
    }
}
