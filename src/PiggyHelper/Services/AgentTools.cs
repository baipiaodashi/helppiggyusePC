using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Win32;

namespace PiggyHelper.Services;

/// <summary>安全助手模式下，AI 可以用的"白名单"工具。</summary>
public sealed record AgentTool(string Name, string Label, bool NeedsConfirm, JsonObject Schema, string Description);

/// <summary>一次工具调用的结果：给 AI 看的内容，以及给用户看的一句话。</summary>
public sealed record AgentToolResult(string ForAi, string ForUser, bool Ok = true);

public static class AgentTools
{
    /// <summary>小猪自己的页面（open_xiaozhu_page 可以打开）。</summary>
    public static readonly IReadOnlyDictionary<string, (string Page, string Title)> PiggyPages = new Dictionary<string, (string, string)>
    {
        ["cleanup"] = ("cleanup", "一键清理"),
        ["big_files"] = ("cleanup:big", "找大文件"),
        ["c_drive_map"] = ("cleanup:map", "C 盘里有什么"),
        ["shortcuts"] = ("shortcuts", "快捷键指南"),
        ["navigator"] = ("navigator", "功能导航"),
        ["settings"] = ("settings", "小猪的设置"),
    };

    /// <summary>可以打开的系统设置和电脑自带工具。</summary>
    public static IEnumerable<NavItem> OpenableSettings =>
        NavCatalog.Items.Where(i => i.Category is NavCatalog.Settings or NavCatalog.Tools);

    private static JsonObject NoArgs() => new() { ["type"] = "object", ["properties"] = new JsonObject() };

    private static JsonObject EnumArg(string name, string desc, IEnumerable<string> values) => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            [name] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = desc,
                ["enum"] = new JsonArray(values.Select(v => (JsonNode)JsonValue.Create(v)!).ToArray()),
            },
        },
        ["required"] = new JsonArray(JsonValue.Create(name)),
    };

    public static IReadOnlyList<AgentTool> All { get; } = new List<AgentTool>
    {
        new("get_pc_info", "看了看电脑配置", false, NoArgs(),
            "Read basic information about this PC: Windows version, CPU, memory (RAM) usage, graphics card, how long since the last restart, battery. Read-only."),
        new("get_disk_usage", "看了看硬盘还剩多少空间", false, NoArgs(),
            "List the drives (C:, D: …) with total size and free space. Read-only."),
        new("scan_junk", "扫描了一下垃圾文件", false, NoArgs(),
            "Measure how much space each kind of junk/cache takes (temp files, browser caches, WeChat/QQ caches, recycle bin…). Read-only, deletes nothing. To actually clean, open the 'cleanup' page for the user."),
        new("check_network", "检查了一下网络", false, NoArgs(),
            "Check the network: whether a network adapter is connected (Wi-Fi or cable), whether DNS works and whether common websites can be reached. Read-only."),
        new("get_running_programs", "看了看正在运行的软件", false, NoArgs(),
            "List open windows and the programs using the most memory. Read-only."),
        new("open_setting", "打开设置", true,
            EnumArg("name", "The exact name of the Windows settings page or built-in tool to open.", NavCatalog.Items.Where(i => i.Category is NavCatalog.Settings or NavCatalog.Tools).Select(i => i.Title)),
            "Open a Windows settings page or built-in tool (for example sound, display, Wi-Fi, Task Manager) so the user can change something themselves. The user is asked to confirm first."),
        new("open_xiaozhu_page", "打开小猪的功能", true,
            EnumArg("page", "cleanup = junk cleaner, big_files = find big files, c_drive_map = explain what is on drive C, shortcuts = keyboard shortcut guide, navigator = feature navigator, settings = Piggy settings.", new[] { "cleanup", "big_files", "c_drive_map", "shortcuts", "navigator", "settings" }),
            "Open one of Piggy Helper's own pages. The user is asked to confirm first."),
    };

    public static AgentTool? Find(string name) => All.FirstOrDefault(t => t.Name == name);

    public static JsonArray Definitions()
    {
        var arr = new JsonArray();
        foreach (var t in All)
            arr.Add(new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = t.Name,
                    ["description"] = t.Description,
                    ["parameters"] = t.Schema.DeepClone(),
                },
            });
        return arr;
    }

    public static string SystemPromptAddon() =>
