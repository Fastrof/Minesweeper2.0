using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace MineSweeper.App.Linux.Controls;

/// <summary>笑脸的三种状态（对应 Windows 端 ViewModel.SmileyGlyph）。</summary>
public enum SmileyState
{
    /// <summary>🙂 准备 / 进行中。</summary>
    Normal,

    /// <summary>😎 胜利。</summary>
    Won,

    /// <summary>😵 踩雷。</summary>
    Lost,
}

/// <summary>
/// 自绘笑脸按钮的图形（不依赖 emoji 字体）。
///
/// 为什么不用 "🙂" 字符：Linux 的 fontconfig 会把 U+1F642 回退到 Symbola（单色轮廓），
/// 而 Windows 用的是 Segoe UI Emoji 彩色字形 —— 同一个字符在两个平台观感完全不同。
/// 用 Skia 直接画圆/眼/嘴，三个平台都能保证是同一张"黄色笑脸"。
/// </summary>
public sealed class SmileyControl : Control
{
    private SmileyState _state = SmileyState.Normal;

    public SmileyState State
    {
        get => _state;
        set
        {
            if (_state == value)
            {
                return;
            }

            _state = value;
            InvalidateVisual();
        }
    }

    public override void Render(DrawingContext context)
    {
        double size = Math.Min(Bounds.Width, Bounds.Height);
        if (size <= 6)
        {
            return;
        }

        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
        double radius = (size / 2) - 1.5;

        // 脸：暖黄色 + 深一点的描边
        var faceBrush = new SolidColorBrush(Color.Parse("#FFD34D"));
        var outline = new Pen(new SolidColorBrush(Color.Parse("#C9A227")), Math.Max(1, radius * 0.06));
        context.DrawEllipse(faceBrush, outline, center, radius, radius);

        var ink = new SolidColorBrush(Color.Parse("#2B2B2B"));
        double eyeRadius = radius * 0.14;
        double eyeDx = radius * 0.36;
        double eyeDy = radius * 0.30;

        if (_state == SmileyState.Won)
        {
            // 笑眼：两条向下的弧
            DrawArc(context, ink, Math.Max(1.2, radius * 0.09),
                new Point(center.X - eyeDx - eyeRadius, center.Y - eyeDy + eyeRadius * 0.4),
                new Point(center.X - eyeDx + eyeRadius, center.Y - eyeDy - eyeRadius * 0.4),
                new Point(center.X - eyeDx, center.Y - eyeDy + eyeRadius));
            DrawArc(context, ink, Math.Max(1.2, radius * 0.09),
                new Point(center.X + eyeDx - eyeRadius, center.Y - eyeDy - eyeRadius * 0.4),
                new Point(center.X + eyeDx + eyeRadius, center.Y - eyeDy + eyeRadius * 0.4),
                new Point(center.X + eyeDx, center.Y - eyeDy + eyeRadius));
        }
        else if (_state == SmileyState.Lost)
        {
            // X 眼
            var pen = new Pen(ink, Math.Max(1.2, radius * 0.09));
            double xSize = eyeRadius * 1.15;
            DrawLine(context, pen, center.X - eyeDx - xSize, center.Y - eyeDy - xSize, center.X - eyeDx + xSize, center.Y - eyeDy + xSize);
            DrawLine(context, pen, center.X - eyeDx + xSize, center.Y - eyeDy - xSize, center.X - eyeDx - xSize, center.Y - eyeDy + xSize);
            DrawLine(context, pen, center.X + eyeDx - xSize, center.Y - eyeDy - xSize, center.X + eyeDx + xSize, center.Y - eyeDy + xSize);
            DrawLine(context, pen, center.X + eyeDx + xSize, center.Y - eyeDy - xSize, center.X + eyeDx - xSize, center.Y - eyeDy + xSize);
        }
        else
        {
            // 圆眼
            context.DrawEllipse(ink, null, new Point(center.X - eyeDx, center.Y - eyeDy), eyeRadius, eyeRadius);
            context.DrawEllipse(ink, null, new Point(center.X + eyeDx, center.Y - eyeDy), eyeRadius, eyeRadius);
        }

        // 嘴：Normal/Won 是微笑弧，Lost 是向下撇
        var mouthPen = new Pen(ink, Math.Max(1.4, radius * 0.10));
        double mouthHalf = radius * 0.44;
        if (_state == SmileyState.Lost)
        {
            DrawArc(context, ink, mouthPen.Thickness,
                new Point(center.X - mouthHalf, center.Y + radius * 0.42),
                new Point(center.X, center.Y + radius * 0.18),
                new Point(center.X + mouthHalf, center.Y + radius * 0.42),
                filled: false,
                stroke: mouthPen);
        }
        else
        {
            DrawArc(context, ink, mouthPen.Thickness,
                new Point(center.X - mouthHalf, center.Y + radius * 0.18),
                new Point(center.X, center.Y + radius * 0.46),
                new Point(center.X + mouthHalf, center.Y + radius * 0.18),
                filled: false,
                stroke: mouthPen);
        }
    }

    private static void DrawLine(DrawingContext context, Pen pen, double x1, double y1, double x2, double y2)
    {
        var geometry = new StreamGeometry();
        using (StreamGeometryContext ctx = geometry.Open())
        {
            ctx.BeginFigure(new Point(x1, y1), isFilled: false);
            ctx.LineTo(new Point(x2, y2));
            ctx.EndFigure(isClosed: false);
        }

        context.DrawGeometry(null, pen, geometry);
    }

    private static void DrawArc(
        DrawingContext context,
        IBrush brush,
        double thickness,
        Point start,
        Point control,
        Point end,
        bool filled = false,
        Pen? stroke = null)
    {
        var geometry = new StreamGeometry();
        using (StreamGeometryContext ctx = geometry.Open())
        {
            ctx.BeginFigure(start, isFilled: false);
            ctx.QuadraticBezierTo(control, end);
            ctx.EndFigure(isClosed: false);
        }

        context.DrawGeometry(filled ? brush : null, stroke ?? new Pen(brush, thickness), geometry);
    }
}
