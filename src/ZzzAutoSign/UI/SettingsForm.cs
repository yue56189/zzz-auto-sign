using ZzzAutoSign.Config;
using ZzzAutoSign.Core;
using ZzzAutoSign.Logging;
using ZzzAutoSign.Startup;

namespace ZzzAutoSign.UI;

/// <summary>设置窗口：录入凭证、调整参数、重置今日状态。</summary>
public sealed class SettingsForm : Form
{
    private readonly AppSettings _settings;
    private readonly ILogger _log;
    private readonly AutoStartManager _autoStart;
    private readonly DailyGate _gate;

    private readonly TextBox _cookieBox = new();
    private readonly TextBox _stokenBox = new();
    private readonly TextBox _stuidBox = new();
    private readonly TextBox _midBox = new();
    private readonly ComboBox _signModeBox = new();
    private readonly NumericUpDown _maxAttempts = new();
    private readonly CheckBox _autoStartCheck = new();
    private readonly Label _statusLabel = new();

    public SettingsForm(AppSettings settings, ILogger log, AutoStartManager autoStart, DailyGate gate)
    {
        _settings = settings;
        _log = log;
        _autoStart = autoStart;
        _gate = gate;

        Text = $"{AppPaths.DisplayName} - 设置";
        Width = 720;
        Height = 620;
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Font = new Font("Microsoft YaHei UI", 9F);

        BuildUi();
        LoadValues();
    }

    private void BuildUi()
    {
        int y = 16;
        const int labelWidth = 110;
        const int left = 16;

        // ---- 向导提示 ----
        var tip = new Label
        {
            Left = left,
            Top = y,
            Width = ClientSize.Width - 40,
            Height = 62,
            Text = "如何获取凭证：\n" +
                   "1. 运行同目录下的 CredentialGrabber.exe，按提示在浏览器里登录米游社；\n" +
                   "2. 工具会把 cookie / stoken 复制到剪贴板，粘贴到下面的输入框即可。\n" +
                   "程序不会保存你的账号密码。",
            ForeColor = Color.FromArgb(60, 60, 60)
        };
        Controls.Add(tip);
        y += tip.Height + 10;

        // ---- Cookie ----
        AddLabel("Cookie（必填）", left, y, labelWidth);
        _cookieBox.Left = left + labelWidth;
        _cookieBox.Top = y;
        _cookieBox.Width = ClientSize.Width - left - labelWidth - 40;
        _cookieBox.Height = 60;
        _cookieBox.Multiline = true;
        _cookieBox.ScrollBars = ScrollBars.Vertical;
        _cookieBox.PlaceholderText = "cookie_token=...; ltoken=...; ltuid=...; mid=...";
        Controls.Add(_cookieBox);
        y += _cookieBox.Height + 10;

        // ---- stoken ----
        AddLabel("stoken（强烈建议）", left, y, labelWidth);
        _stokenBox.Left = left + labelWidth;
        _stokenBox.Top = y;
        _stokenBox.Width = ClientSize.Width - left - labelWidth - 40;
        _stokenBox.PlaceholderText = "长效令牌，用于 cookie_token 失效后自动刷新（约 30 天有效）";
        Controls.Add(_stokenBox);
        y += _stokenBox.Height + 10;

        // ---- stuid / mid ----
        AddLabel("stuid / ltuid", left, y, labelWidth);
        _stuidBox.Left = left + labelWidth;
        _stuidBox.Top = y;
        _stuidBox.Width = 200;
        _stuidBox.PlaceholderText = "数字 UID";
        Controls.Add(_stuidBox);

        var midLabel = new Label { Left = left + labelWidth + 220, Top = y + 3, Width = 40, Text = "mid" };
        Controls.Add(midLabel);

        _midBox.Left = left + labelWidth + 262;
        _midBox.Top = y;
        _midBox.Width = 200;
        _midBox.PlaceholderText = "v2_stoken 必填";
        Controls.Add(_midBox);
        y += _midBox.Height + 14;

        // ---- 签名模式 ----
        AddLabel("签名模式", left, y, labelWidth);
        _signModeBox.Left = left + labelWidth;
        _signModeBox.Top = y;
        _signModeBox.Width = 320;
        _signModeBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _signModeBox.Items.AddRange(new object[]
        {
            "0 - Web DS1（默认，client_type=5）",
            "1 - App DS1（client_type=2）",
            "2 - X6 DS2（带 body/query 签名）"
        });
        Controls.Add(_signModeBox);

        var probeHint = new Label
        {
            Left = left + labelWidth + 330,
            Top = y + 3,
            Width = 220,
            Text = "返回“签名无效”时逐个切换试试",
            ForeColor = Color.Gray
        };
        Controls.Add(probeHint);
        y += _signModeBox.Height + 10;

        // ---- 重试上限 ----
        AddLabel("每日最多尝试", left, y, labelWidth);
        _maxAttempts.Left = left + labelWidth;
        _maxAttempts.Top = y;
        _maxAttempts.Width = 80;
        _maxAttempts.Minimum = 0;
        _maxAttempts.Maximum = 20;
        Controls.Add(_maxAttempts);

        var attemptHint = new Label
        {
            Left = left + labelWidth + 90,
            Top = y + 3,
            Width = 400,
            Text = "0 表示不限制；超过上限后当天不再自动重试",
            ForeColor = Color.Gray
        };
        Controls.Add(attemptHint);
        y += _maxAttempts.Height + 10;

        // ---- 开机自启 ----
        _autoStartCheck.Left = left + labelWidth;
        _autoStartCheck.Top = y;
        _autoStartCheck.Width = 400;
        _autoStartCheck.Text = "开机自启（后台常驻，监听游戏启动）";
        Controls.Add(_autoStartCheck);
        y += _autoStartCheck.Height + 14;

        // ---- 状态 ----
        _statusLabel.Left = left;
        _statusLabel.Top = y;
        _statusLabel.Width = ClientSize.Width - 40;
        _statusLabel.Height = 40;
        _statusLabel.ForeColor = Color.FromArgb(30, 90, 160);
        Controls.Add(_statusLabel);
        y += _statusLabel.Height + 6;

        // ---- 按钮 ----
        int buttonY = ClientSize.Height - 56;
        int bx = ClientSize.Width - 40 - 90;

        AddButton("保存", bx, buttonY, 90, OnSave);
        bx -= 100;
        AddButton("测试签到", bx, buttonY, 90, OnTestSign);
        bx -= 100;
        AddButton("重置今日", bx, buttonY, 90, OnResetToday);
        bx -= 100;
        AddButton("取消", bx, buttonY, 90, (_, _) => Close());
    }

