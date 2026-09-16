using ZzzAutoSign.Config;
using ZzzAutoSign.Logging;
using ZzzAutoSign.MiHoYo;
using ZzzAutoSign.MiHoYo.Models;

namespace ZzzAutoSign.Core;

/// <summary>单个角色的签到结果。</summary>
public sealed class RoleSignResult
{
    public required string Uid { get; init; }
    public required string Region { get; init; }
    public required string Nickname { get; init; }

    /// <summary>true 表示该角色今天已处于「已签到」状态（无论本次是否真的签了）。</summary>
    public bool Signed { get; init; }

    /// <summary>true 表示本次调用是「今日已签到」的幂等返回。</summary>
    public bool AlreadySigned { get; init; }

    /// <summary>失败原因；成功时为 null。</summary>
    public string? FailureReason { get; init; }

    public bool Failed => FailureReason is not null;
}

/// <summary>一次完整签到流程的结果。</summary>
public sealed class SignRunResult
{
    public bool Skipped { get; init; }
    public string? SkipReason { get; init; }
    public List<RoleSignResult> Results { get; init; } = new();

    public int SuccessCount => Results.Count(r => !r.Failed);
    public int FailureCount => Results.Count(r => r.Failed);
    public bool AllSucceeded => Results.Count > 0 && FailureCount == 0;
    public bool AllAlreadySigned => Results.Count > 0 && Results.All(r => r.AlreadySigned);

    /// <summary>用于通知标题的简短结论。</summary>
    public string Title => FailureCount switch
    {
        0 when Results.Count == 0 => "签到失败",
        0 => "签到完成",
        _ when SuccessCount > 0 => "签到完成（部分失败）",
        _ => "签到失败"
    };

    /// <summary>用于通知正文的明细。</summary>
    public string Body
    {
        get
        {
            if (Skipped)
            {
                return SkipReason ?? "本次未执行签到";
            }

            if (Results.Count == 0)
            {
                return "未获取到任何绝区零角色";
            }

            if (AllSucceeded)
            {
                return Results.Count == 1
                    ? $"「{Results[0].Nickname}」签到完成"
                    : $"{Results.Count} 个角色签到完成：{string.Join("、", Results.Select(r => r.Nickname))}";
            }

            if (FailureCount == 0)
            {
                return "已签到";
            }

            var parts = new List<string>
            {
                $"{SuccessCount}/{Results.Count} 个角色签到成功"
            };

            foreach (var f in Results.Where(r => r.Failed))
            {
                parts.Add($"{f.Nickname}（{f.Uid}）失败：{f.FailureReason}");
            }

            return string.Join("；", parts);
        }
    }
}

/// <summary>
/// 签到流程编排：闸门 → 凭证 → 角色列表 → 逐角色签到 → 汇总。
/// </summary>
public sealed class SignOrchestrator
{
    private readonly AppSettings _settings;
    private readonly CredentialStore _credentials;
    private readonly DailyGate _gate;
    private readonly MiHoYoHttpClient _http;
    private readonly ZzzSignApi _signApi;
    private readonly BindingApi _bindingApi;
    private readonly TokenApi _tokenApi;
    private readonly ILogger _log;
    private readonly SemaphoreSlim _runLock = new(1, 1);

    /// <summary>进度与结果回调，供 UI 展示。参数为人类可读的进度文本。</summary>
    public event Action<string>? Progress;

    public SignOrchestrator(
        AppSettings settings,
        CredentialStore credentials,
        DailyGate gate,
        MiHoYoHttpClient http,
        ZzzSignApi signApi,
        BindingApi bindingApi,
        TokenApi tokenApi,
        ILogger log)
    {
        _settings = settings;
        _credentials = credentials;
        _gate = gate;
        _http = http;
        _signApi = signApi;
        _bindingApi = bindingApi;
        _tokenApi = tokenApi;
        _log = log;

        _http.SignMode = (SignMode)Math.Clamp(settings.PreferredSignMode, 0, 2);
    }

    /// <summary>当前使用的签名模式。</summary>
    public SignMode CurrentSignMode => _http.SignMode;

