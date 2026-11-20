using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace PiggyHelper.Services.Harness;

/// <summary>
/// CI 用的自检：用一个假的 AI 接口（harness/selftest/fake_llm.py）把高级模式完整走一遍。
/// 高级模式按 Harness 原本的方式运行：普通命令直接执行，引擎自己的沙盒挡住工作文件夹外面的改动，
/// AI 申请更大权限时引擎会问用户（同意才做、拒绝就不做）。
/// </summary>
public static class HarnessSelfTest
{
    public static async Task<int> RunAsync(string reportPath, string baseUrl = "http://127.0.0.1:18080/v1")
    {
        var log = new StringBuilder();
        void L(string s) { log.AppendLine(s); Console.WriteLine(s); }
        bool pass = true;
        void Check(bool ok, string what) { L((ok ? "PASS " : "FAIL ") + what); if (!ok) pass = false; }

        AcpClient? client = null;
        try
        {
            var dir = HarnessEngine.FindEngine();
            if (dir == null) { L("FAIL engine not found"); Write(reportPath, log); return 2; }
            L("engine: " + dir);
            L("engine-info: " + (HarnessEngine.EngineInfo(dir) ?? "(none)").Replace("\n", " | "));
            HarnessEngine.PrepareHome(baseUrl, "fake-model", 65536, 4096);

            var decisions = new ConcurrentQueue<string>();
            var calls = new ConcurrentDictionary<string, HarnessToolCall>();
            string? fatal = null;
            var text = new StringBuilder();
            bool reject = false;

            client = new AcpClient();
            client.PermissionHandler = (call, _) =>
            {
                var card = ActionDescriber.Describe(call.ToolName, call.Input);
                bool allow = !reject;
                decisions.Enqueue($"{call.ToolName}|{card.Risk.Risk}|{(allow ? "allow" : "deny")}|asks={call.Asks}|{card.Title}|{card.Raw.Replace("\n", " ")}");
                return Task.FromResult(allow);
            };
            client.ToolCallChanged += c => calls[c.Id] = c;
            client.Fatal += m => fatal = m;
            client.TextChunk += t => { lock (text) text.Append(t); };

            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(6));
            var sw = Stopwatch.StartNew();
            await client.StartAsync(dir, "sk-test", cts.Token);
            L($"started in {sw.Elapsed.TotalSeconds:F1}s, workspace: {HarnessEngine.WorkspaceDir}, temp: {HarnessEngine.TempDir ?? "(default)"}");

            async Task<(string Stop, List<string> Decisions, List<HarnessToolCall> Calls, string Text)> Round(string prompt)
            {
                while (decisions.TryDequeue(out _)) { }
                calls.Clear();
                lock (text) text.Clear();
                var t = Stopwatch.StartNew();
                var stop = await client!.PromptAsync(prompt, cts.Token);
                await Task.Delay(300); // 等最后的通知
                var d = decisions.ToList();
                var c = calls.Values.ToList();
                string said; lock (text) said = text.ToString();
                L($"--- prompt '{prompt}' -> stop={stop} in {t.Elapsed.TotalSeconds:F1}s");
                foreach (var x in d) L("  decision: " + x);
                foreach (var x in c) L($"  call {x.ToolName} status={x.Status} decided={x.Decided} allowed={x.Allowed} output={Trim(x.Output)}");
                L("  text: " + Trim(said));
                return (stop, d, c, said);
            }

            // 1. 普通命令：直接执行（小猪不额外询问），能看到输出
            var r1 = await Round("Please say hello");
            Check(r1.Decisions.Count == 0, "normal command ran without an extra prompt");
            Check(r1.Calls.Any(c => c.Status == "completed" && c.Output.Contains("hello-from-harness")), "normal command ran and returned output");
            Check(r1.Text.Contains("All done"), "assistant replied after the tool");

            // 2. 删系统文件：小猪不拦，但引擎自己的沙盒应该挡住
            await client.NewSessionAsync(cts.Token);
            var hosts = OperatingSystem.IsWindows() ? @"C:\Windows\System32\drivers\etc\hosts" : "/etc/hosts";
            bool hostsBefore = File.Exists(hosts);
            var r2 = await Round("PIGGY_DANGER delete the hosts file");
            Check(r2.Decisions.Count == 0, "Piggy did not add its own block");
            Check(File.Exists(hosts) == hostsBefore, "engine sandbox kept the hosts file");

            var probe = OperatingSystem.IsWindows() ? @"C:\piggy-sandbox-probe.txt" : "/opt/piggy-sandbox-probe.txt";
            try { File.Delete(probe); } catch { }

            // 3. 往工作文件夹外面写：沙盒挡住
            await client.NewSessionAsync(cts.Token);
            var r3 = await Round("PIGGY_SANDBOX write outside the workspace");
            Check(!File.Exists(probe), "engine sandbox blocked writing outside the workspace");
            try { File.Delete(probe); } catch { }

            // 4. AI 申请更大权限，用户点"不同意"：引擎问了，没有执行
            await client.NewSessionAsync(cts.Token);
            reject = true;
            var r4 = await Round("PIGGY_ESCALATE write outside the workspace");
            reject = false;
            Check(r4.Decisions.Count >= 1 && r4.Decisions.All(x => x.Contains("|deny|")), "engine asked before escalating; the user said no");
            Check(!File.Exists(probe) && r4.Calls.All(c => c.Status != "completed"), "rejected escalation did not run");

            // 5. 同样的申请，用户点"同意"：执行
            await client.NewSessionAsync(cts.Token);
            var r5 = await Round("PIGGY_ESCALATE write outside the workspace");
            Check(r5.Decisions.Count >= 1 && r5.Decisions.All(x => x.Contains("|allow|")), "engine asked before escalating; the user said yes");
            Check(r5.Calls.Any(c => c.Status == "completed" && c.Output.Contains("wrote-outside-workspace")), "approved escalation ran");
            L($"INFO escalated write landed = {File.Exists(probe)}");
            try { File.Delete(probe); } catch { }

            Check(fatal == null, "no fatal engine error" + (fatal == null ? "" : ": " + fatal));
        }
        catch (Exception ex)
        {
            L("FAIL exception: " + ex);
            pass = false;
        }
        finally
        {
            if (client != null)
            {
                L("stderr tail:\n" + client.StderrTail);
                await client.DisposeAsync();
            }
        }
        L(pass ? "RESULT: PASS" : "RESULT: FAIL");
        Write(reportPath, log);
        return pass ? 0 : 1;
    }

    private static string Trim(string s)
    {
        s = s.Replace("\r", "").Replace("\n", " ⏎ ");
        return s.Length > 300 ? s[..300] + "…" : s;
    }

    private static void Write(string path, StringBuilder log)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.WriteAllText(path, log.ToString(), new UTF8Encoding(false));
        }
        catch { }
    }
}
