using System.Security.Cryptography;
using System.Text;

namespace ZzzAutoSign.MiHoYo;

/// <summary>签名模式。切换时 salt / client_type / DS 形态三者必须整体配对。</summary>
public enum SignMode
{
    /// <summary>client_type = 5，SaltWeb，DS1。</summary>
    WebDs1 = 0,

    /// <summary>client_type = 2，SaltApp，DS1。</summary>
    AppDs1 = 1,

    /// <summary>SaltX6，DS2（携带规范化后的 body / query）。</summary>
    X6Ds2 = 2
}

/// <summary>
/// 米游社 DS（Dynamic Secret）签名。
/// 签名必须与 salt / x-rpc-app_version / x-rpc-client_type 整体配对，否则服务端返回 retcode = -1。
/// </summary>
public static class DsSigner
{
    private const string RandomChars = "abcdefghijklmnopqrstuvwxyz0123456789";

    /// <summary>该模式对应的 x-rpc-client_type。</summary>
    public static int ClientTypeOf(SignMode mode) => mode switch
    {
        SignMode.WebDs1 => 5,
        SignMode.AppDs1 => 2,
        SignMode.X6Ds2 => 5,
        _ => 5
    };

    /// <summary>
    /// 生成 DS。
    /// </summary>
    /// <param name="endpoints">提供 salt 与版本常量。</param>
    /// <param name="mode">签名模式。</param>
    /// <param name="query">仅 DS2 使用：按键名排序后的 k=v&amp;k=v 串。</param>
    /// <param name="body">仅 DS2 使用：与实际发送字节完全一致的紧凑 JSON。</param>
    /// <param name="timestamp">可注入固定时间戳，便于单元测试。默认取当前秒级时间戳。</param>
    /// <param name="random">可注入固定随机数，便于单元测试。默认随机生成。</param>
    public static string Sign(
        MiHoYoEndpoints endpoints,
        SignMode mode,
        string query = "",
        string body = "",
        long? timestamp = null,
        string? random = null)
    {
        long t = timestamp ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        return mode switch
        {
            SignMode.X6Ds2 => SignDs2(endpoints.SaltX6, t, query, body, random),
            SignMode.AppDs1 => SignDs1(endpoints.SaltApp, t, random),
            _ => SignDs1(endpoints.SaltWeb, t, random)
        };
    }

    /// <summary>
    /// DS1（基础版）：md5($"salt={salt}&amp;t={t}&amp;r={r}")，返回 "{t},{r},{md5}"。
    /// r 为 6 位 [a-z0-9] 随机字符串。
    /// </summary>
    public static string SignDs1(string salt, long t, string? random = null)
    {
        string r = random ?? RandomText(6);
        string raw = $"salt={salt}&t={t}&r={r}";
        return $"{t},{r},{Md5Hex(raw)}";
    }

    /// <summary>
    /// DS2（带 body/query）：md5($"salt={salt}&amp;t={t}&amp;r={r}&amp;b={body}&amp;q={query}")，
    /// 返回 "{t},{r},{md5}"。r 为 100001–200000 的整数。
    /// </summary>
    public static string SignDs2(string salt, long t, string query = "", string body = "", string? random = null)
    {
        // 边界规则：随机到 100000 时官方实现会加 542367。直接取 [100001, 200000] 可自然规避该分支。
        string r = random ?? Random.Shared.Next(100001, 200001).ToString();
        string raw = $"salt={salt}&t={t}&r={r}&b={body}&q={query}";
        return $"{t},{r},{Md5Hex(raw)}";
    }

    /// <summary>
    /// POST body 的规范化：按键名字母序排序后序列化为紧凑 JSON。
    /// 若不做规范化，签名与官方服务端计算结果不一致，会得到 retcode = -1。
    /// </summary>
    public static string CanonicalJson(IEnumerable<KeyValuePair<string, string>> pairs)
    {
        var ordered = pairs.OrderBy(p => p.Key, StringComparer.Ordinal).ToList();
        var sb = new StringBuilder("{");
        for (int i = 0; i < ordered.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(',');
            }

            sb.Append('"').Append(EscapeJson(ordered[i].Key)).Append("\":");
            sb.Append('"').Append(EscapeJson(ordered[i].Value)).Append('"');
        }

        sb.Append('}');
        return sb.ToString();
    }

    /// <summary>GET query 的规范化：按键名字母序排序后以 &amp; 拼接 k=v。</summary>
    public static string CanonicalQuery(IEnumerable<KeyValuePair<string, string>> pairs)
    {
        return string.Join("&", pairs
            .OrderBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => $"{p.Key}={p.Value}"));
    }

    /// <summary>UTF-8 编码后计算 MD5，输出小写十六进制。</summary>
    private static string Md5Hex(string s)
        => Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(s))).ToLowerInvariant();

    private static string RandomText(int len)
    {
        return string.Create(len, RandomChars, static (span, chars) =>
        {
            for (int i = 0; i < span.Length; i++)
            {
                span[i] = chars[Random.Shared.Next(chars.Length)];
            }
        });
    }

    private static string EscapeJson(string s)
        => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
