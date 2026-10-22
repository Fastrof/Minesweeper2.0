using Avalonia;

namespace MineSweeper.App.Linux.Rendering;

/// <summary>
/// 视口换算（**移植自** Windows 端 <c>src\MineSweeper.App\Rendering\BoardViewport.cs</c>）。
///
/// 原版依赖 <c>Windows.Foundation.Rect</c> / <c>Windows.UI.Color</c>，这里换成 Avalonia 的
/// <see cref="Rect"/>；其余公式（格 ↔ 屏幕、可见区间、边界裁剪）逐行保留，因为它们是
/// "棋盘画得对不对"的核心，重写没有意义。差异只有一处：Avalonia 的绘制设备无关单位是 DIP，
/// 而自定义 draw-op 拿到的是物理像素——换算放在渲染层，本结构只认 DIP。
/// </summary>
public readonly struct BoardViewport
{
    public BoardViewport(
        double cellSize,
        double offsetX,
        double offsetY,
        double controlWidth,
        double controlHeight,
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

    public double CellSize { get; }
    public double OffsetX { get; }
    public double OffsetY { get; }
    public double ControlWidth { get; }
    public double ControlHeight { get; }
    public int FirstCol { get; }
    public int LastCol { get; }
    public int FirstRow { get; }
    public int LastRow { get; }

    /// <summary>是否处于远景（只画色块）。</summary>
    public bool IsFarZoom => CellSize < LinuxBoardTheme.FarZoomCellSize;

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

    /// <summary>整个棋盘的矩形。</summary>
    public static Rect BoardBounds(int width, int height, double cellSize)
        => new(0, 0, width * cellSize, height * cellSize);

    /// <summary>某个格的矩形（局部重绘用）。</summary>
    public Rect CellRectForInvalidate(int col, int row, int marginCells = 0)
    {
        double m = marginCells * CellSize;
        return new Rect(
            OffsetX + (col * CellSize) - m,
            OffsetY + (row * CellSize) - m,
            CellSize + (m * 2),
            CellSize + (m * 2));
    }

    /// <summary>把矩形与控件可见区域求交。</summary>
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
