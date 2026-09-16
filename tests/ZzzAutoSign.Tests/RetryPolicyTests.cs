using System.Net;
using Xunit;
using ZzzAutoSign.Core;
using ZzzAutoSign.MiHoYo;
using ZzzAutoSign.MiHoYo.Models;

namespace ZzzAutoSign.Tests;

public class RetryPolicyTests
{
    private static readonly RetryPolicy Policy = new(networkRetries: 3, rateLimitRetries: 3);

    [Fact]
    public void Network_ShouldRetryUpToConfiguredCount()
    {
        // 前 3 次（attempt 0/1/2）应重试，第 4 次（attempt 3）应放弃
        Assert.True(Policy.Decide(FailureKind.Network, 0).ShouldRetry);
        Assert.True(Policy.Decide(FailureKind.Network, 1).ShouldRetry);
        Assert.True(Policy.Decide(FailureKind.Network, 2).ShouldRetry);
        Assert.False(Policy.Decide(FailureKind.Network, 3).ShouldRetry);
    }

    [Fact]
    public void Network_BackoffShouldBeExponentialAndCapped()
    {
        Assert.Equal(TimeSpan.FromSeconds(2), RetryPolicy.NetworkBackoff(0));
        Assert.Equal(TimeSpan.FromSeconds(4), RetryPolicy.NetworkBackoff(1));
        Assert.Equal(TimeSpan.FromSeconds(8), RetryPolicy.NetworkBackoff(2));
        Assert.Equal(TimeSpan.FromSeconds(16), RetryPolicy.NetworkBackoff(3));

        // 上限 30 秒
        Assert.Equal(TimeSpan.FromSeconds(30), RetryPolicy.NetworkBackoff(10));
    }

    [Fact]
    public void RateLimit_BackoffShouldGrowAndCap()
    {
        Assert.Equal(TimeSpan.FromSeconds(5), RetryPolicy.RateLimitBackoff(0));
        Assert.Equal(TimeSpan.FromSeconds(15), RetryPolicy.RateLimitBackoff(1));
        Assert.Equal(TimeSpan.FromSeconds(45), RetryPolicy.RateLimitBackoff(2));

        // 上限 60 秒
        Assert.Equal(TimeSpan.FromSeconds(60), RetryPolicy.RateLimitBackoff(10));
    }

    [Fact]
    public void InvalidSignature_ShouldRetryExactlyOnce()
    {
        Assert.True(Policy.Decide(FailureKind.InvalidSignature, 0).ShouldRetry);
        Assert.False(Policy.Decide(FailureKind.InvalidSignature, 1).ShouldRetry);
    }

    [Fact]
    public void AuthExpired_ShouldRetryExactlyOnce()
    {
        Assert.True(Policy.Decide(FailureKind.AuthExpired, 0).ShouldRetry);
        Assert.False(Policy.Decide(FailureKind.AuthExpired, 1).ShouldRetry);
    }

    [Fact]
    public void Captcha_ShouldNeverRetry()
    {
        // 风控类错误重试只会加重风控，必须一次都不重试
        Assert.False(Policy.Decide(FailureKind.Captcha, 0).ShouldRetry);
        Assert.False(Policy.Decide(FailureKind.Captcha, 1).ShouldRetry);
    }

    [Fact]
    public void BusinessAndConfig_ShouldNeverRetry()
    {
        Assert.False(Policy.Decide(FailureKind.Business, 0).ShouldRetry);
        Assert.False(Policy.Decide(FailureKind.Config, 0).ShouldRetry);
    }

    [Fact]
    public void ZeroRetries_ShouldNeverRetry()
    {
        var p = new RetryPolicy(networkRetries: 0, rateLimitRetries: 0);
        Assert.False(p.Decide(FailureKind.Network, 0).ShouldRetry);
        Assert.False(p.Decide(FailureKind.RateLimited, 0).ShouldRetry);
    }

    [Fact]
    public void Classify_ShouldMapHttp429ToRateLimited()
    {
        var ex = new MiHoYoApiException("限流", HttpStatusCode.TooManyRequests, ZzzRetCode.TooManyRequests);
        Assert.Equal(FailureKind.RateLimited, RetryPolicy.Classify(ex));
    }

    [Fact]
    public void Classify_ShouldMapRetCodesCorrectly()
    {
        Assert.Equal(FailureKind.InvalidSignature,
            RetryPolicy.Classify(new MiHoYoApiException("签名", retCode: ZzzRetCode.InvalidSignature)));

        Assert.Equal(FailureKind.AuthExpired,
            RetryPolicy.Classify(new MiHoYoApiException("凭证", retCode: ZzzRetCode.NotLoggedIn)));

        Assert.Equal(FailureKind.Captcha,
            RetryPolicy.Classify(new MiHoYoApiException("风控", retCode: ZzzRetCode.NeedCaptcha)));

        Assert.Equal(FailureKind.Business,
            RetryPolicy.Classify(new MiHoYoApiException("其他", retCode: -12345)));
    }

