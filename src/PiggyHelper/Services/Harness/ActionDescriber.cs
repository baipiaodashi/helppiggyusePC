using System.Text.Json;
using System.Text.Json.Nodes;

namespace PiggyHelper.Services.Harness;

/// <summary>给小白看的"这一步要做什么"：大白话标题、说明、风险，以及折叠起来的原始内容。</summary>
public sealed record ActionCard(string Title, string Detail, GuardResult Risk, string Raw);

/// <summary>把 Harness 的工具调用翻译成大白话，并用安全规则判断风险。</summary>
public static class ActionDescriber
{
    public static ActionCard Describe(string tool, JsonObject? input)
    {
        var card = DescribeCore(tool, input);
        // AI 申请"跳出保护范围"运行：至少要特别提醒
        if (input != null && input.TryGetPropertyValue("sandbox_permissions", out var sp) && sp is JsonValue spv &&
            spv.TryGetValue<string>(out var perm) && !string.IsNullOrWhiteSpace(perm) && perm != "use_default")
        {
            var just = input.TryGetPropertyValue("justification", out var j) && j is JsonValue jv2 && jv2.TryGetValue<string>(out var js) ? js : null;
            var risk = card.Risk.Risk == CommandRisk.Block ? card.Risk
                : new GuardResult(CommandRisk.Warn, "这一步要跳出引擎的保护范围（沙盒），用更大的权限运行" + (just == null ? "" : "（AI 说：" + just + "）"));
            card = card with { Risk = risk };
        }
        return card;
    }

    private static ActionCard DescribeCore(string tool, JsonObject? input)
    {
        string? S(params string[] names)
        {
            if (input == null) return null;
            foreach (var n in names)
                if (input.TryGetPropertyValue(n, out var v) && v is JsonValue jv && jv.TryGetValue<string>(out var s) && !string.IsNullOrWhiteSpace(s))
                    return s;
            return null;
        }
        var raw = input == null ? "" : input.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });

        switch (tool)
        {
            case "pwsh":
            case "bash":
            {
                var cmd = S("command", "script") ?? "";
                var why = S("description", "explanation");
                var risk = CommandGuard.Check(cmd);
                return new ActionCard("运行一条电脑命令", why ?? "AI 没有写这条命令是做什么的，看不懂的话就点“不同意”。", risk, cmd);
            }
            case "read":
            case "read_image":
            {
                var path = S("file_path", "path", "filePath") ?? "（没说）";
                return new ActionCard("查看一个文件", path, new GuardResult(CommandRisk.Ok, "只是看看，不会改动"), raw);
            }
            case "write":
            {
                var path = S("file_path", "path", "filePath");
                var risk = CommandGuard.CheckWritePath(path);
                return new ActionCard("新建或覆盖一个文件", path ?? "（没说）", risk, raw);
            }
            case "edit":
            {
                var path = S("file_path", "path", "filePath");
                var risk = CommandGuard.CheckWritePath(path);
                return new ActionCard("修改一个文件的内容", path ?? "（没说）", risk, raw);
            }
            case "glob":
            case "grep":
            {
                var what = S("pattern", "query", "glob") ?? "";
                var where = S("path", "cwd", "directory");
                return new ActionCard("在文件夹里找东西", where == null ? $"找：{what}" : $"在 {where} 里找：{what}",
                    new GuardResult(CommandRisk.Ok, "只是查找，不会改动"), raw);
            }
            case "web_fetch":
                return new ActionCard("打开一个网页读取内容", S("url") ?? "（没说）", new GuardResult(CommandRisk.Ok, "只读取网页文字"), raw);
            case "web_search":
                return new ActionCard("上网搜索", S("query", "q") ?? "", new GuardResult(CommandRisk.Ok, "只是搜索"), raw);
            case "working_directory":
                return new ActionCard("换一个工作文件夹", S("path", "directory") ?? "", new GuardResult(CommandRisk.Ok, "只是换个位置"), raw);
            case "todo_write":
                return new ActionCard("记一下待办步骤", "", new GuardResult(CommandRisk.Ok, "只是做个记录"), raw);
            case "subagent":
            case "subagent_fork":
                return new ActionCard("派一个小助手去做一部分事", S("description", "task", "prompt") ?? "", new GuardResult(CommandRisk.Ok, "小助手做的每一步也会显示出来"), raw);
            case "list_agents":
            case "send_message":
            case "wait_agent":
                return new ActionCard("和小助手沟通", S("message", "agent", "name") ?? "", new GuardResult(CommandRisk.Ok, "只是沟通"), raw);
            case "workflow":
                return new ActionCard("按步骤安排一组任务", S("description", "name") ?? "", new GuardResult(CommandRisk.Ok, "引擎自带的任务安排"), raw);
            case "create_goal":
            case "get_goal":
            case "update_goal":
                return new ActionCard("记一下要完成的目标", S("goal", "description", "title") ?? "", new GuardResult(CommandRisk.Ok, "只是做个记录"), raw);
            case "job_list":
            case "job_output":
            case "job_kill":
                return new ActionCard(tool == "job_kill" ? "停掉一个后台任务" : "看看后台任务", S("id", "job_id") ?? "", new GuardResult(CommandRisk.Ok, "后台任务"), raw);
            case "skill":
                return new ActionCard("使用一个技能", S("name", "skill") ?? "", new GuardResult(CommandRisk.Ok, "引擎自带的技能"), raw);
            default:
                return new ActionCard($"使用工具：{tool}", "", new GuardResult(CommandRisk.Ok, "引擎自带的工具"), raw);
        }
    }
}