@"

你现在是""安全助手""模式，可以使用工具：
- 查看类工具（看配置、看硬盘、扫垃圾、查网络、看正在运行的软件）会直接执行，只读不改。遇到需要了解这台电脑情况的问题，先用工具看一看，再根据真实情况回答，不要让用户自己去查。
- open_setting 和 open_xiaozhu_page 会先弹出卡片问用户同不同意。调用之前先用一句话说明要打开什么、为什么。
- 你不能删除文件、不能运行命令、不能改系统设置。需要改的地方，就帮用户打开对应的设置页面，再一步步告诉他点哪里。
- 用户不同意的话，不要再调用同一个工具，直接告诉他可以自己怎么做。
- 工具返回的是真实数据，用大白话总结给用户，不要原样贴出来。";

    /// <summary>把 AI 传来的参数取出来（参数是 JSON 字符串）。</summary>
    public static string? ArgOf(string arguments, string name)
    {
        try
        {
            if (JsonNode.Parse(arguments) is JsonObject o && o[name] is JsonValue v && v.TryGetValue<string>(out var s)) return s.Trim();
        }
        catch { }
        return null;
    }

    /// <summary>确认卡片上的大白话说明。</summary>
    public static (string Title, string Detail) DescribeConfirm(ToolCall call)
    {
        switch (call.Name)
        {
            case "open_setting":
            {
                var n = ArgOf(call.Arguments, "name") ?? "";
                var item = OpenableSettings.FirstOrDefault(i => i.Title == n);
                return ($"打开“{n}”", item?.Tip ?? "打开一个系统设置页面，打开后你自己来改，小猪不会替你改。");
            }
            case "open_xiaozhu_page":
            {
                var p = ArgOf(call.Arguments, "page") ?? "";
                var title = PiggyPages.TryGetValue(p, out var pg) ? pg.Title : p;
                return ($"打开小猪的“{title}”", "会切换到这个页面，聊天记录还在，随时可以回来。");
            }
            default:
                return ("使用工具：" + call.Name, "");
        }
    }

    // ---------- 执行 ----------

    /// <summary>执行一个只读工具（在后台线程里运行）。</summary>
    public static Task<AgentToolResult> RunReadOnlyAsync(string name, CancellationToken ct) => name switch
    {
        "get_pc_info" => Task.Run(PcInfo, ct),
        "get_disk_usage" => Task.Run(DiskUsage, ct),
        "scan_junk" => Task.Run(() => ScanJunk(ct), ct),
        "check_network" => CheckNetworkAsync(ct),
        "get_running_programs" => Task.Run(RunningPrograms, ct),
        _ => Task.FromResult(new AgentToolResult($"Error: unknown tool '{name}'.", "小猪不认识这个工具", false)),
    };

    private static AgentToolResult PcInfo()
    {
        var sb = new StringBuilder();
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            var product = k?.GetValue("ProductName") as string ?? "Windows";
            var build = k?.GetValue("CurrentBuildNumber") as string ?? Environment.OSVersion.Version.Build.ToString();
            var display = k?.GetValue("DisplayVersion") as string ?? k?.GetValue("ReleaseId") as string;
            if (int.TryParse(build, out var b) && b >= 22000) product = product.Replace("Windows 10", "Windows 11");
            sb.AppendLine($"Windows: {product} {display} (build {build}), {(Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit")}");
        }
        catch { sb.AppendLine("Windows: " + Environment.OSVersion.VersionString); }
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            sb.AppendLine($"CPU: {(k?.GetValue("ProcessorNameString") as string)?.Trim()} ({Environment.ProcessorCount} threads)");
        }
        catch { }
        var mem = new MemoryStatusEx();
        if (GlobalMemoryStatusEx(mem))
            sb.AppendLine($"Memory (RAM): {Format.Bytes((long)mem.ullTotalPhys)} total, {Format.Bytes((long)mem.ullAvailPhys)} free, {mem.dwMemoryLoad}% in use");
        try
        {
            using var cls = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
            var gpus = new List<string>();
            foreach (var sub in cls?.GetSubKeyNames() ?? Array.Empty<string>())
            {
                if (sub.Length != 4) continue;
                try
                {
                    using var g = cls!.OpenSubKey(sub);
                    if (g?.GetValue("DriverDesc") is string d && !gpus.Contains(d) && !d.Contains("Basic Display") && !d.Contains("Remote")) gpus.Add(d);
                }
                catch { }
            }
            if (gpus.Count > 0) sb.AppendLine("Graphics: " + string.Join("; ", gpus));
        }
        catch { }
        var up = TimeSpan.FromMilliseconds(Environment.TickCount64);
        sb.AppendLine($"Time since last restart: {(int)up.TotalDays} days {up.Hours} hours");
        if (GetSystemPowerStatus(out var ps) && ps.BatteryFlag != 128 && ps.BatteryFlag != 255)
            sb.AppendLine($"Battery: {(ps.BatteryLifePercent <= 100 ? ps.BatteryLifePercent + "%" : "unknown")}, {(ps.ACLineStatus == 1 ? "plugged in" : "on battery")}");
        else sb.AppendLine("Battery: none (desktop PC)");
        return new AgentToolResult(sb.ToString().TrimEnd(), "小猪看了看电脑配置");
    }

    private static AgentToolResult DiskUsage()
    {
        var sb = new StringBuilder();
        var sys = DiskMapService.SystemDrive;
        foreach (var d in DriveInfo.GetDrives())
        {
            try
            {
                if (!d.IsReady || d.DriveType is not (DriveType.Fixed or DriveType.Removable)) continue;
                var free = d.AvailableFreeSpace;
                var used = d.TotalSize - d.TotalFreeSpace;
                var pct = d.TotalSize > 0 ? (int)Math.Round(100.0 * used / d.TotalSize) : 0;
                sb.AppendLine($"{d.Name.TrimEnd('\\')} {(string.IsNullOrWhiteSpace(d.VolumeLabel) ? "" : "\"" + d.VolumeLabel + "\" ")}" +
                              $"{(d.DriveType == DriveType.Removable ? "(USB / removable) " : "")}{(string.Equals(d.Name, sys, StringComparison.OrdinalIgnoreCase) ? "(system drive) " : "")}" +
                              $"total {Format.Bytes(d.TotalSize)}, free {Format.Bytes(free)}, {pct}% used");
            }
            catch { }
        }
        return new AgentToolResult(sb.Length == 0 ? "No drives found." : sb.ToString().TrimEnd(), "小猪看了看硬盘空间");
    }

    private static AgentToolResult ScanJunk(CancellationToken ct)
    {
        var items = CleanupService.CreateItems();
        bool admin = CleanupService.IsAdmin();
        var sizes = new long[items.Count];
        Parallel.For(0, items.Count, new ParallelOptions { CancellationToken = ct, MaxDegreeOfParallelism = 4 }, i =>
        {
            try { sizes[i] = items[i].IsRecycleBin ? RecycleBin.QuerySize() : CleanupService.Measure(items[i], ct); }
            catch { sizes[i] = -1; }
        });
        var sb = new StringBuilder();
        long total = 0;
        for (int i = 0; i < items.Count; i++)
        {
            var it = items[i];
            if (sizes[i] > 0) total += sizes[i];
            var note = it.NeedsAdmin && !admin ? " (needs administrator, Piggy can't clean it now)" : "";
            sb.AppendLine($"- {it.Title}: {(sizes[i] < 0 ? "unknown" : Format.Bytes(Math.Max(0, sizes[i])))}{note}");
        }
        sb.Insert(0, $"Junk found, about {Format.Bytes(total)} in total. Nothing was deleted. The user can clean it on Piggy's 'cleanup' page.\n");
        return new AgentToolResult(sb.ToString().TrimEnd(), $"小猪扫描了垃圾文件（大约 {Format.Bytes(total)}）");
    }

    private static async Task<AgentToolResult> CheckNetworkAsync(CancellationToken ct)
    {
        var sb = new StringBuilder();
        bool any = false;
        try
        {
            foreach (var n in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (n.OperationalStatus != OperationalStatus.Up) continue;
                if (n.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
                if (n.Description.Contains("Virtual", StringComparison.OrdinalIgnoreCase) || n.Description.Contains("VMware", StringComparison.OrdinalIgnoreCase)) continue;
                var kind = n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ? "Wi-Fi" :
                           n.NetworkInterfaceType == NetworkInterfaceType.Ethernet ? "cable (Ethernet)" : n.NetworkInterfaceType.ToString();
                bool gw = n.GetIPProperties().GatewayAddresses.Any(g => !g.Address.Equals(System.Net.IPAddress.Any));
                sb.AppendLine($"Adapter connected: {kind} \"{n.Name}\"{(gw ? "" : " (no gateway, may not reach the internet)")}");
                any = true;
            }
        }
        catch { }
        if (!any) sb.AppendLine("No network adapter is connected (Wi-Fi off or cable unplugged?).");

        bool dns;
        try
        {
            using var dt = CancellationTokenSource.CreateLinkedTokenSource(ct);
            dt.CancelAfter(4000);
            dns = (await System.Net.Dns.GetHostAddressesAsync("www.baidu.com", dt.Token)).Length > 0;
        }
        catch { dns = false; }
        sb.AppendLine("DNS (turning website names into addresses): " + (dns ? "working" : "NOT working"));

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        foreach (var (name, url) in new[] { ("Baidu", "https://www.baidu.com"), ("Bing", "https://cn.bing.com") })
        {
            var sw = Stopwatch.StartNew();
            try
            {
                using var r = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
                sb.AppendLine($"{name}: reachable ({sw.ElapsedMilliseconds} ms)");
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch { sb.AppendLine($"{name}: NOT reachable"); }
        }
        var ok = sb.ToString().Contains("reachable (");
        return new AgentToolResult(sb.ToString().TrimEnd(), ok ? "小猪检查了网络（能上网）" : "小猪检查了网络（好像连不上）");
    }

    private static AgentToolResult RunningPrograms()
    {
        var sb = new StringBuilder();
        var procs = Process.GetProcesses();
        try
        {
            var windows = procs.Where(p => { try { return !string.IsNullOrWhiteSpace(p.MainWindowTitle); } catch { return false; } })
                .Select(p => p.ProcessName).Distinct().Take(20).ToList();
            sb.AppendLine("Programs with open windows: " + (windows.Count == 0 ? "(none)" : string.Join(", ", windows)));
            var top = procs.Select(p => { try { return (p.ProcessName, Mem: p.WorkingSet64); } catch { return (p.ProcessName, Mem: 0L); } })
                .GroupBy(x => x.ProcessName).Select(g => (Name: g.Key, Mem: g.Sum(x => x.Mem), Count: g.Count()))
                .OrderByDescending(x => x.Mem).Take(8);
            sb.AppendLine("Using the most memory:");
            foreach (var t in top) sb.AppendLine($"- {t.Name}{(t.Count > 1 ? $" ({t.Count} processes)" : "")}: {Format.Bytes(t.Mem)}");
        }
        finally { foreach (var p in procs) p.Dispose(); }
        return new AgentToolResult(sb.ToString().TrimEnd(), "小猪看了看正在运行的软件");
    }

    // ---------- Win32 ----------
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private sealed class MemoryStatusEx
    {
        public uint dwLength = (uint)Marshal.SizeOf<MemoryStatusEx>();
        public uint dwMemoryLoad;
        public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte ACLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag;
        public int BatteryLifeTime, BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MemoryStatusEx buffer);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemPowerStatus(out SystemPowerStatus status);
}
