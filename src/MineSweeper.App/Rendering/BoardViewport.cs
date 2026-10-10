using Windows.Foundation;
using Windows.UI;

namespace MineSweeper.App.Rendering;

/// <summary>
/// 视口（§1.5 / §4.5）：把"棋盘格坐标"与"屏幕 DIP 坐标"互相换算，
/// 并预先算好当前可见的行列区间，让渲染只遍历可视范围。
/// DIP 与物理像素的换算交给 Win2D（它的坐标本来就是 DIP），Core 层只认格索引。
/// </summary>
internal readonly struct BoardViewport
{
    public BoardViewport(
        float cellSize,
        float offsetX,
        float offsetY,
        float controlWidth,
        float controlHeight,
        int firstCol,
        int lastCol,
        int firstRow,
        int lastRow)
    {
        CellSize = cellSize;
        OffsetX = offsetX;
        OffsetY = offsetY;
        ControlWidth = controlWidth;
        ControlHeight = controlHeight;
        FirstCol = firstCol;
        LastCol = lastCol;
        FirstRow = firstRow;
        LastRow = lastRow;
    }

    /// <summary>当前格宽（DIP）。</summary>
    public float CellSize { get; }

    /// <summary>棋盘原点在控件内的 X 偏移。</summary>
    public float OffsetX { get; }

    /// <summary>棋盘原点在控件内的 Y 偏移。</summary>
    public float OffsetY { get; }

    public float ControlWidth { get; }

    public float ControlHeight { get; }

    /// <summary>可见区间（闭区间）。</summary>
    public int FirstCol { get; }

    public int LastCol { get; }

    public int FirstRow { get; }

    public int LastRow { get; }

    /// <summary>是否处于远景（§1.5：只画色块，不画数字与旗子）。</summary>
    public bool IsFarZoom => CellSize < BoardTheme.FarZoomCellSize;

    /// <summary>格 → 矩形。</summary>
    public Rect CellRect(int col, int row)
        => new(OffsetX + (col * CellSize), OffsetY + (row * CellSize), CellSize, CellSize);

    /// <summary>屏幕坐标 → 列（越界返回 -1）。</summary>
    public int ColumnAt(double x)
    {
        if (CellSize <= 0)
        {
            return -1;
        }

        double col = Math.Floor((x - OffsetX) / CellSize);
        return col < 0 ? -1 : (int)col;
    }

    /// <summary>屏幕坐标 → 行（越界返回 -1）。</summary>
    public int RowAt(double y)
    {
        if (CellSize <= 0)
        {
            return -1;
        }

        double row = Math.Floor((y - OffsetY) / CellSize);
        return row < 0 ? -1 : (int)row;
    }

    /// <summary>整个棋盘的像素矩形（适应窗口用）。</summary>
    public static Rect BoardBounds(int width, int height, float cellSize)
        => new(0, 0, width * cellSize, height * cellSize);

    /// <summary>某个格的像素矩形在控件坐标系里的位置（局部重绘用，§4.5）。</summary>
    public Rect CellRectForInvalidate(int col, int row, int marginCells = 0)
    {
        double m = marginCells * CellSize;
        return new Rect(
            OffsetX + (col * CellSize) - m,
            OffsetY + (row * CellSize) - m,
            CellSize + (m * 2),
            CellSize + (m * 2));
    }

    /// <summary>把一个矩形与控件可见区域求交（避免无效重绘）。</summary>
    public Rect ClampToControl(Rect rect)
    {
        double left = Math.Max(0, rect.Left);
        double top = Math.Max(0, rect.Top);
        double right = Math.Min(ControlWidth, rect.Right);
        double bottom = Math.Min(ControlHeight, rect.Bottom);

        if (right <= left || bottom <= top)
        {
            return new Rect(0, 0, 0, 0);
        }

        return new Rect(left, top, right - left, bottom - top);
    }
}
