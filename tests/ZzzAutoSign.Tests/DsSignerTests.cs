using System.Security.Cryptography;
using System.Text;
using Xunit;
using ZzzAutoSign.MiHoYo;

namespace ZzzAutoSign.Tests;

/// <summary>
/// DS 签名测试。固定 t / r 后，输出必须与手工计算的 MD5 完全一致。
/// 这些用例是「签名算法没被改错」的守门人 —— 签名错会导致 retcode = -1。
/// </summary>
public class DsSignerTests
{
    private static readonly MiHoYoEndpoints Ep = new();

    /// <summary>独立实现一遍 MD5，避免与被测代码共用实现而产生假阳性。</summary>
    private static string ReferenceMd5(string s)
    {
        byte[] hash = MD5.HashData(Encoding.UTF8.GetBytes(s));
        var sb = new StringBuilder();
        foreach (byte b in hash)
        {
            sb.Append(b.ToString("x2"));
        }

        return sb.ToString();
    }

    [Fact]
    public void SignDs1_ShouldMatchReferenceAlgorithm()
    {
        const long t = 1700000000;
        const string r = "abcdef";

        string expectedMd5 = ReferenceMd5($"salt={Ep.SaltWeb}&t={t}&r={r}");
        string expected = $"{t},{r},{expectedMd5}";

        string actual = DsSigner.SignDs1(Ep.SaltWeb, t, r);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void SignDs2_ShouldIncludeBodyAndQuery()
    {
        const long t = 1700000000;
        const string r = "123456";
        const string body = "{\"act_id\":\"e202406242138391\",\"region\":\"prod_gf_cn\",\"uid\":\"10000001\"}";
        const string query = "act_id=e202406242138391&region=prod_gf_cn&uid=10000001";

        string expectedMd5 = ReferenceMd5($"salt={Ep.SaltX6}&t={t}&r={r}&b={body}&q={query}");
        string expected = $"{t},{r},{expectedMd5}";

        string actual = DsSigner.SignDs2(Ep.SaltX6, t, query, body, r);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void SignDs1_ShouldProduceThreeCommaSeparatedParts()
    {
        string ds = DsSigner.SignDs1(Ep.SaltWeb, 1700000000, "abc123");

        string[] parts = ds.Split(',');
        Assert.Equal(3, parts.Length);
        Assert.Equal("1700000000", parts[0]);
        Assert.Equal("abc123", parts[1]);
        Assert.Equal(32, parts[2].Length);                 // MD5 十六进制长度
        Assert.Equal(parts[2].ToLowerInvariant(), parts[2]); // 必须小写
    }

    [Fact]
    public void SignDs1_AutoRandom_ShouldBeSixLowercaseAlphanumeric()
    {
        for (int i = 0; i < 200; i++)
        {
            string ds = DsSigner.SignDs1(Ep.SaltWeb, 1700000000);
            string r = ds.Split(',')[1];

            Assert.Equal(6, r.Length);
            Assert.All(r, ch => Assert.True(
                (ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9'),
                $"字符 '{ch}' 不在 [a-z0-9] 范围内"));
        }
    }

    [Fact]
    public void SignDs2_AutoRandom_ShouldBeInExpectedIntegerRange()
    {
        for (int i = 0; i < 300; i++)
        {
            string ds = DsSigner.SignDs2(Ep.SaltX6, 1700000000);
            string r = ds.Split(',')[1];

            Assert.True(int.TryParse(r, out int value), $"r 应为整数，实际为 '{r}'");
            Assert.InRange(value, 100001, 200000);
        }
    }

    [Fact]
    public void Sign_ByMode_ShouldSelectCorrectSalt()
    {
        const long t = 1700000000;
        const string r = "zzzz11";

        // WebDs1 应当用 SaltWeb
        Assert.Equal(
            DsSigner.SignDs1(Ep.SaltWeb, t, r),
            DsSigner.Sign(Ep, SignMode.WebDs1, timestamp: t, random: r));

        // AppDs1 应当用 SaltApp
        Assert.Equal(
            DsSigner.SignDs1(Ep.SaltApp, t, r),
            DsSigner.Sign(Ep, SignMode.AppDs1, timestamp: t, random: r));

        // 两者 salt 不同，结果必须不同
        Assert.NotEqual(
            DsSigner.Sign(Ep, SignMode.WebDs1, timestamp: t, random: r),
            DsSigner.Sign(Ep, SignMode.AppDs1, timestamp: t, random: r));
    }

    [Fact]
    public void ClientTypeOf_ShouldMatchDocumentedValues()
    {
        Assert.Equal(5, DsSigner.ClientTypeOf(SignMode.WebDs1));
        Assert.Equal(2, DsSigner.ClientTypeOf(SignMode.AppDs1));
        Assert.Equal(5, DsSigner.ClientTypeOf(SignMode.X6Ds2));
    }

    [Fact]
    public void CanonicalJson_ShouldSortKeysAlphabetically()
    {
        var pairs = new List<KeyValuePair<string, string>>
        {
            new("uid", "10000001"),
            new("act_id", "e202406242138391"),
            new("region", "prod_gf_cn")
        };

        string json = DsSigner.CanonicalJson(pairs);

        Assert.Equal("{\"act_id\":\"e202406242138391\",\"region\":\"prod_gf_cn\",\"uid\":\"10000001\"}", json);
    }

    [Fact]
    public void CanonicalQuery_ShouldSortKeysAlphabetically()
    {
        var pairs = new List<KeyValuePair<string, string>>
        {
            new("uid", "10000001"),
            new("act_id", "e202406242138391"),
            new("region", "prod_gf_cn")
        };

        string query = DsSigner.CanonicalQuery(pairs);

        Assert.Equal("act_id=e202406242138391&region=prod_gf_cn&uid=10000001", query);
    }

    [Fact]
    public void CanonicalJson_ShouldEscapeQuotesAndBackslashes()
    {
        var pairs = new List<KeyValuePair<string, string>>
        {
            new("a", "he said \"hi\""),
            new("b", @"c:\path")
        };

        string json = DsSigner.CanonicalJson(pairs);

        Assert.Equal("{\"a\":\"he said \\\"hi\\\"\",\"b\":\"c:\\\\path\"}", json);
    }
}
