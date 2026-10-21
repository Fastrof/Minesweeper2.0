using System.Diagnostics;
using Microsoft.Graphics.Canvas;
using MineSweeper.App.Services;
using MineSweeper.Core;
using Windows.UI;

namespace MineSweeper.App.Rendering;

/// <summary>一次渲染基准的结果。</summary>
internal readonly record struct RenderPerfSample(string Label, int VisibleCells, double MsPerFrame)
{
    /// <summary>折算帧率（仅作参考，不是真实循环里的帧率）。</summary>
    public double Fps => MsPerFrame <= 0 ? 0 : 1000.0 / MsPerFrame;

    public override string ToString()
        => $"{Label}: {VisibleCells:N0} 格可见, {MsPerFrame:F2} ms/帧 (~{Fps:F0} FPS)";
}

/// <summary>整轮自检的结果。</summary>
internal sealed record RenderPerfReport(
    IReadOnlyList<string> PlacementLines,
    IReadOnlyList<RenderPerfSample> FrameSamples)
{
    public string ToLogText()
    {
        var lines = new List<string> { "=== 渲染/布雷 性能自检 ===" };

        lines.AddRange(PlacementLines);
        foreach (RenderPerfSample sample in FrameSamples)
        {
            lines.Add(sample.ToString());
        }

        return string.Join(Environment.NewLine, lines);
    }
}

/// <summary>
/// 性能自检（文档 M2 的验收标准："1000×1000 棋盘缩放到全景不卡顿"）。
///
/// 为什么需要它：这条验收标准原本从没被验证过，而远景降级路径是"每格一次 FillRectangle"，
/// 1000×1000 在 0.02× 下视口覆盖整盘就是每帧 100 万次绘制调用——到底是 5ms 还是 500ms，
/// 只能实测，不能靠感觉。
///
/// 它跑在<b>真实设备</b>上：离屏 <see cref="CanvasRenderTarget"/> 由调用方传入的
/// <see cref="ICanvasResourceCreator"/>（就是棋盘控件的 Win2D 设备）创建，
/// 并且调用的是<b>真正的</b> <see cref="BoardRenderer.Draw"/>，不是副本。
/// </summary>
internal static class RenderPerfProbe
{
    /// <summary>自检用的固定种子，保证每次跑的都是同一张盘。</summary>
    private const int ProbeSeed = 20261009;

    /// <summary>
    /// 跑完整自检：先测布雷（含邻雷数计算）耗时，再测几个缩放档位的绘制耗时。
    /// </summary>
    /// <param name="resourceCreator">真实 Win2D 设备（棋盘控件的设备）。</param>
    /// <param name="controlWidth">离屏画布宽度（DIP）。</param>
    /// <param name="controlHeight">离屏画布高度（DIP）。</param>
    public static RenderPerfReport Run(ICanvasResourceCreator resourceCreator, float controlWidth, float controlHeight, string? snapshotDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(resourceCreator);

        var placement = new List<string>
        {
            MeasurePlacement(1000, 1000, 100_000),
            MeasurePlacement(4096, 4096, 1_600_000),
        };

        // 棋盘一次性建好（锁定盘面：雷位与首点都由 seed 决定，适合做基准）
        GameSession board = CreateProbeSession(1000, 1000, 100_000);

        var samples = new List<RenderPerfSample>();
        foreach (float zoom in new[] { 0.02f, 0.1f, 0.5f, 1f })
        {
            samples.Add(MeasureFrames(resourceCreator, board, zoom, controlWidth, controlHeight, frames: 30, snapshotDirectory: snapshotDirectory));
        }

        samples.Add(MeasureFrames(resourceCreator, board, 0.02f, controlWidth, controlHeight, frames: 30, snapshotDirectory: snapshotDirectory, heatmap: true, labelSuffix: " + 雷密度热力图"));

        return new RenderPerfReport(placement, samples);
    }

    /// <summary>轻微版：只测 1000×1000 的几个缩放档位（设置面板按钮用）。</summary>
    public static RenderPerfReport RunQuick(ICanvasResourceCreator resourceCreator, float controlWidth, float controlHeight)
    {
        ArgumentNullException.ThrowIfNull(resourceCreator);

        GameSession board = CreateProbeSession(1000, 1000, 100_000);
        var samples = new List<RenderPerfSample>();
        foreach (float zoom in new[] { 0.02f, 0.1f, 1f })
        {
            samples.Add(MeasureFrames(resourceCreator, board, zoom, controlWidth, controlHeight, frames: 20, snapshotDirectory: null));
        }

        return new RenderPerfReport(Array.Empty<string>(), samples);
    }

