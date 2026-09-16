using System.Net;
using System.Net.Http.Headers;
using System.Text;
using ZzzAutoSign.Config;
using ZzzAutoSign.Logging;
using ZzzAutoSign.MiHoYo.Models;

namespace ZzzAutoSign.MiHoYo;

/// <summary>一次 HTTP 调用失败时抛出的异常，携带分类信息以便上层决定是否重试。</summary>
public sealed class MiHoYoApiException : Exception
{
    public MiHoYoApiException(string message, HttpStatusCode? statusCode = null, int? retCode = null, Exception? inner = null)
        : base(message, inner)
    {
        StatusCode = statusCode;
        RetCode = retCode;
    }

    public HttpStatusCode? StatusCode { get; }

    /// <summary>米游社业务返回码；HTTP 层失败时为 null。</summary>
    public int? RetCode { get; }
}

/// <summary>
/// 米游社 HTTP 客户端：负责拼装统一请求头（含 DS 签名）并解析外层响应。
/// </summary>
public sealed class MiHoYoHttpClient : IDisposable
{
    private readonly MiHoYoEndpoints _endpoints;
    private readonly CredentialStore _credentials;
    private readonly ILogger _log;
    private readonly HttpClient _http;

    /// <summary>当前签名模式，可在运行时切换（探测后由外部写入）。</summary>
    public SignMode SignMode { get; set; } = SignMode.WebDs1;

    public MiHoYoHttpClient(MiHoYoEndpoints endpoints, CredentialStore credentials, ILogger log,
        int timeoutSeconds = 20)
    {
        _endpoints = endpoints;
        _credentials = credentials;
        _log = log;

        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            UseCookies = false   // Cookie 由我们自己按请求拼装
        };

