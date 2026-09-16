using Microsoft.Toolkit.Uwp.Notifications;
using ZzzAutoSign.Config;
using ZzzAutoSign.Notify;

namespace ZzzAutoSign;

internal static class Program
{
    private const string MutexName = @"Global\ZzzAutoSign_Singleton";

    [STAThread]
    private static void Main(string[] args)
    {
        // 最早期设置 AUMID：Toast 通知的归属依赖它
        try
        {
            ToastNotificationManagerCompat.SetCurrentAppUserModelId(AppPaths.AppUserModelId);
        }
        catch
        {
            // 设置失败不阻断启动，后续通知会降级
        }

        AppPaths.EnsureCreated();

        // 单实例：第二个实例直接退出（避免重复监听与重复签到）
        using var mutex = new Mutex(initiallyOwned: true, MutexName, out bool isNewInstance);
        if (!isNewInstance)
        {
            return;
        }

        ApplicationConfiguration.Initialize();

        // 确保开始菜单快捷方式存在，否则自定义 AUMID 的 Toast 可能不显示
        try
        {
            var bootLog = new Logging.FileLogger(AppPaths.LogDir);
            AumidHelper.EnsureShortcut(bootLog);
        }
        catch
        {
            // 快捷方式创建失败不影响主流程
        }

        using var host = new AppContextHost(args);
        host.Start();

        Application.Run(host.ApplicationContext);

        // 托盘退出后清理 Toast 注册的 COM 组件
        try
        {
            ToastNotificationManagerCompat.Uninstall();
        }
        catch
        {
            // 忽略
        }
    }
}
