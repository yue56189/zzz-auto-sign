using System.Management;
using System.Security.Principal;
using ZzzAutoSign.Logging;

namespace ZzzAutoSign.Core;

/// <summary>
/// 基于 WMI 事件的进程启动监听器。
/// 特点：事件驱动（无轮询）、进程名不区分大小写匹配、带订阅自愈。
/// </summary>
public sealed class ProcessWatcher : IDisposable
{
    private readonly ILogger _log;
    private readonly string _targetLower;
    private readonly object _sync = new();

    private ManagementEventWatcher? _watcher;
    private CancellationTokenSource? _cts;
    private Timer? _watchdog;
    private bool _disposed;

    /// <summary>目标进程启动时触发，参数为进程 ID。</summary>
    public event EventHandler<int>? TargetProcessStarted;

    /// <summary>监听是否处于工作状态。</summary>
    public bool IsRunning
    {
        get
        {
            lock (_sync)
            {
                return _watcher is not null;
            }
        }
    }

    public ProcessWatcher(ILogger log, string targetProcessName = "zenlesszonezero.exe")
    {
        _log = log;
        _targetLower = (targetProcessName ?? string.Empty).Trim().ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(_targetLower))
        {
            throw new ArgumentException("目标进程名不能为空", nameof(targetProcessName));
        }
    }

    /// <summary>当前进程是否以管理员身份运行。WMI 进程事件订阅需要管理员权限。</summary>
    public static bool IsCurrentUserAdmin()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>启动监听。失败时抛 <see cref="ManagementException"/>（通常因权限不足）。</summary>
    public void Start()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(ProcessWatcher));
            }

            if (_watcher is not null)
            {
                return;
            }

            _watchdog ??= new Timer(_ => RebuildSubscription(), null,
                dueTime: TimeSpan.FromMinutes(10), period: TimeSpan.FromMinutes(10));

            SubscribeLocked();
        }
    }

    /// <summary>必须在持有 _sync 时调用。</summary>
    private void SubscribeLocked()
    {
        try
        {
            string query = "SELECT * FROM __InstanceCreationEvent WITHIN 1 " +
                           "WHERE TargetInstance ISA 'Win32_Process'";

            var watcher = new ManagementEventWatcher(new WqlEventQuery(query));
            watcher.EventArrived += OnEventArrived;
            watcher.Options.Timeout = ManagementOptions.InfiniteTimeout;

            watcher.Start();

            _watcher = watcher;
            _log.Info($"WMI 进程监听已启动，目标：{_targetLower}（管理员={IsCurrentUserAdmin()}）");
        }
        catch (ManagementException ex)
        {
            _log.Error("启动 WMI 进程监听失败（常见原因：未以管理员身份运行）", ex);
            throw;
        }
    }

    /// <summary>WMI 服务重启会导致订阅静默失效，定期重建。</summary>
    private void RebuildSubscription()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            try
            {
                _watcher?.Stop();
                _watcher?.Dispose();
                _watcher = null;

                SubscribeLocked();
                _log.Debug("WMI 订阅已重建");
            }
            catch (Exception ex)
            {
                _watcher = null;
                _log.Warn($"WMI 订阅重建失败，将在下个周期重试：{ex.Message}");
            }
        }
    }

    /// <summary>
    /// WMI 事件回调。运行在 WMI 线程上，必须：
    /// 1) 绝不向外抛异常（否则可能中断订阅）；
    /// 2) 只做轻量投递，重活交给上层 Task。
    /// </summary>
    private void OnEventArrived(object sender, EventArrivedEventArgs e)
    {
        try
        {
            // TargetInstance 是 ManagementBaseObject，必须显式释放
            using var target = e.NewEvent?["TargetInstance"] as ManagementBaseObject;
            if (target is null)
            {
                return;
            }

            if (target["Name"] is not string name || string.IsNullOrEmpty(name))
            {
                return;
            }

            // 不区分大小写比对：统一转小写后按序数比较
            if (!name.ToLowerInvariant().Equals(_targetLower, StringComparison.Ordinal))
            {
                return;
            }

            int pid = 0;
            try
            {
                pid = Convert.ToInt32(target["ProcessId"]);
            }
            catch
            {
                // 拿不到 pid 不影响触发
            }

            _log.Info($"检测到目标进程启动：{name}（pid={pid}）");
            TargetProcessStarted?.Invoke(this, pid);
        }
        catch (Exception ex)
        {
            // 回调内兜住一切异常
            _log.Warn($"WMI 事件处理失败：{ex.Message}");
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            _watchdog?.Dispose();
            _watchdog = null;

            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;

            if (_watcher is not null)
            {
                try
                {
                    _watcher.EventArrived -= OnEventArrived;
                    _watcher.Stop();
                }
                catch
                {
                    // 停止失败不影响释放
                }

                _watcher.Dispose();
                _watcher = null;
            }
        }

        _log.Info("WMI 进程监听已停止");
    }
}
