using System.Diagnostics;
using ZzzAutoSign.Config;
using ZzzAutoSign.Core;
using ZzzAutoSign.Logging;
using ZzzAutoSign.Notify;
using ZzzAutoSign.Startup;

namespace ZzzAutoSign.UI;

/// <summary>
/// 托盘宿主：NotifyIcon + 右键菜单。
/// 程序无主窗口，托盘是唯一的常驻可见入口。
/// </summary>
public sealed class TrayHost : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly AppSettings _settings;
    private readonly ILogger _log;
    private readonly NotificationService _notify;
    private readonly AutoStartManager _autoStart;
    private readonly Func<Task> _manualSign;
    private readonly DailyGate _gate;
    private readonly ProcessWatcher _watcher;

    private readonly ToolStripMenuItem _autoStartItem;
    private readonly ToolStripMenuItem _statusItem;

    public TrayHost(
        AppSettings settings,
        ILogger log,
        NotificationService notify,
        AutoStartManager autoStart,
        DailyGate gate,
        ProcessWatcher watcher,
        Func<Task> manualSign)
    {
        _settings = settings;
        _log = log;
        _notify = notify;
        _autoStart = autoStart;
        _gate = gate;
        _watcher = watcher;
        _manualSign = manualSign;

        _statusItem = new ToolStripMenuItem("状态：启动中…") { Enabled = false };
        _autoStartItem = new ToolStripMenuItem("开机自启")
        {
            CheckOnClick = true,
            Checked = autoStart.IsEnabled()
        };
        _autoStartItem.Click += OnToggleAutoStart;

        var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripMenuItem("立即签到", null, async (_, _) => await SafeManualSign()));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_statusItem);
        menu.Items.Add(new ToolStripMenuItem("设置…", null, (_, _) => OpenSettings()));
        menu.Items.Add(new ToolStripMenuItem("打开日志", null, (_, _) => OpenLog()));
        menu.Items.Add(_autoStartItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("测试通知", null, (_, _) => TestNotification()));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("退出", null, (_, _) => ExitApp()));

        _icon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = AppPaths.DisplayName,
            Visible = true,
            ContextMenuStrip = menu
        };

        _icon.DoubleClick += (_, _) => OpenSettings();

        // 注入气泡降级通道
        _notify.BalloonFallback = ShowBalloon;

        _watcher.TargetProcessStarted += (_, _) => RefreshStatus();
        RefreshStatus();

        // 无凭证时在启动后提醒一次
        if (!File.Exists(AppPaths.CredentialFile))
        {
            _icon.BalloonTipIcon = ToolTipIcon.Warning;
            _icon.BalloonTipTitle = AppPaths.DisplayName;
            _icon.BalloonTipText = "尚未配置登录凭证，请右键托盘图标 → 设置。";
            _icon.ShowBalloonTip(8000);
        }
    }

    /// <summary>刷新菜单里的状态行。</summary>
    public void RefreshStatus()
    {
        if (_icon.InvokeRequired)
        {
            _icon.BeginInvoke(RefreshStatus);
            return;
        }

        var state = _gate.Snapshot();
        string listening = _watcher.IsRunning ? $"监听 {_settings.TargetProcessName}" : "监听未启动";
        string today = DailyGate.Today;

        string status = state.LastSuccessDate == today
            ? $"今日已签到（{today}），{listening}"
            : $"今日未签到，已尝试 {state.AttemptCount} 次，{listening}";

        _statusItem.Text = "状态：" + status;
        _icon.Text = Truncate(AppPaths.DisplayName + " - " + status, 63);

        _autoStartItem.Checked = _autoStart.IsEnabled();
    }

    private async Task SafeManualSign()
    {
        try
        {
            await _manualSign().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.Error("手动签到失败", ex);
            _notify.NotifyFailure(ex.Message);
        }
        finally
        {
            RefreshStatus();
        }
    }

    private void OnToggleAutoStart(object? sender, EventArgs e)
    {
        try
        {
            if (_autoStartItem.Checked)
            {
                bool ok = _autoStart.Enable();
                _settings.AutoStart = ok;
                if (!ok)
                {
                    _autoStartItem.Checked = false;
                }
            }
            else
            {
                _autoStart.Disable();
                _settings.AutoStart = false;
            }

            _settings.Save(AppPaths.SettingsFile);
        }
        catch (Exception ex)
        {
            _log.Error("切换开机自启失败", ex);
        }
    }

    private void TestNotification()
    {
        _notify.NotifySuccess("这是一条测试通知，收到即表示通知功能正常。");
    }

    private void OpenSettings()
    {
        try
        {
            using var form = new SettingsForm(_settings, _log, _autoStart, _gate);
            form.ShowDialog();
            RefreshStatus();
        }
        catch (Exception ex)
        {
            _log.Error("打开设置窗口失败", ex);
        }
    }

    private void OpenLog()
    {
        try
        {
            string dir = AppPaths.LogDir;
            Directory.CreateDirectory(dir);

            // 优先用默认文本编辑器打开今天的日志
            string current = Path.Combine(dir, $"zzz-autosign-{DateTime.Now:yyyy-MM-dd}.log");
            if (File.Exists(current))
            {
                Process.Start(new ProcessStartInfo(current) { UseShellExecute = true });
            }
            else
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
            }
        }
        catch (Exception ex)
        {
            _log.Error("打开日志失败", ex);
        }
    }

    private void ShowBalloon(string title, string body)
    {
        if (_icon.InvokeRequired)
        {
            _icon.BeginInvoke(() => ShowBalloon(title, body));
            return;
        }

        _icon.BalloonTipIcon = ToolTipIcon.Info;
        _icon.BalloonTipTitle = title;
        _icon.BalloonTipText = body;
        _icon.ShowBalloonTip(6000);
    }

    private void ExitApp()
    {
        _log.Info("用户从托盘菜单退出程序");
        _icon.Visible = false;
        Application.Exit();
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
