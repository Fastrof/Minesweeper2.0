using System.Text;

namespace MineSweeper.Core.Persistence;

/// <summary>
/// 存档文件的原子写盘（§6.1）。
///
/// 直接 <c>File.WriteAllText</c> 覆盖目标文件的话，中途杀进程/断电就会留下一个被截断的
/// JSON——玩家几十局的进度和记录一次性报废。所以统一走"先写同目录临时文件，再原子替换"：
/// 替换成功之前，原文件始终是完整可读的旧内容。
///
/// <see cref="ProgressStore"/> 与 <see cref="RecordsStore"/> 共用这一份实现，
/// 避免两个存档文件出现"一个安全一个不安全"的偏差。
/// </summary>
internal static class AtomicFile
{
    /// <summary>把文本原子地写到 <paramref name="path"/>（UTF-8 无 BOM，与现有存档一致）。</summary>
    public static void WriteAllText(string path, string contents)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string full = Path.GetFullPath(path);
        string? directory = Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string temp = full + ".tmp";
        File.WriteAllText(temp, contents, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        if (!File.Exists(full))
        {
            File.Move(temp, full, overwrite: true);
            return;
        }

        // File.Replace 是同卷原子替换；某些文件系统不支持（PlatformNotSupported），
        // 或者临时文件与目标不同卷时抛 IOException，两种情况都退回覆盖式 Move。
        try
        {
            File.Replace(temp, full, destinationBackupFileName: null);
            return;
        }
        catch (IOException)
        {
            TryMoveOverwrite(temp, full);
        }
        catch (PlatformNotSupportedException)
        {
            TryMoveOverwrite(temp, full);
        }
        catch (UnauthorizedAccessException)
        {
            TryMoveOverwrite(temp, full);
        }

        if (File.Exists(temp))
        {
            File.Delete(temp);
        }
    }

    private static void TryMoveOverwrite(string temp, string full)
    {
        try
        {
            File.Move(temp, full, overwrite: true);
        }
        catch (IOException)
        {
            // 落到调用方统一清理临时文件
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
