using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Text;

namespace PiggyHelper.Services.Harness;

/// <summary>
/// 高级模式用的 DeepSeek Harness 引擎（Node.js + Harness）。
/// 引擎很大，不打进主程序：第一次开启高级模式时，从 GitHub Release 下载 PiggyHarness-win-x64.zip。
/// 也支持把解压好的 harness 文件夹放在 PiggyHelper.exe 旁边（离线用）。
/// </summary>
public static class HarnessEngine
{
    public const string AssetName = "PiggyHarness-win-x64.zip";

    private static string Version =>
        (Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly()).GetName().Version?.ToString(3) ?? "1.0.0";
    private const string Repo = "baipiaodashi/helppiggyusePC";

    private static string AppData => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PiggyHelper");

    /// <summary>下载后解压到这里。</summary>
    public static string InstallDir => Path.Combine(AppData, "harness");

    /// <summary>Harness 自己的数据目录（DSH_HOME）：配置、会话记录、闸门插件。</summary>
    public static string HomeDir => Path.Combine(AppData, "dsh-home");

    /// <summary>
    /// AI 干活用的文件夹：用户自己的文件夹（桌面、文档、下载都在里面）。
    /// 引擎的沙盒只允许改这里面的东西，Windows 系统文件夹和别的盘改不了；每一步还要用户点头。
    /// </summary>
    public static string WorkspaceDir => Dirs.Value.Workspace;

    /// <summary>
    /// 给引擎用的临时文件夹。Windows 上的沙盒要求临时文件夹不能在工作文件夹里面，
    /// 而系统默认的 Temp 在用户文件夹里，所以换到"公用"文件夹下。为 null 时用系统默认的。
    /// </summary>
    public static string? TempDir => Dirs.Value.Temp;

