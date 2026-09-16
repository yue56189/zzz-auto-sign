using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CredentialGrabber;

/// <summary>
/// 凭证提取工具的解密/写入逻辑。
/// 为了不与主程序产生编译依赖，这里复刻了 credential.bin 的格式：
///   [ASCII "ZZZAS1"] + DPAPI(CurrentUser) 加密的 UTF-8 JSON
/// </summary>
public static class CredentialWriter
{
    private const string BinaryHeader = "ZZZAS1";

    /// <summary>主程序的数据目录。</summary>
    public static string DataDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ZzzAutoSign");

    public static string CredentialFile { get; } = Path.Combine(DataDir, "credential.bin");

    /// <summary>解析出的凭证。</summary>
    public sealed class ParsedCredential
    {
        [JsonPropertyName("cookie")]
        public string Cookie { get; set; } = string.Empty;

        [JsonPropertyName("stoken")]
        public string Stoken { get; set; } = string.Empty;

        [JsonPropertyName("stuid")]
        public string Stuid { get; set; } = string.Empty;

        [JsonPropertyName("mid")]
        public string Mid { get; set; } = string.Empty;

        [JsonPropertyName("deviceId")]
        public string DeviceId { get; set; } = string.Empty;

        [JsonPropertyName("savedAt")]
        public DateTimeOffset SavedAt { get; set; } = DateTimeOffset.UtcNow;

        [JsonPropertyName("lastTokenRefreshAt")]
        public DateTimeOffset? LastTokenRefreshAt { get; set; }
    }

    /// <summary>
    /// 解析用户粘贴的文本。同时支持：
    /// 1) 完整 JSON（F12 脚本的输出）
    /// 2) 裸 Cookie 串（"a=b; c=d"）
    /// </summary>
    public static ParsedCredential Parse(string raw)
    {
        string text = (raw ?? string.Empty).Trim();

        if (text.Length == 0)
        {
            throw new ArgumentException("内容为空");
        }

        // JSON 形态：截取第一个 { 到最后一个 }
        int start = text.IndexOf('{');
        int end = text.LastIndexOf('}');
        if (start >= 0 && end > start)
        {
            string candidate = text[start..(end + 1)];
            try
            {
                var parsed = JsonSerializer.Deserialize<ParsedCredential>(candidate,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (parsed is not null && (!string.IsNullOrWhiteSpace(parsed.Cookie) || !string.IsNullOrWhiteSpace(parsed.Stoken)))
                {
                    Normalize(parsed);
                    return parsed;
                }
            }
            catch (JsonException)
            {
                // 不是合法 JSON，按裸 Cookie 处理
            }
        }

        // 裸 Cookie 形态
        var fromCookie = new ParsedCredential { Cookie = NormalizeCookieString(text) };
        Normalize(fromCookie);

        if (string.IsNullOrWhiteSpace(fromCookie.Cookie))
        {
            throw new ArgumentException("无法解析出 Cookie，请检查粘贴内容");
        }

        return fromCookie;
    }

    /// <summary>补齐缺失字段：从 Cookie 串里提取 stuid / mid / stoken。</summary>
    private static void Normalize(ParsedCredential c)
    {
        c.Cookie = NormalizeCookieString(c.Cookie);

        if (string.IsNullOrWhiteSpace(c.Stuid))
        {
            c.Stuid = Extract(c.Cookie, "stuid")
                      ?? Extract(c.Cookie, "ltuid")
                      ?? Extract(c.Cookie, "account_id")
                      ?? string.Empty;
        }

        if (string.IsNullOrWhiteSpace(c.Mid))
        {
            c.Mid = Extract(c.Cookie, "mid")
                    ?? Extract(c.Cookie, "account_mid_v2")
                    ?? Extract(c.Cookie, "ltmid_v2")
                    ?? string.Empty;
        }

        if (string.IsNullOrWhiteSpace(c.Stoken))
        {
            c.Stoken = Extract(c.Cookie, "stoken") ?? string.Empty;
        }
    }

    /// <summary>把 Cookie 规范化为 "k=v; k=v" 形式，并剔除 Cookie 串里的 stoken（它单独存）。</summary>
    public static string NormalizeCookieString(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var parts = raw
            .Replace("\r", " ")
            .Replace("\n", " ")
            .Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Trim())
            .Where(p =>
            {
                int eq = p.IndexOf('=');
                if (eq <= 0)
                {
                    return false;
                }

                string key = p[..eq].Trim();
                // 不把 stoken 混进 cookie 串
                return !key.Equals("stoken", StringComparison.OrdinalIgnoreCase);
            })
            .ToList();

        return string.Join("; ", parts);
    }

