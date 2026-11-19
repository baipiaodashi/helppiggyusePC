using System.Diagnostics;

namespace PiggyHelper.Services;

public static class Launcher
{
    /// <summary>打开一个系统设置 / 工具 / 网站。成功返回 true。</summary>
    public static bool Open(NavItem item, out string? error)
    {
        error = null;
        try
        {
            var psi = new ProcessStartInfo(item.Target) { UseShellExecute = true };
            if (!string.IsNullOrEmpty(item.Args)) psi.Arguments = item.Args;
            Process.Start(psi);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }
}