    private static readonly Lazy<(string Workspace, string? Temp)> Dirs = new(() =>
    {
        var fallback = Path.Combine(AppData, "workspace");
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(home)) return (fallback, null);
        if (!OperatingSystem.IsWindows()) return (home, null);
        try
        {
            var pub = Environment.GetEnvironmentVariable("PUBLIC");
            if (string.IsNullOrEmpty(pub)) pub = Path.Combine(Path.GetDirectoryName(home.TrimEnd('\\')) ?? @"C:\Users", "Public");
            var tmp = Path.Combine(pub, "PiggyHelper", "temp-" + Environment.UserName);
            Directory.CreateDirectory(tmp);
            var full = Path.GetFullPath(tmp);
            if (full.StartsWith(Path.GetFullPath(home).TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)) return (fallback, null);
            return (home, full);
        }
        catch
        {
            // 公用文件夹用不了：退回小猪自己的工作文件夹（改桌面上的文件时，引擎会再申请一次权限）
            return (fallback, null);
        }
    });

    /// <summary>找到可用的引擎目录（里面有 node.exe 和 node_modules）。</summary>
    public static string? FindEngine()
    {
        var candidates = new List<string>();
        var exeDir = AppContext.BaseDirectory;
        if (!string.IsNullOrEmpty(Environment.ProcessPath)) exeDir = Path.GetDirectoryName(Environment.ProcessPath) ?? exeDir;
        candidates.Add(Path.Combine(exeDir, "harness"));
        candidates.Add(InstallDir);
        var env = Environment.GetEnvironmentVariable("PIGGY_HARNESS_DIR");
        if (!string.IsNullOrWhiteSpace(env)) candidates.Insert(0, env);
        return candidates.FirstOrDefault(IsValid);
    }

    public static bool IsInstalled => FindEngine() != null;

    private static bool IsValid(string dir) =>
        File.Exists(NodeExe(dir)) && File.Exists(DshBin(dir));

    public static string NodeExe(string dir) => Path.Combine(dir, OperatingSystem.IsWindows() ? "node.exe" : "node");

    public static string DshBin(string dir) =>
        Path.Combine(dir, "node_modules", "@deepseek-ai", "dsh", "lib", "bin.js");

    /// <summary>引擎的版本说明（engine-info.txt，由打包脚本写入）。</summary>
    public static string? EngineInfo(string dir)
    {
        try
        {
            var p = Path.Combine(dir, "engine-info.txt");
            return File.Exists(p) ? File.ReadAllText(p).Trim() : null;
        }
        catch { return null; }
    }

    /// <summary>下载并解压引擎。progress 回调 (已下载字节, 总字节或 -1, 当前阶段文字)。</summary>
    public static async Task InstallAsync(Action<long, long, string> progress, CancellationToken ct)
    {
        Directory.CreateDirectory(AppData);
        var zip = Path.Combine(AppData, "harness-download.zip");
        var tmp = Path.Combine(AppData, "harness-new");

        var urls = new[]
        {
            $"https://github.com/{Repo}/releases/download/v{Version}/{AssetName}",
            $"https://github.com/{Repo}/releases/latest/download/{AssetName}",
        };
        using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("PiggyHelper/" + Version);
        Exception? last = null;
        foreach (var url in urls)
        {
            try
            {
                progress(0, -1, "正在连接下载服务器……");
                using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
                if (!resp.IsSuccessStatusCode) { last = new IOException($"下载失败（{(int)resp.StatusCode}）"); continue; }
                long total = resp.Content.Headers.ContentLength ?? -1;
                await using (var src = await resp.Content.ReadAsStreamAsync(ct))
                await using (var dst = File.Create(zip))
                {
                    var buf = new byte[1 << 20];
                    long done = 0;
                    var lastReport = DateTime.MinValue;
                    int n;
                    while ((n = await src.ReadAsync(buf, ct)) > 0)
                    {
                        await dst.WriteAsync(buf.AsMemory(0, n), ct);
                        done += n;
                        if ((DateTime.Now - lastReport).TotalMilliseconds > 200)
                        {
                            lastReport = DateTime.Now;
                            progress(done, total, "正在下载引擎……");
                        }
                    }
                }
                last = null;
                break;
            }
            catch (OperationCanceledException) { TryDelete(zip); throw; }
            catch (Exception ex) { last = ex; }
        }
        if (last != null)
        {
            TryDelete(zip);
            throw new IOException("引擎下载失败，检查一下网络再试试。（" + last.Message + "）", last);
        }

        progress(0, -1, "正在解压，文件很多，需要几分钟……");
        await Task.Run(() =>
        {
            if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
            ZipFile.ExtractToDirectory(zip, tmp);
            // 压缩包里可能多包了一层文件夹
            var root = IsValid(tmp) ? tmp : Directory.GetDirectories(tmp).FirstOrDefault(IsValid);
            if (root == null) throw new IOException("下载的引擎文件不完整，请重试。");
            if (Directory.Exists(InstallDir)) Directory.Delete(InstallDir, true);
            Directory.Move(root, InstallDir);
            if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
        }, ct);
        TryDelete(zip);
        progress(1, 1, "装好啦！");
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    // ---------- 把小猪的 AI 设置换算成引擎的设置 ----------

    /// <summary>引擎要用的接口地址（去掉 /chat/completions）、模型名和输出长度。</summary>
    public static (string BaseUrl, string Model, int ContextWindow, int MaxTokens) ProviderConfig(AiProvider p)
    {
        var url = p.Url.Trim();
        int cut = url.IndexOf("/chat/completions", StringComparison.OrdinalIgnoreCase);
        if (cut >= 0) url = url[..cut];
        url = url.TrimEnd('/');
        var model = p.Model.Trim();
        // Kimi 的 8k 模型装不下引擎的工具说明，高级模式换成同一家的 32k 版本
        if (p.Id == "kimi" && model.EndsWith("-8k", StringComparison.OrdinalIgnoreCase)) model = model[..^3] + "-32k";
        var m = model.ToLowerInvariant();
        int ctx = m.Contains("8k") ? 8192 : m.Contains("32k") ? 32768 : m.Contains("128k") ? 131072 :
                  m.Contains("deepseek") || m.Contains("qwen") || m.Contains("kimi") || m.Contains("glm") ? 65536 : 32768;
        int max = Math.Min(8192, ctx / 4);
        return (url, model, ctx, max);
    }

    // ---------- DSH_HOME：配置和闸门插件 ----------

    /// <summary>
    /// 每次启动前重写一遍配置和闸门插件（防止被改动）。
    /// provider 用 OpenAI 兼容接口；API Key 不写进文件，只通过环境变量传给引擎。
    /// </summary>
    public static void PrepareHome(string baseUrl, string model, int contextWindow = 65536, int maxTokens = 8192)
    {
        Directory.CreateDirectory(HomeDir);
        Directory.CreateDirectory(WorkspaceDir);
        var gateDir = Path.Combine(HomeDir, "node_modules", "piggy-gate");
        Directory.CreateDirectory(gateDir);
        File.WriteAllText(Path.Combine(gateDir, "package.json"),
            "{ \"name\": \"piggy-gate\", \"version\": \"1.0.0\", \"type\": \"module\", \"main\": \"index.js\" }\n");
        File.WriteAllText(Path.Combine(gateDir, "index.js"), GateSource(), new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(HomeDir, "cordis.patch.yml"), PatchYaml(baseUrl, model, contextWindow, maxTokens), new UTF8Encoding(false));
    }

    public static string GateMarkerPath => Path.Combine(HomeDir, "piggy-gate.ready");

    private static string GateSource()
    {
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("piggy-gate.js")
                      ?? throw new InvalidOperationException("缺少闸门插件");
        using var r = new StreamReader(s, Encoding.UTF8);
        return r.ReadToEnd();
    }

    private static string Q(string s) => "'" + s.Replace("'", "''") + "'";

    public static string PatchYaml(string baseUrl, string model, int contextWindow, int maxTokens)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# 小猪电脑助手自动生成，每次启动都会重写。");
        sb.AppendLine("- id: llm-pi-ai");
        sb.AppendLine("  config:");
        sb.AppendLine("    providers:");
        sb.AppendLine("      piggy:");
        sb.AppendLine("        displayName: Piggy");
        sb.AppendLine("        apiKeyEnv: PIGGY_AI_KEY");
        sb.AppendLine("        api: openai-completions");
        sb.AppendLine($"        baseURL: {Q(baseUrl)}");
        sb.AppendLine("        models:");
        sb.AppendLine($"          - id: {Q(model)}");
        sb.AppendLine($"            name: {Q(model)}");
        sb.AppendLine($"            contextWindow: {contextWindow}");
        sb.AppendLine($"            maxTokens: {maxTokens}");
        foreach (var id in new[] { "agent-default-model", "acp" })
        {
            sb.AppendLine($"- id: {id}");
            sb.AppendLine("  config:");
            sb.AppendLine("    provider: piggy");
            sb.AppendLine($"    model: {Q(model)}");
        }
        sb.AppendLine("- id: system-prompt");
        sb.AppendLine("  config:");
        sb.AppendLine($"    personaPrefix: {Q(Persona)}");
        // 关掉：遥测上报、远程访问、账号登录、子代理、后台任务、技能、计划模式等用不到的部分
        foreach (var id in new[]
        {
            "session-telemetry-otel", "otel", "remote-access", "typert-gateway", "deepseek-account", "llm-deepseek-account",
            "web-search-deepseek", "web-deepseek-api", "subagent", "agent-team", "tool-agent-team", "subagent-spawn-in-process",
            "subagent-fork-in-process", "tool-subagent-control", "tool-subagent", "tool-subagent-fork", "ptc-runtime", "workflow-ptc",
            "tool-workflow", "goal", "goal-round-driver", "command-goal", "tool-goal", "jobs", "tool-jobs", "skill",
            "skill-filesystem", "tool-skill", "plan-mode",
        })
        {
            sb.AppendLine($"- id: {id}");
            sb.AppendLine("  disabled: true");
        }
        // 自动测试用：指定用哪个 PowerShell（比如 Windows 自带的 5.1 版）
        var pwsh = Environment.GetEnvironmentVariable("PIGGY_PWSH_PATH");
        if (!string.IsNullOrWhiteSpace(pwsh))
        {
            sb.AppendLine("- id: pwsh-sandbox");
            sb.AppendLine("  config:");
            sb.AppendLine($"    pwshPath: {Q(pwsh)}");
        }
        sb.AppendLine("- insert:");
        sb.AppendLine("    - id: piggy-gate");
        sb.AppendLine("      name: 'piggy-gate'");
        return sb.ToString();
    }

    private const string Persona =
        "You are Piggy (小猪), a patient and friendly computer helper inside the Piggy Helper app, powered by the {{model}} model. " +
        "The user is a student who is new to computers, using Windows 10 or 11. Always reply in simple, warm Chinese, short and step by step. " +
        "You can use tools to look at and operate this computer, but EVERY tool call is shown to the user as a card and runs only after the user clicks allow. " +
        "Before each tool call, say in one plain sentence what you are about to do and why. Prefer read-only commands; do one small step at a time. " +
        "When you run a PowerShell command, always fill the description with a short plain Chinese explanation. " +
        "Never try to: format or partition disks, delete or modify system files (C:\\Windows, Program Files), edit the registry, disable antivirus or the firewall, " +
        "download and run programs, add users, change permissions, or run anything as administrator. Piggy blocks these automatically. " +
        "If the user refuses an action, do not retry it or look for a workaround; explain what they can do themselves instead. " +
        "Commands must finish within 60 seconds and must not wait for input. " +
        "Your working folder is the user's own home folder (Desktop, Documents, Downloads, Pictures are inside it); you can only change files there. " +
        "When deleting the user's files, move them to the Recycle Bin instead of deleting permanently.";
}
