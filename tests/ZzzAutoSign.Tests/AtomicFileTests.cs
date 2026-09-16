using System.Text.Json;
using Xunit;
using ZzzAutoSign.Config;

namespace ZzzAutoSign.Tests;

public class AtomicFileTests : IDisposable
{
    private readonly string _dir;

    public AtomicFileTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "zzz-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch
        {
            // 忽略清理失败
        }
    }

    private sealed class Sample
    {
        public string Name { get; set; } = string.Empty;
        public int Value { get; set; }
    }

    [Fact]
    public void WriteThenRead_ShouldRoundTrip()
    {
        string path = Path.Combine(_dir, "state.json");
        AtomicFile.WriteJson(path, new Sample { Name = "测试", Value = 42 });

        var read = AtomicFile.ReadJsonOrNull<Sample>(path);

        Assert.NotNull(read);
        Assert.Equal("测试", read!.Name);
        Assert.Equal(42, read.Value);
    }

    [Fact]
    public void ReadMissingFile_ShouldReturnNull()
    {
        var read = AtomicFile.ReadJsonOrNull<Sample>(Path.Combine(_dir, "not-exist.json"));
        Assert.Null(read);
    }

    [Fact]
    public void ReadCorruptFile_ShouldReturnNullAndQuarantine()
    {
        string path = Path.Combine(_dir, "corrupt.json");
        File.WriteAllText(path, "{ this is not valid json ");

        var read = AtomicFile.ReadJsonOrNull<Sample>(path);

        Assert.Null(read);
        Assert.False(File.Exists(path));
        Assert.True(File.Exists(path + ".corrupt"), "损坏文件应被改名留档");
    }

    [Fact]
    public void Overwrite_ShouldLeaveNoTempFiles()
    {
        string path = Path.Combine(_dir, "repeat.json");

        for (int i = 0; i < 20; i++)
        {
            AtomicFile.WriteJson(path, new Sample { Name = $"第{i}次", Value = i });
        }

        var read = AtomicFile.ReadJsonOrNull<Sample>(path);
        Assert.Equal(19, read!.Value);

        // 目录里只应有目标文件，没有残留临时文件
        string[] files = Directory.GetFiles(_dir);
        Assert.Single(files);
    }

    [Fact]
    public void WriteJson_ShouldNotEscapeChinese()
    {
        string path = Path.Combine(_dir, "cn.json");
        AtomicFile.WriteJson(path, new Sample { Name = "签到完成" });

        string raw = File.ReadAllText(path);

        // 使用 UnsafeRelaxedJsonEscaping，中文应原样保留（便于人工排查）
        Assert.Contains("签到完成", raw);
    }

    [Fact]
    public void WriteAllBytes_ShouldProduceExactContent()
    {
        string path = Path.Combine(_dir, "blob.bin");
        byte[] data = { 1, 2, 3, 250, 251, 252 };

        AtomicFile.WriteAllBytes(path, data);

        Assert.Equal(data, File.ReadAllBytes(path));
    }

    [Fact]
    public void ReadJson_ShouldHandleConcurrentReadersDuringWrites()
    {
        string path = Path.Combine(_dir, "concurrent.json");
        AtomicFile.WriteJson(path, new Sample { Name = "初始", Value = 0 });

        // 写的同时反复读，绝不应读到半截 JSON 而抛异常
        var errors = new List<Exception>();

        var writer = Task.Run(() =>
        {
            for (int i = 1; i <= 50; i++)
            {
                AtomicFile.WriteJson(path, new Sample { Name = new string('x', 500), Value = i });
            }
        });

        // 起 4 个并发读任务。这里用显式数组而不用 Enumerable.Select，
        // 是为了避免把 lambda 形参写成 "_"：那样内层的 "_ = 读取结果"
        // 会被解析成给 int 形参赋值，从而报 CS0029。
        var readers = new Task[4];
        for (int r = 0; r < readers.Length; r++)
        {
            readers[r] = Task.Run(() =>
            {
                for (int i = 0; i < 200; i++)
                {
                    try
                    {
                        _ = AtomicFile.ReadJsonOrNull<Sample>(path);
                    }
                    catch (Exception ex)
                    {
                        lock (errors)
                        {
                            errors.Add(ex);
                        }
                    }
                }
            });
        }

        Task.WaitAll(readers.Append(writer).ToArray());

        Assert.Empty(errors);
    }
}

public class CredentialHelperTests
{
    [Theory]
    [InlineData("cookie_token=AAA; ltoken=BBB", "cookie_token", "AAA")]
    [InlineData("cookie_token=AAA; ltoken=BBB", "ltoken", "BBB")]
    [InlineData(" ltoken = BBB ;", "ltoken", "BBB")]
    [InlineData("LTUID=123", "ltuid", "123")]
    public void TryExtractCookieValue_ShouldParseVariousFormats(string cookie, string key, string expected)
    {
        Assert.Equal(expected, Core.SignOrchestrator.TryExtractCookieValue(cookie, key));
    }

    [Fact]
    public void TryExtractCookieValue_ShouldReturnNullWhenMissing()
    {
        Assert.Null(Core.SignOrchestrator.TryExtractCookieValue("a=1; b=2", "stoken"));
    }

    [Theory]
    [InlineData("stuid=111; stoken=x", "111")]
    [InlineData("ltuid=222", "222")]
    [InlineData("account_id=333", "333")]
    [InlineData("a=1", "")]
    public void TryExtractStuid_ShouldPreferKnownKeys(string cookie, string expected)
    {
        Assert.Equal(expected, Core.SignOrchestrator.TryExtractStuid(cookie));
    }

    [Fact]
    public void MergeCookieToken_ShouldReplaceExistingToken()
    {
        string merged = Core.SignOrchestrator.MergeCookieToken("cookie_token=OLD; ltoken=L", "NEW");
        Assert.Contains("cookie_token=NEW", merged);
        Assert.DoesNotContain("OLD", merged);
        Assert.Contains("ltoken=L", merged);
    }

    [Fact]
    public void MergeCookieToken_ShouldAppendWhenAbsent()
    {
        string merged = Core.SignOrchestrator.MergeCookieToken("ltoken=L", "NEW");
        Assert.Contains("cookie_token=NEW", merged);
        Assert.Contains("ltoken=L", merged);
    }

    [Fact]
    public void MergeCookieToken_ShouldHandleEmptyCookie()
    {
        string merged = Core.SignOrchestrator.MergeCookieToken(string.Empty, "NEW");
        Assert.Equal("cookie_token=NEW", merged);
    }
}
