namespace MineSweeper.Core.Items;

/// <summary>
/// 放大镜（§5）。
///
/// 三条硬性约束（§5.4），全都体现在这个类的实现里：
/// 1. 不自动插旗 —— 只写 <see cref="CellFlags.ItemRevealed"/>，不写 IsFlagged。
///    否则"透视 → 插旗 → 计入获取阈值 → 再换一个放大镜"会形成循环套利。
/// 2. 不计入"已翻开" —— 不写 IsRevealed，因此不会触发洪水填充、不影响剩余雷数、
///    也不参与胜利判定。
/// 3. 信息永久保留 —— 标记位一直留着，UI 一直按透视样式绘制。
/// </summary>
public sealed class MagnifierItem : IItem
{
    /// <summary>持久化与配置里使用的稳定 Id。</summary>
    public const string ItemId = "magnifier";

    /// <summary>默认透视半径：1 → 3×3。</summary>
    public const int DefaultRadius = 1;

    public MagnifierItem(int radius = DefaultRadius)
    {
        Radius = Math.Clamp(radius, 0, 4);
    }

    public MagnifierItem(ItemConfig config)
        : this(config?.MagnifierRadius ?? DefaultRadius)
    {
    }

    /// <inheritdoc />
    public string Id => ItemId;

    /// <inheritdoc />
    public string DisplayName => "放大镜";

    /// <summary>透视半径（1 即 3×3）。</summary>
    public int Radius { get; }

    /// <inheritdoc />
    public ItemUseCheck Check(GameSession session, int targetIndex)
    {
        ArgumentNullException.ThrowIfNull(session);
        return session.CheckMagnifier(targetIndex);
    }

    /// <summary>
    /// 计算中心格周围 radius 圈内的格索引（自动按棋盘边界裁切，不环绕、不补偿）。
    /// </summary>
    public static IReadOnlyList<int> GetAffectedIndexes(Board board, int centerIndex, int radius)
    {
        ArgumentNullException.ThrowIfNull(board);

        var result = new List<int>();
        if (!board.IsValidIndex(centerIndex))
        {
            return result;
        }

        int cx = board.XOf(centerIndex);
        int cy = board.YOf(centerIndex);

        int x0 = Math.Max(0, cx - radius);
        int x1 = Math.Min(board.Width - 1, cx + radius);
        int y0 = Math.Max(0, cy - radius);
        int y1 = Math.Min(board.Height - 1, cy + radius);

        for (int y = y0; y <= y1; y++)
        {
            for (int x = x0; x <= x1; x++)
            {
                result.Add(board.Index(x, y));
            }
        }

        return result;
    }

    /// <summary>目标区域是否已全部被透视过（§5.3：是的话禁止使用，且不消耗道具）。</summary>
    public static bool AllAffectedAlreadyItemRevealed(Board board, int centerIndex, int radius)
    {
        IReadOnlyList<int> affected = GetAffectedIndexes(board, centerIndex, radius);
        if (affected.Count == 0)
        {
            return true;
        }

        foreach (int index in affected)
        {
            if (!board.IsItemRevealed(index))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 只读地准备透视内容（不改棋盘状态），供 UI 做悬停预览。
    /// </summary>
    public MagnifierReveal Preview(Board board, int centerIndex)
    {
        ArgumentNullException.ThrowIfNull(board);
        return BuildReveal(board, centerIndex, apply: false);
    }

    /// <summary>
    /// 结算透视：标记 <see cref="CellFlags.ItemRevealed"/> 并返回每格内容。
    /// 只标记透视位，绝不改 IsRevealed / IsFlagged。
    /// </summary>
    internal MagnifierReveal Apply(Board board, int centerIndex)
    {
        ArgumentNullException.ThrowIfNull(board);
        return BuildReveal(board, centerIndex, apply: true);
    }

    private MagnifierReveal BuildReveal(Board board, int centerIndex, bool apply)
    {
        IReadOnlyList<int> affected = GetAffectedIndexes(board, centerIndex, Radius);
        var cells = new List<MagnifierCellInfo>(affected.Count);

        int mines = 0;
        int empty = 0;
        int newlyRevealed = 0;

        foreach (int index in affected)
        {
            bool wasAlready = board.IsItemRevealed(index);
            bool isMine = board.IsMine(index);

            if (apply && !wasAlready)
            {
                board.AddFlag(index, CellFlags.ItemRevealed);
                newlyRevealed++;
            }

            if (isMine)
            {
                mines++;
            }
            else
            {
                empty++;
            }

            cells.Add(new MagnifierCellInfo(index, isMine, board.GetAdjacentMines(index), wasAlready));
        }

        return new MagnifierReveal(centerIndex, Radius, cells, mines, empty, newlyRevealed);
    }
}
