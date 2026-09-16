using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ZzzAutoSign.Config;

/// <summary>
/// 统一的 JSON 序列化设置。
/// 注意：<see cref="JsonSerializerOptions"/> 复用是线程安全的（只读使用），因此这里用静态单例。
/// </summary>
public static class JsonHelper
{
    /// <summary>用于本地配置/状态文件：缩进可读，中文不转义。</summary>
    public static readonly JsonSerializerOptions Readable = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// 用于 API 请求体：必须紧凑、键名按原样输出。
    /// DS2 签名要求 body 与「实际发送的字节」完全一致，因此序列化后不允许再加工。
    /// </summary>
    public static readonly JsonSerializerOptions Compact = new()
    {
        WriteIndented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string Serialize<T>(T value, JsonSerializerOptions? options = null)
        => JsonSerializer.Serialize(value, options ?? Readable);

    public static T? Deserialize<T>(string json, JsonSerializerOptions? options = null)
        => JsonSerializer.Deserialize<T>(json, options ?? Readable);
}
