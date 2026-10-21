namespace MineSweeper.Core;

/// <summary>
/// 盘面快照（§6.3）：保存"雷位 + 首点"即可完整重建一局，用于"重开本局（同一盘面）"
/// 以及把盘面分享给队友。
/// </summary>
public sealed record BoardSnapshot(
    int Width,
    int Height,
    int MineCount,
    int Seed,
    int FirstClickIndex,
    bool UsedExtendedSafeZone,
    int[] MineIndexes)
{
    /// <summary>
    /// 是否"锁定盘面"模式（§2.6）：先按 seed 布雷、再取一个安全首点。
    /// 这种盘面完全不依赖玩家点在哪里，所以同 seed 必然同盘；分享串必须带上这一位才能精确复现。
    /// </summary>
    public bool Locked { get; init; }

    /// <summary>
    /// 把快照序列化成可分享的种子串：
    /// "宽x高:雷数:种子:首点:扩展安全区[:锁定]"，最后一节只有锁定盘面时才写。
    /// </summary>
    public string ToShareCode()
        => NoGuess
            ? $"{Width}x{Height}:{MineCount}:{Seed}:{FirstClickIndex}:{(UsedExtendedSafeZone ? 1 : 0)}:{(Locked ? 1 : 0)}:1"
            : Locked
                ? $"{Width}x{Height}:{MineCount}:{Seed}:{FirstClickIndex}:{(UsedExtendedSafeZone ? 1 : 0)}:1"
                : $"{Width}x{Height}:{MineCount}:{Seed}:{FirstClickIndex}:{(UsedExtendedSafeZone ? 1 : 0)}";

    /// <summary>
    /// 是否按无猜模式布雷（v1.4）。无猜盘要靠"同 seed + 同首点 + 同样的重抽规则"才能复现，
    /// 所以分享串必须带上这一位：此时写成 7 节，第 6 节是锁定标志（0/1），第 7 节固定为 1。
    /// 不开无猜时分享串格式与旧版完全一样，旧版程序照样能读。
    /// </summary>
    public bool NoGuess { get; init; }

    /// <summary>解析种子串（不关心是否锁定盘面）。格式不对时返回 false。</summary>
    public static bool TryParseShareCode(
        string? code,
        out int width,
        out int height,
        out int mineCount,
        out int seed,
        out int firstClickIndex,
        out bool usedExtendedSafeZone)
        => TryParseShareCode(code, out width, out height, out mineCount, out seed, out firstClickIndex, out usedExtendedSafeZone, out _, out _);

    /// <summary>解析种子串（不关心无猜标志）。</summary>
    public static bool TryParseShareCode(
        string? code,
        out int width,
        out int height,
        out int mineCount,
        out int seed,
        out int firstClickIndex,
        out bool usedExtendedSafeZone,
        out bool locked)
        => TryParseShareCode(code, out width, out height, out mineCount, out seed, out firstClickIndex, out usedExtendedSafeZone, out locked, out _);

    /// <summary>
    /// 解析种子串。<paramref name="locked"/> 为 true 表示这是"锁定盘面"的分享串。
    /// 兼容 5 节（旧格式，非锁定）、6 节（带锁定标志）与 7 节（v1.4，带无猜标志）三种写法。
    /// </summary>
    public static bool TryParseShareCode(
        string? code,
        out int width,
        out int height,
        out int mineCount,
        out int seed,
        out int firstClickIndex,
        out bool usedExtendedSafeZone,
        out bool locked,
        out bool noGuess)
    {
        width = height = mineCount = seed = firstClickIndex = 0;
        usedExtendedSafeZone = false;
        locked = false;
        noGuess = false;

        if (string.IsNullOrWhiteSpace(code))
        {
            return false;
        }

        string[] parts = code.Trim().Split(':', StringSplitOptions.TrimEntries);
        if (parts.Length is not (5 or 6 or 7))
        {
            return false;
        }

        string[] size = parts[0].Split('x', StringSplitOptions.TrimEntries);
        if (size.Length != 2)
        {
            return false;
        }

        if (!int.TryParse(size[0], out width)
            || !int.TryParse(size[1], out height)
            || !int.TryParse(parts[1], out mineCount)
            || !int.TryParse(parts[2], out seed)
            || !int.TryParse(parts[3], out firstClickIndex))
        {
            return false;
        }

        usedExtendedSafeZone = parts[4] == "1";
        locked = parts.Length >= 6 && parts[5] == "1";
        noGuess = parts.Length == 7 && parts[6] == "1";
        return true;
    }
}
