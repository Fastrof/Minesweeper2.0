namespace MineSweeper.Core;

/// <summary>
/// 布雷器（§2.2）。
///
/// 设计要点：
/// 1. 开局不布雷，等玩家第一次点击时才以该格为中心布雷（§0 的"首点安全"）。
/// 2. 安全区永远是"点击格 + 8 邻格"（§0 第 2 条，硬性要求）。
///    这个排除本身已经保证点击格的邻雷数为 0，也就是 §2.2 说的
///    "首点必开一片空区"其实自动成立。
/// 3. <see cref="GameSettings.FirstClickOpening"/> 打开时，再把安全区扩到 5×5，
///    让首点必定翻开 ≥3×3 一整片区域；棋盘太小或雷太密时自动退回 3×3。
/// 4. 采样用"选择采样法"（Algorithm S）单趟遍历完成：精确均匀、O(N) 时间、O(1) 额外内存，
///    不像"候选数组 + 洗牌"那样在 4096×4096 上要吃掉几十 MB。
/// </summary>
public static class BoardGenerator
{
    /// <summary>布雷结果。</summary>
    public readonly record struct PlacementOutcome(bool Success, string? Error, int SafeZoneCellCount, bool UsedExtendedSafeZone);

    /// <summary>首点安全区半径 1 → 3×3（9 格）。</summary>
    private const int DefaultSafeRadius = 1;

    /// <summary>首点扩展安全区半径 2 → 5×5（25 格）。</summary>
    private const int ExtendedSafeRadius = 2;

    /// <summary>
    /// 无猜模式最多重抽多少张盘（v1.5）。高级 30×16/99 雷平均几十次内就能抽到，
    /// 雷特别密的自定义盘可能一直抽不到，这时退回普通盘并把 <see cref="Board.NoGuessGuaranteed"/> 置为 false。
    /// </summary>
    public const int NoGuessMaxAttempts = 2000;

    /// <summary>
    /// 无猜重抽的时间预算（毫秒，v1.5）。超过预算就接受当前这张盘并标记"未保证无猜"，
    /// 不把首点卡在 UI 线程上太久（只有极端密雷的自定义盘会用到）。
    /// </summary>
    public const int NoGuessMaxMilliseconds = 500;

    /// <summary>判断某格是否落在首点安全区内（切比雪夫距离 ≤ radius）。</summary>
    public static bool IsInSafeZone(Board board, int firstClickIndex, int index, int radius)
    {
        int dx = Math.Abs(board.XOf(index) - board.XOf(firstClickIndex));
        int dy = Math.Abs(board.YOf(index) - board.YOf(firstClickIndex));
        return dx <= radius && dy <= radius;
    }

    /// <summary>安全区实际覆盖的格数（贴边/角落会被棋盘裁切，所以可能少于 9 或 25）。</summary>
    public static int CountSafeZoneCells(Board board, int firstClickIndex, int radius)
    {
        int x = board.XOf(firstClickIndex);
        int y = board.YOf(firstClickIndex);

        int x0 = Math.Max(0, x - radius);
        int x1 = Math.Min(board.Width - 1, x + radius);
        int y0 = Math.Max(0, y - radius);
        int y1 = Math.Min(board.Height - 1, y + radius);

        return (x1 - x0 + 1) * (y1 - y0 + 1);
    }

