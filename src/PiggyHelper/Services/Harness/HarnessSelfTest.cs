using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace PiggyHelper.Services.Harness;

/// <summary>
/// CI 用的自检：用一个假的 AI 接口（harness/selftest/fake_llm.py）把高级模式完整走一遍，
/// 确认每一步都先问了小猪、同意了才执行、危险命令被拦下、拒绝了就不执行。
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
                bool allow = !reject && card.Risk.Risk != CommandRisk.Block && call.Asks <= 1;
                decisions.Enqueue($"{call.ToolName}|{card.Risk.Risk}|{(allow ? "allow" : "deny")}|asks={call.Asks}|{card.Title}|{card.Raw.Replace("\n", " ")}");
                return Task.FromResult(allow);
            };
            client.ToolCallChanged += c => calls[c.Id] = c;
            client.Fatal += m => fatal = m;
            client.TextChunk += t => { lock (text) text.Append(t); };

            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(6));
            var sw = Stopwatch.StartNew();
            await client.StartAsync(dir, "sk-test", cts.Token);
            L($"started in {sw.Elapsed.TotalSeconds:F1}s, gate marker present: {File.Exists(HarnessEngine.GateMarkerPath)}");

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

            // 1. 安全的命令：先问，同意后执行，能看到输出
            var r1 = await Round("Please say hello");
            Check(r1.Decisions.Count >= 1 && r1.Decisions.All(x => x.Contains("|allow|")), "safe command asked for permission and was allowed");
            Check(r1.Calls.Any(c => c.Status == "completed" && c.Output.Contains("hello-from-harness")), "safe command ran and returned output");
            Check(r1.Text.Contains("All done"), "assistant replied after the tool");

            // 2. 危险命令：小猪的规则拦下，不执行
            await client.NewSessionAsync(cts.Token);
            var hosts = OperatingSystem.IsWindows() ? @"C:\Windows\System32\drivers\etc\hosts" : "/etc/hosts";
            bool hostsBefore = File.Exists(hosts);
            var r2 = await Round("PIGGY_DANGER delete the hosts file");
            Check(r2.Decisions.Count >= 1 && r2.Decisions.All(x => x.Contains("|Block|deny|")), "dangerous command was blocked by Piggy's rules");
            Check(r2.Calls.All(c => c.Status != "completed"), "dangerous command did not run");
            Check(File.Exists(hosts) == hostsBefore, "hosts file untouched");

            // 3. 用户点"不同意"：不执行
            await client.NewSessionAsync(cts.Token);
            reject = true;
            var r3 = await Round("Please say hello again");
            reject = false;
            Check(r3.Decisions.Count >= 1 && r3.Decisions.All(x => x.Contains("|deny|")), "user rejection was sent to the engine");
            Check(r3.Calls.All(c => c.Status != "completed" && !c.Output.Contains("hello-from-harness")), "rejected command did not run");

            // 4. 沙盒：同意了一条往工作文件夹外面写文件的命令，看看引擎自己的保护有没有挡住（只记录，不算失败）
            await client.NewSessionAsync(cts.Token);
            var probe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "piggy-sandbox-probe.txt");
            try { File.Delete(probe); } catch { }
            var r4 = await Round("PIGGY_SANDBOX write outside the workspace");
            L($"INFO sandbox: probe file written outside workspace = {File.Exists(probe)}; asks = {string.Join(",", r4.Decisions.Select(x => x.Split('|')[3]))}");
            Check(r4.Decisions.Count >= 1, "sandbox probe asked for permission");
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
