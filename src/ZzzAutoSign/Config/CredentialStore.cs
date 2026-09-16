using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace ZzzAutoSign.Config;

/// <summary>
/// 敏感凭证的持久化载体。序列化后用 DPAPI(CurrentUser) 加密写入 credential.bin。
/// </summary>
public sealed class CredentialData
{
    /// <summary>完整 Cookie 串（含 cookie_token / ltoken / ltuid 或 stuid / mid）。</summary>
    public string Cookie { get; set; } = string.Empty;

    /// <summary>
    /// 长效令牌（约 30 天）。用于在 cookie_token（约 1 天）失效时自动换取新的 cookie_token。
    /// 可能带有 "v2_" 前缀，v2 形态需要 <see cref="Mid"/> 配合。
    /// </summary>
    public string Stoken { get; set; } = string.Empty;

    /// <summary>stuid / ltuid / account_id，任取其一即可，用于拼装 stoken 形式的 Cookie 与查询角色。</summary>
    public string Stuid { get; set; } = string.Empty;

    /// <summary>v2_stoken 必需。</summary>
    public string Mid { get; set; } = string.Empty;

    /// <summary>设备指纹，首次生成后固定复用，避免频繁变化被判异常。</summary>
    public string DeviceId { get; set; } = string.Empty;

    /// <summary>凭证录入时间（UTC）。</summary>
    public DateTimeOffset SavedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>上次成功用 stoken 换取新 cookie_token 的时间（UTC）。</summary>
    public DateTimeOffset? LastTokenRefreshAt { get; set; }

    public bool IsStokenV2 => Stoken.StartsWith("v2_", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// 凭证存储：DPAPI 加密（用户作用域）读写 credential.bin。
/// 换用户或换机器后无法解密，这是有意的安全属性。
/// </summary>
public sealed class CredentialStore
{
    private const string BinaryHeader = "ZZZAS1";   // 格式版本头，便于将来迁移

    private readonly string _path;
    private readonly object _sync = new();
    private CredentialData _current = new();

    public CredentialStore(string path)
    {
        _path = path;
        _current = Load() ?? new CredentialData();
    }

    /// <summary>当前凭证（内存副本，直接修改后请调用 <see cref="Save"/>）。</summary>
    public CredentialData Current
    {
        get
        {
            lock (_sync)
            {
                return _current;
            }
        }
    }

    /// <summary>内存中是否存在可用的 cookie（不校验有效性）。</summary>
    public bool HasCookie
    {
        get
        {
            lock (_sync)
            {
                return !string.IsNullOrWhiteSpace(_current.Cookie);
            }
        }
    }

    /// <summary>内存中是否存在可用于刷新的 stoken。</summary>
    public bool HasStoken
    {
        get
        {
            lock (_sync)
            {
                return !string.IsNullOrWhiteSpace(_current.Stoken);
            }
        }
    }

    public void Update(Action<CredentialData> mutate)
    {
        lock (_sync)
        {
            mutate(_current);
        }
    }

    public void Replace(CredentialData data)
    {
        lock (_sync)
        {
            _current = data;
        }
    }

    /// <summary>加密落盘。失败时静默返回 false（调用方决定是否提示）。</summary>
    public bool Save()
    {
        lock (_sync)
        {
            try
            {
                _current.SavedAt = _current.SavedAt == default ? DateTimeOffset.UtcNow : _current.SavedAt;

                byte[] plain = Encoding.UTF8.GetBytes(JsonHelper.Serialize(_current));

                byte[] cipher;
                try
                {
                    // DPAPI：仅当前用户可解密
                    cipher = ProtectedData.Protect(plain, optionalEntropy: null, DataProtectionScope.CurrentUser);
                }
                catch (PlatformNotSupportedException)
                {
                    // 非 Windows 环境（例如单测）降级为明文，仅在测试里出现
                    cipher = plain;
                }

                byte[] payload = new byte[BinaryHeader.Length + cipher.Length];
                Encoding.ASCII.GetBytes(BinaryHeader).CopyTo(payload, 0);
                cipher.CopyTo(payload, BinaryHeader.Length);

                AtomicFile.WriteAllBytes(_path, payload);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>读取并解密。文件不存在或无法解密时返回 null。</summary>
    private CredentialData? Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return null;
            }

            byte[] payload = File.ReadAllBytes(_path);
            if (payload.Length <= BinaryHeader.Length)
            {
                return null;
            }

            string header = Encoding.ASCII.GetString(payload, 0, BinaryHeader.Length);
            if (header != BinaryHeader)
            {
                return null;
            }

            byte[] cipher = new byte[payload.Length - BinaryHeader.Length];
            Array.Copy(payload, BinaryHeader.Length, cipher, 0, cipher.Length);

            byte[] plain;
            try
            {
                plain = ProtectedData.Unprotect(cipher, optionalEntropy: null, DataProtectionScope.CurrentUser);
            }
            catch (PlatformNotSupportedException)
            {
                plain = cipher;
            }

            return JsonHelper.Deserialize<CredentialData>(Encoding.UTF8.GetString(plain));
        }
        catch (CryptographicException)
        {
            // 换用户/换机器后无法解密：视为没有凭证
            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>删除凭证文件。</summary>
    public void Clear()
    {
        lock (_sync)
        {
            _current = new CredentialData();
            try
            {
                if (File.Exists(_path))
                {
                    File.Delete(_path);
                }
            }
            catch
            {
                // 删不掉就算了，内存已清空
            }
        }
    }
}
