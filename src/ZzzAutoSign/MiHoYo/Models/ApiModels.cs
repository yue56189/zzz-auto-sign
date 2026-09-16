using System.Text.Json.Serialization;

namespace ZzzAutoSign.MiHoYo.Models;

/// <summary>
/// 米游社接口统一外层响应。
/// retcode = 0 表示 HTTP 层面成功，业务结果仍需看 data 内部字段。
/// </summary>
public sealed class ApiEnvelope<T>
{
    [JsonPropertyName("retcode")]
    public int RetCode { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("data")]
    public T? Data { get; set; }
}

/// <summary>签到接口返回的 data。</summary>
public sealed class SignResultData
{
    /// <summary>0 = 签到成功；1 = 需要验证码/风险校验；其余见官方文档。</summary>
    [JsonPropertyName("success")]
    public int Success { get; set; }

    [JsonPropertyName("gt")]
    public string? Gt { get; set; }

    [JsonPropertyName("challenge")]
    public string? Challenge { get; set; }
}

/// <summary>签到状态查询返回的 data。</summary>
public sealed class SignInfoData
{
    [JsonPropertyName("is_sign")]
    public bool IsSign { get; set; }

    [JsonPropertyName("total_sign_day")]
    public int TotalSignDay { get; set; }

    [JsonPropertyName("today")]
    public string? Today { get; set; }

    [JsonPropertyName("first_bind")]
    public bool FirstBind { get; set; }

    [JsonPropertyName("is_sub")]
    public bool IsSub { get; set; }

    [JsonPropertyName("region")]
    public string? Region { get; set; }

    [JsonPropertyName("uid")]
    public string? Uid { get; set; }

    /// <summary>奖励列表（部分实现挂在 info 下）。</summary>
    [JsonPropertyName("awards")]
    public List<SignAward>? Awards { get; set; }
}

public sealed class SignAward
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("icon")]
    public string? Icon { get; set; }

    [JsonPropertyName("cnt")]
    public int Count { get; set; }
}

/// <summary>角色绑定信息。</summary>
public sealed class GameRole
{
    [JsonPropertyName("game_biz")]
    public string? GameBiz { get; set; }

    [JsonPropertyName("region")]
    public string? Region { get; set; }

    [JsonPropertyName("region_name")]
    public string? RegionName { get; set; }

    [JsonPropertyName("game_uid")]
    public string? GameUid { get; set; }

    [JsonPropertyName("nickname")]
    public string? Nickname { get; set; }

    [JsonPropertyName("level")]
    public int Level { get; set; }

    [JsonPropertyName("is_chosen")]
    public bool IsChosen { get; set; }

    [JsonPropertyName("is_official")]
    public bool IsOfficial { get; set; }

    /// <summary>DisplayName 用于日志与通知展示。</summary>
    [JsonIgnore]
    public string DisplayName =>
        string.IsNullOrWhiteSpace(Nickname) ? (GameUid ?? "未知角色") : Nickname!;
}

/// <summary>getUserGameRolesByCookie 返回的 data。</summary>
public sealed class GameRolesData
{
    [JsonPropertyName("list")]
    public List<GameRole>? List { get; set; }
}

/// <summary>stoken 换 cookie_token 返回的 data。</summary>
public sealed class CookieTokenData
{
    [JsonPropertyName("cookie_token")]
    public string? CookieToken { get; set; }

    [JsonPropertyName("uid")]
    public string? Uid { get; set; }
}

/// <summary>奖励列表 home 接口返回。</summary>
public sealed class HomeData
{
    [JsonPropertyName("awards")]
    public List<SignAward>? Awards { get; set; }

    [JsonPropertyName("total_sign_day")]
    public int TotalSignDay { get; set; }
}
