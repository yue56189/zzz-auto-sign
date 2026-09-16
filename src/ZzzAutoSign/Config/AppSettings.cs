namespace ZzzAutoSign.Config;

/// <summary>
/// 非敏感配置，存 %LOCALAPPDATA%\ZzzAutoSign\settings.json。
/// 敏感凭证一律不放这里（见 <see cref="CredentialStore"/>）。
/// </summary>
public sealed class AppSettings
{
    /// <summary>被监听的目标进程名（不含路径）。比对时不区分大小写。</summary>
    public string TargetProcessName { get; set; } = "zenlesszonezero.exe";

    /// <summary>是否开机自启。</summary>
    public bool AutoStart { get; set; } = true;

    /// <summary>
    /// 阶段 4 探测结论：优先使用的签名模式。
    /// 0 = client_type 5 + SaltWeb + DS1（默认）
    /// 1 = client_type 2 + SaltApp + DS1
    /// 2 = SaltX6 + DS2（带 body/query）
    /// </summary>
    public int PreferredSignMode { get; set; } = 0;

    /// <summary>网络错误重试次数（不含首次）。</summary>
    public int NetworkRetryCount { get; set; } = 3;

    /// <summary>HTTP 429 重试次数。</summary>
    public int TooManyRequestsRetryCount { get; set; } = 3;

    /// <summary>当天最多尝试签到次数（防止反复失败反复请求）。设为 0 表示不限制。</summary>
    public int MaxAttemptsPerDay { get; set; } = 2;

    /// <summary>失败时是否弹出通知。</summary>
    public bool NotifyOnFailure { get; set; } = true;

    /// <summary>请求超时（秒）。</summary>
    public int HttpTimeoutSeconds { get; set; } = 20;

    /// <summary>多角色签到时，角色之间的间隔（毫秒），用于降低风控风险。</summary>
    public int InterRoleDelayMs { get; set; } = 1200;

    /// <summary>“今日已签到”时是否也弹通知。默认 false（仅记日志）。</summary>
    public bool NotifyWhenAlreadySigned { get; set; } = false;

    /// <summary>日志最低级别：0=Debug 1=Info 2=Warn 3=Error</summary>
    public int LogMinLevel { get; set; } = 1;

    /// <summary>日志保留天数。</summary>
    public int LogRetainDays { get; set; } = 14;

    /// <summary>上次成功签到的本地日期（yyyy-MM-dd）。实际权威值是 state.json，这里仅作备份显示。</summary>
    public string? LastSuccessDateBackup { get; set; }

    public static AppSettings LoadOrDefaults(string path)
    {
        return AtomicFile.ReadJsonOrNull<AppSettings>(path) ?? new AppSettings();
    }

    public void Save(string path) => AtomicFile.WriteJson(path, this);
}
