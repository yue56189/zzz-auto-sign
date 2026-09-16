using Microsoft.Playwright;

namespace CredentialGrabber;

/// <summary>
/// 方案 A：用 Playwright 打开真实浏览器，由用户手动登录米游社，然后读取 Cookie。
/// 
/// 说明：这里**不做**自动填表登录。米哈游通行证的密码登录强制极验（Geetest）行为验证码，
/// 自动化填表既无法通过验证，也会显著提高账号被风控的概率。
/// 本方案让用户在真实的浏览器窗口里正常登录，工具只负责把登录后的 Cookie 读出来。
/// </summary>
public sealed class PlaywrightGrabber : IAsyncDisposable
{
    private IPlaywright? _playwright;
    private IBrowserContext? _context;
    private IBrowser? _browser;

    public Action<string>? Log { get; set; }

    /// <summary>
    /// 启动浏览器并等待用户登录完成。返回 null 表示用户取消或超时。
    /// </summary>
    /// <param name="timeout">等待用户登录的最长时间。</param>
    public async Task<CredentialWriter.ParsedCredential?> RunAsync(TimeSpan timeout)
    {
        _playwright = await Playwright.CreateAsync();

        // 使用系统已安装的 Chrome/Edge，避免额外下载浏览器内核
        _browser = await LaunchAsync(_playwright);

        _context = await _browser.NewContextAsync(new BrowserNewContextOptions
        {
            Locale = "zh-CN"
        });

        var page = await _context.NewPageAsync();

        Log?.Invoke("正在打开米游社登录页…");

        await page.GotoAsync("https://www.miyoushe.com/", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.DOMContentLoaded,
            Timeout = 60000
        });

        Log?.Invoke("请在浏览器窗口中完成登录，登录成功后回到本窗口点击「我已登录」。");

        // 轮询等待用户登录：检测是否出现了关键的登录态 Cookie
        var deadline = DateTime.UtcNow + timeout;
        CredentialWriter.ParsedCredential? credential = null;

        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(2000);

            // Playwright 1.47 起 CookiesAsync 返回 IReadOnlyList<BrowserContextCookiesResult>，
            // 不再是 Cookie[]。
            IReadOnlyList<BrowserContextCookiesResult> cookies;
            try
            {
                cookies = await _context.CookiesAsync(new[] { "https://www.miyoushe.com", "https://user.mihoyo.com" });
            }
            catch
            {
                continue;
            }

            bool hasLogin = cookies.Any(c =>
                c.Name.Equals("cookie_token", StringComparison.OrdinalIgnoreCase) ||
                c.Name.Equals("cookie_token_v2", StringComparison.OrdinalIgnoreCase) ||
                c.Name.Equals("ltoken", StringComparison.OrdinalIgnoreCase) ||
                c.Name.Equals("stoken", StringComparison.OrdinalIgnoreCase));

            if (hasLogin)
            {
                Log?.Invoke("检测到登录态，正在读取凭证…");
                credential = BuildFromCookies(cookies);

                if (!string.IsNullOrWhiteSpace(credential.Cookie))
                {
                    break;
                }
            }

            // 浏览器被用户关掉就退出
            if (_browser is null || !_browser.IsConnected)
            {
                Log?.Invoke("浏览器已关闭，操作取消。");
                return null;
            }
        }

        if (credential is null || string.IsNullOrWhiteSpace(credential.Cookie))
        {
            Log?.Invoke("未读取到有效凭证（可能未完成登录或超时）。");
            return null;
        }

        return credential;
    }

    /// <summary>优先用系统 Chrome / Edge，其次退回 Playwright 自带 Chromium。</summary>
    private async Task<IBrowser> LaunchAsync(IPlaywright playwright)
    {
        var launchOptions = new BrowserTypeLaunchOptions
        {
            Headless = false,
            Args = new[] { "--start-maximized" }
        };

        foreach (string channel in new[] { "chrome", "msedge" })
        {
            try
            {
                Log?.Invoke($"尝试启动系统浏览器：{channel}");

                return await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
                {
                    Channel = channel,
                    Headless = false,
                    Args = new[] { "--start-maximized" }
                });
            }
            catch (PlaywrightException)
            {
                // 该浏览器未安装，换下一个
            }
        }

        Log?.Invoke("未找到系统 Chrome/Edge，尝试使用内置 Chromium…");
        try
        {
            return await playwright.Chromium.LaunchAsync(launchOptions);
        }
        catch (PlaywrightException ex)
        {
            throw new InvalidOperationException(
                "无法启动浏览器。请先安装 Chrome 或 Edge；若使用内置 Chromium，" +
                "请在工具目录执行 .\\playwright.ps1 install chromium 安装浏览器内核。\n" +
                $"原始错误：{ex.Message}", ex);
        }
    }

    /// <summary>把 Playwright 读到的 Cookie 列表整理成凭证对象。</summary>
    private static CredentialWriter.ParsedCredential BuildFromCookies(IEnumerable<BrowserContextCookiesResult> cookies)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var c in cookies)
        {
            if (!string.IsNullOrWhiteSpace(c.Name) && !string.IsNullOrWhiteSpace(c.Value) && !map.ContainsKey(c.Name))
            {
                map[c.Name] = c.Value;
            }
        }

        string Get(params string[] names)
        {
            foreach (string n in names)
            {
                if (map.TryGetValue(n, out string? v) && !string.IsNullOrWhiteSpace(v))
                {
                    return v;
                }
            }

            return string.Empty;
        }

        string stoken = Get("stoken");

        // cookie 串里排除 stoken（单独存）
        var parts = map
            .Where(kv => !kv.Key.Equals("stoken", StringComparison.OrdinalIgnoreCase))
            .Select(kv => $"{kv.Key}={kv.Value}");

        return new CredentialWriter.ParsedCredential
        {
            Cookie = string.Join("; ", parts),
            Stoken = stoken,
            Stuid = Get("stuid", "ltuid", "account_id", "ltuid_v2", "account_id_v2"),
            Mid = Get("mid", "account_mid_v2", "ltmid_v2")
        };
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_context is not null)
            {
                await _context.CloseAsync();
            }
        }
        catch
        {
            // 忽略
        }

        try
        {
            if (_browser is not null)
            {
                await _browser.CloseAsync();
            }
        }
        catch
        {
            // 忽略
        }

        _playwright?.Dispose();
    }
}