    /// <summary>
    /// 为首次点击布雷，并把 <see cref="Board.MinesPlaced"/> 置为 true。
    /// 失败时棋盘保持未布雷状态（不做任何静默退化）。
    /// </summary>
    public static bool TryPlaceMines(Board board, int firstClickIndex, GameSettings settings, Random random, out string? error)
    {
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(random);

        if (!board.IsValidIndex(firstClickIndex))
        {
            error = MineSweeper.Core.Localization.Strings.Get("board.indexOutOfRange");
            return false;
        }

        if (board.MinesPlaced)
        {
            error = MineSweeper.Core.Localization.Strings.Get("board.alreadyPlaced");
            return false;
        }

        if (!GameLimits.ValidateParameters(board.Width, board.Height, board.MineCount, out error))
        {
            return false;
        }

        int radius = DefaultSafeRadius;
        if (settings.FirstClickOpening &&
            (long)board.CellCount - GameLimits.ExtendedSafeZoneCellCount >= board.MineCount)
        {
            radius = ExtendedSafeRadius;
        }

        int safeZoneCells = CountSafeZoneCells(board, firstClickIndex, radius);
        int allowedCells = board.CellCount - safeZoneCells;

        if (board.MineCount > allowedCells)
        {
            error = MineSweeper.Core.Localization.Strings.Get("board.mineCountTooHigh", allowedCells, safeZoneCells);
            return false;
        }

        bool noGuess = settings.NoGuess && board.CellCount <= Solver.LogicSolver.NoGuessMaxCells;
        int attempts = noGuess ? NoGuessMaxAttempts : 1;
        bool solvable = false;
        var noGuessWatch = noGuess ? System.Diagnostics.Stopwatch.StartNew() : null;

        for (int attempt = 0; attempt < attempts; attempt++)
        {
            if (attempt > 0)
            {
                board.ClearAllState();
                board.MinesPlaced = false;
            }

            PlaceMinesBySelectionSampling(board, firstClickIndex, radius, random);
            ComputeAdjacentMines(board);

            if (!noGuess)
            {
                break;
            }

            // 无猜模式（v1.5）：只接受"从首点出发全程可纯逻辑推完"的盘；不行就用同一个 Random 继续抽下一张。
            // 同一个 seed + 同一个首点仍然得到同一张盘（重试序列也是确定的），分享串照样能复现。
            board.MinesPlaced = true;
            if (Solver.LogicSolver.CanSolveWithoutGuessing(board, firstClickIndex))
            {
                solvable = true;
                break;
            }

            // 时间预算兜底：极端密雷的盘可能 2000 次都抽不到，不能把首点卡住太久。
            if (noGuessWatch!.ElapsedMilliseconds >= NoGuessMaxMilliseconds)
            {
                break;
            }
        }

        board.FirstClickIndex = firstClickIndex;
        board.UsedExtendedSafeZone = radius == ExtendedSafeRadius;
        board.NoGuessRequested = settings.NoGuess;
        board.NoGuessGuaranteed = solvable;
        board.MinesPlaced = true;

        error = null;
        return true;
    }

    /// <summary>
    /// 锁定盘面模式（§2.6）：先按 seed 在全盘均匀布雷，再取一个安全首点。
    ///
    /// 与 <see cref="TryPlaceMines"/> 的本质区别：雷位<b>完全不依赖"玩家点在哪"</b>，
    /// 所以同一个 seed 永远得到同一张盘——这正是"锁定盘面"要解决的问题
    /// （普通模式下同 seed 换个首点就是另一张盘）。
    /// </summary>
    public static bool TryPlaceMinesLocked(Board board, GameSettings settings, Random random, out string? error)
    {
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(random);

        if (board.MinesPlaced)
        {
            error = MineSweeper.Core.Localization.Strings.Get("board.alreadyPlaced");
            return false;
        }

        if (!GameLimits.ValidateParameters(board.Width, board.Height, board.MineCount, out error))
        {
            return false;
        }

        int radius = DefaultSafeRadius;
        if (settings.FirstClickOpening &&
            (long)board.CellCount - GameLimits.ExtendedSafeZoneCellCount >= board.MineCount)
        {
            radius = ExtendedSafeRadius;
        }

        // 1) 全盘均匀布雷：不排除任何格子，这是"盘面与首点无关"的关键
        PlaceMinesUniformly(board, random);

        // 2) 取一个安全首点：从棋盘中心向外找，让首点落在更像玩家会点的位置
        int firstClick = FindSafeFirstClick(board, radius);

        if (firstClick < 0)
        {
            // 3) 找不到就把中心格的雷搬出去（雷数保持不变），保证首点一定安全
            firstClick = board.Index(board.Width / 2, board.Height / 2);
            if (!RelocateMinesOutOfSafeZone(board, firstClick, radius, random))
            {
                error = MineSweeper.Core.Localization.Strings.Get("board.tooDense");
                return false;
            }
        }

        ComputeAdjacentMines(board);

        board.FirstClickIndex = firstClick;
        board.UsedExtendedSafeZone = radius == ExtendedSafeRadius;
        board.MinesPlaced = true;

        error = null;
        return true;
    }

