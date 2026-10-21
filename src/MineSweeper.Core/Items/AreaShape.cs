namespace MineSweeper.Core.Items;

/// <summary>
/// 一次道具作用的区域形状。
///
/// 为什么不再用"半径"表示：半径只能表达**奇数边长**的正方形（1→3×3、2→5×5），
/// 而 v1.4 起放大镜被削弱成 **2×2**（偶数边长），半径表达不了。
///
/// 所以这里统一用"宽 × 高 + 锚点偏移"：
/// <list type="bullet">
/// <item><see cref="Width"/>/<see cref="Height"/> 是区域边长（格数）。</item>
/// <item><see cref="AnchorX"/>/<see cref="AnchorY"/> 说明**被点中的那一格**在区域里的位置
///       （0 = 左上角）。放大镜点哪格就翻以它为左上角的 2×2，所以锚点是 (0,0)；
///       声呐是 3×3、点的那格在正中间，所以锚点是 (1,1)。</item>
/// </list>
/// 这样两种形状共用同一套取格逻辑，贴边时统一按棋盘边界裁切（不环绕、不补偿）。
/// </summary>
public readonly record struct AreaShape(int Width, int Height, int AnchorX, int AnchorY)
{
    /// <summary>最基础的正方形区域，锚点在正中间 —— 即"半径 r"的等价写法（边长 2r+1）。</summary>
    public static AreaShape Square(int radius)
    {
        int side = (Math.Max(0, radius) * 2) + 1;
        int center = Math.Max(0, radius);
        return new AreaShape(side, side, center, center);
    }

    /// <summary>以被点中的格子为**左上角**展开的矩形（放大镜的 2×2 就是 <c>TopLeft(2, 2)</c>）。</summary>
    public static AreaShape TopLeft(int width, int height) => new(width, height, 0, 0);

    /// <summary>区域是否完整落在棋盘内（不裁切）。贴边使用会少于宽高乘积那么多格。</summary>
    public bool FitsAt(Board board, int index)
    {
        ArgumentNullException.ThrowIfNull(board);

        if (!board.IsValidIndex(index))
        {
            return false;
        }

        int x = board.XOf(index) - AnchorX;
        int y = board.YOf(index) - AnchorY;

        return x >= 0 && y >= 0 && x + Width <= board.Width && y + Height <= board.Height;
    }

    /// <summary>
    /// 取这个区域覆盖的格索引，自动按棋盘边界裁切（不环绕、不补偿）。
    /// 顺序为"逐行、从左到右"，与 <see cref="MagnifierCellInfo"/> 的展示顺序一致。
    /// </summary>
    public IReadOnlyList<int> IndexesAt(Board board, int index)
    {
        ArgumentNullException.ThrowIfNull(board);

        var result = new List<int>(Math.Max(1, Width * Height));
        if (!board.IsValidIndex(index))
        {
            return result;
        }

        int x0 = board.XOf(index) - AnchorX;
        int y0 = board.YOf(index) - AnchorY;

        int fromX = Math.Max(0, x0);
        int toX = Math.Min(board.Width - 1, x0 + Width - 1);
        int fromY = Math.Max(0, y0);
        int toY = Math.Min(board.Height - 1, y0 + Height - 1);

        for (int y = fromY; y <= toY; y++)
        {
            for (int x = fromX; x <= toX; x++)
            {
                result.Add(board.Index(x, y));
            }
        }

        return result;
    }

    /// <summary>给日志与文案用的人类可读形状，如 <c>2×2</c>。</summary>
    public override string ToString() => $"{Width}×{Height}";
}