    [Fact]
    public void Classify_ShouldMapNetworkExceptions()
    {
        Assert.Equal(FailureKind.Network, RetryPolicy.Classify(new TaskCanceledException()));
        Assert.Equal(FailureKind.Network, RetryPolicy.Classify(new TimeoutException()));
        Assert.Equal(FailureKind.Network, RetryPolicy.Classify(new HttpRequestException("dns")));
        Assert.Equal(FailureKind.Network, RetryPolicy.Classify(new IOException()));
    }

    [Fact]
    public void Classify_ShouldDefaultUnknownToBusiness()
    {
        Assert.Equal(FailureKind.Business, RetryPolicy.Classify(new InvalidOperationException("随便")));
    }
}

public class ZzzRetCodeTests
{
    [Theory]
    [InlineData(-5003, "今日已签到")]
    [InlineData(-1, "签名")]
    [InlineData(-100, "登录凭证")]
    [InlineData(1034, "风险校验")]
    [InlineData(429, "限流")]
    public void Describe_ShouldContainMeaningfulKeyword(int retCode, string keyword)
    {
        string text = ZzzRetCode.Describe(retCode);
        Assert.Contains(keyword, text);
    }

    [Fact]
    public void Describe_ShouldAppendServerMessage()
    {
        string text = ZzzRetCode.Describe(-1, "invalid sign");
        Assert.Contains("invalid sign", text);
        Assert.Contains("-1", text);
    }

    [Fact]
    public void Describe_ShouldIncludeRetCodeForNonZero()
    {
        string text = ZzzRetCode.Describe(-12345);
        Assert.Contains("-12345", text);
    }
}

public class DailyGateTests : IDisposable
{
    private readonly string _path;

    public DailyGateTests()
    {
        _path = Path.Combine(Path.GetTempPath(), $"gate-{Guid.NewGuid():N}.json");
    }

    public void Dispose()
    {
        try
        {
            if (File.Exists(_path))
            {
                File.Delete(_path);
            }
        }
        catch
        {
            // 忽略
        }
    }

    [Fact]
    public void FreshGate_ShouldProceed()
    {
        var dailyGate = new DailyGate(_path, Logging.NullLogger.Instance);

        var decision = dailyGate.Evaluate(maxAttemptsPerDay: 2);

        Assert.True(decision.Proceed);
        Assert.False(decision.ShouldSkip);
    }

    [Fact]
    public void AfterSuccess_ShouldSkip()
    {
        var gate = new DailyGate(_path, Logging.NullLogger.Instance);

        gate.MarkSucceeded();

        var decision = gate.Evaluate(maxAttemptsPerDay: 5);

        Assert.True(decision.ShouldSkip);
        Assert.Equal(GateSkipReason.AlreadySucceededToday, decision.Reason);
    }

    [Fact]
    public void AttemptLimit_ShouldBlockAfterReachingCap()
    {
        var gate = new DailyGate(_path, Logging.NullLogger.Instance);

        gate.RecordAttempt();
        gate.RecordAttempt();

        var decision = gate.Evaluate(maxAttemptsPerDay: 2);

        Assert.True(decision.ShouldSkip);
        Assert.Equal(GateSkipReason.AttemptLimitReached, decision.Reason);
    }

    [Fact]
    public void ZeroMaxAttempts_ShouldMeanUnlimited()
    {
        var gate = new DailyGate(_path, Logging.NullLogger.Instance);

        for (int i = 0; i < 10; i++)
        {
            gate.RecordAttempt();
        }

        Assert.True(gate.Evaluate(maxAttemptsPerDay: 0).Proceed);
    }

    [Fact]
    public void StateShouldPersistAcrossInstances()
    {
        var first = new DailyGate(_path, Logging.NullLogger.Instance);
        first.MarkSucceeded();

        var second = new DailyGate(_path, Logging.NullLogger.Instance);

        Assert.True(second.Evaluate(maxAttemptsPerDay: 5).ShouldSkip);
    }

    [Fact]
    public void ResetToday_ShouldAllowProceedingAgain()
    {
        var gate = new DailyGate(_path, Logging.NullLogger.Instance);
        gate.MarkSucceeded();

        Assert.True(gate.Evaluate(5).ShouldSkip);

        gate.ResetToday();

        Assert.True(gate.Evaluate(5).Proceed);
    }

    [Fact]
    public void Snapshot_ShouldReflectCurrentState()
    {
        var gate = new DailyGate(_path, Logging.NullLogger.Instance);
        gate.RecordAttempt();
        gate.RecordResult("测试结果");

        var snapshot = gate.Snapshot();

        Assert.Equal(1, snapshot.AttemptCount);
        Assert.Equal("测试结果", snapshot.LastResult);
        Assert.Equal(DailyGate.Today, snapshot.AttemptDate);
    }
}