    /// <summary>在全盘所有格子上均匀布雷（选择采样法，不排除任何格子）。</summary>
    private static void PlaceMinesUniformly(Board board, Random random)
    {
        int remainingToPlace = board.MineCount;
        if (remainingToPlace <= 0)
        {
            return;
        }

        int remainingCandidates = board.CellCount;

        for (int index = 0; index < board.CellCount && remainingToPlace > 0; index++)
        {
            if (random.NextDouble() < (double)remainingToPlace / remainingCandidates)
            {
                board.AddFlag(index, CellFlags.IsMine);
                remainingToPlace--;
            }

            remainingCandidates--;
        }
    }

    /// <summary>某个 (x,y) 的 radius 圈内是否一颗雷都没有（越界按裁切处理）。</summary>
    private static bool IsSafeFirstClick(Board board, int x, int y, int radius)
    {
        int x0 = Math.Max(0, x - radius);
        int x1 = Math.Min(board.Width - 1, x + radius);
        int y0 = Math.Max(0, y - radius);
        int y1 = Math.Min(board.Height - 1, y + radius);

        for (int ny = y0; ny <= y1; ny++)
        {
            for (int nx = x0; nx <= x1; nx++)
            {
                if (board.IsMine(board.Index(nx, ny)))
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// 从棋盘中心一圈一圈向外找第一个"安全区无雷"的格子；找不到返回 -1。
    /// 按"圈"遍历而不是直接扫索引，是为了让首点尽量落在棋盘中部（更像玩家会点的位置），
    /// 同时保持完全确定性。
    /// </summary>
    private static int FindSafeFirstClick(Board board, int radius)
    {
        int cx = board.Width / 2;
        int cy = board.Height / 2;
        int maxRadius = Math.Max(board.Width, board.Height);

        for (int r = 0; r <= maxRadius; r++)
        {
            int y0 = Math.Max(0, cy - r);
            int y1 = Math.Min(board.Height - 1, cy + r);
            int x0 = Math.Max(0, cx - r);
            int x1 = Math.Min(board.Width - 1, cx + r);

            for (int y = y0; y <= y1; y++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    // r > 0 时只走这一圈的边界，内圈上一轮已经查过
                    if (r > 0 && Math.Abs(x - cx) != r && Math.Abs(y - cy) != r)
                    {
                        continue;
                    }

                    if (IsSafeFirstClick(board, x, y, radius))
                    {
                        return board.Index(x, y);
                    }
                }
            }
        }

        return -1;
    }

    /// <summary>
    /// 把首点安全区内的雷挪到安全区外（雷数不变）。
    /// 只在"全盘找不到任何安全首点"时才走这条路径（雷很密的自定义盘）。
    /// </summary>
    private static bool RelocateMinesOutOfSafeZone(Board board, int firstClick, int radius, Random random)
    {
        int cx = board.XOf(firstClick);
        int cy = board.YOf(firstClick);

        var trapped = new List<int>();
        for (int y = Math.Max(0, cy - radius); y <= Math.Min(board.Height - 1, cy + radius); y++)
        {
            for (int x = Math.Max(0, cx - radius); x <= Math.Min(board.Width - 1, cx + radius); x++)
            {
                int index = board.Index(x, y);
                if (board.IsMine(index))
                {
                    trapped.Add(index);
                }
            }
        }

        foreach (int mine in trapped)
        {
            board.RemoveFlag(mine, CellFlags.IsMine);

            int target = FindFreeCellOutsideSafeZone(board, firstClick, radius, random);
            if (target < 0)
            {
                // 放不回去：撤销这一次搬运，避免雷数变少（正常参数下不会走到这里）
                board.AddFlag(mine, CellFlags.IsMine);
                continue;
            }

            board.AddFlag(target, CellFlags.IsMine);
        }

        return IsSafeFirstClick(board, cx, cy, radius);
    }

    /// <summary>找一个"安全区之外且不是雷"的空格：先随机试若干次，再线性兜底。</summary>
    private static int FindFreeCellOutsideSafeZone(Board board, int firstClick, int radius, Random random)
    {
        for (int attempt = 0; attempt < 200; attempt++)
        {
            int candidate = random.Next(board.CellCount);
            if (!board.IsMine(candidate) && !IsInSafeZone(board, firstClick, candidate, radius))
            {
                return candidate;
            }
        }

        for (int index = 0; index < board.CellCount; index++)
        {
            if (!board.IsMine(index) && !IsInSafeZone(board, firstClick, index, radius))
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>
    /// 按已知雷位直接铺设（同盘重开 / 快照还原，§6.3）。不重新随机，盘面完全一致。
    /// </summary>
    public static bool TryPlaceMinesAt(Board board, int firstClickIndex, IReadOnlyList<int> mineIndexes, out string? error)
    {
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(mineIndexes);

        if (board.MinesPlaced)
        {
            error = MineSweeper.Core.Localization.Strings.Get("board.alreadyPlaced");
            return false;
        }

        foreach (int index in mineIndexes)
        {
            if (!board.IsValidIndex(index))
            {
                error = MineSweeper.Core.Localization.Strings.Get("board.mineIndexOutOfRange", index);
                return false;
            }

            board.AddFlag(index, CellFlags.IsMine);
        }

        int mineCount = board.GetMineIndexes().Length;
        if (!GameLimits.ValidateParameters(board.Width, board.Height, mineCount, out error))
        {
            return false;
        }

        board.MineCount = mineCount;
        ComputeAdjacentMines(board);

        board.FirstClickIndex = firstClickIndex;
        board.MinesPlaced = true;

        error = null;
        return true;
    }

    /// <summary>
    /// 选择采样法（Algorithm S）：一趟遍历，边扫边按"剩余要放的雷数 / 剩余可用格数"的概率决定。
    /// 结果与"候选集合里均匀抽 M 个"等价，但不需要候选数组。
    /// </summary>
    private static void PlaceMinesBySelectionSampling(Board board, int firstClickIndex, int radius, Random random)
    {
        int remainingToPlace = board.MineCount;
        if (remainingToPlace <= 0)
        {
            return;
        }

        int remainingCandidates = board.CellCount - CountSafeZoneCells(board, firstClickIndex, radius);
        if (remainingCandidates <= 0)
        {
            return;
        }

        for (int index = 0; index < board.CellCount && remainingToPlace > 0; index++)
        {
            if (IsInSafeZone(board, firstClickIndex, index, radius))
            {
                continue;
            }

            if (random.NextDouble() < (double)remainingToPlace / remainingCandidates)
            {
                board.AddFlag(index, CellFlags.IsMine);
                remainingToPlace--;
            }

            remainingCandidates--;
        }
    }

    /// <summary>
    /// 计算全盘邻雷数。这是首次点击时的一次性 O(8N) 开销，也是全项目唯一的"每格都跑一遍"的热路径，
    /// 所以必须用 <see cref="Board.GetNeighbors8"/> 的 <see cref="Span{T}"/> 版本：
    /// <see cref="Board.Neighbors8"/> 是 <c>yield return</c> 迭代器，每格都会分配一个状态机对象，
    /// 在 4096×4096（1677 万格）上就是 1677 万次堆分配，首次点击要卡住好几秒。
    /// 洪水填充早就走了同样的零分配路径（见 <see cref="FloodFillOperation"/>），这里对齐。
    /// </summary>
    private static void ComputeAdjacentMines(Board board)
    {
        Span<int> buffer = stackalloc int[8];

        for (int index = 0; index < board.CellCount; index++)
        {
            int count = 0;
            int neighborCount = board.GetNeighbors8(index, buffer);
            for (int i = 0; i < neighborCount; i++)
            {
                if (board.IsMine(buffer[i]))
                {
                    count++;
                }
            }

            board.SetAdjacentMines(index, (byte)count);
        }
    }
}
