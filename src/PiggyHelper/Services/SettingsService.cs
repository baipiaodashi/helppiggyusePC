using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace PiggyHelper.Services;

/// <summary>保存在 %APPDATA%\PiggyHelper\settings.json 里的设置。</summary>
public sealed class AppSettings
{
    public bool ShortcutPopup { get; set; } = true;
    public double HoldSeconds { get; set; } = 1.0;
    public bool PopupCtrl { get; set; } = true;
    public bool PopupAlt { get; set; } = true;
    public bool PopupWin { get; set; } = true;
    public bool CloseToTray { get; set; } = true;
    public bool TrayTipShown { get; set; }

    public string AiProvider { get; set; } = "deepseek";
    /// <summary>用 Windows 自带的加密（DPAPI）保存的 API Key，只有当前电脑当前用户能解开。</summary>
    public string? AiKeyProtected { get; set; }
    public string? AiCustomUrl { get; set; }
    public string? AiCustomModel { get; set; }

    /// <summary>AI 动手的方式："safe" = 安全助手（默认），"advanced" = 高级模式（DeepSeek Harness）。</summary>
    public string AgentMode { get; set; } = "safe";

    // ---------- 小猪剪贴板 ----------
    /// <summary>记录复制过的东西。</summary>
    public bool ClipboardEnabled { get; set; } = true;
    /// <summary>按 Win+V 打开小猪剪贴板（代替 Windows 自带的剪贴板历史）。</summary>
    public bool ClipboardWinV { get; set; } = true;
    /// <summary>最多留几条（收藏的不算）：100 / 300 / 1000。</summary>
    public int ClipboardMaxItems { get; set; } = 300;
    /// <summary>留几天：1 / 7 / 30，0 = 一直留着。</summary>
    public int ClipboardKeepDays { get; set; } = 30;
    /// <summary>像密码、身份证号的内容干脆不记（默认是记下来但打码、加密保存）。</summary>
    public bool ClipboardSkipSensitive { get; set; }
    /// <summary>从这些软件复制的东西不记（exe 文件名）。修改时整个换掉，不要原地改。</summary>
    public List<string> ClipboardIgnoreApps { get; set; } = new(DefaultIgnoreApps);

    public static readonly string[] DefaultIgnoreApps = { "KeePass.exe", "KeePassXC.exe", "1Password.exe", "Bitwarden.exe", "Enpass.exe" };
}

public static class SettingsService
{
    private static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PiggyHelper");
    private static readonly string FilePath = Path.Combine(Dir, "settings.json");
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static AppSettings Current { get; private set; } = Load();

    public static event Action? Changed;

    private static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch { /* 文件坏了就用默认设置 */ }
        return new AppSettings();
    }

    public static void Save()
    {
        try
        {
            // 先写到临时文件再替换，写到一半断电也不会把设置弄坏
            Directory.CreateDirectory(Dir);
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(Current, Json));
            File.Move(tmp, FilePath, true);
        }
        catch { }
        Changed?.Invoke();
    }

    // ---------- API Key（加密保存） ----------
    public static string? GetApiKey()
    {
        if (string.IsNullOrEmpty(Current.AiKeyProtected)) return null;
        try
        {
            var bytes = ProtectedData.Unprotect(Convert.FromBase64String(Current.AiKeyProtected), null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch { return null; }
    }

    public static void SetApiKey(string? key)
    {
        Current.AiKeyProtected = string.IsNullOrWhiteSpace(key)
            ? null
            : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(key.Trim()), null, DataProtectionScope.CurrentUser));
        Save();
    }

    // ---------- 开机自启 ----------
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunName = "PiggyHelper";

    public static bool StartWithWindows
    {
        get
        {
            try
            {
                using var k = Registry.CurrentUser.OpenSubKey(RunKey);
                return k?.GetValue(RunName) is string;
            }
            catch { return false; }
        }
        set
        {
            try
            {
                using var k = Registry.CurrentUser.CreateSubKey(RunKey);
                if (value && Environment.ProcessPath is { } exe) k.SetValue(RunName, $"\"{exe}\" --minimized");
                else k.DeleteValue(RunName, false);
            }
            catch { }
        }
    }
}
