using ZzzAutoSign.Config;
using ZzzAutoSign.Logging;

namespace ZzzAutoSign.Core;

/// <summary>每日闸门的状态载体。</summary>
public sealed class DailyGateState
{
    /// <summary>最近一次全部角色签到成功的本地日期（yyyy-MM-dd）。</summary>
    public string? LastSuccessDate { get; set; }

    /// <summary>当天已尝试的次数。</summary>
    public int AttemptCount { get; set; }

    /// <summary>尝试计数所属日期（yyyy-MM-dd）。</summary>
    public string? AttemptDate { get; set; }

    /// <summary>最近一次尝试的结果描述，便于排查。</summary>
    public string? LastResult { get; set; }

    /// <summary>最近一次尝试时间（本地）。</summary>
    public DateTimeOffset? LastAttemptAt { get; set; }
}

/// <summary>
/// 每日闸门：保证「每天只在首次启动游戏时签到一次」。
/// 状态落盘使用原子写，日期一律用本机本地日期，跨天边界以本地时区为准。
/// </summary>
public sealed class DailyGate
{
    private readonly string _statePath;
    private readonly ILogger _log;
    private readonly object _sync = new();
    private DailyGateState _state;

    public DailyGate(string statePath, ILogger log)
    {
        _statePath = statePath;
        _log = log;
        _state = AtomicFile.ReadJsonOrNull<DailyGateState>(statePath) ?? new DailyGateState();
        NormalizeForToday();
    }

    /// <summary>今天的本地日期字符串。</summary>
    public static string Today => DateTime.Now.ToString("yyyy-MM-dd");

    /// <summary>是否应该本次真正执行签到。</summary>
    public GateDecision Evaluate(int maxAttemptsPerDay)
    {
        lock (_sync)
        {
            NormalizeForToday();

            if (_state.LastSuccessDate == Today)
            {
                return GateDecision.Skip(GateSkipReason.AlreadySucceededToday);
            }

            if (maxAttemptsPerDay > 0 && _state.AttemptCount >= maxAttemptsPerDay)
            {
                return GateDecision.Skip(GateSkipReason.AttemptLimitReached);
            }

            return GateDecision.Allow();
        }
    }

    /// <summary>记录一次尝试（无论成败）。</summary>
    public void RecordAttempt()
    {
        lock (_sync)
        {
            NormalizeForToday();
            _state.AttemptCount++;
            _state.LastAttemptAt = DateTimeOffset.Now;
            Persist();
        }
    }

    /// <summary>记录一次结果描述。</summary>
    public void RecordResult(string result)
    {
        lock (_sync)
        {
            _state.LastResult = result;
            Persist();
        }
    }

    /// <summary>标记「今天已全部签到成功」，当天后续启动不再触发。</summary>
    public void MarkSucceeded()
    {
        lock (_sync)
        {
            _state.LastSuccessDate = Today;
            _state.LastAttemptAt = DateTimeOffset.Now;
            Persist();
            _log.Info($"已记录今日签到成功：{Today}");
        }
    }

    /// <summary>手动重置（设置界面的「重置今日状态」用）。</summary>
    public void ResetToday()
    {
        lock (_sync)
        {
            _state.LastSuccessDate = null;
            _state.AttemptCount = 0;
            _state.AttemptDate = Today;
            _state.LastResult = "手动重置";
            Persist();
            _log.Info("今日签到状态已被手动重置");
        }
    }

    /// <summary>当前状态快照，用于展示。</summary>
    public DailyGateState Snapshot()
    {
        lock (_sync)
        {
            NormalizeForToday();
            return new DailyGateState
            {
                LastSuccessDate = _state.LastSuccessDate,
                AttemptCount = _state.AttemptCount,
                AttemptDate = _state.AttemptDate,
                LastResult = _state.LastResult,
                LastAttemptAt = _state.LastAttemptAt
            };
        }
    }

    /// <summary>跨天时把当天尝试计数归零。</summary>
    private void NormalizeForToday()
    {
        if (_state.AttemptDate != Today)
        {
            _state.AttemptDate = Today;
            _state.AttemptCount = 0;
        }
    }

    private void Persist() => AtomicFile.WriteJson(_statePath, _state);
}

public enum GateSkipReason
{
    /// <summary>不跳过，应执行签到。</summary>
    None,

    /// <summary>今天已经签到成功。</summary>
    AlreadySucceededToday,

    /// <summary>当天尝试次数已达上限。</summary>
    AttemptLimitReached
}

public readonly struct GateDecision
{
    private GateDecision(bool proceed, GateSkipReason reason)
    {
        Proceed = proceed;
        Reason = reason;
    }

    /// <summary>本次是否应真正执行签到。</summary>
    public bool Proceed { get; }

    public GateSkipReason Reason { get; }

    /// <summary>构造一个「应执行」的决策。</summary>
    public static GateDecision Allow() => new(true, GateSkipReason.None);

    /// <summary>构造一个「应跳过」的决策。</summary>
    public static GateDecision Skip(GateSkipReason reason) => new(false, reason);

    /// <summary>是否跳过。</summary>
    public bool ShouldSkip => !Proceed;
}
