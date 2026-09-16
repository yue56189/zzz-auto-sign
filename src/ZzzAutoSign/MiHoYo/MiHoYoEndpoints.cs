namespace ZzzAutoSign.MiHoYo;

/// <summary>
/// 所有接口地址与常量。
/// salt 与版本号可在运行时通过 %LOCALAPPDATA%\ZzzAutoSign\endpoints.json 覆盖，
/// 这样官方升级客户端导致 salt 失效时，用户不必等新版本 exe。
/// </summary>
public sealed class MiHoYoEndpoints
{
    // ---------- 常量 ----------

    /// <summary>绝区零 CN 服活动 ID。</summary>
    public string ActId { get; set; } = "e202406242138391";

    /// <summary>绝区零签到 API 根域名。</summary>
    public string ZzzWebApi { get; set; } = "https://act-nap-api.mihoyo.com";

    /// <summary>角色绑定 / 令牌相关 API。</summary>
    public string ApiTakumi { get; set; } = "https://api-takumi.mihoyo.com";

    /// <summary>固定请求头用。</summary>
    public string ActOrigin { get; set; } = "https://act.mihoyo.com";

    // ---------- 签名常量 ----------

    /// <summary>DS1，配合 x-rpc-client_type = 5（Web）。</summary>
    public string SaltWeb { get; set; } = "DlOUwIupfU6YespEUWDJmXtutuXV6owG";

    /// <summary>DS2，带 body/query 签名。</summary>
    public string SaltX6 { get; set; } = "t0qEgfub6cvueAPgR5m9aQWWVciEer7v";

    /// <summary>DS1，配合 x-rpc-client_type = 2（安卓客户端）。</summary>
    public string SaltApp { get; set; } = "b0EofkfMKq2saWV9fwux18J5vzcFTlex";

    /// <summary>x-rpc-app_version，与 salt 强耦合。</summary>
    public string AppVersion { get; set; } = "2.99.1";

    /// <summary>
    /// x-rpc-channel。留空则不发送该头。
    /// 【需实测确认】部分接口对 channel 有校验，若遭遇签名通过但业务报错，可尝试 "miyousheluodi"。
    /// </summary>
    public string? Channel { get; set; }

    // ---------- 端点拼装 ----------

    public string RewardsUrl => $"{ZzzWebApi}/event/luna/zzz/home?lang=zh-cn";

    public string InfoUrl => $"{ZzzWebApi}/event/luna/zzz/info";

    public string SignUrl => $"{ZzzWebApi}/event/luna/zzz/sign";

    public string GameRolesUrl => $"{ApiTakumi}/binding/api/getUserGameRolesByCookie";

    public string CookieTokenByStokenUrl => $"{ApiTakumi}/auth/api/getCookieAccountInfoBySToken";

    // ---------- 覆盖文件 ----------

    /// <summary>
    /// 从 endpoints.json 加载覆盖值。文件不存在时返回内置默认值。
    /// </summary>
    public static MiHoYoEndpoints LoadWithOverride(string overridePath)
    {
        var defaults = new MiHoYoEndpoints();
        var overlay = Config.AtomicFile.ReadJsonOrNull<MiHoYoEndpoints>(overridePath);
        if (overlay is null)
        {
            return defaults;
        }

        // 只覆盖用户显式提供且非空的字段，其余保留默认（避免漏字段导致空字符串覆盖）
        if (!string.IsNullOrWhiteSpace(overlay.ActId)) defaults.ActId = overlay.ActId;
        if (!string.IsNullOrWhiteSpace(overlay.ZzzWebApi)) defaults.ZzzWebApi = overlay.ZzzWebApi.TrimEnd('/');
        if (!string.IsNullOrWhiteSpace(overlay.ApiTakumi)) defaults.ApiTakumi = overlay.ApiTakumi.TrimEnd('/');
        if (!string.IsNullOrWhiteSpace(overlay.ActOrigin)) defaults.ActOrigin = overlay.ActOrigin;
        if (!string.IsNullOrWhiteSpace(overlay.SaltWeb)) defaults.SaltWeb = overlay.SaltWeb;
        if (!string.IsNullOrWhiteSpace(overlay.SaltX6)) defaults.SaltX6 = overlay.SaltX6;
        if (!string.IsNullOrWhiteSpace(overlay.SaltApp)) defaults.SaltApp = overlay.SaltApp;
        if (!string.IsNullOrWhiteSpace(overlay.AppVersion)) defaults.AppVersion = overlay.AppVersion;
        if (!string.IsNullOrWhiteSpace(overlay.Channel)) defaults.Channel = overlay.Channel;

        return defaults;
    }
}
