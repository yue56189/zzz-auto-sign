namespace ZzzAutoSign.MiHoYo.Models;

/// <summary>
/// 米游社返回码及其处置语义。
/// </summary>
public static class ZzzRetCode
{
    /// <summary>成功。</summary>
    public const int Ok = 0;

    /// <summary>今日已签到。应视为幂等成功，不报错。</summary>
    public const int AlreadySigned = -5003;

    /// <summary>DS 签名无效。通常意味着 salt / 版本 / client_type 不匹配。</summary>
    public const int InvalidSignature = -1;

    /// <summary>Cookie 失效，可尝试用 stoken 刷新。</summary>
    public const int NotLoggedIn = -100;

    /// <summary>需要验证码 / 风险校验，无法自动完成。</summary>
    public const int NeedCaptcha = 1034;

    /// <summary>请求过于频繁。</summary>
    public const int TooManyRequests = 429;

    /// <summary>该账号未绑定该游戏角色。</summary>
    public const int NotBound = 10001;

    /// <summary>把返回码翻译成中文可读原因，用于通知与日志。</summary>
    public static string Describe(int retCode, string? serverMessage = null)
    {
        string baseText = retCode switch
        {
            Ok => "成功",
            AlreadySigned => "今日已签到",
            InvalidSignature => "请求签名无效（salt 或客户端版本不匹配），可尝试切换签名模式",
            NotLoggedIn => "登录凭证已失效，请重新获取 Cookie",
            NeedCaptcha => "触发了米游社风险校验（需要验证码），无法自动完成，请手动签到一次",
            TooManyRequests => "请求过于频繁，已被限流，请稍后再试",
            NotBound => "该账号未绑定绝区零角色",
            // 注意：-100 已由上方 NotLoggedIn 分支覆盖，重复的常量模式会触发
            // CS8510「模式不可访问」，故此处不再列出。
            _ => $"接口返回错误"
        };

        if (!string.IsNullOrWhiteSpace(serverMessage))
        {
            return $"{baseText}（retcode={retCode}，{serverMessage}）";
        }

        return baseText == "成功" ? baseText : $"{baseText}（retcode={retCode}）";
    }
}
