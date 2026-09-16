using Microsoft.Win32;
using ZzzAutoSign.Config;
using ZzzAutoSign.Logging;

namespace ZzzAutoSign.Startup;

/// <summary>
/// 开机自启管理。
/// 提供两种方式：
/// 1) HKCU\...\Run 注册表项（简单，但每次开机可能触发 UAC，因为本程序要求管理员权限）；
/// 2) 计划任务（「登录时」触发 + 最高权限运行，可避免 UAC 弹窗，推荐）。
/// </summary>
public sealed class AutoStartManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = AppPaths.AppName;
    private const string TaskName = AppPaths.AppName;

    private readonly ILogger _log;
    private readonly bool _useScheduledTask;

    public AutoStartManager(ILogger log, bool useScheduledTask = true)
    {
        _log = log;
        _useScheduledTask = useScheduledTask;
    }

    /// <summary>当前是否已启用自启。</summary>
    public bool IsEnabled()
    {
        if (_useScheduledTask)
        {
            if (ScheduledTaskExists())
            {
                return true;
            }

            // 计划任务不存在时也检查注册表，兼容旧配置
        }

        return RunKeyExists();
    }

    /// <summary>启用自启。优先创建计划任务，失败则退回注册表。</summary>
    public bool Enable()
    {
        string? exe = AppPaths.ExecutablePath;
        if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe))
        {
            _log.Warn("无法确定自身路径，启用自启失败");
            return false;
        }

        if (_useScheduledTask && CreateScheduledTask(exe))
        {
            // 计划任务生效后清掉注册表项，避免双重启动
            RemoveRunKey();
            _log.Info("已通过计划任务启用开机自启");
            return true;
        }

        bool ok = SetRunKey(exe);
        _log.Info(ok ? "已通过注册表启用开机自启" : "启用开机自启失败");
        return ok;
    }

    /// <summary>禁用自启（两种方式都清理）。</summary>
    public bool Disable()
    {
        bool a = DeleteScheduledTask();
        bool b = RemoveRunKey();
        _log.Info("已禁用开机自启");
        return a || b;
    }

    // ---------------- 注册表方式 ----------------

    private bool RunKeyExists()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return key?.GetValue(RunValueName) is string s && !string.IsNullOrWhiteSpace(s);
        }
        catch
        {
            return false;
        }
    }

    private bool SetRunKey(string exe)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            if (key is null)
            {
                return false;
            }

            // 路径可能含空格，必须加引号
            key.SetValue(RunValueName, $"\"{exe}\" --autostart", RegistryValueKind.String);
            return true;
        }
        catch (Exception ex)
        {
            _log.Warn($"写入注册表自启项失败：{ex.Message}");
            return false;
        }
    }

    private bool RemoveRunKey()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            if (key?.GetValue(RunValueName) is not null)
            {
                key.DeleteValue(RunValueName, throwOnMissingValue: false);
                return true;
            }
        }
        catch (Exception ex)
        {
            _log.Warn($"清理注册表自启项失败：{ex.Message}");
        }

        return false;
    }

    // ---------------- 计划任务方式 ----------------

    private bool ScheduledTaskExists()
    {
        return RunSchTasks($"/Query /TN \"{TaskName}\"") == 0;
    }

    /// <summary>
    /// 创建计划任务：登录时触发、最高权限运行，从而避免每次开机弹 UAC。
    /// 使用 schtasks.exe，避免额外依赖 TaskScheduler COM。
    /// </summary>
    private bool CreateScheduledTask(string exe)
    {
        // /RL HIGHEST 表示以最高权限运行；/F 覆盖同名任务
        string args = $"/Create /TN \"{TaskName}\" /TR \"\\\"{exe}\\\" --autostart\" " +
                      "/SC ONLOGON /RL HIGHEST /F";

        int code = RunSchTasks(args);
        if (code == 0)
        {
            return true;
        }

        _log.Warn($"创建计划任务失败（退出码 {code}），将退回注册表方式");
        return false;
    }

    private bool DeleteScheduledTask()
    {
        return RunSchTasks($"/Delete /TN \"{TaskName}\" /F") == 0;
    }

    private int RunSchTasks(string arguments)
    {
        try
        {
            using var proc = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });

            if (proc is null)
            {
                return -1;
            }

            // 必须读完输出，否则可能死锁
            proc.StandardOutput.ReadToEnd();
            proc.StandardError.ReadToEnd();

            if (!proc.WaitForExit(15_000))
            {
                try
                {
                    proc.Kill(entireProcessTree: true);
                }
                catch
                {
                    // 忽略
                }

                return -1;
            }

            return proc.ExitCode;
        }
        catch (Exception ex)
        {
            _log.Warn($"执行 schtasks 失败：{ex.Message}");
            return -1;
        }
    }
}
