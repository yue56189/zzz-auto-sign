using System.Reflection;

namespace ZzzAutoSign.Config;

/// <summary>
/// 统一管理所有本地路径。
/// 注意：单文件发布（PublishSingleFile=true）时 <see cref="Assembly.Location"/> 为空字符串，
/// 必须使用 <see cref="Environment.ProcessPath"/> 取自身 exe 路径。
/// </summary>
public static class AppPaths
{
    /// <summary>程序名，用于目录名与自启项名。</summary>
    public const string AppName = "ZzzAutoSign";

    /// <summary>中文显示名，用于通知与托盘 Tooltip。</summary>
    public const string DisplayName = "绝区零自动签到";

    /// <summary>
    /// Toast 通知用的 AUMID。使用自定义字符串时，Windows 需要一个带该 AUMID 的快捷方式才能正常显示。
    /// <see cref="Notify.AumidHelper"/> 会在启动时确保该快捷方式存在。
    /// </summary>
    public const string AppUserModelId = "ZzzAutoSign.DailyCheckin";

    /// <summary>数据根目录：%LOCALAPPDATA%\ZzzAutoSign</summary>
    public static string DataDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        AppName);

    /// <summary>日志目录</summary>
    public static string LogDir { get; } = Path.Combine(DataDir, "logs");

    /// <summary>非敏感配置：settings.json</summary>
    public static string SettingsFile { get; } = Path.Combine(DataDir, "settings.json");

    /// <summary>DPAPI 加密凭证：credential.bin</summary>
    public static string CredentialFile { get; } = Path.Combine(DataDir, "credential.bin");

    /// <summary>每日闸门状态：state.json</summary>
    public static string StateFile { get; } = Path.Combine(DataDir, "state.json");

    /// <summary>可选的运行时端点/签名覆盖文件：endpoints.json（salt 失效时无需重新打包）</summary>
    public static string EndpointsOverrideFile { get; } = Path.Combine(DataDir, "endpoints.json");

    /// <summary>当前 exe 的完整路径。单文件发布下 Assembly.Location 为空，故用 ProcessPath。</summary>
    public static string ExecutablePath
    {
        get
        {
            string? p = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(p) && File.Exists(p))
            {
                return p;
            }

            // 兜底：非单文件场景
            string loc = Assembly.GetEntryAssembly()?.Location ?? string.Empty;
            if (!string.IsNullOrEmpty(loc))
            {
                return loc;
            }

            return Path.Combine(AppContext.BaseDirectory, AppName + ".exe");
        }
    }

    /// <summary>确保所有目录存在。</summary>
    public static void EnsureCreated()
    {
        Directory.CreateDirectory(DataDir);
        Directory.CreateDirectory(LogDir);
    }
}
