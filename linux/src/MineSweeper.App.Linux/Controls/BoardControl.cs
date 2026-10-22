using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Automation.Peers;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.Media.TextFormatting;
using MineSweeper.App.Linux.Rendering;
using MineSweeper.App.Linux.Services;
using MineSweeper.Core;

namespace MineSweeper.App.Linux.Controls;

/// <summary>
/// 自绘棋盘控件（Linux / Avalonia 版）。
///
/// 对应 Windows 端的 <c>Views/BoardView.cs</c>（1,805 行）+ <c>Rendering/BoardRenderer.cs</c>（676 行）。
/// 本类只做三件事：
///   1. **渲染**：正常缩放逐格画；远景走"同色行程 + 连续行色带合并"（算法逐行移植自 Windows 端）；
///   2. **输入**：左键翻开（按下即生效）/ 右键三态 / 双击·中键·左右同按 chord / 滚轮缩放 / 拖动平移；
///   3. **转发**：所有规则动作交给 <see cref="GameCoordinator"/>（它再调 Core），本类零规则代码。
/// </summary>
public class BoardControl : Control
{
    private readonly Dictionary<uint, IBrush> _brushCache = new();
    private List<FarRun> _runsPrev = new(64);
    private List<FarRun> _runsCurr = new(64);

    private GameCoordinator? _game;
    private LinuxBoardTheme _theme = LinuxBoardTheme.Classic;

    private double _cellSize = BoardMetrics.DefaultCellSize;
    private double _offsetX;
    private double _offsetY;

    // 声呐扫过的区域：只做标记、不揭示内容（玩家随时能回看"我刚才扫的是哪块"）
    private readonly HashSet<int> _sonarMarks = new();
    private (int FirstCol, int FirstRow, int LastCol, int LastRow)? _sonarBounds;

    // 手势状态（与 Windows 端一致：每个瞬时状态都必须能被"自愈"重置）
    private bool _panning;
    private bool _pressed;
    private Point _pressPoint;
    private int _middleDownCell = -1;
    private long _lastLeftDownMs;
    private int _lastLeftDownCell = -1;

    private bool _fitted;

    /// <summary>玩家是否手动缩放/平移过（是的话，窗口变化时不再自动 fit）。</summary>
    private bool _userAdjusted;

    /// <summary>玩家是否手动缩放/平移过。</summary>
    public bool IsUserAdjusted => _userAdjusted;
    private int _hoverIndex = -1;
    private int _aimingIndex = -1;
    private int _hintIndex = -1;
    private long _hintSetMs;

    /// <summary>
    /// 提示高亮的自动消失定时器。
    /// **不能**再靠 Render 里判断"过了 2 秒吗"——那样只有恰好有别的重绘时才会清掉，
    /// 玩家不点任何东西的话绿框会一直挂着，换一局也还留在新盘上（这就是"重开后提示还在"的根因）。
    /// </summary>
    private DispatcherTimer? _hintTimer;

    private double _lastRenderMs;
    private int _lastRenderCells;
    private int _lastRenderDrawCalls;

    public BoardControl()
    {
        ClipToBounds = true;
        Focusable = false;
    }

    /// <summary>棋盘数据与动作入口。</summary>
    public GameCoordinator? Game
    {
        get => _game;
        set
        {
            _game = value;
            ClearSonarMarks();
            _fitted = false;
            FitToWindow();
            InvalidateVisual();
        }
    }

    /// <summary>当前主题（由 ThemeService 注入）。</summary>
    public LinuxBoardTheme BoardTheme
    {
        get => _theme;
        set
        {
            _theme = value;
            InvalidateVisual();
        }
    }

    /// <summary>当前缩放（1.0 = 默认格宽 26 DIP）。</summary>
    public double Zoom => _cellSize / BoardMetrics.DefaultCellSize;

    public double LastRenderMs => _lastRenderMs;
    public int LastRenderCells => _lastRenderCells;
    public int LastRenderDrawCalls => _lastRenderDrawCalls;

    /// <summary>无障碍：棋盘当前局面的文字摘要（屏幕阅读器读这一句）。</summary>
    public string BuildAccessibleName()
    {
        GameSession? session = _game?.Session;
        if (session is null)
        {
            return "扫雷棋盘（未开局）";
        }

        string status = session.Status switch
        {
            GameStatus.NotStarted => "准备中",
            GameStatus.InProgress => "进行中",
            GameStatus.Won => "已胜利",
            GameStatus.Lost => "踩雷失败",
            _ => "未知",
        };

        return $"扫雷棋盘 {session.Board.Width}×{session.Board.Height}，{session.Board.MineCount} 颗雷，" +
               $"已翻开 {session.RevealedCount} 格，剩余雷数 {session.RemainingMines}，{status}";
    }

