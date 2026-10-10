namespace MineSweeper.Core;

/// <summary>
/// chord（双击展开，§2.4）。
///
/// 判定与结算刻意分成两个纯函数，方便单独测：
/// 条件 = 该格已翻开、是数字格、且周围已插旗数正好等于该数字。
/// 注意：旗子插错位置时 chord 会踩雷失败——这是原版行为，必须保留，
/// 不要"贴心地"帮玩家过滤掉。
/// </summary>
public static class ChordSolver
{
    /// <summary>是否满足 chord 条件。</summary>
    public static bool CanChord(Board board, int index)
    {
        ArgumentNullException.ThrowIfNull(board);

        if (!board.IsValidIndex(index) || !board.IsRevealed(index))
        {
            return false;
        }

        byte adjacent = board.GetAdjacentMines(index);
        if (adjacent == 0)
        {
            // 0 格周围本来就没有雷，chord 无意义
            return false;
        }

        return board.CountNeighborsWith(index, CellFlags.IsFlagged) == adjacent;
    }

    /// <summary>收集会被 chord 翻开的格子（周围既未插旗也未翻开的格）。</summary>
    public static void CollectTargets(Board board, int index, List<int> targets)
    {
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(targets);

        targets.Clear();

        if (!board.IsValidIndex(index))
        {
            return;
        }

        foreach (int neighbor in board.Neighbors8(index))
        {
            if (!board.IsRevealed(neighbor) && !board.IsFlagged(neighbor))
            {
                targets.Add(neighbor);
            }
        }
    }
}