    private void AddLabel(string text, int left, int top, int width)
    {
        Controls.Add(new Label
        {
            Left = left,
            Top = top + 3,
            Width = width,
            Text = text
        });
    }

    private void AddButton(string text, int left, int top, int width, EventHandler handler)
    {
        var btn = new Button
        {
            Left = left,
            Top = top,
            Width = width,
            Height = 30,
            Text = text
        };
        btn.Click += handler;
        Controls.Add(btn);
    }

    private void LoadValues()
    {
        var store = new CredentialStore(AppPaths.CredentialFile);
        var cred = store.Current;

        _cookieBox.Text = cred.Cookie;
        _stokenBox.Text = cred.Stoken;
        _stuidBox.Text = cred.Stuid;
        _midBox.Text = cred.Mid;

        _signModeBox.SelectedIndex = Math.Clamp(_settings.PreferredSignMode, 0, 2);
        _maxAttempts.Value = Math.Clamp(_settings.MaxAttemptsPerDay, 0, 20);
        _autoStartCheck.Checked = _autoStart.IsEnabled();

        var state = _gate.Snapshot();
        _statusLabel.Text = $"上次成功：{state.LastSuccessDate ?? "从未"}　" +
                            $"今日尝试：{state.AttemptCount} 次　" +
                            $"最近结果：{state.LastResult ?? "-"}";
    }

