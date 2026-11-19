using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PiggyHelper.Services;

/// <summary>一家 AI 服务（都兼容 OpenAI 的接口格式）。</summary>
public sealed record AiProvider(string Id, string Name, string Url, string Model, string KeyUrl, string Intro);

public sealed record ChatMessage(string Role, string Content);

public static class AiService
{
    public static readonly IReadOnlyList<AiProvider> Providers = new List<AiProvider>
    {
        new("deepseek", "DeepSeek", "https://api.deepseek.com/chat/completions", "deepseek-chat",
            "https://platform.deepseek.com/api_keys", "国产 AI，回答质量高、价格便宜，推荐首选。"),
        new("qwen", "通义千问", "https://dashscope.aliyuncs.com/compatible-mode/v1/chat/completions", "qwen-plus",
            "https://bailian.console.aliyun.com/?apiKey=1", "阿里出品，新用户有免费额度。"),
        new("siliconflow", "硅基流动", "https://api.siliconflow.cn/v1/chat/completions", "deepseek-ai/DeepSeek-V3",
            "https://cloud.siliconflow.cn/account/ak", "一个 Key 能用很多模型，注册送体验金。"),
        new("kimi", "Kimi", "https://api.moonshot.cn/v1/chat/completions", "moonshot-v1-8k",
            "https://platform.moonshot.cn/console/api-keys", "月之暗面出品，擅长读长文章。"),
        new("custom", "自定义", "", "",
            "", "填入任意兼容 OpenAI 格式的接口地址和模型名。"),
    };

    public static AiProvider CurrentProvider
    {
        get
        {
            var s = SettingsService.Current;
            var p = Providers.FirstOrDefault(x => x.Id == s.AiProvider) ?? Providers[0];
            if (p.Id == "custom")
            {
                var url = (s.AiCustomUrl ?? "").Trim();
                if (url.Length > 0 && !url.Contains("/chat/completions")) url = url.TrimEnd('/') + "/chat/completions";
                p = p with { Url = url, Model = (s.AiCustomModel ?? "").Trim() };
            }
            return p;
        }
    }

    public static bool IsConfigured =>
        !string.IsNullOrEmpty(SettingsService.GetApiKey()) &&
        !string.IsNullOrEmpty(CurrentProvider.Url) && !string.IsNullOrEmpty(CurrentProvider.Model);

