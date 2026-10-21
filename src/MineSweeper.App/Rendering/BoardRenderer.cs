using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Text;
using MineSweeper.Core;
using Windows.Foundation;
using Windows.UI;

namespace MineSweeper.App.Rendering;

/// <summary>
/// 棋盘绘制（§4.1 / §4.5）。
///
/// 调色板由调用方以 <see cref="BoardTheme"/> 实例传入（支持切换主题），本类不持有主题状态。
///
/// 性能设计（这两条都是实测逼出来的，见 <see cref="RenderPerfProbe"/>）：
/// <list type="number">
/// <item>
/// <b>远景合并同色行程</b>：实测每格约 0.34 µs，耗时几乎线性于"可见格数"——
/// 1000×1000 全景 = 100 万格 = 332 ms/帧（3 FPS）。所以远景路径不再逐格画，
/// 而是把<b>同一行里颜色相同的连续格子合成一条矩形</b>：几乎全是未翻开格时，
/// 100 万次调用会塌缩成约 1000 次。这是把 M2 验收从"不达标"拉回"达标"的关键。
/// </item>
/// <item>
/// <b>未翻开格位图切片</b>（文档 §4.1 的建议）：正常缩放下每个未翻开格要 1 次填充 + 4 条描边，
/// 实测 0.5× 时约 2.4 µs/格。预渲染成一张格子位图后变成 1 次 DrawImage。
/// </item>
/// </list>
/// 只遍历视口内的行列区间，不碰看不见的格子；文字格式按字号缓存。
/// </summary>
internal static class BoardRenderer
{
    /// <summary>热力图叠加的不透明度。</summary>
    private const float HeatOpacity = 0.55f;


    private static readonly Dictionary<int, CanvasTextFormat> TextFormats = new();

    // ---- 未翻开格的位图切片缓存（按 设备/尺寸/主题 三个维度失效）----
    private static CanvasRenderTarget? _coveredTile;
    private static int _coveredTileSize = -1;
    private static ICanvasResourceCreator? _coveredTileCreator;
    private static BoardTheme? _coveredTileTheme;

    public static void Draw(
        CanvasDrawingSession ds,
        ICanvasResourceCreator resourceCreator,
        BoardTheme theme,
        GameSession session,
        in BoardViewport viewport,
        int hoverIndex,
        bool aiming,
        int highlightCenter,
        float highlightAlpha,
        int hintCenter,
        float hintAlpha,
        bool heatmap)
    {
        Board board = session.Board;
        bool lost = session.Status == GameStatus.Lost;
        int exploded = session.ExplodedIndex ?? -1;
        MineSweeper.Core.Items.AreaShape shape = session.MagnifierShape;

        if (viewport.IsFarZoom)
        {
            // 远景：只画色块（§1.5），按"同色行程"合并绘制调用
            DrawFarZoom(ds, theme, board, viewport, exploded, heatmap);
        }
        else
        {
            for (int row = viewport.FirstRow; row <= viewport.LastRow; row++)
            {
                for (int col = viewport.FirstCol; col <= viewport.LastCol; col++)
                {
                    DrawCell(ds, resourceCreator, theme, board, board.Index(col, row), col, row, viewport, lost, exploded);
                }
            }
        }

        if (aiming && board.IsValidIndex(hoverIndex))
        {
            DrawAreaOverlay(ds, board, hoverIndex, viewport, shape, theme.AimingRing, theme.AimingFill);
        }

        // §5.4 的视觉反馈：透视结算后那一圈高亮在 1 秒内淡出（alpha 由 BoardView 逐帧给）
        if (highlightAlpha > 0f && board.IsValidIndex(highlightCenter))
        {
            DrawAreaOverlay(
                ds,
                board,
                highlightCenter,
                viewport,
                shape,
                theme.HighlightRing,
                theme.HighlightFill,
                highlightAlpha);
        }

        // v1.5 提示：金色单格高亮（2 秒淡出，由 BoardView 逐帧给 alpha）。只做视觉提示。
        if (hintAlpha > 0f && board.IsValidIndex(hintCenter))
        {
            DrawAreaOverlay(
                ds,
                board,
                hintCenter,
                viewport,
                MineSweeper.Core.Items.AreaShape.TopLeft(1, 1),
                Color.FromArgb(255, 0xFF, 0xC1, 0x07),
                Color.FromArgb(0x40, 0xFF, 0xC1, 0x07),
                hintAlpha);
        }

    }

