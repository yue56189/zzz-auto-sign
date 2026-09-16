using ZzzAutoSign.Config;
using ZzzAutoSign.Core;
using ZzzAutoSign.Logging;
using ZzzAutoSign.MiHoYo;
using ZzzAutoSign.Notify;
using ZzzAutoSign.Startup;
using ZzzAutoSign.UI;

namespace ZzzAutoSign;

/// <summary>
/// 组合根：装配所有模块、持有生命周期、把进程启动事件接到签到流程。
/// </summary>
public sealed class AppContextHost : IDisposable
{
    private readonly ILogger _log;
    private readonly AppSettings _settings;
    private readonly CredentialStore _credentials;
    private readonly DailyGate _gate;
    private readonly ProcessWatcher _watcher;
    private readonly SignOrchestrator _orchestrator;
    private readonly MiHoYoHttpClient _http;
    private readonly NotificationService _notify;
    private readonly AutoStartManager _autoStart;
    private TrayHost? _tray;

    public ApplicationContext ApplicationContext { get; } = new();

    public AppContextHost(string[] args)
    {
        AppPaths.EnsureCreated();

        _log = new FileLogger(AppPaths.LogDir, LogLevel.Info);
        _settings = AppSettings.LoadOrDefaults(AppPaths.SettingsFile);
        _credentials = new CredentialStore(AppPaths.CredentialFile);
        _gate = new DailyGate(AppPaths.StateFile, _log);

        var endpoints = MiHoYoEndpoints.LoadWithOverride(AppPaths.EndpointsOverrideFile);

        _http = new MiHoYoHttpClient(endpoints, _credentials, _log, _settings.HttpTimeoutSeconds);
        var signApi = new ZzzSignApi(_http, endpoints, _log);
        var bindingApi = new BindingApi(_http, endpoints, _log);
        var tokenApi = new TokenApi(_http, endpoints, _log);

        _orchestrator = new SignOrchestrator(_settings, _credentials, _gate, _http,
            signApi, bindingApi, tokenApi, _log);

        _notify = new NotificationService(_log);
        _autoStart = new AutoStartManager(_log, useScheduledTask: true);
        _watcher = new ProcessWatcher(_log, _settings.TargetProcessName);

        _orchestrator.Progress += msg => _log.Debug($"[进度] {msg}");
        _watcher.TargetProcessStarted += OnGameStarted;
    }

    /// <summary>启动常驻服务。</summary>
    public void Start()
    {
        _log.Info("========== 程序启动 ==========");
        _log.Info($"自身路径：{AppPaths.ExecutablePath}");
        _log.Info($"数据目录：{AppPaths.DataDir}");

        bool isAdmin = ProcessWatcher.IsCurrentUserAdmin();
        if (!isAdmin)
        {
            _log.Warn("当前未以管理员身份运行，WMI 进程监听可能失败");
        }

        // 启动时同步一次自启状态（用户可能在系统里改过）
        if (_settings.AutoStart && !_autoStart.IsEnabled())
        {
            _autoStart.Enable();
        }

        // 托盘先建好，后续通知可降级到气泡
        _tray = new TrayHost(_settings, _log, _notify, _autoStart, _gate, _watcher, ManualSignAsync);

        try
        {
            _watcher.Start();
        }
        catch (Exception ex)
        {
            _log.Error("启动进程监听失败", ex);
            _notify.NotifyFailure("进程监听启动失败，请以管理员身份运行本程序");
        }

        _tray.RefreshStatus();
        _log.Info("程序已进入后台运行状态");
    }

    /// <summary>游戏进程启动事件的处理器。WMI 线程只负责投递。</summary>
    private void OnGameStarted(object? sender, int pid)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                var result = await _orchestrator.RunAsync($"游戏启动（pid={pid}）").ConfigureAwait(false);
                ReportResult(result);
            }
            catch (Exception ex)
            {
                _log.Error("游戏启动触发的签到流程异常", ex);
                _notify.NotifyFailure(ex.Message);
            }
            finally
            {
                _tray?.RefreshStatus();
            }
        });
    }

    /// <summary>托盘「立即签到」调用，绕过每日闸门。</summary>
    private async Task ManualSignAsync()
    {
        var result = await _orchestrator.RunAsync("托盘手动触发", bypassGate: true).ConfigureAwait(false);
        ReportResult(result);
    }

    /// <summary>
    /// 按约定策略发送通知：
    /// - 跳过（今日已签到/达上限）→ 不发通知
    /// - 全部成功 → 「签到完成」
    /// - 有失败 → 「签到失败，原因xxx」
    /// </summary>
    private void ReportResult(SignRunResult result)
    {
        if (result.Skipped)
        {
            _log.Info($"本次未发起签到：{result.SkipReason}");

            // 全部角色今天本来就是已签到状态且用户希望提醒时，发一条成功通知
            if (result.AllAlreadySigned && _settings.NotifyWhenAlreadySigned)
            {
                _notify.NotifySuccess(result.Body);
            }

            return;
        }

        if (result.FailureCount == 0)
        {
            // 全部成功；若全是「今日已签到」的幂等结果且用户不想被提醒，则静默
            if (result.AllAlreadySigned && !_settings.NotifyWhenAlreadySigned)
            {
                _log.Info("全部角色今日已签到，按设置不发送通知");
                return;
            }

            _notify.NotifySuccess(result.Body);
            return;
        }

        if (_settings.NotifyOnFailure)
        {
            _notify.NotifyFailure(result.Body);
        }
    }

    public void Dispose()
    {
        _watcher.Dispose();
        _tray?.Dispose();
        _http.Dispose();
        ApplicationContext.Dispose();
        _log.Info("程序已退出");
    }
}