    /// <summary>从 Cookie 串中提取某个键的值。</summary>
    public static string? Extract(string cookie, string key)
    {
        if (string.IsNullOrWhiteSpace(cookie))
        {
            return null;
        }

        foreach (string part in cookie.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            string trimmed = part.Trim();
            int eq = trimmed.IndexOf('=');
            if (eq <= 0)
            {
                continue;
            }

            if (trimmed[..eq].Trim().Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                return trimmed[(eq + 1)..].Trim();
            }
        }

        return null;
    }

    /// <summary>把凭证加密写入主程序的 credential.bin。已存在时先备份。</summary>
    public static void Save(ParsedCredential credential)
    {
        Directory.CreateDirectory(DataDir);

        // 保留已有的 deviceId，避免每次提取都换设备指纹
        var existing = TryLoadExisting();
        if (existing is not null && string.IsNullOrWhiteSpace(credential.DeviceId))
        {
            // 仅当 cookie 未变化时复用
            credential.DeviceId = string.Equals(existing.Cookie, credential.Cookie, StringComparison.Ordinal)
                ? existing.DeviceId
                : string.Empty;
        }

        credential.SavedAt = DateTimeOffset.UtcNow;

        if (File.Exists(CredentialFile))
        {
            try
            {
                File.Copy(CredentialFile, CredentialFile + ".bak", overwrite: true);
            }
            catch
            {
                // 备份失败不阻断
            }
        }

        byte[] plain = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(credential,
            new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            }));

        byte[] cipher = ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser);

        byte[] payload = new byte[BinaryHeader.Length + cipher.Length];
        Encoding.ASCII.GetBytes(BinaryHeader).CopyTo(payload, 0);
        cipher.CopyTo(payload, BinaryHeader.Length);

        // 原子写
        string tmp = CredentialFile + ".tmp";
        File.WriteAllBytes(tmp, payload);
        File.Move(tmp, CredentialFile, overwrite: true);
    }

    /// <summary>读取已有的 credential.bin（用于复用 deviceId）。失败返回 null。</summary>
    public static ParsedCredential? TryLoadExisting()
    {
        try
        {
            if (!File.Exists(CredentialFile))
            {
                return null;
            }

            byte[] payload = File.ReadAllBytes(CredentialFile);
            if (payload.Length <= BinaryHeader.Length)
            {
                return null;
            }

            if (Encoding.ASCII.GetString(payload, 0, BinaryHeader.Length) != BinaryHeader)
            {
                return null;
            }

            byte[] cipher = new byte[payload.Length - BinaryHeader.Length];
            Array.Copy(payload, BinaryHeader.Length, cipher, 0, cipher.Length);

            byte[] plain = ProtectedData.Unprotect(cipher, null, DataProtectionScope.CurrentUser);

            return JsonSerializer.Deserialize<ParsedCredential>(Encoding.UTF8.GetString(plain),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch
        {
            return null;
        }
    }

    /// <summary>生成与主程序一致的设备指纹：uuid3(NAMESPACE_URL, cookie)。</summary>
    public static string GenerateDeviceId(string cookie)
    {
        var ns = Guid.Parse("6ba7b811-9dad-11d1-80b4-00c04fd430c8");
        byte[] nsBytes = ns.ToByteArray();
        Swap(nsBytes);

        byte[] nameBytes = Encoding.UTF8.GetBytes(cookie ?? string.Empty);

        byte[] combined = new byte[nsBytes.Length + nameBytes.Length];
        nsBytes.CopyTo(combined, 0);
        nameBytes.CopyTo(combined, nsBytes.Length);

        byte[] hash = MD5.HashData(combined);
        byte[] guidBytes = new byte[16];
        Array.Copy(hash, guidBytes, 16);

        guidBytes[6] = (byte)((guidBytes[6] & 0x0F) | 0x30);
        guidBytes[8] = (byte)((guidBytes[8] & 0x3F) | 0x80);

        Swap(guidBytes);
        return new Guid(guidBytes).ToString();
    }

    private static void Swap(byte[] b)
    {
        static void S(byte[] a, int i, int j) => (a[i], a[j]) = (a[j], a[i]);
        S(b, 0, 3);
        S(b, 1, 2);
        S(b, 4, 5);
        S(b, 6, 7);
    }
}