    // ---------------------------------------------------------------- 远景路径

    /// <summary>远景里的一段"同色行程"。</summary>
    private readonly record struct FarRun(int StartCol, int EndCol, uint Key);

    /// <summary>
    /// 远景（格宽 &lt; 6 DIP）的绘制策略：先把每一行压成"同色行程"，再把<b>行程完全相同的连续行</b>
    /// 合并成一条竖直色带，最后每条色带只画一次矩形。
    ///
    /// 两个收益：
    /// <list type="bullet">
    /// <item>性能：几乎全是未翻开格时，100 万格会塌缩成个位数～几十次矩形绘制（实测 332ms → 约 17ms）。</item>
    /// <item>观感：格宽是小数（0.52 DIP）时，逐格绘制的矩形边缘会被抗锯齿混出一条条横向接缝；
    /// 行内与行间合并之后，大片同色区域是一条矩形，接缝自然消失。</item>
    /// </list>
    /// </summary>
    private static void DrawFarZoom(
        CanvasDrawingSession ds,
        BoardTheme theme,
        Board board,
        in BoardViewport viewport,
        int explodedIndex,
        bool heatmap)
    {
        var previous = new List<FarRun>(64);
        var current = new List<FarRun>(64);

        // 远景全是纯色块，关掉抗锯齿既更快，也让相邻矩形精确对齐像素、不再有接缝
        // （格宽只有 0.5 DIP 时，锯齿边缘会把大片同色区域混出一条条横纹）
        CanvasAntialiasing previousAntialiasing = ds.Antialiasing;
        ds.Antialiasing = CanvasAntialiasing.Aliased;

        try
        {
            int bandStartRow = viewport.FirstRow;
            BuildFarRuns(board, viewport, viewport.FirstRow, explodedIndex, previous);

            for (int row = viewport.FirstRow + 1; row <= viewport.LastRow; row++)
            {
                BuildFarRuns(board, viewport, row, explodedIndex, current);

                if (SameRuns(previous, current))
                {
                    continue;
                }

                FlushFarBand(ds, theme, viewport, bandStartRow, row - 1, previous);
                bandStartRow = row;

                // 交换复用两个列表，避免每行都分配
                (previous, current) = (current, previous);
            }

            FlushFarBand(ds, theme, viewport, bandStartRow, viewport.LastRow, previous);

            if (heatmap)
            {
                DrawHeatOverlay(ds, theme, board, viewport);
            }
        }
        finally
        {
            ds.Antialiasing = previousAntialiasing;
        }
    }

    /// <summary>
    /// 远景雷密度热力图（§1.5 可选）：按<b>块</b>统计平均邻雷数再着色。
    ///
    /// 刻意不逐格着色：远景本来就看不清单格，逐格着色会把"同色行程合并"彻底打碎
    /// （实测逐格热力图在 100 万格全景下 130 ms/帧，块状只要二十几毫秒），
    /// 而且块状的视觉呈现反而更接近"热力图"该有的样子。
    /// </summary>
    private static void DrawHeatOverlay(CanvasDrawingSession ds, BoardTheme theme, Board board, in BoardViewport viewport)
    {
        // 每块至少约 8 DIP 见方：太小会退化成逐格，太大就看不出分布
        int blockCells = Math.Max(1, (int)Math.Ceiling(8f / Math.Max(0.01f, viewport.CellSize)));

        int cols = (viewport.LastCol - viewport.FirstCol) + 1;
        int rows = (viewport.LastRow - viewport.FirstRow) + 1;
        int blocksX = Math.Max(1, (cols + blockCells - 1) / blockCells);
        int blocksY = Math.Max(1, (rows + blockCells - 1) / blockCells);

        for (int by = 0; by < blocksY; by++)
        {
            int y0 = viewport.FirstRow + (by * blockCells);
            int y1 = Math.Min(viewport.LastRow, y0 + blockCells - 1);

            for (int bx = 0; bx < blocksX; bx++)
            {
                int x0 = viewport.FirstCol + (bx * blockCells);
                int x1 = Math.Min(viewport.LastCol, x0 + blockCells - 1);

                int sum = 0;
                int count = 0;
                for (int y = y0; y <= y1; y++)
                {
                    int rowBase = y * board.Width;
                    for (int x = x0; x <= x1; x++)
                    {
                        sum += board.GetAdjacentMines(rowBase + x);
                        count++;
                    }
                }

                if (count == 0)
                {
                    continue;
                }

                double average = sum / (double)count;
                float t = (float)Math.Clamp(average / 8.0, 0.0, 1.0);

                var rect = new Rect(
                    viewport.OffsetX + (x0 * viewport.CellSize),
                    viewport.OffsetY + (y0 * viewport.CellSize),
                    (x1 - x0 + 1) * viewport.CellSize,
                    (y1 - y0 + 1) * viewport.CellSize);

                Color tint = Lerp(theme.HeatLowFill, theme.HeatHighFill, t);
                ds.FillRectangle(rect, ApplyOpacity(tint, HeatOpacity));
            }
        }
    }

