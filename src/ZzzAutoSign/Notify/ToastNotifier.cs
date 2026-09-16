using Microsoft.Toolkit.Uwp.Notifications;

namespace ZzzAutoSign.Notify;

/// <summary>
/// Windows Toast 通知。
/// 使用 CommunityToolkit.WinUI.Notifications 的 Notifier 形式（无需 COM 注册，单文件 exe 友好）。
/// 关键：必须在启动最早期调用 SetCurrentAppUserModelId，否则通知可能静默不显示。
/// </summary>
public sealed class ToastNotifier
{
    /// <summary>
    /// 发送一条 Toast 通知。失败时返回 false，调用方应降级到气泡提示。
    /// </summary>
    /// <param name="title">标题。</param>
    /// <param name="body">正文。</param>
    /// <param name="isError">是否作为错误提示（附加一行提示文本）。</param>
    public bool Show(string title, string body, bool isError = false)
    {
        try
        {
            var builder = new ToastContentBuilder()
                .AddText(title)
                .AddText(body);

            if (isError)
            {
                builder.AddText("点击托盘图标可查看日志");
            }

            builder.Show();
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Toast 发送失败：{ex.Message}");
            return false;
        }
    }
}