    private static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };

    public static string SystemPrompt()
    {
        var features = string.Join("、", NavCatalog.Items.Where(i => i.Category is NavCatalog.Settings or NavCatalog.Tools).Select(i => i.Title));
        return
$@"你是""小猪""，是""小猪电脑助手""软件里的 AI 小帮手，性格亲切、有耐心。你的用户是刚开始用电脑的学生，电脑是 Windows 10 或 Windows 11。

回答要求：
1. 用简单亲切的大白话，少用专业术语；必须用到时，顺便用一句话解释它是什么。
2. 有操作步骤时，用 1. 2. 3. 分步写，每步一句话，写清楚点哪里、看到什么。
3. 回答尽量简短，先给最可能有效的办法，不要一次列太多。
4. 涉及删除文件、修改系统设置、下载软件、输入密码或付款时，提醒注意安全（软件去官网下载，不要点""高速下载""，不要把验证码告诉别人）。
5. 如果小猪电脑助手里有能直接帮上忙的功能，在回答最后单独一行写 [[打开:功能名]]，最多写 2 个。功能名只能从下面这些里选：
{features}、一键清理、快捷键指南
6. 不确定的事情就说不确定，不要编造。";
    }

    /// <summary>从 AI 的回答里取出 [[打开:xxx]] 标记，返回去掉标记后的文字和功能名列表。</summary>
    public static (string Text, List<string> Actions) ExtractActions(string answer)
    {
        var actions = new List<string>();
        var text = Regex.Replace(answer, @"\[\[\s*打开\s*[:：]\s*(.+?)\s*\]\]", m =>
        {
            var name = m.Groups[1].Value.Trim();
            if (!actions.Contains(name)) actions.Add(name);
            return "";
        });
        return (text.TrimEnd(), actions);
    }

    /// <summary>以"流式"的方式提问：AI 每写出一点就回调一次 onDelta，像打字一样显示出来。</summary>
    public static async Task<string> AskAsync(IReadOnlyList<ChatMessage> history, Action<string> onDelta, CancellationToken ct,
        AiProvider? provider = null, string? apiKey = null)
    {
        provider ??= CurrentProvider;
        apiKey ??= SettingsService.GetApiKey();
        if (string.IsNullOrEmpty(apiKey)) throw new AiException("还没有填写 API Key，先去设置一下吧。");
        if (string.IsNullOrEmpty(provider.Url) || string.IsNullOrEmpty(provider.Model))
            throw new AiException("自定义的接口地址或模型名还没填。");

        var messages = new List<object> { new { role = "system", content = SystemPrompt() } };
        messages.AddRange(history.Select(m => (object)new { role = m.Role, content = m.Content }));
        var body = JsonSerializer.Serialize(new { model = provider.Model, messages, stream = true, temperature = 0.7 });

        using var req = new HttpRequestMessage(HttpMethod.Post, provider.Url)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(30)); // 30 秒内必须开始回答

        HttpResponseMessage resp;
        try
        {
            resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new AiException("等了 30 秒 AI 都没回应，可能是网络不好，稍后再试试。");
        }
        catch (HttpRequestException)
        {
            throw new AiException("连不上 AI 服务，检查一下电脑有没有联网。");
        }

        using (resp)
        {
            if (!resp.IsSuccessStatusCode)
            {
                string detail = "";
                try { detail = await resp.Content.ReadAsStringAsync(ct); } catch { }
                throw new AiException(FriendlyError(resp.StatusCode, detail));
            }
            timeout.CancelAfter(Timeout.InfiniteTimeSpan);

            var sb = new StringBuilder();
            using var stream = await resp.Content.ReadAsStreamAsync(ct);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            while (!reader.EndOfStream)
            {
                ct.ThrowIfCancellationRequested();
                var line = await reader.ReadLineAsync(ct);
                if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data:")) continue;
                var data = line[5..].Trim();
                if (data == "[DONE]") break;
                try
                {
                    using var doc = JsonDocument.Parse(data);
                    if (!doc.RootElement.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0) continue;
                    var first = choices[0];
                    if (first.TryGetProperty("delta", out var delta) &&
                        delta.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String)
                    {
                        var piece = content.GetString();
                        if (!string.IsNullOrEmpty(piece))
                        {
                            sb.Append(piece);
                            onDelta(sb.ToString());
                        }
                    }
                }
                catch (JsonException) { /* 偶尔有不完整的数据行，跳过 */ }
            }
            if (sb.Length == 0) throw new AiException("AI 没有返回内容，换个问法再试试。");
            return sb.ToString();
        }
    }

    private static string FriendlyError(HttpStatusCode code, string detail)
    {
        string msg = (int)code switch
        {
            401 or 403 => "API Key 不对或已经失效，去“设置”里检查一下有没有复制完整。",
            402 => "这个 AI 账户的余额不足了，需要去平台充值（或者换一家 AI 服务）。",
            404 => "接口地址或模型名称不对。",
            429 => "问得太快啦，或者免费额度用完了，休息一会儿再试。",
            >= 500 => "AI 服务那边出了点问题，稍后再试试。",
            _ => $"出错了（错误码 {(int)code}）。",
        };
        try
        {
            using var doc = JsonDocument.Parse(detail);
            if (doc.RootElement.TryGetProperty("error", out var err))
            {
                var m = err.ValueKind == JsonValueKind.Object && err.TryGetProperty("message", out var mm) ? mm.GetString() : err.ToString();
                if (!string.IsNullOrWhiteSpace(m)) msg += $"\n（服务返回：{m}）";
            }
        }
        catch { }
        return msg;
    }
}

public sealed class AiException : Exception
{
    public AiException(string message) : base(message) { }
}