    /// <summary>把一行压成"同色行程"列表（低 3 位底色种类 + 热力图档位作为 key）。</summary>
    private static void BuildFarRuns(
        Board board,
        in BoardViewport viewport,
        int row,
        int explodedIndex,
        List<FarRun> runs)
    {
        runs.Clear();

        int runStart = viewport.FirstCol;
        uint key = FarCellKey(board, board.Index(runStart, row), explodedIndex);

        for (int col = viewport.FirstCol + 1; col <= viewport.LastCol; col++)
        {
            uint next = FarCellKey(board, board.Index(col, row), explodedIndex);
            if (next == key)
            {
                continue;
            }

            runs.Add(new FarRun(runStart, col - 1, key));
            runStart = col;
            key = next;
        }

        runs.Add(new FarRun(runStart, viewport.LastCol, key));
    }

    private static bool SameRuns(List<FarRun> left, List<FarRun> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (int i = 0; i < left.Count; i++)
        {
            if (left[i] != right[i])
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>把一条竖直色带（若干相同结构的行）按行程逐段画出来。</summary>
    private static void FlushFarBand(
        CanvasDrawingSession ds,
        BoardTheme theme,
        in BoardViewport viewport,
        int firstRow,
        int lastRow,
        List<FarRun> runs)
    {
        if (lastRow < firstRow)
        {
            return;
        }

        double top = viewport.OffsetY + (firstRow * viewport.CellSize);
        double bottom = viewport.OffsetY + ((lastRow + 1) * viewport.CellSize);

        foreach (FarRun run in runs)
        {
            var rect = new Rect(
                viewport.OffsetX + (run.StartCol * viewport.CellSize),
                top,
                (run.EndCol - run.StartCol + 1) * viewport.CellSize,
                bottom - top);

            ds.FillRectangle(rect, FarCellColor(theme, run.Key));
        }
    }

    /// <summary>远景格子的"外观 key"：低 3 位是底色种类，高位是热力图档位。</summary>
    private static uint FarCellKey(Board board, int index, int explodedIndex)
    {
        CellFlags flags = board.GetFlags(index);

        uint kind;
        if (index == explodedIndex)
        {
            kind = 0; // 踩到的那颗雷
        }
        else if ((flags & CellFlags.IsRevealed) != 0)
        {
            kind = 1;
        }
        else if ((flags & CellFlags.IsFlagged) != 0)
        {
            kind = 2;
        }
        else if ((flags & CellFlags.ItemRevealed) != 0)
        {
            kind = 3;
        }
        else
        {
            kind = 4; // 迷雾：热力图只叠加在这一类上
        }

        return kind;
    }

    private static Color FarCellColor(BoardTheme theme, uint key)
    {
        uint kind = key & 0b111;

        return kind switch
        {
            0 => theme.ExplodedFill,
            1 => theme.RevealedFill,
            2 => theme.FlagCloth,
            3 => theme.ItemRevealedFill,
            _ => theme.CoveredFill,
        };
    }

    // ---------------------------------------------------------------- 正常路径

    private static void DrawCell(
        CanvasDrawingSession ds,
        ICanvasResourceCreator resourceCreator,
        BoardTheme theme,
        Board board,
        int index,
        int col,
        int row,
        in BoardViewport viewport,
        bool lost,
        int explodedIndex)
    {
        Rect rect = viewport.CellRect(col, row);
        CellFlags flags = board.GetFlags(index);

        bool revealed = (flags & CellFlags.IsRevealed) != 0;
        bool flagged = (flags & CellFlags.IsFlagged) != 0;
        bool question = (flags & CellFlags.IsQuestion) != 0;
        bool itemRevealed = (flags & CellFlags.ItemRevealed) != 0;
        bool isMine = (flags & CellFlags.IsMine) != 0;
        bool isExploded = index == explodedIndex;

        // ---- 底色 ----
        if (isExploded)
        {
            ds.FillRectangle(rect, theme.ExplodedFill);
        }
        else if (revealed)
        {
            ds.FillRectangle(rect, theme.RevealedFill);
        }
        else if (itemRevealed)
        {
            // 透视过的格子：和"已翻开"明显区分（透视 ≠ 翻开，§5.4）
            ds.FillRectangle(rect, theme.ItemRevealedFill);
        }
        else
        {
            DrawCoveredCellCached(ds, resourceCreator, theme, rect, viewport.CellSize);
        }

        // ---- 网格线 ----
        if (viewport.CellSize >= 8f)
        {
            ds.DrawRectangle(rect, theme.GridLine, 1f);
        }

        // ---- 内容 ----
        bool showContent = revealed || itemRevealed || lost;

        if (showContent && isMine && (lost || itemRevealed))
        {
            DrawMine(ds, theme, rect, viewport.CellSize, outlineForItemReveal: itemRevealed && !lost);
        }
        else if (revealed || itemRevealed)
        {
            byte adjacent = board.GetAdjacentMines(index);
            if (adjacent > 0 && !isMine)
            {
                DrawNumber(ds, theme, rect, adjacent, viewport.CellSize, dimmed: itemRevealed && !revealed);
            }
        }

        if (flagged)
        {
            bool wrong = lost && !isMine;
            DrawFlag(ds, theme, rect, viewport.CellSize, wrong);
        }
        else if (question)
        {
            DrawQuestionMark(ds, theme, rect, viewport.CellSize);
        }
    }

    /// <summary>
    /// 未翻开格：优先用预渲染好的位图切片（1 次 DrawImage），
    /// 切片建不出来（设备丢失等）时退回矢量绘制。
    /// </summary>
    private static void DrawCoveredCellCached(
        CanvasDrawingSession ds,
        ICanvasResourceCreator resourceCreator,
        BoardTheme theme,
        Rect rect,
        float cellSize)
    {
        CanvasRenderTarget? tile = GetCoveredTile(resourceCreator, theme, cellSize);

        if (tile is null)
        {
            DrawCoveredCell(ds, theme, rect);
            return;
        }

        ds.DrawImage(tile, rect, tile.Bounds, 1f, CanvasImageInterpolation.NearestNeighbor);
    }

    /// <summary>
    /// 取（必要时重建）未翻开格的位图切片。
    /// 失效条件有三个：设备换了、格宽四舍五入后变了、主题换了。
    /// </summary>
    private static CanvasRenderTarget? GetCoveredTile(ICanvasResourceCreator resourceCreator, BoardTheme theme, float cellSize)
    {
        int size = Math.Max(2, (int)Math.Round(cellSize));

        if (_coveredTile is not null &&
            _coveredTileSize == size &&
            ReferenceEquals(_coveredTileCreator, resourceCreator) &&
            ReferenceEquals(_coveredTileTheme, theme))
        {
            return _coveredTile;
        }

        try
        {
            _coveredTile?.Dispose();

            var target = new CanvasRenderTarget(resourceCreator, size, size, 96);
            using (CanvasDrawingSession tileSession = target.CreateDrawingSession())
            {
                // 全透明清底（这个投影里没有 Windows.UI.Colors，直接构造）
                tileSession.Clear(Color.FromArgb(0, 0, 0, 0));
                DrawCoveredCell(tileSession, theme, new Rect(0, 0, size, size));
            }

            _coveredTile = target;
            _coveredTileSize = size;
            _coveredTileCreator = resourceCreator;
            _coveredTileTheme = theme;
            return target;
        }
        catch (Exception)
        {
            // 拿不到位图不值得让绘制失败，退回矢量路径
            _coveredTile = null;
            _coveredTileSize = -1;
            _coveredTileCreator = null;
            _coveredTileTheme = null;
            return null;
        }
    }

    /// <summary>未翻开格：底色 + 左上亮边 + 右下暗边，做出原版的凸起感（§4.1）。</summary>
    private static void DrawCoveredCell(CanvasDrawingSession ds, BoardTheme theme, Rect rect)
    {
        ds.FillRectangle(rect, theme.CoveredFill);

        float thickness = (float)Math.Max(1.0, rect.Width * 0.09);
        float right = (float)rect.Right - 1f;
        float bottom = (float)rect.Bottom - 1f;

        ds.DrawLine((float)rect.Left + 1f, (float)rect.Top + 1f, right, (float)rect.Top + 1f, theme.CoveredHighlight, thickness);
        ds.DrawLine((float)rect.Left + 1f, (float)rect.Top + 1f, (float)rect.Left + 1f, bottom, theme.CoveredHighlight, thickness);
        ds.DrawLine((float)rect.Left + 1f, bottom, right, bottom, theme.CoveredShadow, thickness);
        ds.DrawLine(right, (float)rect.Top + 1f, right, bottom, theme.CoveredShadow, thickness);
    }

    private static void DrawNumber(CanvasDrawingSession ds, BoardTheme theme, Rect rect, int number, float cellSize, bool dimmed)
    {
        Color color = theme.NumberColor(number);
        if (dimmed)
        {
            color = Color.FromArgb(0xB0, color.R, color.G, color.B);
        }

        CanvasTextFormat format = GetTextFormat(cellSize * 0.66f);
        ds.DrawText(number.ToString(), rect, color, format);
    }

    /// <summary>雷：圆点 + 四根刺；透视出来的雷额外加一圈轮廓（§5.4）。</summary>
    private static void DrawMine(CanvasDrawingSession ds, BoardTheme theme, Rect rect, float cellSize, bool outlineForItemReveal)
    {
        float cx = (float)(rect.Left + (rect.Width / 2));
        float cy = (float)(rect.Top + (rect.Height / 2));
        float radius = cellSize * 0.22f;
        float spike = cellSize * 0.36f;
        float thickness = Math.Max(1f, cellSize * 0.09f);

        Color body = outlineForItemReveal
            ? Color.FromArgb(0xA0, theme.MineBody.R, theme.MineBody.G, theme.MineBody.B)
            : theme.MineBody;

        ds.DrawLine(cx - spike, cy, cx + spike, cy, body, thickness);
        ds.DrawLine(cx, cy - spike, cx, cy + spike, body, thickness);
        float diagonal = spike * 0.72f;
        ds.DrawLine(cx - diagonal, cy - diagonal, cx + diagonal, cy + diagonal, body, thickness);
        ds.DrawLine(cx - diagonal, cy + diagonal, cx + diagonal, cy - diagonal, body, thickness);
        ds.FillCircle(cx, cy, radius, body);

        if (outlineForItemReveal)
        {
            ds.DrawCircle(cx, cy, radius + (cellSize * 0.12f), theme.ItemRevealedMineOutline, Math.Max(1.5f, cellSize * 0.07f));
        }
    }

    /// <summary>旗子：旗杆 + 旗面；插错位置时额外打叉（§2.5 要求视觉上能区分）。</summary>
    private static void DrawFlag(CanvasDrawingSession ds, BoardTheme theme, Rect rect, float cellSize, bool wrong)
    {
        float poleX = (float)(rect.Left + (rect.Width * 0.46));
        float topY = (float)(rect.Top + (rect.Height * 0.22));
        float bottomY = (float)(rect.Top + (rect.Height * 0.78));
        float thickness = Math.Max(1f, cellSize * 0.09f);

        ds.DrawLine(poleX, topY, poleX, bottomY, theme.FlagPole, thickness);

        var cloth = new Rect(
            poleX,
            topY,
            rect.Width * 0.32,
            rect.Height * 0.28);

        ds.FillRectangle(cloth, theme.FlagCloth);

        if (wrong)
        {
            ds.DrawLine(
                (float)(rect.Left + (rect.Width * 0.2)),
                (float)(rect.Top + (rect.Height * 0.2)),
                (float)(rect.Left + (rect.Width * 0.8)),
                (float)(rect.Top + (rect.Height * 0.8)),
                theme.WrongFlagCross,
                Math.Max(1.5f, cellSize * 0.1f));

            ds.DrawLine(
                (float)(rect.Left + (rect.Width * 0.8)),
                (float)(rect.Top + (rect.Height * 0.2)),
                (float)(rect.Left + (rect.Width * 0.2)),
                (float)(rect.Top + (rect.Height * 0.8)),
                theme.WrongFlagCross,
                Math.Max(1.5f, cellSize * 0.1f));
        }
    }

    private static void DrawQuestionMark(CanvasDrawingSession ds, BoardTheme theme, Rect rect, float cellSize)
    {
        ds.DrawText("?", rect, theme.QuestionMark, GetTextFormat(cellSize * 0.66f));
    }

    /// <summary>
    /// 把一个"区域形状"圈成高亮框（§5.3 的瞄准态、§5.4 的结算高亮共用）。
    ///
    /// 参数是形状而不是半径：v1.4 起放大镜是 **2×2 且点中的格子是左上角**，
    /// 按半径居中画会把框画偏一格。形状里自带锚点，画出来和实际翻开的格子一定对齐。
    /// </summary>
    private static void DrawAreaOverlay(
        CanvasDrawingSession ds,
        Board board,
        int centerIndex,
        in BoardViewport viewport,
        MineSweeper.Core.Items.AreaShape shape,
        Color ring,
        Color fill,
        float opacity = 1f)
    {
        int cx = board.XOf(centerIndex);
        int cy = board.YOf(centerIndex);

        int x0 = Math.Max(0, cx - shape.AnchorX);
        int x1 = Math.Min(board.Width - 1, x0 + shape.Width - 1);
        int y0 = Math.Max(0, cy - shape.AnchorY);
        int y1 = Math.Min(board.Height - 1, y0 + shape.Height - 1);

        // 贴边时要保证框至少覆盖被点中的那一格（锚点可能被裁掉）
        x0 = Math.Min(x0, cx);
        y0 = Math.Min(y0, cy);
        x1 = Math.Max(x1, cx);
        y1 = Math.Max(y1, cy);

        var rect = new Rect(
            viewport.OffsetX + (x0 * viewport.CellSize),
            viewport.OffsetY + (y0 * viewport.CellSize),
            (x1 - x0 + 1) * viewport.CellSize,
            (y1 - y0 + 1) * viewport.CellSize);

        ds.FillRectangle(rect, ApplyOpacity(fill, opacity));
        ds.DrawRectangle(rect, ApplyOpacity(ring, opacity), Math.Max(2f, viewport.CellSize * 0.1f));
    }

    private static Color ApplyOpacity(Color color, float opacity)
    {
        float clamped = Math.Clamp(opacity, 0f, 1f);
        return Color.FromArgb((byte)(color.A * clamped), color.R, color.G, color.B);
    }

    private static Color Lerp(Color from, Color to, float t)
    {
        float k = Math.Clamp(t, 0f, 1f);
        return Color.FromArgb(
            (byte)(from.A + ((to.A - from.A) * k)),
            (byte)(from.R + ((to.R - from.R) * k)),
            (byte)(from.G + ((to.G - from.G) * k)),
            (byte)(from.B + ((to.B - from.B) * k)));
    }


    private static CanvasTextFormat GetTextFormat(float fontSize)
    {
        int key = (int)Math.Round(fontSize * 2);
        key = Math.Clamp(key, 8, 400);

        if (!TextFormats.TryGetValue(key, out CanvasTextFormat? format))
        {
            format = new CanvasTextFormat
            {
                FontSize = key / 2f,
                FontFamily = "Consolas",
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                HorizontalAlignment = CanvasHorizontalAlignment.Center,
                VerticalAlignment = CanvasVerticalAlignment.Center,
                WordWrapping = CanvasWordWrapping.NoWrap,
            };

            TextFormats[key] = format;
        }

        return format;
    }
}