    /// <summary>
    /// 执行签到。
    /// </summary>
    /// <param name="trigger">触发来源描述，写入日志。</param>
    /// <param name="bypassGate">true 表示手动触发，跳过每日闸门。</param>
    public async Task<SignRunResult> RunAsync(string trigger, bool bypassGate = false,
        CancellationToken ct = default)
    {
        // 同一时刻只允许一个流程，避免进程抖动导致并发签到
        if (!await _runLock.WaitAsync(TimeSpan.FromSeconds(1), ct).ConfigureAwait(false))
        {
            _log.Info("已有签到流程在执行，本次触发忽略");
            return new SignRunResult { Skipped = true, SkipReason = "已有签到流程正在执行" };
        }

        try
        {
            return await RunInternalAsync(trigger, bypassGate, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.Error("签到流程发生未预期异常", ex);
            return new SignRunResult
            {
                Results = new List<RoleSignResult>
                {
                    new()
                    {
                        Uid = "-", Region = "-", Nickname = "账号",
                        FailureReason = ex.Message
                    }
                }
            };
        }
        finally
        {
            _runLock.Release();
        }
    }

    private async Task<SignRunResult> RunInternalAsync(string trigger, bool bypassGate, CancellationToken ct)
    {
        _log.Info($"开始签到流程（触发来源：{trigger}，跳过闸门={bypassGate}）");
        Progress?.Invoke("检查凭证…");

        // 1) 凭证检查
        if (!_credentials.HasCookie)
        {
            _log.Warn("未配置 Cookie，无法签到");
            _gate.RecordResult("未配置凭证");
            return Fail("未配置登录凭证，请先运行凭证提取工具");
        }

        // 2) 每日闸门
        if (!bypassGate)
        {
            var decision = _gate.Evaluate(_settings.MaxAttemptsPerDay);
            if (decision.ShouldSkip)
            {
                string reason = decision.Reason switch
                {
                    GateSkipReason.AlreadySucceededToday => "今天已完成签到",
                    GateSkipReason.AttemptLimitReached => $"今天尝试次数已达上限（{_settings.MaxAttemptsPerDay} 次）",
                    _ => "已跳过"
                };

                _log.Info($"跳过签到：{reason}");
                return new SignRunResult { Skipped = true, SkipReason = reason };
            }
        }

        _gate.RecordAttempt();

        // 3) 拉取角色列表（必须先拉列表，否则不知道 uid/region）
        Progress?.Invoke("获取角色列表…");
        List<GameRole> roles;
        try
        {
            roles = await ExecuteWithRetryAsync(
                ct => _bindingApi.GetZzzRolesAsync(ct),
                "获取角色列表",
                ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            string reason = Describe(ex);
            _log.Error($"获取角色列表失败：{reason}");
            _gate.RecordResult(reason);
            return Fail(reason);
        }

        if (roles.Count == 0)
        {
            const string reason = "该账号下没有查询到绝区零角色，请确认已绑定角色";
            _log.Warn(reason);
            _gate.RecordResult(reason);
            return Fail(reason);
        }

        // 4) 逐角色处理
        var results = new List<RoleSignResult>();
        for (int i = 0; i < roles.Count; i++)
        {
            var role = roles[i];

            if (ct.IsCancellationRequested)
            {
                break;
            }

            Progress?.Invoke($"签到中（{i + 1}/{roles.Count}）：{role.DisplayName}");
            var result = await SignSingleRoleAsync(role, ct).ConfigureAwait(false);
            results.Add(result);

            // 角色之间留间隔，降低风控风险
            if (i < roles.Count - 1 && _settings.InterRoleDelayMs > 0)
            {
                await Task.Delay(_settings.InterRoleDelayMs, ct).ConfigureAwait(false);
            }
        }

        var run = new SignRunResult { Results = results };

        // 5) 只有全部成功才标记今天已完成
        if (run.AllSucceeded)
        {
            _gate.MarkSucceeded();
            _gate.RecordResult($"成功 {run.SuccessCount} 个角色");
        }
        else
        {
            _gate.RecordResult($"失败 {run.FailureCount} 个角色，成功 {run.SuccessCount} 个");
        }

        _log.Info($"签到流程结束：成功 {run.SuccessCount}，失败 {run.FailureCount}，跳过 {run.Skipped}");
        Progress?.Invoke("完成");
        return run;
    }

    /// <summary>处理单个角色：先查状态，未签则签。</summary>
    private async Task<RoleSignResult> SignSingleRoleAsync(GameRole role, CancellationToken ct)
    {
        string uid = role.GameUid ?? string.Empty;
        string region = role.Region ?? string.Empty;
        string name = role.DisplayName;

        try
        {
            // 先查状态，避免无谓的签到请求
            var info = await ExecuteWithRetryAsync(
                c => _signApi.GetInfoAsync(region, uid, c),
                $"查询 {name} 签到状态",
                ct).ConfigureAwait(false);

            if (info.RetCode != ZzzRetCode.Ok)
            {
                return FailRole(uid, region, name, ZzzRetCode.Describe(info.RetCode, info.Message));
            }

            if (info.Data?.IsSign == true)
            {
                _log.Info($"「{name}」今日已签到，跳过");
                return new RoleSignResult
                {
                    Uid = uid, Region = region, Nickname = name,
                    Signed = true, AlreadySigned = true
                };
            }

            // 执行签到
            var signResult = await ExecuteWithRetryAsync(
                c => _signApi.SignAsync(region, uid, c),
                $"签到 {name}",
                ct).ConfigureAwait(false);

            if (signResult.RetCode == ZzzRetCode.Ok && signResult.Data?.Success == 0)
            {
                _log.Info($"「{name}」签到成功");
                return new RoleSignResult
                {
                    Uid = uid, Region = region, Nickname = name, Signed = true
                };
            }

            if (signResult.RetCode == ZzzRetCode.AlreadySigned)
            {
                // 幂等：状态查询与签到之间已被签过
                _log.Info($"「{name}」今日已签到（幂等返回）");
                return new RoleSignResult
                {
                    Uid = uid, Region = region, Nickname = name,
                    Signed = true, AlreadySigned = true
                };
            }

            string reason = signResult.RetCode == ZzzRetCode.Ok
                ? $"签到未成功（success={signResult.Data?.Success}，可能需要验证码）"
                : ZzzRetCode.Describe(signResult.RetCode, signResult.Message);

            _log.Warn($"「{name}」签到失败：{reason}");
            return FailRole(uid, region, name, reason);
        }
        catch (OperationCanceledException)
        {
            return FailRole(uid, region, name, "已取消");
        }
        catch (Exception ex)
        {
            string reason = Describe(ex);
            _log.Error($"「{name}」签到异常：{reason}");
            return FailRole(uid, region, name, reason);
        }
    }

    /// <summary>
    /// 带重试的执行包装。
    /// 特殊处理两类需要「换策略」而不是「简单重试」的错误：
    /// - 签名无效 → 切换签名模式
    /// - 凭证失效 → 用 stoken 刷新 cookie_token
    /// </summary>
    private async Task<T> ExecuteWithRetryAsync<T>(
        Func<CancellationToken, Task<T>> action,
        string description,
        CancellationToken ct)
    {
        var policy = new RetryPolicy(_settings.NetworkRetryCount, _settings.TooManyRequestsRetryCount);
        int attempt = 0;
        bool signatureSwitched = false;
        bool tokenRefreshed = false;

        while (true)
        {
            try
            {
                return await action(ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                FailureKind kind = RetryPolicy.Classify(ex);

                // 签名无效：切换签名模式后重试
                if (kind == FailureKind.InvalidSignature && !signatureSwitched)
                {
                    signatureSwitched = true;
                    var next = NextSignMode(_http.SignMode);
                    _log.Warn($"{description}：签名无效（当前 {_http.SignMode}），切换为 {next} 重试");
                    _http.SignMode = next;
                    _settings.PreferredSignMode = (int)next;
                    _settings.Save(AppPaths.SettingsFile);
                    await Task.Delay(500, ct).ConfigureAwait(false);
                    continue;
                }

                // 凭证失效：用 stoken 刷新后重试
                if (kind == FailureKind.AuthExpired && !tokenRefreshed)
                {
                    tokenRefreshed = true;
                    _log.Warn($"{description}：凭证失效，尝试用 stoken 刷新 cookie_token");
                    bool refreshed = await TryRefreshCookieTokenAsync(ct).ConfigureAwait(false);
                    if (refreshed)
                    {
                        await Task.Delay(1000, ct).ConfigureAwait(false);
                        continue;
                    }

                    throw new MiHoYoApiException("登录凭证已失效，且无法用 stoken 自动刷新，请重新获取凭证",
                        retCode: ZzzRetCode.NotLoggedIn, inner: ex);
                }

                var decision = policy.Decide(kind, attempt);
                if (!decision.ShouldRetry)
                {
                    throw;
                }

                attempt++;
                _log.Warn($"{description} 第 {attempt} 次失败（{kind}）：{ex.Message}；{decision.Reason}，" +
                          $"{decision.Delay.TotalSeconds:0.#}s 后重试");
                await Task.Delay(decision.Delay, ct).ConfigureAwait(false);
            }
        }
    }

    /// <summary>用 stoken 换新的 cookie_token，成功后回写凭证并把新 token 合入 Cookie 串。</summary>
    private async Task<bool> TryRefreshCookieTokenAsync(CancellationToken ct)
    {
        var cred = _credentials.Current;

        if (string.IsNullOrWhiteSpace(cred.Stoken))
        {
            _log.Warn("没有配置 stoken，无法自动刷新凭证");
            return false;
        }

        string stuid = string.IsNullOrWhiteSpace(cred.Stuid) ? TryExtractStuid(cred.Cookie) : cred.Stuid;

        if (string.IsNullOrWhiteSpace(stuid))
        {
            _log.Warn("无法确定 stuid/ltuid，无法自动刷新凭证");
            return false;
        }

        string? newToken;
        try
        {
            newToken = await _tokenApi.GetCookieTokenAsync(cred.Stoken, stuid, cred.Mid, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.Warn($"刷新 cookie_token 异常：{ex.Message}");
            return false;
        }

        if (string.IsNullOrWhiteSpace(newToken))
        {
            return false;
        }

        _credentials.Update(c =>
        {
            c.Cookie = MergeCookieToken(c.Cookie, newToken!);
            c.Stuid = stuid;
            c.LastTokenRefreshAt = DateTimeOffset.UtcNow;
        });
        _credentials.Save();

        _log.Info("cookie_token 已刷新并写回凭证");
        return true;
    }

    /// <summary>把新的 cookie_token 合并进现有 Cookie 串（存在则替换，不存在则追加）。</summary>
    public static string MergeCookieToken(string cookie, string newToken)
    {
        var parts = (cookie ?? string.Empty)
            .Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Trim())
            .Where(p => p.Length > 0)
            .ToList();

        bool replaced = false;
        for (int i = 0; i < parts.Count; i++)
        {
            if (parts[i].StartsWith("cookie_token=", StringComparison.OrdinalIgnoreCase))
            {
                parts[i] = $"cookie_token={newToken}";
                replaced = true;
                break;
            }
        }

        if (!replaced)
        {
            parts.Add($"cookie_token={newToken}");
        }

        return string.Join("; ", parts);
    }

    /// <summary>从 Cookie 串里提取 stuid / ltuid / account_id。</summary>
    public static string TryExtractStuid(string cookie)
    {
        foreach (string key in new[] { "stuid", "ltuid", "account_id", "ltuid_v2", "account_id_v2" })
        {
            string? v = TryExtractCookieValue(cookie, key);
            if (!string.IsNullOrWhiteSpace(v))
            {
                return v!;
            }
        }

        return string.Empty;
    }

    /// <summary>从 Cookie 串提取指定键的值。</summary>
    public static string? TryExtractCookieValue(string cookie, string key)
    {
        if (string.IsNullOrWhiteSpace(cookie))
        {
            return null;
        }

        foreach (string part in cookie.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            string trimmed = part.Trim();
            int eq = trimmed.IndexOf('=');
            if (eq <= 0)
            {
                continue;
            }

            if (trimmed.AsSpan(0, eq).Trim().Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                return trimmed[(eq + 1)..].Trim();
            }
        }

        return null;
    }

    /// <summary>签名模式轮换顺序：WebDs1 → AppDs1 → X6Ds2 → WebDs1。</summary>
    private static SignMode NextSignMode(SignMode current) => current switch
    {
        SignMode.WebDs1 => SignMode.AppDs1,
        SignMode.AppDs1 => SignMode.X6Ds2,
        _ => SignMode.WebDs1
    };

    private SignRunResult Fail(string reason)
        => new()
        {
            Results = new List<RoleSignResult>
            {
                new() { Uid = "-", Region = "-", Nickname = "账号", FailureReason = reason }
            }
        };

    private static RoleSignResult FailRole(string uid, string region, string name, string reason)
        => new()
        {
            Uid = uid, Region = region, Nickname = name, Signed = false, FailureReason = reason
        };

    /// <summary>把异常翻译成适合放进通知的中文原因。</summary>
    private static string Describe(Exception ex) => ex switch
    {
        MiHoYoApiException api => api.Message,
        TaskCanceledException or TimeoutException => "请求超时",
        HttpRequestException http => $"网络不可用（{http.Message}）",
        _ => ex.Message
    };
}
