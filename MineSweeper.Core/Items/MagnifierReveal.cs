namespace MineSweeper.Core.Items;

/// <summary>透视到的单个格子的内容（§5.4）。</summary>
public readonly record struct MagnifierCellInfo(
    int Index,
    bool IsMine,
    byte AdjacentMines,
    bool WasAlreadyItemRevealed);

/// <summary>一次透视的完整结果。UI 用它画高亮、写提示条（"已透视 9 格：雷 2 / 空 7"）。</summary>
public sealed record MagnifierReveal(
    int CenterIndex,
    int Radius,
    IReadOnlyList<MagnifierCellInfo> Cells,
    int MineCount,
    int EmptyCount,
    int NewlyRevealedCount)
{
    /// <summary>实际覆盖的格数（贴边/角落会被裁切，角落只有 4 格、边缘 6 格）。</summary>
    public int AffectedCount => Cells.Count;

    /// <summary>
    /// 把这次透视的区域列成人类可读的文本（§5.6 备选：右侧信息面板 / 无障碍 / 截图分享）。
    /// 坐标用 1 起的行列号，和玩家看到的棋盘对得上。
    /// </summary>
    public string ToPanelText(Board board)
    {
        ArgumentNullException.ThrowIfNull(board);

        var lines = new List<string>
        {
            $"中心 第{board.YOf(CenterIndex) + 1}行 第{board.XOf(CenterIndex) + 1}列 · 覆盖 {AffectedCount} 格 · 雷 {MineCount} / 空 {EmptyCount}",
        };

        foreach (MagnifierCellInfo cell in Cells)
        {
            string content = cell.IsMine
                ? "雷"
                : cell.AdjacentMines == 0
                    ? "空"
                    : $"数字 {cell.AdjacentMines}";

            string already = cell.WasAlreadyItemRevealed ? "（此前已透视）" : string.Empty;
            lines.Add($"第{board.YOf(cell.Index) + 1}行 第{board.XOf(cell.Index) + 1}列：{content}{already}");
        }

        return string.Join(Environment.NewLine, lines);
    }
}

/// <summary>一次道具使用的结果（§5.3）。</summary>
public sealed record MagnifierUseResult(
    bool Success,
    ItemUseCheck Check,
    string? Reason,
    MagnifierReveal? Reveal,
    int RemainingCount,
    bool Consumed)
{
    public static MagnifierUseResult Rejected(ItemUseCheck check, int remainingCount) =>
        new(false, check, ItemUseCheckText.Describe(check), null, remainingCount, false);

    public static MagnifierUseResult Used(MagnifierReveal reveal, int remainingCount) =>
        new(true, ItemUseCheck.Ok, null, reveal, remainingCount, true);
}
