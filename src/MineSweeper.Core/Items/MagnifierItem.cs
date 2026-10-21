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
///
/// v1.4：作用范围从 3×3 削弱为 **2×2**，且**被点中的格子是区域左上角**
/// （点哪格就翻"它 + 右 + 下 + 右下"这 4 格）。贴边时按棋盘边界裁切，所以
/// 最右一列/最下一行实际只翻得到 2 格。
/// </summary>
public sealed class MagnifierItem : IItem
{
    /// <summary>持久化与配置里使用的稳定 Id。</summary>
    public const string ItemId = "magnifier";

    /// <summary>默认作用范围：2×2（v1.4 起，原为 3×3）。</summary>
    public const int DefaultWidth = 2;

    /// <summary>默认作用范围的高：2×2。</summary>
    public const int DefaultHeight = 2;

    /// <summary>旧的"半径"默认值，保留给 <see cref="GetAffectedIndexes"/> 这类按半径取格的调用。</summary>
    public const int DefaultRadius = 1;

    public MagnifierItem(int width = DefaultWidth, int height = DefaultHeight)
    {
        Width = Math.Clamp(width, 1, 9);
        Height = Math.Clamp(height, 1, 9);
    }

    public MagnifierItem(ItemConfig config)
        : this(config?.MagnifierWidth ?? DefaultWidth, config?.MagnifierHeight ?? DefaultHeight)
    {
    }

    /// <inheritdoc />
    public string Id => ItemId;

    /// <inheritdoc />
    public string DisplayName => "放大镜";

    /// <summary>作用区域的宽（格）。</summary>
    public int Width { get; }

    /// <summary>作用区域的高（格）。</summary>
    public int Height { get; }

    /// <summary>作用区域的形状（左上角锚点）。</summary>
    public AreaShape Shape => AreaShape.TopLeft(Width, Height);

    /// <summary>
    /// 等价的"半径"表述，仅为兼容旧调用：形状不是正方形时返回 -1。
    /// 新代码请直接用 <see cref="Shape"/> / <see cref="Width"/> / <see cref="Height"/>。
    /// </summary>
    [Obsolete("v1.4 起放大镜是矩形，请改用 Shape / Width / Height。")]
    public int Radius => Width == Height ? (Width - 1) / 2 : -1;

    /// <inheritdoc />
    public ItemUseCheck Check(GameSession session, int targetIndex)
    {
        ArgumentNullException.ThrowIfNull(session);
        return session.CheckMagnifier(targetIndex);
    }

    /// <summary>
    /// 计算"以 <paramref name="centerIndex"/> 为中心、半径 <paramref name="radius"/> 的方形区域"。
    /// 这是**声呐**仍在用的取格规则（3×3 居中），放大镜已不用它。
    /// </summary>
    public static IReadOnlyList<int> GetAffectedIndexes(Board board, int centerIndex, int radius)
        => AreaShape.Square(radius).IndexesAt(board, centerIndex);

    /// <summary>按形状取格（放大镜用 <see cref="Shape"/>，声呐用 <see cref="AreaShape.Square"/>）。</summary>
    public static IReadOnlyList<int> GetAffectedIndexes(Board board, int centerIndex, AreaShape shape)
        => shape.IndexesAt(board, centerIndex);

    /// <summary>目标区域是否已全部被透视过（§5.3：是的话禁止使用，且不消耗道具）。</summary>
    public static bool AllAffectedAlreadyItemRevealed(Board board, int centerIndex, AreaShape shape)
    {
        IReadOnlyList<int> affected = GetAffectedIndexes(board, centerIndex, shape);
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

    /// <summary>半径版本的兼容重载（声呐在用）。</summary>
    public static bool AllAffectedAlreadyItemRevealed(Board board, int centerIndex, int radius)
        => AllAffectedAlreadyItemRevealed(board, centerIndex, AreaShape.Square(radius));

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
        IReadOnlyList<int> affected = Shape.IndexesAt(board, centerIndex);
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

        return new MagnifierReveal(centerIndex, Width, Height, cells, mines, empty, newlyRevealed);
    }
}