    /// <summary>无障碍：把棋盘暴露成 Pane（只读，不可 Invoke）。</summary>
    protected override AutomationPeer OnCreateAutomationPeer() => new BoardAutomationPeer(this);

    /// <summary>提示：金色高亮一个安全格 2 秒（不翻开）。</summary>
    public void ShowHint(int index)
    {
        _hintIndex = index;
        _hintSetMs = Environment.TickCount64;
        InvalidateVisual();

        _hintTimer ??= CreateHintTimer();
        _hintTimer.Stop();
        _hintTimer.Start();
    }

    /// <summary>清掉提示高亮（换局/重新开局时必须调用）。</summary>
    public void ClearHint()
    {
        _hintTimer?.Stop();
        if (_hintIndex < 0)
        {
            return;
        }

        _hintIndex = -1;
        InvalidateVisual();
    }

    private DispatcherTimer CreateHintTimer()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        timer.Tick += (_, _) => ClearHint();
        return timer;
    }

    // ---------------------------------------------------------------- 道具瞄准态

    /// <summary>当前瞄准的道具（None = 普通对局）。</summary>
    public AimingMode Aiming { get; private set; } = AimingMode.None;

    /// <summary>给宿主的状态反馈（例如"声呐报告 3×3 有 2 颗雷"）。</summary>
    public event EventHandler<string>? StatusMessage;

    /// <summary>瞄准态变化（宿主据此显示/隐藏右侧信息面板）。</summary>
    public event EventHandler? AimingChanged;

    /// <summary>声呐扫描完成（宿主据此弹出结果卡片）。</summary>
    public event EventHandler<Core.SonarScanResult>? SonarScanned;

    /// <summary>瞄准时鼠标所在格（-1 = 不在棋盘上）。</summary>
    public int AimingPreviewIndex => _aimingIndex;

    /// <summary>进入瞄准态（放大镜 / 声呐）。右键或离开棋盘可取消。</summary>
    public void BeginAiming(AimingMode mode)
    {
        GameSession? session = _game?.Session;
        if (session is null || mode == AimingMode.None)
        {
            return;
        }

        Aiming = mode;
        _aimingIndex = -1;
        InvalidateVisual();
        AimingChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>取消瞄准（右键 / 换局 / 用完）。</summary>
    public void CancelAiming()
    {
        if (Aiming == AimingMode.None)
        {
            return;
        }

        Aiming = AimingMode.None;
        _aimingIndex = -1;
        InvalidateVisual();
        AimingChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>放大镜的透视区域形状（2×2，点中格是左上角）。</summary>
    private static (int width, int height, int anchorX, int anchorY) ShapeOf(AimingMode mode, GameSession session)
        => mode == AimingMode.Magnifier
            ? (session.MagnifierWidth, session.MagnifierHeight, 0, 0)
            : (3, 3, 1, 1);

    // ---------------------------------------------------------------- 视图

    /// <summary>
    /// 标记一次声呐扫描的区域（浅蓝覆盖 + 区域外框），保留到下一局开始。
    /// 声呐**不揭示雷的位置**，所以这里只标区域、不画任何内容。
    /// </summary>
    public void MarkSonar(int centerIndex, int radius)
    {
        _sonarMarks.Clear();
        _sonarBounds = null;

        GameSession? session = _game?.Session;
        if (session is null || centerIndex < 0 || radius <= 0)
        {
            InvalidateVisual();
            return;
        }

        Board board = session.Board;
        int centerRow = centerIndex / board.Width;
        int centerCol = centerIndex % board.Width;
        int firstRow = Math.Max(0, centerRow - radius);
        int lastRow = Math.Min(board.Height - 1, centerRow + radius);
        int firstCol = Math.Max(0, centerCol - radius);
        int lastCol = Math.Min(board.Width - 1, centerCol + radius);

        for (int row = firstRow; row <= lastRow; row++)
        {
            for (int col = firstCol; col <= lastCol; col++)
            {
                _sonarMarks.Add((row * board.Width) + col);
            }
        }

        _sonarBounds = (firstCol, firstRow, lastCol, lastRow);
        InvalidateVisual();
    }

    /// <summary>清掉声呐标记（换局时调用）。</summary>
    public void ClearSonarMarks()
    {
        if (_sonarMarks.Count == 0 && _sonarBounds is null)
        {
            return;
        }

        _sonarMarks.Clear();
        _sonarBounds = null;
        InvalidateVisual();
    }

    /// <summary>适应窗口：把整盘缩放到控件内并居中（对应 Windows 端 FitToWindow）。</summary>
    public void FitToWindow()
    {
        _userAdjusted = false;
        GameSession? session = _game?.Session;
        if (session is null)
        {
            return;
        }

        double width = Bounds.Width;
        double height = Bounds.Height;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        Board board = session.Board;
        double fit = Math.Min((width - 8) / board.Width, (height - 8) / board.Height);
        _cellSize = Math.Clamp(fit, BoardMetrics.MinCellSize, BoardMetrics.MaxFitCellSize);
        CenterBoard();
        InvalidateVisual();
    }

    private void CenterBoard()
    {
        GameSession? session = _game?.Session;
        if (session is null)
        {
            return;
        }

        _offsetX = (Bounds.Width - (session.Board.Width * _cellSize)) / 2;
        _offsetY = (Bounds.Height - (session.Board.Height * _cellSize)) / 2;
    }

    private void ClampOffset()
    {
        GameSession? session = _game?.Session;
        if (session is null)
        {
            return;
        }

        double boardWidth = session.Board.Width * _cellSize;
        double boardHeight = session.Board.Height * _cellSize;
        double marginX = Math.Min(boardWidth, Bounds.Width) * 0.5;
        double marginY = Math.Min(boardHeight, Bounds.Height) * 0.5;

        _offsetX = Math.Clamp(_offsetX, -boardWidth + marginX, Bounds.Width - marginX);
        _offsetY = Math.Clamp(_offsetY, -boardHeight + marginY, Bounds.Height - marginY);
    }

    /// <summary>以某个屏幕点为锚缩放（滚轮/捏合共用）。</summary>
    private void ZoomAt(Point anchor, double factor)
    {
        _userAdjusted = true;
        GameSession? session = _game?.Session;
        if (session is null)
        {
            return;
        }

        double oldCell = _cellSize;
        double newCell = Math.Clamp(oldCell * factor, BoardMetrics.MinCellSize, BoardMetrics.MaxCellSize);
        if (Math.Abs(newCell - oldCell) < 0.0001)
        {
            return;
        }

        // 保证"光标下的棋盘点不动"
        double boardX = (anchor.X - _offsetX) / oldCell;
        double boardY = (anchor.Y - _offsetY) / oldCell;
        _cellSize = newCell;
        _offsetX = anchor.X - (boardX * newCell);
        _offsetY = anchor.Y - (boardY * newCell);
        ClampOffset();
        InvalidateVisual();
    }

    private void PanBy(double dx, double dy)
    {
        _userAdjusted = true;
        _offsetX += dx;
        _offsetY += dy;
        ClampOffset();
        InvalidateVisual();
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);

        // 首次拿到真实尺寸时自动适应窗口（之后玩家缩放/平移不再被重置）
        if (!_fitted && Bounds.Width > 0 && Bounds.Height > 0)
        {
            _fitted = true;
            FitToWindow();
        }

        ClampOffset();
        InvalidateVisual();
    }

    // ---------------------------------------------------------------- 渲染

    public override void Render(DrawingContext context)
    {
        long start = Stopwatch.GetTimestamp();
        context.FillRectangle(GetBrush(_theme.Background), new Rect(Bounds.Size));
        _lastRenderDrawCalls = 1;

        GameSession? session = _game?.Session;
        if (session is null || Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            _lastRenderMs = 0;
            return;
        }

        if (_cellSize <= 0)
        {
            FitToWindow();
        }

        BoardViewport viewport = BuildViewport(session.Board);

        if (viewport.IsFarZoom)
        {
            DrawFar(context, session, viewport);
        }
        else
        {
            DrawNormal(context, session, viewport);
        }

        DrawAiming(context, session, viewport);
        DrawHintHighlight(context, session, viewport);
        _lastRenderMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    }

    /// <summary>瞄准态的区域框（放大镜 2×2 左上角锚定 / 声呐 3×3 居中）。</summary>
    private void DrawAiming(DrawingContext context, GameSession session, in BoardViewport viewport)
    {
        if (Aiming == AimingMode.None || _aimingIndex < 0)
        {
            return;
        }

        (int width, int height, int anchorX, int anchorY) = ShapeOf(Aiming, session);
        int col = (_aimingIndex % session.Board.Width) - anchorX;
        int row = (_aimingIndex / session.Board.Width) - anchorY;

        // 贴边裁切（与 Core 的 AreaShape.IndexesAt 一致）
        int x0 = Math.Max(0, col);
        int y0 = Math.Max(0, row);
        int x1 = Math.Min(session.Board.Width - 1, col + width - 1);
        int y1 = Math.Min(session.Board.Height - 1, row + height - 1);
        if (x1 < x0 || y1 < y0)
        {
            return;
        }

        var rect = new Rect(
            viewport.OffsetX + (x0 * viewport.CellSize),
            viewport.OffsetY + (y0 * viewport.CellSize),
            (x1 - x0 + 1) * viewport.CellSize,
            (y1 - y0 + 1) * viewport.CellSize);

        var fill = new SolidColorBrush(_theme.AimingRing, 0.12);
        var pen = new Pen(GetBrush(_theme.AimingRing), 2);
        context.FillRectangle(fill, rect);
        context.DrawRectangle(null, pen, rect);
    }

    private BoardViewport BuildViewport(Board board)
    {
        int firstCol = Math.Clamp((int)Math.Floor((0 - _offsetX) / _cellSize), 0, board.Width - 1);
        int lastCol = Math.Clamp((int)Math.Floor((Bounds.Width - _offsetX) / _cellSize), 0, board.Width - 1);
        int firstRow = Math.Clamp((int)Math.Floor((0 - _offsetY) / _cellSize), 0, board.Height - 1);
        int lastRow = Math.Clamp((int)Math.Floor((Bounds.Height - _offsetY) / _cellSize), 0, board.Height - 1);

        return new BoardViewport(
            _cellSize, _offsetX, _offsetY, Bounds.Width, Bounds.Height,
            firstCol, lastCol, firstRow, lastRow);
    }

    private int HitTestIndex(Point position, GameSession session)
    {
        BoardViewport viewport = BuildViewport(session.Board);
        int col = viewport.ColumnAt(position.X);
        int row = viewport.RowAt(position.Y);
        if (col < 0 || row < 0 || col >= session.Board.Width || row >= session.Board.Height)
        {
            return -1;
        }

        return (row * session.Board.Width) + col;
    }

    // ---------------------------------------------------------------- 输入

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        GameCoordinator? game = _game;
        GameSession? session = game?.Session;
        if (game is null || session is null)
        {
            return;
        }

        PointerPoint point = e.GetCurrentPoint(this);
        Point position = point.Position;
        int index = HitTestIndex(position, session);

        // 瞄准态：左键落子（放大镜/声呐），右键取消
        if (Aiming != AimingMode.None)
        {
            if (point.Properties.IsRightButtonPressed)
            {
                CancelAiming();
                StatusMessage?.Invoke(this, "已取消道具瞄准");
                e.Handled = true;
                return;
            }

            if (point.Properties.IsLeftButtonPressed)
            {
                UseItemAt(index);
                e.Handled = true;
                return;
            }
        }

        // 左右同按 = chord（必须放在右键分支之前，否则永远不可达）
        if (point.Properties.IsLeftButtonPressed && point.Properties.IsRightButtonPressed)
        {
            if (index >= 0)
            {
                game.Chord(index);
                InvalidateVisual();
            }

            e.Handled = true;
            _pressed = false;
            return;
        }

        if (point.Properties.IsLeftButtonPressed)
        {
            if (index < 0)
            {
                return;
            }

            // 双击 = 第二次左键按下落在同一格且该格已翻开 → chord
            long now = Environment.TickCount64;
            if (_lastLeftDownCell == index && now - _lastLeftDownMs <= BoardMetrics.DoubleClickMs
                && session.Board.IsRevealed(index))
            {
                _lastLeftDownCell = -1;
                game.Chord(index);
                e.Handled = true;
                InvalidateVisual();
                return;
            }

            _lastLeftDownMs = now;
            _lastLeftDownCell = index;

            // 与 Windows 端一致：鼠标"按下即生效"，用按下的坐标判定格
            game.Reveal(index);
            e.Handled = true;
            InvalidateVisual();
            return;
        }

        if (point.Properties.IsRightButtonPressed)
        {
            _pressed = true;
            _panning = false;
            _pressPoint = position;
            _middleDownCell = index;
            e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }

        if (point.Properties.IsMiddleButtonPressed)
        {
            _pressed = true;
            _panning = false;
            _pressPoint = position;
            _middleDownCell = index;
            e.Pointer.Capture(this);
            e.Handled = true;
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        GameCoordinator? game = _game;
        GameSession? session = game?.Session;
        if (game is null || session is null)
        {
            return;
        }

        Point position = e.GetCurrentPoint(this).Position;
        _hoverIndex = HitTestIndex(position, session);

        if (Aiming != AimingMode.None)
        {
            // 瞄准态：跟随鼠标显示区域框（不触发任何规则动作）
            if (_aimingIndex != _hoverIndex)
            {
                _aimingIndex = _hoverIndex;
                InvalidateVisual();
                AimingChanged?.Invoke(this, EventArgs.Empty);
            }

            return;
        }

        if (!_pressed)
        {
            return;
        }

        double dx = position.X - _pressPoint.X;
        double dy = position.Y - _pressPoint.Y;

        if (!_panning && (Math.Abs(dx) > BoardMetrics.PanThreshold || Math.Abs(dy) > BoardMetrics.PanThreshold))
        {
            _panning = true;
        }

        if (_panning)
        {
            PanBy(dx, dy);
            _pressPoint = position;
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        GameCoordinator? game = _game;
        GameSession? session = game?.Session;
        if (game is null || session is null)
        {
            ResetInputState();
            return;
        }

        bool wasPressed = _pressed;
        bool wasPanning = _panning;
        int cell = _middleDownCell;
        PointerUpdateKind kind = e.GetCurrentPoint(this).Properties.PointerUpdateKind;
        ResetInputState();

        if (!wasPressed || wasPanning || cell < 0)
        {
            return;
        }

        // 右键单击 = 三态插旗；中键单击 = chord
        if (kind == PointerUpdateKind.RightButtonReleased)
        {
            game.ToggleFlag(cell);
            InvalidateVisual();
        }
        else if (kind == PointerUpdateKind.MiddleButtonReleased)
        {
            game.Chord(cell);
            InvalidateVisual();
        }
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        ResetInputState();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);

        Point position = e.GetCurrentPoint(this).Position;
        bool ctrl = (e.KeyModifiers & KeyModifiers.Control) != 0;
        bool shift = (e.KeyModifiers & KeyModifiers.Shift) != 0;

        if (ctrl)
        {
            ZoomAt(position, e.Delta.Y > 0 ? 1.15 : 1 / 1.15);
        }
        else if (shift)
        {
            PanBy(e.Delta.Y * BoardMetrics.WheelPanStep, 0);
        }
        else
        {
            PanBy(0, e.Delta.Y * BoardMetrics.WheelPanStep);
        }

        e.Handled = true;
    }

    /// <summary>输入状态自愈：任何"怎么点都没反应"都应该是这里没被调用。</summary>
    private void ResetInputState()
    {
        _pressed = false;
        _panning = false;
        _middleDownCell = -1;
        _lastLeftDownCell = -1;
    }

    /// <summary>在目标格使用当前瞄准的道具（结果通过 <see cref="StatusMessage"/> 反馈）。</summary>
    private void UseItemAt(int index)
    {
        GameCoordinator? game = _game;
        if (game is null || index < 0)
        {
            return;
        }

        if (Aiming == AimingMode.Magnifier)
        {
            bool ok = game.UseMagnifier(index, out string message);
            StatusMessage?.Invoke(this, message);
            if (ok)
            {
                CancelAiming();
            }

            InvalidateVisual();
            return;
        }

        if (Aiming == AimingMode.Sonar)
        {
            Core.SonarScanResult result = game.UseSonar(index);
            if (result.Success)
            {
                SonarScanned?.Invoke(this, result);
                CancelAiming();
            }
            else
            {
                StatusMessage?.Invoke(this, LocalizationService.T("sonar.rejected", "这里放不下声呐扫描区域", "Sonar area does not fit here"));
            }

            InvalidateVisual();
        }
    }

    // ---------------------------------------------------------------- 正常缩放路径

    private void DrawNormal(DrawingContext context, GameSession session, in BoardViewport viewport)
    {
        Board board = session.Board;
        bool showGrid = viewport.CellSize >= 8;
        bool showText = viewport.CellSize >= 12;
        bool lost = session.Status == GameStatus.Lost;
        int cells = 0;
        int calls = _lastRenderDrawCalls;

        for (int row = viewport.FirstRow; row <= viewport.LastRow; row++)
        {
            int rowBase = row * board.Width;
            for (int col = viewport.FirstCol; col <= viewport.LastCol; col++)
            {
                int index = rowBase + col;
                Rect rect = viewport.CellRect(col, row);
                CellFlags flags = board.GetFlags(index);
                bool revealed = (flags & CellFlags.IsRevealed) != 0;
                bool itemRevealed = (flags & CellFlags.ItemRevealed) != 0;
                bool exploded = session.ExplodedIndex == index;

                Color fill = exploded
                    ? _theme.ExplodedFill
                    : revealed
                        ? _theme.RevealedFill
                        : (flags & CellFlags.ItemRevealed) != 0
                            ? _theme.ItemRevealedFill
                            : _theme.CoveredFill;

                context.FillRectangle(GetBrush(fill), rect);
                calls++;

                // 声呐标记：只在"仍盖着"的格子上叠半透明蓝（翻开/透视/爆炸格优先显示自己的样子）
                if (_sonarMarks.Count > 0
                    && !revealed
                    && !itemRevealed
                    && !exploded
                    && _sonarMarks.Contains(index))
                {
                    context.FillRectangle(GetBrush(_theme.SonarMarkFill), rect);
                    calls++;
                }

                if (!revealed && !itemRevealed && !exploded && viewport.CellSize >= 6)
                {
                    double t = Math.Max(1, viewport.CellSize * 0.12);
                    context.FillRectangle(GetBrush(_theme.CoveredHighlight), new Rect(rect.X, rect.Y, rect.Width, t));
                    context.FillRectangle(GetBrush(_theme.CoveredHighlight), new Rect(rect.X, rect.Y, t, rect.Height));
                    context.FillRectangle(GetBrush(_theme.CoveredShadow), new Rect(rect.X, rect.Bottom - t, rect.Width, t));
                    context.FillRectangle(GetBrush(_theme.CoveredShadow), new Rect(rect.Right - t, rect.Y, t, rect.Height));
                    calls += 4;
                }

                if (showGrid && (revealed || itemRevealed))
                {
                    context.FillRectangle(GetBrush(_theme.GridLine), new Rect(rect.X, rect.Y, rect.Width, 1));
                    calls++;
                }

                bool showContent = revealed || itemRevealed || exploded || (lost && (flags & CellFlags.IsMine) != 0);
                if (showContent)
                {
                    if ((flags & CellFlags.IsMine) != 0)
                    {
                        DrawMine(context, rect, viewport.CellSize, itemRevealed && !lost);
                        calls += 5;
                    }
                    else if (showText)
                    {
                        int adjacent = board.GetAdjacentMines(index);
                        if (adjacent > 0)
                        {
                            DrawCenteredText(context, adjacent.ToString(CultureInfo.InvariantCulture), rect, _theme.NumberColor(adjacent), rect.Width * 0.62);
                            calls++;
                        }
                    }
                }
                else
                {
                    if ((flags & CellFlags.IsFlagged) != 0 && viewport.CellSize >= 8)
                    {
                        DrawFlag(context, rect);
                        calls += 2;
                    }
                    else if ((flags & CellFlags.IsQuestion) != 0 && showText)
                    {
                        DrawCenteredText(context, "?", rect, _theme.QuestionFill, rect.Width * 0.6);
                        calls++;
                    }
                }

                cells++;
            }
        }

        _lastRenderCells = cells;
        _lastRenderDrawCalls = calls;

        DrawSonarOutline(context, viewport);
    }

    /// <summary>声呐区域的外框：让玩家一眼看出扫过的范围边界。</summary>
    private void DrawSonarOutline(DrawingContext context, in BoardViewport viewport)
    {
        if (_sonarBounds is not { } bounds)
        {
            return;
        }

        Rect first = viewport.CellRect(bounds.FirstCol, bounds.FirstRow);
        Rect last = viewport.CellRect(bounds.LastCol, bounds.LastRow);
        var outline = new Rect(first.X, first.Y, last.Right - first.X, last.Bottom - first.Y);
        context.DrawRectangle(null, new Pen(GetBrush(_theme.SonarMarkBorder), 2), outline);
    }

    /// <summary>
    /// 远景：每行压成"同色行程"，再把结构完全相同的连续行合并成竖直色带。
    /// 算法逐行移植自 Windows 端 <c>BoardRenderer.BuildFarRuns / SameRuns / FlushFarBand</c>。
    /// </summary>
    private void DrawFar(DrawingContext context, GameSession session, in BoardViewport viewport)
    {
        Board board = session.Board;
        int? exploded = session.ExplodedIndex;

        BuildFarRuns(board, viewport, viewport.FirstRow, exploded, _runsPrev);
        int bandStart = viewport.FirstRow;
        int calls = _lastRenderDrawCalls;

        for (int row = viewport.FirstRow + 1; row <= viewport.LastRow; row++)
        {
            BuildFarRuns(board, viewport, row, exploded, _runsCurr);
            if (SameRuns(_runsPrev, _runsCurr))
            {
                continue;
            }

            calls += FlushFarBand(context, viewport, bandStart, row - 1, _runsPrev);
            (_runsPrev, _runsCurr) = (_runsCurr, _runsPrev);
            bandStart = row;
        }

        calls += FlushFarBand(context, viewport, bandStart, viewport.LastRow, _runsPrev);

        _lastRenderCells = (viewport.LastRow - viewport.FirstRow + 1) * (viewport.LastCol - viewport.FirstCol + 1);
        _lastRenderDrawCalls = calls;
    }

    private static void BuildFarRuns(Board board, in BoardViewport viewport, int row, int? explodedIndex, List<FarRun> runs)
    {
        runs.Clear();
        int rowBase = row * board.Width;
        int runStart = viewport.FirstCol;
        uint key = FarCellKey(board, rowBase + runStart, explodedIndex);

        for (int col = viewport.FirstCol + 1; col <= viewport.LastCol; col++)
        {
            uint next = FarCellKey(board, rowBase + col, explodedIndex);
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

    private int FlushFarBand(DrawingContext context, in BoardViewport viewport, int firstRow, int lastRow, List<FarRun> runs)
    {
        if (lastRow < firstRow)
        {
            return 0;
        }

        double top = viewport.OffsetY + (firstRow * viewport.CellSize);
        double bottom = viewport.OffsetY + ((lastRow + 1) * viewport.CellSize);
        int calls = 0;

        foreach (FarRun run in runs)
        {
            var rect = new Rect(
                viewport.OffsetX + (run.StartCol * viewport.CellSize),
                top,
                (run.EndCol - run.StartCol + 1) * viewport.CellSize,
                bottom - top);

            context.FillRectangle(GetBrush(FarCellColor(run.Key)), rect);
            calls++;
        }

        return calls;
    }

    private static uint FarCellKey(Board board, int index, int? explodedIndex)
    {
        if (index == explodedIndex)
        {
            return 0;
        }

        CellFlags flags = board.GetFlags(index);
        if ((flags & CellFlags.IsRevealed) != 0)
        {
            return 1;
        }

        if ((flags & CellFlags.IsFlagged) != 0)
        {
            return 2;
        }

        if ((flags & CellFlags.ItemRevealed) != 0)
        {
            return 3;
        }

        return 4;
    }

    private Color FarCellColor(uint key) => (key & 0b111) switch
    {
        0 => _theme.ExplodedFill,
        1 => _theme.RevealedFill,
        2 => _theme.FlagCloth,
        3 => _theme.ItemRevealedFill,
        _ => _theme.CoveredFill,
    };

    // ---------------------------------------------------------------- 提示高亮

    private void DrawHintHighlight(DrawingContext context, GameSession session, in BoardViewport viewport)
    {
        if (_hintIndex < 0)
        {
            return;
        }


        Board board = session.Board;
        int col = _hintIndex % board.Width;
        int row = _hintIndex / board.Width;
        if (col < viewport.FirstCol || col > viewport.LastCol || row < viewport.FirstRow || row > viewport.LastRow)
        {
            return;
        }

        Rect rect = viewport.CellRect(col, row);
        var ring = new Pen(GetBrush(_theme.HighlightRing), Math.Max(2, rect.Width * 0.08));
        context.DrawRectangle(null, ring, rect.Deflate(rect.Width * 0.1));
    }

    // ---------------------------------------------------------------- 绘制小工具

    private void DrawCenteredText(DrawingContext context, string text, Rect rect, Color color, double fontSize)
    {
        var layout = new TextLayout(text, new Typeface("DejaVu Sans"), fontSize, GetBrush(color));
        layout.Draw(context, new Point(
            rect.X + ((rect.Width - layout.Width) / 2),
            rect.Y + ((rect.Height - layout.Height) / 2)));
    }

    /// <summary>
    /// 雷的样式：**四根刺 + 中心实心圆**（逐行移植 Windows 端 BoardRenderer.DrawMine）。
    /// Windows 画的是 Win2D 的 FillCircle / DrawLine，这里换成 Skia 的等价调用，尺寸比例完全一致：
    ///   radius = cellSize × 0.22，刺长 = cellSize × 0.36（对角 ×0.72），线宽 = cellSize × 0.09。
    /// 透视出来的雷做成半透明 + 一圈蓝色轮廓（对应 Windows 的 outlineForItemReveal）。
    /// </summary>
    private void DrawMine(DrawingContext context, Rect rect, double cellSize, bool outlineForItemReveal)
    {
        double cx = rect.Center.X;
        double cy = rect.Center.Y;
        double radius = cellSize * 0.22;
        double spike = cellSize * 0.36;
        double thickness = Math.Max(1, cellSize * 0.09);

        Color body = outlineForItemReveal
            ? Color.FromArgb(0xA0, _theme.MineFill.R, _theme.MineFill.G, _theme.MineFill.B)
            : _theme.MineFill;

        var brush = GetBrush(body);
        var pen = new Pen(brush, thickness);

        // 水平 / 垂直两根长刺
        context.DrawLine(pen, new Point(cx - spike, cy), new Point(cx + spike, cy));
        context.DrawLine(pen, new Point(cx, cy - spike), new Point(cx, cy + spike));

        // 两条对角短刺
        double diagonal = spike * 0.72;
        context.DrawLine(pen, new Point(cx - diagonal, cy - diagonal), new Point(cx + diagonal, cy + diagonal));
        context.DrawLine(pen, new Point(cx - diagonal, cy + diagonal), new Point(cx + diagonal, cy - diagonal));

        // 中心圆
        context.DrawEllipse(brush, null, new Point(cx, cy), radius, radius);

        if (outlineForItemReveal)
        {
            var outlinePen = new Pen(GetBrush(_theme.ItemRevealedMineOutline), Math.Max(1.5, cellSize * 0.07));
            double outlineRadius = radius + (cellSize * 0.12);
            context.DrawEllipse(null, outlinePen, new Point(cx, cy), outlineRadius, outlineRadius);
        }
    }

    private void DrawFlag(DrawingContext context, Rect rect)
    {
        double w = rect.Width;
        double poleWidth = Math.Max(1, w * 0.08);
        context.FillRectangle(
            GetBrush(_theme.FlagPole),
            new Rect(rect.X + (w * 0.46), rect.Y + (w * 0.2), poleWidth, w * 0.6));

        var flag = new StreamGeometry();
        using (StreamGeometryContext geometry = flag.Open())
        {
            geometry.BeginFigure(new Point(rect.X + (w * 0.5), rect.Y + (w * 0.2)), true);
            geometry.LineTo(new Point(rect.X + (w * 0.84), rect.Y + (w * 0.35)));
            geometry.LineTo(new Point(rect.X + (w * 0.5), rect.Y + (w * 0.5)));
            geometry.EndFigure(true);
        }

        context.DrawGeometry(GetBrush(_theme.FlagCloth), null, flag);
    }

    private IBrush GetBrush(Color color)
    {
        uint key = ((uint)color.A << 24) | ((uint)color.R << 16) | ((uint)color.G << 8) | color.B;
        if (!_brushCache.TryGetValue(key, out IBrush? brush))
        {
            brush = new SolidColorBrush(color);
            _brushCache[key] = brush;
        }

        return brush;
    }

    private readonly record struct FarRun(int StartCol, int EndCol, uint Key);
}

/// <summary>道具瞄准态。</summary>
public enum AimingMode
{
    None = 0,
    Magnifier = 1,
    Sonar = 2,
}

/// <summary>棋盘视图常量（与 Windows 端 BoardTheme 的数值对齐）。</summary>
public static class BoardMetrics
{
    public const double DefaultCellSize = 26;
    public const double MaxFitCellSize = 44;
    public const double MinCellSize = 0.52;   // 0.02×
    public const double MaxCellSize = 104;    // 4×
    public const double PanThreshold = 4;
    public const double WheelPanStep = 90;
    public const long DoubleClickMs = 500;
}
