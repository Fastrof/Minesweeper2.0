namespace MineSweeper.Core;

/// <summary>
/// 参数边界（§0.1）。"雷数无上限"是卖点口径，工程上必须有明确边界，
/// 否则玩家会设出"雷数 = 全部格子"这种无法布雷的参数。
/// </summary>
public static class GameLimits
{
    /// <summary>棋盘宽度上限（§0.1）。</summary>
    public const int MaxWidth = 4096;

    /// <summary>棋盘高度上限（§0.1）。</summary>
    public const int MaxHeight = 4096;

    /// <summary>
    /// 棋盘宽度下限。
    /// 文档只规定了上限（§0.1），下限交给"首点 3×3 安全区"那条规则去兜底：
    /// 真正生效的约束是 N ≥ 9 + 雷数，所以 1×N 的单行棋盘也是合法盘面。
    /// </summary>
    public const int MinWidth = 1;

    /// <summary>棋盘高度下限（理由同 <see cref="MinWidth"/>）。</summary>
    public const int MinHeight = 1;

    /// <summary>首点强制安全区格数（点击格 + 8 邻格，§0 第 2 条）。</summary>
    public const int SafeZoneCellCount = 9;

    /// <summary>首点扩展安全区格数（5×5，<see cref="GameSettings.FirstClickOpening"/> 打开时的目标）。</summary>
    public const int ExtendedSafeZoneCellCount = 25;

    /// <summary>
    /// 校验棋盘参数（§2.2）。返回 false 时 <paramref name="error"/> 给出可直接展示给玩家的原因。
    /// 一律不做静默退化：非法就是非法，让调用方禁止开局（§0.1）。
    /// </summary>
    public static bool ValidateParameters(int width, int height, int mineCount, out string? error)
    {
        if (width < MinWidth || width > MaxWidth)
        {
            error = $"宽度必须在 {MinWidth}~{MaxWidth} 之间，当前为 {width}。";
            return false;
        }

        if (height < MinHeight || height > MaxHeight)
        {
            error = $"高度必须在 {MinHeight}~{MaxHeight} 之间，当前为 {height}。";
            return false;
        }

        long cellCount = (long)width * height;

        if (mineCount < 0)
        {
            error = "雷数不能为负数。";
            return false;
        }

        long maxMines = cellCount - SafeZoneCellCount;
        if (maxMines < 0)
        {
            error = $"棋盘太小：{width}×{height} 只有 {cellCount} 格，无法为首点的 3×3 安全区（{SafeZoneCellCount} 格）让出位置。";
            return false;
        }

        if (mineCount > maxMines)
        {
            error = $"雷数超过 {maxMines}，无法保证首点安全，请降低雷数或扩大棋盘。";
            return false;
        }

        error = null;
        return true;
    }
}
