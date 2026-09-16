using System.Net;
using ZzzAutoSign.MiHoYo;
using ZzzAutoSign.MiHoYo.Models;

namespace ZzzAutoSign.Core;

/// <summary>失败的分类，决定是否重试以及退避时长。</summary>
public enum FailureKind
{
    /// <summary>可恢复的网络类错误（超时、DNS、连接失败）。</summary>
    Network,

    /// <summary>HTTP 429 限流。</summary>
    RateLimited,

    /// <summary>DS 签名无效：换个签名模式重试一次。</summary>
    InvalidSignature,

    /// <summary>凭证失效：刷新 cookie_token 后重试一次。</summary>
    AuthExpired,

    /// <summary>触发风险校验，不可重试。</summary>
    Captcha,

    /// <summary>其他业务错误，不可重试。</summary>
    Business,

    /// <summary>本地配置问题（未配置凭证），不可重试。</summary>
    Config
}

/// <summary>重试决策。</summary>
public sealed record RetryDecision(bool ShouldRetry, TimeSpan Delay, string Reason);

/// <summary>
/// 重试与退避策略。
/// 区分「可重试错误」与「不可重试错误」——风控类错误绝不能重试，否则会加重风控。
/// </summary>
public sealed class RetryPolicy
{
    private readonly int _networkRetries;
    private readonly int _rateLimitRetries;

    public RetryPolicy(int networkRetries = 3, int rateLimitRetries = 3)
    {
        _networkRetries = Math.Clamp(networkRetries, 0, 10);
        _rateLimitRetries = Math.Clamp(rateLimitRetries, 0, 10);
    }

    /// <summary>网络错误退避序列：2s → 4s → 8s ...（上限 30s）</summary>
    public static TimeSpan NetworkBackoff(int attempt)
        => TimeSpan.FromSeconds(Math.Min(30, Math.Pow(2, attempt + 1)));

    /// <summary>429 退避序列：5s → 15s → 30s（上限 60s）</summary>
    public static TimeSpan RateLimitBackoff(int attempt)
        => TimeSpan.FromSeconds(Math.Min(60, 5 * Math.Pow(3, attempt)));

    /// <summary>
    /// 判断是否重试。<paramref name="attempt"/> 是已经失败过的次数（从 0 开始计）。
    /// </summary>
    public RetryDecision Decide(FailureKind kind, int attempt)
    {
        switch (kind)
        {
            case FailureKind.Network:
                if (attempt < _networkRetries)
                {
                    return new RetryDecision(true, NetworkBackoff(attempt), "网络异常，退避后重试");
                }

                return new RetryDecision(false, TimeSpan.Zero, "网络异常重试次数已用尽");

            case FailureKind.RateLimited:
                if (attempt < _rateLimitRetries)
                {
                    return new RetryDecision(true, RateLimitBackoff(attempt), "被限流，退避后重试");
                }

                return new RetryDecision(false, TimeSpan.Zero, "限流重试次数已用尽");

            case FailureKind.InvalidSignature:
                // 只换签名重试一次，避免反复无效请求
                return attempt < 1
                    ? new RetryDecision(true, TimeSpan.FromMilliseconds(500), "签名无效，切换签名模式重试")
                    : new RetryDecision(false, TimeSpan.Zero, "已尝试全部签名模式仍失败");

            case FailureKind.AuthExpired:
                return attempt < 1
                    ? new RetryDecision(true, TimeSpan.FromSeconds(1), "凭证失效，刷新后重试")
                    : new RetryDecision(false, TimeSpan.Zero, "凭证刷新后仍失败");

            case FailureKind.Captcha:
                return new RetryDecision(false, TimeSpan.Zero, "触发风险校验，不重试");

            case FailureKind.Business:
                return new RetryDecision(false, TimeSpan.Zero, "业务错误，不重试");

            case FailureKind.Config:
            default:
                return new RetryDecision(false, TimeSpan.Zero, "本地配置问题，不重试");
        }
    }

    /// <summary>把异常分类到 <see cref="FailureKind"/>。</summary>
    public static FailureKind Classify(Exception ex)
    {
        if (ex is MiHoYoApiException api)
        {
            if (api.StatusCode == HttpStatusCode.TooManyRequests || api.RetCode == ZzzRetCode.TooManyRequests)
            {
                return FailureKind.RateLimited;
            }

            return api.RetCode switch
            {
                ZzzRetCode.InvalidSignature => FailureKind.InvalidSignature,
                ZzzRetCode.NotLoggedIn => FailureKind.AuthExpired,
                ZzzRetCode.NeedCaptcha => FailureKind.Captcha,
                _ => FailureKind.Business
            };
        }

        if (ex is TaskCanceledException or TimeoutException or HttpRequestException or IOException)
        {
            return FailureKind.Network;
        }

        return FailureKind.Business;
    }
}
