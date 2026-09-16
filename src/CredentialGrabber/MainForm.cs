using System.Reflection;
using System.Text;

namespace CredentialGrabber;

/// <summary>
/// 凭证提取工具主窗口。
/// 提供两条路径：
///   A. 半自动：启动浏览器 → 用户手动登录 → 工具读取 Cookie
///   B. 手动：引导用户在 F12 控制台执行脚本，把输出粘回本窗口
/// </summary>
public sealed class MainForm : Form
{
    private readonly TextBox _logBox = new();
    private readonly TextBox _pasteBox = new();
    private readonly Label _statusLabel = new();

    private Button _browserButton = null!;
    private Button _saveButton = null!;

    public MainForm()
    {
        Text = "米游社凭证提取工具";
        Width = 860;
        Height = 700;
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Microsoft YaHei UI", 9F);

        BuildUi();
        AppendLog("欢迎使用米游社凭证提取工具。");
        AppendLog("本工具不会索取或保存你的账号密码。");
        AppendLog($"凭证将写入：{CredentialWriter.CredentialFile}");
        AppendLog(string.Empty);
        AppendLog("请选择下面两种方式之一获取凭证。");
    }

    private void BuildUi()
    {
        int y = 12;
        const int left = 12;
        int width = ClientSize.Width - 24;

        // ---------------- 说明 ----------------
        var intro = new Label
        {
            Left = left,
            Top = y,
            Width = width,
            Height = 70,
            Text =
                "方式 A（推荐）：点「打开浏览器登录」→ 在弹出的浏览器里正常登录米游社 → 工具自动读取凭证。\n" +
                "方式 B：在你自己已登录的浏览器里按 F12，把工具目录下 console-snippet.js 的内容粘进控制台执行，\n" +
                "　　　　然后把输出的 JSON 粘到下面的输入框，点「解析并保存」。",
            ForeColor = Color.FromArgb(50, 50, 50)
        };
        Controls.Add(intro);
        y += intro.Height + 8;

        // ---------------- 按钮 ----------------
        _browserButton = new Button
        {
            Left = left,
            Top = y,
            Width = 160,
            Height = 34,
            Text = "打开浏览器登录"
        };
        _browserButton.Click += OnBrowserLogin;
        Controls.Add(_browserButton);

        var copyScriptButton = new Button
        {
            Left = left + 170,
            Top = y,
            Width = 160,
            Height = 34,
            Text = "复制控制台脚本"
        };
        copyScriptButton.Click += OnCopyScript;
        Controls.Add(copyScriptButton);

        var openFolderButton = new Button
        {
            Left = left + 340,
            Top = y,
            Width = 140,
            Height = 34,
            Text = "打开数据目录"
        };
        openFolderButton.Click += (_, _) => OpenDataFolder();
        Controls.Add(openFolderButton);

        var testButton = new Button
        {
            Left = left + 490,
            Top = y,
            Width = 140,
            Height = 34,
            Text = "检查已有凭证"
        };
        testButton.Click += OnCheckExisting;
        Controls.Add(testButton);

        y += _browserButton.Height + 12;

        // ---------------- 粘贴区 ----------------
        var pasteLabel = new Label
        {
            Left = left,
            Top = y,
            Width = width,
            Height = 20,
            Text = "方式 B：粘贴凭证（完整 JSON 或裸 Cookie 串均可）"
        };
        Controls.Add(pasteLabel);
        y += pasteLabel.Height + 4;

        _pasteBox.Left = left;
        _pasteBox.Top = y;
        _pasteBox.Width = width;
        _pasteBox.Height = 120;
        _pasteBox.Multiline = true;
        _pasteBox.ScrollBars = ScrollBars.Vertical;
        _pasteBox.PlaceholderText = "在此粘贴凭证，然后点击「解析并保存」";
        Controls.Add(_pasteBox);
        y += _pasteBox.Height + 8;

        _saveButton = new Button
        {
            Left = left,
            Top = y,
            Width = 160,
            Height = 34,
            Text = "解析并保存"
        };
        _saveButton.Click += OnParseAndSave;
        Controls.Add(_saveButton);

        var clearButton = new Button
        {
            Left = left + 170,
            Top = y,
            Width = 100,
            Height = 34,
            Text = "清空"
        };
        clearButton.Click += (_, _) => _pasteBox.Clear();
        Controls.Add(clearButton);

        y += _saveButton.Height + 10;

        // ---------------- 状态 ----------------
        _statusLabel.Left = left;
        _statusLabel.Top = y;
        _statusLabel.Width = width;
        _statusLabel.Height = 24;
        _statusLabel.ForeColor = Color.FromArgb(30, 90, 160);
        _statusLabel.Text = "就绪";
        Controls.Add(_statusLabel);
        y += _statusLabel.Height + 6;

        // ---------------- 日志 ----------------
        var logLabel = new Label
        {
            Left = left,
            Top = y,
            Width = width,
            Height = 20,
            Text = "运行日志"
        };
        Controls.Add(logLabel);
        y += logLabel.Height + 4;

        _logBox.Left = left;
        _logBox.Top = y;
        _logBox.Width = width;
        _logBox.Height = ClientSize.Height - y - 16;
        _logBox.Multiline = true;
        _logBox.ReadOnly = true;
        _logBox.ScrollBars = ScrollBars.Vertical;
        _logBox.BackColor = Color.FromArgb(250, 250, 250);
        _logBox.Font = new Font("Consolas", 9F);
        _logBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
        Controls.Add(_logBox);
    }