    private static GameSession CreateProbeSession(int width, int height, int mines)
    {
        var settings = new GameSettings { Mode = GameMode.Custom, FirstClickOpening = true };

        if (!GameSession.TryCreateLocked(width, height, mines, ProbeSeed, settings, out GameSession? session, out string? error))
        {
            throw new InvalidOperationException($"性能自检无法建立 {width}×{height} 棋盘：{error}");
        }

        return session!;
    }

    private static void SaveSnapshot(CanvasRenderTarget target, string? directory, float zoom, bool heatmap)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        try
        {
            // 顺手导出一张截图：远景"同色行程合并"和热力图有没有画错，看一眼比什么都可靠
            System.IO.Directory.CreateDirectory(directory);
            string name = $"perf_zoom{zoom:0.###}".Replace('.', '_') + (heatmap ? "_heat" : string.Empty) + ".png";
            string file = System.IO.Path.Combine(directory, name);
            target.SaveAsync(file, CanvasBitmapFileFormat.Png).AsTask().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            AppPaths.Log($"性能自检截图导出失败：{ex.GetType().Name}：{ex.Message}");
        }
    }

    /// <summary>测"建盘 + 布雷 + 全盘邻雷数"的耗时（就是玩家第一次点击时的同步开销）。</summary>
    private static string MeasurePlacement(int width, int height, int mines)
    {
        var settings = new GameSettings { Mode = GameMode.Custom, FirstClickOpening = true };

        var sw = Stopwatch.StartNew();
        bool ok = GameSession.TryCreateLocked(width, height, mines, ProbeSeed, settings, out _, out string? error);
        sw.Stop();

        string status = ok ? "OK" : $"失败（{error}）";
        return $"布雷 {width}×{height} / {mines} 雷：{sw.Elapsed.TotalMilliseconds:F1} ms [{status}]";
    }

    /// <summary>在指定缩放下连画若干帧，返回平均每帧耗时。</summary>
    private static RenderPerfSample MeasureFrames(
        ICanvasResourceCreator resourceCreator,
        GameSession session,
        float zoom,
        float controlWidth,
        float controlHeight,
        int frames,
        string? snapshotDirectory,
        bool heatmap = false,
        string labelSuffix = "")
    {
        Board board = session.Board;
        float cellSize = BoardTheme.DefaultCellSize * zoom;

        int firstCol = Math.Max(0, (int)Math.Floor((0 - 0f) / cellSize));
        int lastCol = Math.Min(board.Width - 1, (int)Math.Floor((controlWidth - 0f) / cellSize));
        int firstRow = Math.Max(0, (int)Math.Floor((0 - 0f) / cellSize));
        int lastRow = Math.Min(board.Height - 1, (int)Math.Floor((controlHeight - 0f) / cellSize));

        var viewport = new BoardViewport(cellSize, 0f, 0f, controlWidth, controlHeight, firstCol, lastCol, firstRow, lastRow);
        int visibleCells = Math.Max(0, (lastCol - firstCol + 1)) * Math.Max(0, (lastRow - firstRow + 1));

        BoardTheme theme = BoardTheme.Classic;

        using var target = new CanvasRenderTarget(resourceCreator, controlWidth, controlHeight, 96);

        // 预热一帧：别把首帧的设备初始化算进去
        using (CanvasDrawingSession warmup = target.CreateDrawingSession())
        {
            warmup.Clear(Color.FromArgb(0xFF, 0x20, 0x20, 0x22));
            BoardRenderer.Draw(warmup, resourceCreator, theme, session, viewport, -1, false, -1, 0f, -1, 0f, heatmap);
        }

        var sw = Stopwatch.StartNew();
        for (int i = 0; i < frames; i++)
        {
            using CanvasDrawingSession ds = target.CreateDrawingSession();
            ds.Clear(Color.FromArgb(0xFF, 0x20, 0x20, 0x22));
            BoardRenderer.Draw(ds, resourceCreator, theme, session, viewport, -1, false, -1, 0f, -1, 0f, heatmap);
        }

        sw.Stop();

        SaveSnapshot(target, snapshotDirectory, zoom, heatmap);

        string label = $"绘制 zoom={zoom:0.###}×（格宽 {cellSize:0.##} DIP，{(viewport.IsFarZoom ? "远景降级" : "正常绘制")}{labelSuffix}）";
        return new RenderPerfSample(label, visibleCells, sw.Elapsed.TotalMilliseconds / frames);
    }
}
