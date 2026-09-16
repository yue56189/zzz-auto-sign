using ZzzAutoSign.Logging;
using ZzzAutoSign.MiHoYo.Models;

namespace ZzzAutoSign.MiHoYo;

/// <summary>
/// 绝区零签到相关接口：奖励列表 / 签到状态 / 执行签到。
/// </summary>
public sealed class ZzzSignApi
{
    private readonly MiHoYoHttpClient _http;
    private readonly MiHoYoEndpoints _endpoints;
    private readonly ILogger _log;

    public ZzzSignApi(MiHoYoHttpClient http, MiHoYoEndpoints endpoints, ILogger log)
    {
        _http = http;
        _endpoints = endpoints;
        _log = log;
    }

    /// <summary>拉取奖励列表，用于探活与展示。</summary>
    public async Task<ApiEnvelope<HomeData>> GetHomeAsync(CancellationToken ct = default)
    {
        return await _http.GetAsync<HomeData>(_endpoints.RewardsUrl, queryParams: null, ct: ct)
            .ConfigureAwait(false);
    }

    /// <summary>查询某角色的签到状态。</summary>
    public async Task<ApiEnvelope<SignInfoData>> GetInfoAsync(string region, string uid, CancellationToken ct = default)
    {
        var query = new List<KeyValuePair<string, string>>
        {
            new("act_id", _endpoints.ActId),
            new("region", region),
            new("uid", uid)
        };

        return await _http.GetAsync<SignInfoData>(_endpoints.InfoUrl, query, ct: ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 执行签到。调用方需自行按 retcode 判定结果。
    /// 注意：body 键名以字母序序列化（act_id, region, uid），与实际服务端约定一致。
    /// </summary>
    public async Task<ApiEnvelope<SignResultData>> SignAsync(string region, string uid, CancellationToken ct = default)
    {
        var body = new List<KeyValuePair<string, string>>
        {
            new("act_id", _endpoints.ActId),
            new("region", region),
            new("uid", uid)
        };

        return await _http.PostJsonAsync<SignResultData>(_endpoints.SignUrl, body, ct: ct).ConfigureAwait(false);
    }
}

/// <summary>角色绑定接口。</summary>
public sealed class BindingApi
{
    private readonly MiHoYoHttpClient _http;
    private readonly MiHoYoEndpoints _endpoints;
    private readonly ILogger _log;

    public BindingApi(MiHoYoHttpClient http, MiHoYoEndpoints endpoints, ILogger log)
    {
        _http = http;
        _endpoints = endpoints;
        _log = log;
    }

    /// <summary>
    /// 拉取账号下绑定的绝区零角色。
    /// 注意：该接口需要在 api-takumi 域名下调用，且不应携带 Host 为 act-nap-api 的指向；
    /// HttpClient 会按 URL 自动确定 Host，因此无需额外处理。
    /// </summary>
    public async Task<List<GameRole>> GetZzzRolesAsync(CancellationToken ct = default)
    {
        var envelope = await _http.GetAsync<GameRolesData>(
            _endpoints.GameRolesUrl,
            queryParams: new[] { new KeyValuePair<string, string>("game_biz", "nap_cn") },
            ct: ct).ConfigureAwait(false);

        if (envelope.RetCode != ZzzRetCode.Ok)
        {
            throw new MiHoYoApiException(
                ZzzRetCode.Describe(envelope.RetCode, envelope.Message),
                retCode: envelope.RetCode);
        }

        var list = envelope.Data?.List ?? new List<GameRole>();

        // 只保留绝区零角色（game_biz 为 nap_cn；部分账号可能返回其他游戏）
        var zzz = list
            .Where(r => !string.IsNullOrWhiteSpace(r.GameUid) && !string.IsNullOrWhiteSpace(r.Region))
            .Where(r => string.IsNullOrWhiteSpace(r.GameBiz) || r.GameBiz!.Contains("nap", StringComparison.OrdinalIgnoreCase))
            .ToList();

        _log.Info($"获取到 {zzz.Count} 个绝区零角色");
        foreach (var r in zzz)
        {
            _log.Info($"  - {r.DisplayName}（uid={r.GameUid}，region={r.Region}，等级 {r.Level}）");
        }

        return zzz;
    }
}

/// <summary>用 stoken 换取 cookie_token，用于 cookie 过期后的自动恢复。</summary>
public sealed class TokenApi
{
    private readonly MiHoYoHttpClient _http;
    private readonly MiHoYoEndpoints _endpoints;
    private readonly ILogger _log;

    public TokenApi(MiHoYoHttpClient http, MiHoYoEndpoints endpoints, ILogger log)
    {
        _http = http;
        _endpoints = endpoints;
        _log = log;
    }

    /// <summary>
    /// 用长效 stoken 换取新的 cookie_token。
    /// 该接口必须用 stoken 形式的 Cookie 调用，不能用已失效的 cookie_token。
    /// v2 形态的 stoken 还需要 mid。
    /// </summary>
    public async Task<string?> GetCookieTokenAsync(string stoken, string stuid, string mid,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(stoken))
        {
            return null;
        }

        bool isV2 = stoken.StartsWith("v2_", StringComparison.OrdinalIgnoreCase);
        if (isV2 && string.IsNullOrWhiteSpace(mid))
        {
            _log.Warn("v2 形态的 stoken 需要 mid 参数，但当前未配置 mid，无法自动刷新");
            return null;
        }

        string cookie = isV2
            ? $"stuid={stuid};stoken={stoken};mid={mid}"
            : $"stuid={stuid};stoken={stoken}";

        var query = new List<KeyValuePair<string, string>>
        {
            new("stoken", stoken),
            new("uid", stuid)
        };

        var envelope = await _http.GetAsync<CookieTokenData>(
            _endpoints.CookieTokenByStokenUrl, query, cookieOverride: cookie, ct: ct).ConfigureAwait(false);

        if (envelope.RetCode != ZzzRetCode.Ok || string.IsNullOrWhiteSpace(envelope.Data?.CookieToken))
        {
            _log.Warn($"用 stoken 换取 cookie_token 失败：{ZzzRetCode.Describe(envelope.RetCode, envelope.Message)}");
            return null;
        }

        _log.Info("已用 stoken 成功换取新的 cookie_token");
        return envelope.Data!.CookieToken;
    }
}