    private async void OnBrowserLogin(object? sender, EventArgs e)
    {
        _browserButton.Enabled = false;
        SetStatus("正在启动浏览器…");

        try
        {
            await using var grabber = new PlaywrightGrabber
            {
                Log = msg => BeginInvoke(() => AppendLog(msg))
            };

            var credential = await grabber.RunAsync(TimeSpan.FromMinutes(5));

            if (credential is null)
            {
                AppendLog("未获取到凭证。");
                SetStatus("已取消或未读取到凭证");
                return;
            }

            SaveCredential(credential);
        }
        catch (Exception ex)
        {
            AppendLog($"[错误] {ex.Message}");

            if (ex.Message.Contains("浏览器内核") || ex.Message.Contains("无法启动浏览器"))
            {
                AppendLog("提示：可在本工具目录执行以下命令安装浏览器内核：");
                AppendLog("  .\\playwright.ps1 install chromium");
                AppendLog("或直接改用方式 B（F12 控制台脚本）。");
            }

            SetStatus("失败：" + ex.Message);
        }
        finally
        {
            _browserButton.Enabled = true;
        }
    }

    private void OnCopyScript(object? sender, EventArgs e)
    {
        try
        {
            string script = ReadEmbeddedSnippet();

            if (string.IsNullOrWhiteSpace(script))
            {
                // 退回从磁盘读取（开发态）
                string fallback = Path.Combine(AppContext.BaseDirectory, "assets", "console-snippet.js");
                if (File.Exists(fallback))
                {
                    script = File.ReadAllText(fallback);
                }
            }

            if (string.IsNullOrWhiteSpace(script))
            {
                MessageBox.Show(this, "找不到控制台脚本资源。", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            Clipboard.SetText(script);

            AppendLog("控制台脚本已复制到剪贴板。");
            AppendLog("请到浏览器里按 F12 → Console → 粘贴并回车。");
            SetStatus("脚本已复制，请到浏览器控制台执行");
        }
        catch (Exception ex)
        {
            AppendLog($"[错误] 复制脚本失败：{ex.Message}");
        }
    }

    private void OnParseAndSave(object? sender, EventArgs e)
    {
        try
        {
            var credential = CredentialWriter.Parse(_pasteBox.Text);
            SaveCredential(credential);
        }
        catch (Exception ex)
        {
            AppendLog($"[错误] 解析失败：{ex.Message}");
            SetStatus("解析失败");

            MessageBox.Show(this,
                $"解析失败：{ex.Message}\n\n请确认粘贴的是完整的 JSON，或形如 a=b; c=d 的 Cookie 串。",
                "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OnCheckExisting(object? sender, EventArgs e)
    {
        var existing = CredentialWriter.TryLoadExisting();

        if (existing is null)
        {
            AppendLog("未找到已保存的凭证（或无法解密）。");
            SetStatus("未找到凭证");
            return;
        }

        AppendLog("已找到凭证，摘要如下：");
        AppendLog($"  Cookie 长度：{existing.Cookie.Length}");
        AppendLog($"  stoken：{Mask(existing.Stoken)}");
        AppendLog($"  stuid：{existing.Stuid}");
        AppendLog($"  mid：{Mask(existing.Mid)}");
        AppendLog($"  保存时间：{existing.SavedAt.ToLocalTime():yyyy-MM-dd HH:mm:ss}");

        if (string.IsNullOrWhiteSpace(existing.Stoken))
        {
            AppendLog("  [提醒] 缺少 stoken，cookie_token 过期后无法自动刷新。");
        }

        SetStatus("凭证存在且可解密");
    }

    private void SaveCredential(CredentialWriter.ParsedCredential credential)
    {
        if (string.IsNullOrWhiteSpace(credential.DeviceId))
        {
            credential.DeviceId = CredentialWriter.GenerateDeviceId(credential.Cookie);
        }

        CredentialWriter.Save(credential);

        AppendLog("凭证已加密保存：");
        AppendLog($"  Cookie 长度：{credential.Cookie.Length}");
        AppendLog($"  stoken：{Mask(credential.Stoken)}");
        AppendLog($"  stuid：{credential.Stuid}");
        AppendLog($"  mid：{Mask(credential.Mid)}");
        AppendLog($"  写入位置：{CredentialWriter.CredentialFile}");

        if (string.IsNullOrWhiteSpace(credential.Stoken))
        {
            AppendLog("  [提醒] 未获取到 stoken：cookie_token 大约 1 天就会失效，届时需重新提取。");
            AppendLog("         建议在米游社网页版登录后重新用方式 A 或 B 提取一次。");
        }

        SetStatus("保存成功");

        MessageBox.Show(this,
            "凭证已保存。\n\n请重新启动 ZzzAutoSign.exe（或在其设置里确认）以使其生效。",
            "成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void OpenDataFolder()
    {
        try
        {
            Directory.CreateDirectory(CredentialWriter.DataDir);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = CredentialWriter.DataDir,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            AppendLog($"[错误] 打开目录失败：{ex.Message}");
        }
    }

    /// <summary>读取嵌入资源里的控制台脚本。</summary>
    private static string ReadEmbeddedSnippet()
    {
        var asm = Assembly.GetExecutingAssembly();

        string? name = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("console-snippet.js", StringComparison.OrdinalIgnoreCase));

        if (name is null)
        {
            return string.Empty;
        }

        using var stream = asm.GetManifestResourceStream(name);
        if (stream is null)
        {
            return string.Empty;
        }

        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private void AppendLog(string message)
    {
        if (_logBox.IsDisposed)
        {
            return;
        }

        _logBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
    }

    private void SetStatus(string text)
    {
        if (_statusLabel.IsDisposed)
        {
            return;
        }

        _statusLabel.Text = text;
    }

    private static string Mask(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "(空)";
        }

        return value.Length <= 8 ? "***" : $"{value[..4]}***{value[^4..]}";
    }
}
