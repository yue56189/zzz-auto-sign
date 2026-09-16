using ZzzAutoSign.Config;
using ZzzAutoSign.Logging;

namespace ZzzAutoSign.Notify;

/// <summary>
/// 通知统一出口：优先 Toast，失败或不可用时降级到托盘气泡。
/// 由 <see cref="UI.TrayHost"/> 在构造后注入气泡回调。
/// </summary>
public sealed class NotificationService
{
    private readonly ToastNotifier _toast = new();
    private readonly ILogger _log;

    public NotificationService(ILogger log)
    {
        _log = log;
    }

    /// <summary>降级用的气泡提示回调，由托盘注入。</summary>
    public Action<string, string>? BalloonFallback { get; set; }

    /// <summary>发送成功通知。</summary>
    public void NotifySuccess(string body)
        => Send("绝区零签到完成", body, isError: false);

    /// <summary>发送失败通知。</summary>
    public void NotifyFailure(string reason)
        => Send("绝区零签到失败", reason, isError: true);

    private void Send(string title, string body, bool isError)
    {
        _log.Info($"发送通知：{title} / {body}");

        if (_toast.Show(title, body, isError))
        {
            return;
        }

        _log.Warn("Toast 发送失败，降级为托盘气泡提示");

        try
        {
            BalloonFallback?.Invoke(title, body);
        }
        catch (Exception ex)
        {
            _log.Warn($"气泡提示也失败了：{ex.Message}");
        }
    }
}
