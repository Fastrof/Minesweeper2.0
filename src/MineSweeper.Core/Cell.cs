namespace MineSweeper.Core;

/// <summary>
/// 单格数据（§2.1）。刻意保持为 struct 且只有 2 个字段：
/// 1000×1000 时整个棋盘数据只有约 2 MB，压缩棋盘数据没有任何意义——
/// 真正的开销在 UI 元素与绘制调用（见文档 §1.2.1 ①）。
/// </summary>
public struct Cell
{
    /// <summary>邻雷数尚未计算时的脏值。</summary>
    public const byte AdjacentMinesDirty = 0xFF;

    /// <summary>本格周围 8 格的雷数（0..8）。</summary>
    public byte AdjacentMines;

    /// <summary>位标志，见 <see cref="CellFlags"/>。</summary>
    public CellFlags Flags;

    /// <summary>周围 8 格的雷数是否已计算。</summary>
    public readonly bool HasAdjacentComputed => AdjacentMines != AdjacentMinesDirty;
}
