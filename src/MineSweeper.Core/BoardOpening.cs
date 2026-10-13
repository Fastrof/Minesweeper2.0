namespace MineSweeper.Core;

/// <summary>
/// 盘面查询小工具（v1.1）：找"离某一格最近的 0 格"。
///
/// <para>
/// 用途只有一个：锁定盘面下**第一下点到雷**时，这一下必须挪走（否则首点就炸），
/// 挪到哪一格直接决定玩家的观感 —— 原来固定挪到开局算好的那一格，
/// 玩家看到的是"我点在这个角落，它却在另一个角落开了一片"。
/// 挪到**离点击位置最近**的 0 格，既保住"首点必定开出一片"（0 格必然连锁展开），
/// 也不会跳到八竿子打不着的地方。
/// </para>
/// <para>
/// 距离用棋盘上的**切比雪夫距离**（一圈一圈向外找），也就是玩家眼里的"隔了几格"；
/// 同一圈里若有多个 0 格，取扫描顺序里先遇到的（先上后下、先左后右），保证确定性。
/// </para>
/// </summary>
public static class BoardOpening
{
    /// <summary>
    /// 找离 <paramref name="fromIndex"/> 最近的 0 格（周围 8 格无雷）；找不到返回 -1。
    /// 未布雷或索引非法时返回 -1。
    /// </summary>
    public static int FindNearestZeroCell(Board board, int fromIndex)
    {
        ArgumentNullException.ThrowIfNull(board);

        if (!board.MinesPlaced || board.CellCount == 0 || !board.IsValidIndex(fromIndex))
        {
            return -1;
        }

        int width = board.Width;
        int height = board.Height;
        int fromCol = board.XOf(fromIndex);
        int fromRow = board.YOf(fromIndex);

        // 从自己这一圈开始一圈一圈往外找：绝大多数情况下第一两圈就有 0 格，
        // 所以即使 4096×4096 的大盘也不会为这一次点击做全盘扫描。
        for (int radius = 0; radius <= Math.Max(width, height); radius++)
        {
            int col0 = fromCol - radius;
            int col1 = fromCol + radius;
            int row0 = fromRow - radius;
            int row1 = fromRow + radius;

            // 一圈里的上、下两条边（含四个角）
            int left = Math.Max(0, col0);
            int right = Math.Min(width - 1, col1);
            for (int col = left; col <= right; col++)
            {
                if (row0 >= 0 && IsZero(board, col, row0))
                {
                    return board.Index(col, row0);
                }

                if (radius > 0 && row1 < height && IsZero(board, col, row1))
                {
                    return board.Index(col, row1);
                }
            }

            // 一圈里的左、右两条边（去掉已经被上下两条边扫过的角）
            int top = Math.Max(0, row0 + 1);
            int bottom = Math.Min(height - 1, row1 - 1);
            for (int row = top; row <= bottom; row++)
            {
                if (col0 >= 0 && IsZero(board, col0, row))
                {
                    return board.Index(col0, row);
                }

                if (radius > 0 && col1 < width && IsZero(board, col1, row))
                {
                    return board.Index(col1, row);
                }
            }
        }

        return -1;
    }

    private static bool IsZero(Board board, int col, int row)
    {
        int index = board.Index(col, row);

        // 注意"周围无雷"不等于"不是雷"：一颗孤立的雷 GetAdjacentMines 也是 0。
        // 两个条件都要，否则会把雷本身当成 0 格，玩家第一下就被挪到雷上（自检抓到过）。
        return !board.IsMine(index) && board.GetAdjacentMines(index) == 0;
    }
}
