namespace MineSweeper.Core;

/// <summary>
/// 棋盘状态的唯一容器（§2.1）。
/// 一维数组 + 索引换算 <c>index = y * width + x</c>：比二维数组更快，
/// 邻格遍历时也只需要一次边界判断。
/// </summary>
public sealed class Board
{
    private readonly Cell[] _cells;

    public Board(int width, int height, int mineCount = 0, int? seed = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);

        Width = width;
        Height = height;
        MineCount = mineCount;
        Seed = seed;

        _cells = new Cell[width * height];
        for (int i = 0; i < _cells.Length; i++)
        {
            _cells[i].AdjacentMines = Cell.AdjacentMinesDirty;
        }
    }

    /// <summary>棋盘宽度（列数）。</summary>
    public int Width { get; }

    /// <summary>棋盘高度（行数）。</summary>
    public int Height { get; }

    /// <summary>总格数 N。</summary>
    public int CellCount => Width * Height;

    /// <summary>雷数 M。</summary>
    public int MineCount { get; internal set; }

    /// <summary>是否已完成布雷（首点之前为 false，§2.2）。</summary>
    public bool MinesPlaced { get; internal set; }

    /// <summary>本局随机种子（§2.6）。</summary>
    public int? Seed { get; }

    /// <summary>首次点击的格索引（布雷后才有效）。</summary>
    public int FirstClickIndex { get; internal set; } = -1;

    /// <summary>布雷时是否用上了 5×5 扩展安全区（见 <see cref="BoardGenerator"/>）。</summary>
    public bool UsedExtendedSafeZone { get; internal set; }

    /// <summary>本局是否请求了无猜模式（v1.4）。</summary>
    public bool NoGuessRequested { get; internal set; }

    /// <summary>
    /// 本盘是否经推理器验证"从首点出发全程不用猜"（v1.4）。
    /// 请求了无猜但棋盘太大（&gt; <see cref="Solver.LogicSolver.NoGuessMaxCells"/>）或雷太密抽不到时为 false。
    /// </summary>
    public bool NoGuessGuaranteed { get; internal set; }

    internal Cell[] RawCells => _cells;

    // ---------- 坐标换算 ----------

    /// <summary>把 (x, y) 换算为格索引。</summary>
    public int Index(int x, int y) => checked(y * Width + x);

    /// <summary>格索引 → 列。</summary>
    public int XOf(int index) => index % Width;

    /// <summary>格索引 → 行。</summary>
    public int YOf(int index) => index / Width;

    /// <summary>坐标是否落在棋盘内（unsigned 比较，一次判断完成上下界）。</summary>
    public bool InBounds(int x, int y) => (uint)x < (uint)Width && (uint)y < (uint)Height;

    /// <summary>格索引是否合法。</summary>
    public bool IsValidIndex(int index) => (uint)index < (uint)CellCount;

    // ---------- 标志位访问 ----------

    public CellFlags GetFlags(int index) => _cells[index].Flags;

    public bool HasFlag(int index, CellFlags flag) => (_cells[index].Flags & flag) != 0;

    internal void AddFlag(int index, CellFlags flag) => _cells[index].Flags |= flag;

    internal void RemoveFlag(int index, CellFlags flag) => _cells[index].Flags &= ~flag;

    public bool IsMine(int index) => (_cells[index].Flags & CellFlags.IsMine) != 0;

    public bool IsRevealed(int index) => (_cells[index].Flags & CellFlags.IsRevealed) != 0;

    public bool IsFlagged(int index) => (_cells[index].Flags & CellFlags.IsFlagged) != 0;

    public bool IsQuestionMarked(int index) => (_cells[index].Flags & CellFlags.IsQuestion) != 0;

    /// <summary>是否被放大镜透视过（§5.4）。</summary>
    public bool IsItemRevealed(int index) => (_cells[index].Flags & CellFlags.ItemRevealed) != 0;

    // ---------- 邻雷数访问 ----------

    /// <summary>邻雷数（未计算时为 <see cref="Cell.AdjacentMinesDirty"/>）。</summary>
    public byte GetAdjacentMines(int index) => _cells[index].AdjacentMines;

    internal void SetAdjacentMines(int index, byte value) => _cells[index].AdjacentMines = value;

    // ---------- 邻格遍历 ----------

    /// <summary>枚举 index 周围 8 邻格（自动跳过越界格）。通用路径，便于阅读。</summary>
    public IEnumerable<int> Neighbors8(int index)
    {
        int x = index % Width;
        int y = index / Width;

        int x0 = x > 0 ? x - 1 : 0;
        int x1 = x < Width - 1 ? x + 1 : Width - 1;
        int y0 = y > 0 ? y - 1 : 0;
        int y1 = y < Height - 1 ? y + 1 : Height - 1;

        for (int ny = y0; ny <= y1; ny++)
        {
            int rowBase = ny * Width;
            for (int nx = x0; nx <= x1; nx++)
            {
                if (nx == x && ny == y)
                {
                    continue;
                }

                yield return rowBase + nx;
            }
        }
    }

    /// <summary>
    /// 热路径版本：把 8 邻格写进调用方提供的缓冲区，返回实际写入数量（0..8）。
    /// 洪水填充每格都要遍历邻格，用 Span 避免迭代器分配（§2.3）。
    /// </summary>
    public int GetNeighbors8(int index, Span<int> buffer)
    {
        int x = index % Width;
        int y = index / Width;

        int x0 = x > 0 ? x - 1 : 0;
        int x1 = x < Width - 1 ? x + 1 : Width - 1;
        int y0 = y > 0 ? y - 1 : 0;
        int y1 = y < Height - 1 ? y + 1 : Height - 1;

        int count = 0;
        for (int ny = y0; ny <= y1; ny++)
        {
            int rowBase = ny * Width;
            for (int nx = x0; nx <= x1; nx++)
            {
                if (nx == x && ny == y)
                {
                    continue;
                }

                buffer[count++] = rowBase + nx;
            }
        }

        return count;
    }

    /// <summary>统计 index 周围带有指定标志的格数（chord 与道具计数都要用）。</summary>
    public int CountNeighborsWith(int index, CellFlags flag)
    {
        int count = 0;
        foreach (int n in Neighbors8(index))
        {
            if ((_cells[n].Flags & flag) != 0)
            {
                count++;
            }
        }

        return count;
    }

    // ---------- 全盘统计 ----------

    /// <summary>已翻开的格数。</summary>
    public int CountRevealed()
    {
        int count = 0;
        for (int i = 0; i < _cells.Length; i++)
        {
            if ((_cells[i].Flags & CellFlags.IsRevealed) != 0)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>已插旗的格数。</summary>
    public int CountFlagged()
    {
        int count = 0;
        for (int i = 0; i < _cells.Length; i++)
        {
            if ((_cells[i].Flags & CellFlags.IsFlagged) != 0)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>枚举全部雷的格索引（失败后展示全盘雷位、快照、测试都要用）。</summary>
    public IEnumerable<int> EnumerateMines()
    {
        for (int i = 0; i < _cells.Length; i++)
        {
            if ((_cells[i].Flags & CellFlags.IsMine) != 0)
            {
                yield return i;
            }
        }
    }

    /// <summary>取全部雷的格索引数组。</summary>
    public int[] GetMineIndexes()
    {
        var list = new List<int>(Math.Min(MineCount, 1024));
        foreach (int i in EnumerateMines())
        {
            list.Add(i);
        }

        return list.ToArray();
    }

    /// <summary>清空全部状态（同盘重开时复用棋盘对象，避免重新分配，§6.3）。</summary>
    internal void ClearAllState()
    {
        for (int i = 0; i < _cells.Length; i++)
        {
            _cells[i].Flags = CellFlags.None;
            _cells[i].AdjacentMines = Cell.AdjacentMinesDirty;
        }
    }
}
