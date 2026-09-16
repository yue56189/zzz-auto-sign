namespace ZzzAutoSign.Logging;

public enum LogLevel
{
    Debug,
    Info,
    Warn,
    Error
}

public interface ILogger
{
    void Debug(string message);
    void Info(string message);
    void Warn(string message);
    void Error(string message, Exception? ex = null);
}

/// <summary>
/// 按天滚动的文件日志，同时输出到 Debug 通道。
/// 关键特性：凭证脱敏 —— 任何形如 cookie / stoken / token 的值在写入前都会被裁剪成 abcd***wxyz。
/// </summary>
public sealed class FileLogger : ILogger
{
    private readonly string _dir;
    private readonly int _retainDays;
    private readonly object _sync = new();
    private readonly LogLevel _minLevel;
    private DateTime _lastCleanupDate = DateTime.MinValue;

    public FileLogger(string dir, LogLevel minLevel = LogLevel.Info, int retainDays = 14)
    {
        _dir = dir;
        _minLevel = minLevel;
        _retainDays = retainDays;
        Directory.CreateDirectory(_dir);
        CleanupOldLogs();
    }

    /// <summary>当天日志文件路径。</summary>
    public string CurrentLogFile => Path.Combine(_dir, $"zzz-autosign-{DateTime.Now:yyyy-MM-dd}.log");

    public void Debug(string message) => Write(LogLevel.Debug, message, null);
    public void Info(string message) => Write(LogLevel.Info, message, null);
    public void Warn(string message) => Write(LogLevel.Warn, message, null);
    public void Error(string message, Exception? ex = null) => Write(LogLevel.Error, message, ex);

    private void Write(LogLevel level, string message, Exception? ex)
    {
        if (level < _minLevel)
        {
            return;
        }

        string line = FormatLine(level, message, ex);

        lock (_sync)
        {
            try
            {
                // 日志用追加写，不需要原子替换；但滚动切换日期时要顺便做一次清理
                File.AppendAllText(CurrentLogFile, line + Environment.NewLine, System.Text.Encoding.UTF8);

                if (_lastCleanupDate.Date != DateTime.Now.Date)
                {
                    CleanupOldLogs();
                }
            }
            catch
            {
                // 日志写不进去绝不能影响主流程
            }
        }

        System.Diagnostics.Debug.WriteLine(line);
    }

    private string FormatLine(LogLevel level, string message, Exception? ex)
    {
        string tag = level switch
        {
            LogLevel.Debug => "DEBUG",
            LogLevel.Info => "INFO ",
            LogLevel.Warn => "WARN ",
            _ => "ERROR"
        };

        string body = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{tag}] [T{Environment.CurrentManagedThreadId:D2}] {Redact(message)}";

        if (ex is not null)
        {
            body += Environment.NewLine + "        " + Redact(ex.ToString());
        }

        return body;
    }

    /// <summary>
    /// 凭证脱敏。覆盖两种常见形态：
    /// 1) key=value / key: value / "key":"value" 形式的长凭证字段
    /// 2) 裸 token 串（长度 &gt;= 32 的连续字母数字）
    /// </summary>
    public static string Redact(string? input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return string.Empty;
        }

        string s = input;

        // 1) 显式字段名
        s = System.Text.RegularExpressions.Regex.Replace(
            s,
            @"(?i)\b(cookie|stoken|cookie_token|ltoken|ltuid|stuid|mid|account_id|login_ticket|DS)\b(\s*[:=]\s*""?)([^"";\s,}]{6,})",
            m => $"{m.Groups[1].Value}{m.Groups[2].Value}{Mask(m.Groups[3].Value)}");

        // 2) 裸长串（u1F/长 token 之类）
        s = System.Text.RegularExpressions.Regex.Replace(
            s,
            @"\b[A-Za-z0-9_\-]{32,}\b",
            m => Mask(m.Value));

        return s;
    }

    /// <summary>保留首尾各 4 位，中间用 *** 代替。</summary>
    private static string Mask(string value)
    {
        if (value.Length <= 8)
        {
            return "***";
        }

        return string.Concat(value.AsSpan(0, 4), "***", value.AsSpan(value.Length - 4));
    }

    /// <summary>删除超过保留期的日志文件。</summary>
    private void CleanupOldLogs()
    {
        try
        {
            DateTime cutoff = DateTime.Now.Date.AddDays(-_retainDays);
            foreach (string file in Directory.EnumerateFiles(_dir, "zzz-autosign-*.log"))
            {
                try
                {
                    if (File.GetLastWriteTime(file) < cutoff)
                    {
                        File.Delete(file);
                    }
                }
                catch
                {
                    // 单个文件删不掉就跳过
                }
            }

            _lastCleanupDate = DateTime.Now;
        }
        catch
        {
            // 目录不可枚举时忽略
        }
    }
}

/// <summary>丢弃所有日志的实现，用于单元测试与无日志场景。</summary>
public sealed class NullLogger : ILogger
{
    public static readonly NullLogger Instance = new();
    public void Debug(string message) { }
    public void Info(string message) { }
    public void Warn(string message) { }
    public void Error(string message, Exception? ex = null) { }
}