    private void OnSave(object? sender, EventArgs e)
    {
        try
        {
            // 校验必填
            if (string.IsNullOrWhiteSpace(_cookieBox.Text))
            {
                MessageBox.Show(this, "Cookie 不能为空。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var store = new CredentialStore(AppPaths.CredentialFile);

            string cookie = NormalizeCookie(_cookieBox.Text);
            string stoken = _stokenBox.Text.Trim();
            string stuid = _stuidBox.Text.Trim();
            string mid = _midBox.Text.Trim();

            // 未填 stuid 时尝试从 cookie 里提取
            if (string.IsNullOrWhiteSpace(stuid))
            {
                stuid = SignOrchestrator.TryExtractStuid(cookie);
            }

            if (string.IsNullOrWhiteSpace(mid))
            {
                mid = SignOrchestrator.TryExtractCookieValue(cookie, "mid") ?? string.Empty;
            }

            store.Update(c =>
            {
                c.Cookie = cookie;
                c.Stoken = stoken;
                c.Stuid = stuid;
                c.Mid = mid;

                // Cookie 变化时重新生成设备指纹
                if (!string.Equals(c.Cookie, cookie, StringComparison.Ordinal))
                {
                    c.DeviceId = string.Empty;
                }
            });

            if (!store.Save())
            {
                MessageBox.Show(this, "凭证加密写入失败，请检查磁盘权限。", "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            _settings.PreferredSignMode = _signModeBox.SelectedIndex;
            _settings.MaxAttemptsPerDay = (int)_maxAttempts.Value;

            // 自启开关
            if (_autoStartCheck.Checked)
            {
                _autoStart.Enable();
                _settings.AutoStart = true;
            }
            else
            {
                _autoStart.Disable();
                _settings.AutoStart = false;
            }

            _settings.Save(AppPaths.SettingsFile);

            _log.Info("设置已保存");

            MessageBox.Show(this, "已保存。凭证变更后建议重启程序使其完全生效。", "成功",
                MessageBoxButtons.OK, MessageBoxIcon.Information);

            LoadValues();
        }
        catch (Exception ex)
        {
            _log.Error("保存设置失败", ex);
            MessageBox.Show(this, $"保存失败：{ex.Message}", "错误",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async void OnTestSign(object? sender, EventArgs e)
    {
        // 先保存再测，避免测的是旧配置
        OnSave(sender, e);

        _statusLabel.Text = "正在测试签到，请稍候…";

        try
        {
            var log = _log;
            var settings = AppSettings.LoadOrDefaults(AppPaths.SettingsFile);
            var endpoints = MiHoYo.MiHoYoEndpoints.LoadWithOverride(AppPaths.EndpointsOverrideFile);
            var creds = new CredentialStore(AppPaths.CredentialFile);
            var gate = _gate;

            using var http = new MiHoYo.MiHoYoHttpClient(endpoints, creds, log, settings.HttpTimeoutSeconds);
            var signApi = new MiHoYo.ZzzSignApi(http, endpoints, log);
            var bindingApi = new MiHoYo.BindingApi(http, endpoints, log);
            var tokenApi = new MiHoYo.TokenApi(http, endpoints, log);

            var orchestrator = new SignOrchestrator(settings, creds, gate, http, signApi, bindingApi, tokenApi, log);

            // 测试用：绕过每日闸门以验证接口连通性
            var result = await orchestrator.RunAsync("手动测试", bypassGate: true);

            _statusLabel.Text = $"测试结果：{result.Title} - {result.Body}";
        }
        catch (Exception ex)
        {
            _log.Error("测试签到失败", ex);
            _statusLabel.Text = $"测试失败：{ex.Message}";
        }
    }

    private void OnResetToday(object? sender, EventArgs e)
    {
        _gate.ResetToday();
        LoadValues();
        MessageBox.Show(this, "已重置今日状态。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    /// <summary>规范化 Cookie 串：去掉换行与多余空白。</summary>
    private static string NormalizeCookie(string raw)
    {
        return string.Join("; ",
            raw.Replace("\r", " ").Replace("\n", " ")
                .Split(';', StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Trim())
                .Where(p => p.Length > 0));
    }
}
