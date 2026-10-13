using System.Security.Cryptography;

namespace MineSweeper.Core.Updates;

/// <summary>
/// 从发布附件里挑出"该给这台机器装的那个包"。
///
/// 为什么放在 Core：选包规则是纯字符串逻辑，能不开 UI 就测出来 —— 而且它是**安全边界的一部分**：
/// 万一将来发布里混进 arm64 包或者一堆无关附件，选错了就会给玩家装一个跑不起来的东西。
/// </summary>
public static class UpdateAssetSelector
{
    /// <summary>x64。</summary>
    public const string X64 = "x64";

    /// <summary>ARM64。</summary>
    public const string Arm64 = "arm64";

    /// <summary>
    /// 选包优先级：① 名字里带当前架构且以 <c>.msix</c> 结尾 → ② 任意 <c>.msix</c>
    /// → ③ 带当前架构的 <c>.msixbundle</c> → ④ 任意 <c>.msixbundle</c> → 找不到返回 null。
    /// <paramref name="reason"/> 永远给出中文说明，便于直接写进日志。
    /// </summary>
    public static ReleaseAsset? SelectInstaller(
        IReadOnlyList<ReleaseAsset>? assets, string architecture, out string reason)
    {
        if (assets is null || assets.Count == 0)
        {
            reason = MineSweeper.Core.Localization.Strings.Get("update.noAssets");
            return null;
        }

        string arch = string.IsNullOrWhiteSpace(architecture) ? X64 : architecture.Trim().ToLowerInvariant();

        ReleaseAsset?[] candidates =
        [
            Match(assets, a => IsExtension(a, ".msix") && HasArch(a, arch)),
            Match(assets, a => IsExtension(a, ".msix")),
            Match(assets, a => IsExtension(a, ".msixbundle") && HasArch(a, arch)),
            Match(assets, a => IsExtension(a, ".msixbundle")),
        ];

        foreach (ReleaseAsset? candidate in candidates)
        {
            if (candidate is not null)
            {
                reason = $"选中安装包：{candidate.Name}";
                return candidate;
            }
        }

        reason = MineSweeper.Core.Localization.Strings.Get("update.noArchAsset", arch);
        return null;
    }

    private static ReleaseAsset? Match(IReadOnlyList<ReleaseAsset> assets, Func<ReleaseAsset, bool> predicate)
    {
        foreach (ReleaseAsset asset in assets)
        {
            if (!string.IsNullOrWhiteSpace(asset.DownloadUrl) && predicate(asset))
            {
                return asset;
            }
        }

        return null;
    }

    private static bool IsExtension(ReleaseAsset asset, string extension)
        => asset.Name.EndsWith(extension, StringComparison.OrdinalIgnoreCase);

    private static bool HasArch(ReleaseAsset asset, string arch)
        => asset.Name.Contains(arch, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// 从发布说明里抓出 SHA256。
///
/// 为什么不靠 GitHub 自己的校验和：GitHub 的 releases 接口不提供附件哈希，
/// 所以约定"发版说明里写一行 <c>SHA256: &lt;64 位十六进制&gt;</c>"，这也是本项目发布清单里本来就在写的东西。
/// 抓不到不算错（返回 false），调用方按"无从校验"处理：不拦截，但要在日志里说明白。
/// </summary>
public static class ReleaseNotesHash
{
    /// <summary>十六进制摘要的合法长度（SHA256 = 32 字节）。</summary>
    private const int Sha256HexLength = 64;

    /// <summary>从发布说明里提取 SHA256（大小写不敏感，允许全角冒号和多余空白）。</summary>
    public static bool TryExtract(string? notes, out string? hex)
    {
        hex = null;
        if (string.IsNullOrWhiteSpace(notes))
        {
            return false;
        }

        foreach (string rawLine in notes.Split('\n'))
        {
            string line = rawLine.Trim().Trim('*', '`', '#', ' ');
            if (!line.StartsWith("SHA256", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            int colon = line.IndexOfAny([':', '：']);
            if (colon < 0)
            {
                continue;
            }

            string value = line[(colon + 1)..].Trim().Trim('*', '`', ' ');
            if (value.Length == Sha256HexLength && IsHex(value))
            {
                hex = value.ToUpperInvariant();
                return true;
            }
        }

        return false;
    }

    private static bool IsHex(string text)
    {
        foreach (char c in text)
        {
            bool ok = c is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';
            if (!ok)
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>下载后的完整性校验（纯文件逻辑，可单测）。</summary>
public static class FileHash
{
    /// <summary>算文件的 SHA256；读不了就返回 false（不抛）。</summary>
    public static bool TryComputeSha256(string? filePath, out string? hex)
    {
        hex = null;
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return false;
        }

        try
        {
            using FileStream stream = File.OpenRead(filePath);
            byte[] hash = SHA256.HashData(stream);
            hex = Convert.ToHexString(hash);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// 校验下载到的安装包。
    /// <paramref name="expectedHex"/> 为空时返回 true —— 也就是"发布了但没写哈希"这种情况**不拦**，
    /// 因为真正兜底的是安装时的包签名校验；哈希只是提前发现"下坏了"的第一道防线。
    /// </summary>
    public static bool VerifySha256(string? filePath, string? expectedHex, out string reason)
    {
        if (string.IsNullOrWhiteSpace(expectedHex))
        {
            reason = MineSweeper.Core.Localization.Strings.Get("update.noSha");
            return true;
        }

        if (!TryComputeSha256(filePath, out string? actual) || actual is null)
        {
            reason = MineSweeper.Core.Localization.Strings.Get("update.unreadable");
            return false;
        }

        if (!string.Equals(actual, expectedHex.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            reason = MineSweeper.Core.Localization.Strings.Get("update.hashMismatch", expectedHex.Trim()[..Math.Min(16, expectedHex.Trim().Length)], actual[..16]);
            return false;
        }

        reason = MineSweeper.Core.Localization.Strings.Get("update.hashOk");
        return true;
    }
}
