namespace ZzzAutoSign.Config;

/// <summary>
/// 原子文件写入工具。
/// 先写同目录下的临时文件，再通过 File.Replace / File.Move 原子替换，
/// 保证进程在任意时刻被强杀，目标文件要么是完整的旧内容，要么是完整的新内容，绝不出现半截 JSON。
/// </summary>
public static class AtomicFile
{
    /// <summary>原子写入文本（UTF-8，无 BOM）。</summary>
    public static void WriteAllText(string path, string content)
    {
        WriteAllBytes(path, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(content));
    }

    /// <summary>原子写入二进制。</summary>
    public static void WriteAllBytes(string path, byte[] content)
    {
        string dir = Path.GetDirectoryName(path) ?? ".";
        Directory.CreateDirectory(dir);

        // 临时文件必须与目标同目录，否则跨卷时 File.Replace 会失败
        string tmp = Path.Combine(dir, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");

        try
        {
            using (var fs = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                       bufferSize: 4096, FileOptions.WriteThrough))
            {
                fs.Write(content, 0, content.Length);
                fs.Flush(flushToDisk: true);
            }

            if (File.Exists(path))
            {
                // File.Replace 保留目标文件的 ACL，比 Delete+Move 更安全
                // 第三个参数为备份路径，传 null 表示不保留备份
                File.Replace(tmp, path, destinationBackupFileName: null, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(tmp, path);
            }
        }
        finally
        {
            // 异常路径下清理临时文件
            try
            {
                if (File.Exists(tmp))
                {
                    File.Delete(tmp);
                }
            }
            catch
            {
                // 清理失败不影响主流程
            }
        }
    }

    /// <summary>原子写入经序列化的对象。</summary>
    public static void WriteJson<T>(string path, T value)
    {
        WriteAllText(path, JsonHelper.Serialize(value));
    }

    /// <summary>
    /// 读取文本；文件不存在或内容损坏时返回 null（不抛异常）。
    /// 损坏时会把坏文件重命名为 .corrupt 留档，便于排查。
    /// </summary>
    public static string? ReadAllTextOrNull(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            return File.ReadAllText(path);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>读取并反序列化；失败返回 null。</summary>
    public static T? ReadJsonOrNull<T>(string path) where T : class
    {
        string? text = ReadAllTextOrNull(path);
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        try
        {
            return JsonHelper.Deserialize<T>(text);
        }
        catch
        {
            TryQuarantine(path);
            return null;
        }
    }

    /// <summary>把损坏的文件改名留档。</summary>
    private static void TryQuarantine(string path)
    {
        try
        {
            string dest = path + ".corrupt";
            File.Move(path, dest, overwrite: true);
        }
        catch
        {
            // 留档失败不影响主流程
        }
    }
}