        _http = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(Math.Clamp(timeoutSeconds, 5, 120))
        };

        // 不设置默认 UA，每个请求按需设置
        _http.DefaultRequestHeaders.Clear();
    }

    /// <summary>GET 请求。返回外层响应对象。</summary>
    public async Task<ApiEnvelope<T>> GetAsync<T>(
        string url,
        IEnumerable<KeyValuePair<string, string>>? queryParams = null,
        string? cookieOverride = null,
        CancellationToken ct = default)
    {
        var pairs = queryParams?.ToList() ?? new List<KeyValuePair<string, string>>();

        string fullUrl = pairs.Count > 0
            ? $"{url}?{string.Join("&", pairs.Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value)}"))}"
            : url;

        // DS2 需要规范化后的 query（键名字母序，原始未转义值）
        string canonicalQuery = pairs.Count > 0 ? DsSigner.CanonicalQuery(pairs) : string.Empty;

        using var req = new HttpRequestMessage(HttpMethod.Get, fullUrl);
        ApplyHeaders(req, canonicalQuery, body: string.Empty, cookieOverride);

        return await SendAsync<T>(req, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// POST JSON 请求。
    /// 传入的 <paramref name="bodyPairs"/> 会以「键名字母序紧凑 JSON」同时用于签名与实际发送，
    /// 保证二者字节完全一致 —— 这是 DS2 能通过校验的前提。
    /// </summary>
    public async Task<ApiEnvelope<T>> PostJsonAsync<T>(
        string url,
        IEnumerable<KeyValuePair<string, string>> bodyPairs,
        string? cookieOverride = null,
        CancellationToken ct = default)
    {
        var pairs = bodyPairs.ToList();
        string canonicalBody = DsSigner.CanonicalJson(pairs);

        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(canonicalBody, Encoding.UTF8, "application/json")
        };

        ApplyHeaders(req, query: string.Empty, canonicalBody, cookieOverride);

        return await SendAsync<T>(req, ct).ConfigureAwait(false);
    }

    /// <summary>不带泛型 data 的请求（仅关心 retcode）。</summary>
    public Task<ApiEnvelope<object>> GetRawAsync(
        string url,
        IEnumerable<KeyValuePair<string, string>>? queryParams = null,
        string? cookieOverride = null,
        CancellationToken ct = default)
        => GetAsync<object>(url, queryParams, cookieOverride, ct);

    private async Task<ApiEnvelope<T>> SendAsync<T>(HttpRequestMessage req, CancellationToken ct)
    {
        HttpResponseMessage resp;
        try
        {
            resp = await _http.SendAsync(req, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            // HttpClient 超时也表现为 TaskCanceledException
            throw new MiHoYoApiException("请求超时", inner: ex);
        }
        catch (HttpRequestException ex)
        {
            throw new MiHoYoApiException($"网络请求失败：{ex.Message}", inner: ex);
        }

        using (resp)
        {
            string text = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            if (resp.StatusCode == HttpStatusCode.TooManyRequests)
            {
                throw new MiHoYoApiException("请求过于频繁（HTTP 429）", resp.StatusCode, ZzzRetCode.TooManyRequests);
            }

            if (!resp.IsSuccessStatusCode)
            {
                throw new MiHoYoApiException(
                    $"HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}",
                    resp.StatusCode);
            }

            ApiEnvelope<T>? envelope;
            try
            {
                envelope = JsonHelper.Deserialize<ApiEnvelope<T>>(text);
            }
            catch (Exception ex)
            {
                throw new MiHoYoApiException($"响应不是合法 JSON：{Truncate(text, 200)}", resp.StatusCode, inner: ex);
            }

            if (envelope is null)
            {
                throw new MiHoYoApiException("响应为空", resp.StatusCode);
            }

            // retcode != 0 时记一条 debug 日志便于排查签名问题
            if (envelope.RetCode != ZzzRetCode.Ok)
            {
                _log.Debug($"接口返回 retcode={envelope.RetCode} message={envelope.Message}");
            }

            return envelope;
        }
    }

    /// <summary>拼装统一请求头。</summary>
    private void ApplyHeaders(HttpRequestMessage req, string query, string body, string? cookieOverride)
    {
        string cookie = cookieOverride ?? _credentials.Current.Cookie;
        string deviceId = EnsureDeviceId();

        string ds = DsSigner.Sign(_endpoints, SignMode, query, body);

        req.Headers.TryAddWithoutValidation("Cookie", cookie);
        req.Headers.TryAddWithoutValidation("DS", ds);
        req.Headers.TryAddWithoutValidation("x-rpc-app_version", _endpoints.AppVersion);
        req.Headers.TryAddWithoutValidation("x-rpc-client_type", DsSigner.ClientTypeOf(SignMode).ToString());
        req.Headers.TryAddWithoutValidation("x-rpc-device_id", deviceId);
        req.Headers.TryAddWithoutValidation("x-rpc-signgame", "zzz");
        req.Headers.TryAddWithoutValidation("x-rpc-device_name", Environment.MachineName);
        req.Headers.TryAddWithoutValidation("x-rpc-device_model", "PC");
        req.Headers.TryAddWithoutValidation("Origin", _endpoints.ActOrigin);
        req.Headers.TryAddWithoutValidation("Referer", _endpoints.ActOrigin + "/");

        if (!string.IsNullOrWhiteSpace(_endpoints.Channel))
        {
            req.Headers.TryAddWithoutValidation("x-rpc-channel", _endpoints.Channel);
        }

        req.Headers.UserAgent.ParseAdd(
            $"Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36 miHoYoBBS/{_endpoints.AppVersion}");
        req.Headers.Accept.ParseAdd("application/json");
    }

    /// <summary>
    /// 设备指纹：首次生成后持久化复用。
    /// 这里用与参考实现一致的 uuid3(NAMESPACE_URL, cookie) 逻辑，保证同一账号设备稳定。
    /// </summary>
    private string EnsureDeviceId()
    {
        string existing = _credentials.Current.DeviceId;
        if (!string.IsNullOrWhiteSpace(existing))
        {
            return existing;
        }

        string seed = _credentials.Current.Cookie;
        if (string.IsNullOrWhiteSpace(seed))
        {
            seed = Guid.NewGuid().ToString("N");
        }

        string generated = Uuid3(seed);
        _credentials.Update(c => c.DeviceId = generated);
        _credentials.Save();
        return generated;
    }

    /// <summary>uuid3：对命名空间 UUID 与名称拼接后取 MD5，并按 RFC 4122 设置版本位。</summary>
    private static string Uuid3(string name)
    {
        var ns = Guid.Parse("6ba7b811-9dad-11d1-80b4-00c04fd430c8"); // NAMESPACE_URL
        byte[] nsBytes = ns.ToByteArray();
        // .NET 的 Guid.ToByteArray 前 4/2/2 字节为小端，需转回网络序
        SwapGuidByteOrder(nsBytes);

        byte[] nameBytes = Encoding.UTF8.GetBytes(name);
        byte[] combined = new byte[nsBytes.Length + nameBytes.Length];
        nsBytes.CopyTo(combined, 0);
        nameBytes.CopyTo(combined, nsBytes.Length);

        byte[] hash = System.Security.Cryptography.MD5.HashData(combined);

        byte[] guidBytes = new byte[16];
        Array.Copy(hash, guidBytes, 16);

        // 设置版本号 3（MD5）与变体位
        guidBytes[6] = (byte)((guidBytes[6] & 0x0F) | 0x30);
        guidBytes[8] = (byte)((guidBytes[8] & 0x3F) | 0x80);

        SwapGuidByteOrder(guidBytes);
        return new Guid(guidBytes).ToString();
    }

    /// <summary>在小端（Guid 内部表示）与网络序之间互换前 8 字节的分组顺序。</summary>
    private static void SwapGuidByteOrder(byte[] b)
    {
        static void Swap(byte[] a, int i, int j) => (a[i], a[j]) = (a[j], a[i]);

        Swap(b, 0, 3);
        Swap(b, 1, 2);
        Swap(b, 4, 5);
        Swap(b, 6, 7);
    }

    private static string Truncate(string s, int max)
        => s.Length <= max ? s : s[..max] + "...";

    public void Dispose() => _http.Dispose();
}
