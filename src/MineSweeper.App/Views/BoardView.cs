using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using MineSweeper.App.Rendering;
using MineSweeper.Core;
using MineSweeper.Core.Audio;
using MineSweeper.Core.Items;
using MineSweeper.Core.Notifications;
using Windows.Foundation;
using Windows.System;
using Windows.UI;

namespace MineSweeper.App.Views;

/// <summary>
/// 棋盘视图（§1.3 Views/）：承载 Win2D 的 <see cref="CanvasVirtualControl"/>，
/// 并负责输入路由、缩放平移、瞄准态与分帧展开。
///
/// §1.4 决策采纳方案 A：经典模式与自定义模式共用这一条渲染路径，
/// 经典三级难度只是"一组预设参数"。
/// </summary>
public sealed class BoardView : UserControl
{
    private readonly Grid _root = new();
    private readonly CanvasVirtualControl _canvas = new();

    private GameSession? _session;
    private BoardTheme _theme = BoardTheme.Classic;
    private float _cellSize = BoardTheme.DefaultCellSize;
    private float _offsetX;
    private float _offsetY;

    private bool _aiming;

    /// <summary>正在瞄准的物品栏槽位（0 放大镜 / 1 声呐扫描）。</summary>
    private int _aimSlot;

    /// <summary>v1.3：当前正在瞄准的物品栏槽位（0 放大镜 / 1 声呐扫描）；-1 表示没在瞄准。</summary>
    public int AimSlot => _aimSlot;
    private int _hoverIndex = -1;
    private int _mouseCell = -1;

    private bool _panning;
    private Point _panStartPoint;
    private float _panStartOffsetX;
    private float _panStartOffsetY;

    /// <summary>左键按下的位置：松手时用它区分"点击"和"拖拽"（触屏上尤其重要）。</summary>
    private Point _leftDownPoint;

    /// <summary>上一次左键按下的格与时间，用来判定"双击"（chord 的唯一触发入口）。</summary>
    private int _lastLeftPressIndex = -1;
    private DateTimeOffset _lastLeftPressAt;

    private bool _leftDownValid;

    /// <summary>是否正在做触屏手势（捏合/拖动）。手势期间不把松手当成点击。</summary>
    private bool _manipulating;

    /// <summary>远景雷密度热力图开关（§1.5 可选）。</summary>
    private bool _heatmapEnabled;

    /// <summary>布局尺寸还没就绪时先记住"待适应窗口"，等 SizeChanged 到了再真正执行。</summary>
    private bool _needsFit;

    private bool _middlePressed;
    private Point _middleDownPoint;
    private int _middleDownCell = -1;

    /// <summary>透视高亮淡出时长（§5.4：结算瞬间画一圈高亮、1 秒淡出）。</summary>
    private static readonly TimeSpan HighlightDuration = TimeSpan.FromSeconds(1);

    /// <summary>玩家是否手动调过缩放/平移。调过之后，窗口尺寸变化就不再强行重新适应（免得跟玩家抢视图）。</summary>
    private bool _userAdjustedView;

    /// <summary>透视结算后的高亮中心格（-1 表示没有高亮）。</summary>
    private int _highlightCenter = -1;

    private DateTimeOffset _highlightStartedAt;

    /// <summary>需要告诉玩家一件事时抛出。级别由产生通知的地方决定，UI 只负责把它送出去。</summary>
    public event EventHandler<GameNotification>? NotificationRequested;

    /// <summary>需要播放音效时抛出（音效播放器由窗口持有，BoardView 不关心播放细节）。</summary>
    public event EventHandler<SoundEffect>? SoundRequested;

    /// <summary>瞄准态下"即将透视的区域"发生变化（供右侧信息面板显示坐标与内容）。</summary>
    public event EventHandler? MagnifierPreviewChanged;

    /// <summary>
    /// 声呐扫描成功（v0.8）。带的是这次扫描的结果，供窗口弹"需要手动确认"的小窗口 ——
    /// 蓝色瞄准框一退出就没了，玩家很容易忘记刚扫的是哪一块。
    /// </summary>
    public event EventHandler<SonarScanResult>? SonarUsed;

    /// <summary>棋盘状态发生变化（UI 需要刷新计数/计时/物品栏）。</summary>
    public event EventHandler? BoardStateChanged;

    /// <summary>
    /// 一次 chord 成功展开后触发，参数是**这次 chord 一共翻开了多少格**（v1.1）。
    /// 成就里要判"一次 chord 展开 ≥100 格"，而 MainWindow 拿不到 <c>ChordResult.OpenedCount</c>，
    /// 所以由棋盘把结果抛出来 —— 比让 UI 去猜"这一帧翻开数突然变多"可靠得多。
    /// </summary>
    public event EventHandler<int>? ChordResolved;

    /// <summary>瞄准态开关变化。</summary>
    public event EventHandler? AimingChanged;

    public BoardView()
    {
        // 棋盘外围**不留底色**：以前这里硬编码了一整块深色（0x20,0x20,0x22），
        // 于是浅色主题下窗口里会有一大块突兀的黑底。现在交给窗口自己的主题背景，
        // 棋盘就"浮"在页面上，不再有黑框。
        _root.Background = new SolidColorBrush(Color.FromArgb(0x00, 0x00, 0x00, 0x00));
        _canvas.IsHitTestVisible = false;

        // **必须裁掉溢出**：小盘会被 MaxFitCellSize(44) 放大，格子有可能比这一行给的
        // 高度还大；不裁的话棋盘会"长出去"，而主界面第 1 行是 * —— 结果是把**底部的按钮
        // 顶出窗口可视区**（实测踩过：加进关卡导航那一行之后按钮就看不见了）。
        //
        // 注意矩形要跟着实际尺寸走：RectangleGeometry 默认是空矩形，直接 new 一个会把内容全裁掉。
        var clipGeometry = new RectangleGeometry();
        Clip = clipGeometry;
        SizeChanged += (_, e) => clipGeometry.Rect = new Rect(0, 0, e.NewSize.Width, e.NewSize.Height);

        _root.Children.Add(_canvas);
        Content = _root;




        _canvas.CreateResources += OnCreateResources;
        _canvas.RegionsInvalidated += OnRegionsInvalidated;

        _root.PointerPressed += OnPointerPressed;
        _root.PointerMoved += OnPointerMoved;
        _root.PointerReleased += OnPointerReleased;
        _root.PointerWheelChanged += OnPointerWheelChanged;
        _root.PointerExited += OnPointerExited;

        // 指针捕获丢失 = "这次按下不会再有对应的松手事件了"（例如中键拖拽到控件外松手、
        // 或者窗口被系统抢走指针）。这时必须把平移/按住状态清掉，否则 _panning 会一直为真，
        // 之后每次左键都被当成"继续平移"，表现就是"怎么点都点不开、也插不了旗"。
        _root.PointerCaptureLost += (_, _) => ResetInputState();
        _root.DoubleTapped += OnDoubleTapped;
        _root.SizeChanged += OnRootSizeChanged;

        // §1.5 触屏：双指捏合缩放 + 拖动平移
        _root.ManipulationMode = ManipulationModes.Scale | ManipulationModes.TranslateX | ManipulationModes.TranslateY;
        _root.ManipulationStarted += OnManipulationStarted;
        _root.ManipulationDelta += OnManipulationDelta;
        _root.ManipulationCompleted += OnManipulationCompleted;


        // 失去焦点时把输入状态清干净。
        // 否则一次"按下没收到松手"的拖拽会把 _panning 一直挂着：
        // 之后每次左键都被当成继续平移——表现就是"点了没反应"或者"棋盘自己跑到别的区域去了"。
        LostFocus += (_, _) => ResetInputState();
    }

    /// <summary>把"按下/平移"这些瞬时输入状态清回初始值。</summary>
    internal void ResetInputState()
    {
        _panning = false;
        _middlePressed = false;
        _leftDownValid = false;
    }

    /// <summary>是否处于放大镜瞄准态。</summary>
    public bool IsAiming => _aiming;

    /// <summary>Win2D 设备是否已经就绪（性能自检需要它）。</summary>
    internal bool IsDeviceReady => _canvas.ReadyToDraw;

    /// <summary>
    /// 底层 Win2D 资源创建器。性能自检用它建离屏画布，
    /// 这样量到的是<b>同一个 GPU 设备</b>上的真实耗时，而不是另起一个设备。
    /// </summary>
    internal ICanvasResourceCreatorWithDpi ResourceCreator => _canvas;

    /// <summary>切换主题（设置界面改主题时调用）。</summary>
    internal void ApplyTheme(BoardTheme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        _theme = theme;
        RequestRedraw();
    }

    /// <summary>远景雷密度热力图开关（§1.5 可选叠加）。</summary>
    internal void ApplyHeatmap(bool enabled)
    {
        if (_heatmapEnabled == enabled)
        {
            return;
        }

        _heatmapEnabled = enabled;
        RequestRedraw();
    }

    /// <summary>瞄准态当前预览的区域（右侧信息面板用，§5.6）。</summary>
    public MagnifierReveal? MagnifierPreview { get; private set; }

    // ------------------------------------------------------------ 触屏手势（§1.5）

    private void OnManipulationStarted(object sender, ManipulationStartedRoutedEventArgs e)
    {
        _manipulating = true;
        _leftDownValid = false;
    }

    private void OnManipulationDelta(object sender, ManipulationDeltaRoutedEventArgs e)
    {
        if (_session is null)
        {
            return;
        }

        // 平移
        if (Math.Abs(e.Delta.Translation.X) > 0.01 || Math.Abs(e.Delta.Translation.Y) > 0.01)
        {
            PanBy((float)e.Delta.Translation.X, (float)e.Delta.Translation.Y);
        }

        // 双指捏合缩放：绕手指中心缩放，指下的那个点保持不动
        if (Math.Abs(e.Delta.Scale - 1f) > 0.001f)
        {
            ZoomAt(e.Position, e.Delta.Scale);
        }
    }

    private void OnManipulationCompleted(object sender, ManipulationCompletedRoutedEventArgs e)
    {
        _manipulating = false;
        _userAdjustedView = true;
    }

    /// <summary>绑定/切换一局。</summary>
    public void AttachSession(GameSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        _session = session;
        _aiming = false;
        _hoverIndex = -1;
        _mouseCell = -1;
        _highlightCenter = -1;
        ResetInputState();


        // 绑定新对局时窗口往往还没完成排版（ActualWidth 为 0），
        // 所以这里挂一个"待适应"标记，等第一次 SizeChanged 再真正居中适应。
        _needsFit = true;
        _userAdjustedView = false;
        FitToWindow();

        AimingChanged?.Invoke(this, EventArgs.Empty);
        RequestRedraw();
    }

    private void OnRootSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.NewSize.Width <= 1 || e.NewSize.Height <= 1)
        {
            return;
        }

        // 只在"第一次 SizeChanged"里适应是不够的：提示条出现/收起、窗口被拖动缩放，
        // 都会让棋盘行变高变宽，而棋盘会停在旧位置上（看起来偏上偏左、甚至被挤出可视区）。
        // 所以只要玩家没手动调过视图，尺寸一变就重新适应。
        if (_needsFit || !_userAdjustedView)
        {
            _needsFit = false;
            FitToWindow();
            return;
        }

        RequestRedraw();
    }

    // ------------------------------------------------------------ 缩放 / 平移

    /// <summary>
    /// 交给棋盘**真正可用的高度**（DIP）。由宿主窗口在布局变化时写入；0 表示不限制（用自身高度）。
    ///
    /// 为什么需要外部告诉它：棋盘里是个 Win2D 画布，它会一直画到自己的内容边界，
    /// 光靠 <c>Clip</c> / <c>MaxHeight</c> **挡不住**（实测：限制之后格线照样画到控件外，
    /// 把底部的按钮行盖住）。所以必须让它自己把格子算小 —— 小盘被 MaxFitCellSize(44) 放大后
    /// 很容易比这一行还高，那就会盖住下面的界面。
    /// </summary>
    public double AvailableHeight { get; set; }

    /// <summary>适应窗口（§1.5 的复位）：整盘可见并居中。</summary>
    public void FitToWindow()
    {
        if (_session is null || _root.ActualWidth <= 0 || _root.ActualHeight <= 0)
        {
            return;
        }

        // 视口马上就要变了：整块重绘，避免旧的瞄准框留在屏幕上
        RequestRedraw();

        Board board = _session.Board;

        // 可用高度优先取宿主给的值（它才是"这一行真正有多高"）
        double usableHeight = AvailableHeight > 0 ? AvailableHeight : _root.ActualHeight;

        float fit = Math.Min(
            (float)(_root.ActualWidth - 8) / board.Width,
            (float)(usableHeight - 8) / board.Height);

        float min = BoardTheme.DefaultCellSize * BoardTheme.MinZoom;
        float max = BoardTheme.DefaultCellSize * BoardTheme.MaxZoom;

        // 装不下就缩小（下限 MinZoom）；装得下时可以放大到 MaxFitCellSize。
        // 以前上限就是 DefaultCellSize，小盘会缩在一大片空白中间——玩家点到棋盘外面就"没反应"。
        _cellSize = Math.Clamp(Math.Min(BoardTheme.MaxFitCellSize, fit), min, max);
        CenterBoard();
        _userAdjustedView = false;
        RequestRedraw();
    }

    /// <summary>居中时也用"真正可用的高度"，否则小盘会以自身高度为中心而偏下。</summary>
    private double UsableHeight => AvailableHeight > 0 ? AvailableHeight : _root.ActualHeight;

    /// <summary>放大到"可读格宽"并居中到目标格（§5.6：大棋盘用放大镜时自动局部放大）。</summary>
    public void ZoomToCell(int index, float targetCellSize = 30f)
    {
        if (_session is null || !_session.Board.IsValidIndex(index))
        {
            return;
        }

        // 视口马上就要变了：先把旧瞄准框那一块失效掉。
        // 放大镜这条路径尤其关键——ExitAiming 之后紧接着就调这里，
        // 而 ExitAiming 那一刻的视口可能已经被预览面板的布局变化改过了。
        RequestRedraw();

        if (_cellSize < BoardTheme.MinReadableCellSize)
        {
            _cellSize = Math.Clamp(targetCellSize, BoardTheme.MinReadableCellSize, BoardTheme.DefaultCellSize * BoardTheme.MaxZoom);
        }

        Board board = _session.Board;
        double cellCenterX = ((board.XOf(index) + 0.5) * _cellSize);
        double cellCenterY = ((board.YOf(index) + 0.5) * _cellSize);

        _offsetX = (float)((_root.ActualWidth / 2) - cellCenterX);
        _offsetY = (float)((_root.ActualHeight / 2) - cellCenterY);

        _userAdjustedView = true;
        ClampOffset();
        RequestRedraw();
    }

    /// <summary>仅供布局自检：当前格宽与视口（DIP）。</summary>
    internal string ViewportForLayoutCheck()
        => $"cell={_cellSize:F2} offset=({_offsetX:F1},{_offsetY:F1}) " +
           $"可用高={AvailableHeight:F1} 控件高={_root.ActualHeight:F1} " +
           $"棋盘内容高={(SafeCellSpan())}";

    private string SafeCellSpan()
    {
        if (_session is null)
        {
            return "?";
        }

        return $"{_session.Board.Height * _cellSize:F1}";
    }

    private void CenterBoard()
    {
        if (_session is null)
        {
            return;
        }

        _offsetX = (float)((_root.ActualWidth - (_session.Board.Width * _cellSize)) / 2);
        _offsetY = (float)((UsableHeight - (_session.Board.Height * _cellSize)) / 2);
    }

    /// <summary>限制偏移，避免把棋盘拖到完全看不见的地方。</summary>
    private void ClampOffset()
    {
        if (_session is null)
        {
            return;
        }

        float boardWidth = _session.Board.Width * _cellSize;
        float boardHeight = _session.Board.Height * _cellSize;
        float width = (float)_root.ActualWidth;
        float height = (float)_root.ActualHeight;

        float marginX = Math.Min(boardWidth, width) * 0.5f;
        float marginY = Math.Min(boardHeight, height) * 0.5f;

        _offsetX = Math.Clamp(_offsetX, -boardWidth + marginX, width - marginX);
        _offsetY = Math.Clamp(_offsetY, -boardHeight + marginY, height - marginY);
    }

    private void ZoomAt(Point position, float factor)
    {
        if (_session is null)
        {
            return;
        }

        float oldCellSize = _cellSize;
        float max = BoardTheme.DefaultCellSize * BoardTheme.MaxZoom;
        float min = BoardTheme.DefaultCellSize * BoardTheme.MinZoom;
        float newCellSize = Math.Clamp(oldCellSize * factor, min, max);

        if (Math.Abs(newCellSize - oldCellSize) < 0.0001f)
        {
            return;
        }

        // 视口要变了：先失效旧瞄准框的位置
        RequestRedraw();

        // 让光标下的那个点保持不动
        double boardX = (position.X - _offsetX) / oldCellSize;
        double boardY = (position.Y - _offsetY) / oldCellSize;

        _cellSize = newCellSize;
        _offsetX = (float)(position.X - (boardX * newCellSize));
        _offsetY = (float)(position.Y - (boardY * newCellSize));

        _userAdjustedView = true;
        ClampOffset();
        RequestRedraw();
    }

    private void PanBy(float dx, float dy)
    {
        // 视口要变了：先失效旧瞄准框的位置
        RequestRedraw();

        _offsetX += dx;
        _offsetY += dy;
        _userAdjustedView = true;
        ClampOffset();
        RequestRedraw();
    }

    // ------------------------------------------------------------ 通知

    private void Notify(NotificationLevel level, string title, string message, string tag)
        => NotificationRequested?.Invoke(this, new GameNotification(level, title, message, tag));

    /// <summary>只入日志、不弹窗的通知（过程性提示，见 <see cref="GameNotification.Quiet"/>）。</summary>
    private void NotifyQuiet(NotificationLevel level, string title, string message, string tag)
        => NotificationRequested?.Invoke(this, GameNotification.Quiet(level, title, message, tag));

    private void PlaySound(SoundEffect effect) => SoundRequested?.Invoke(this, effect);

    // ------------------------------------------------------------ 瞄准态

    /// <summary>
    /// 进入瞄准态（右键物品栏图标 / 无右键设备长按物品栏图标，§5.3）。
    /// <paramref name="slot"/> 是物品栏槽位（0 起）；目前只有 0 号槽有道具（放大镜），
    /// 2/3 号槽在 UI 里已经预留，传入时会明确告诉玩家"这个槽位还没有道具"而不是静默无反应。
    /// </summary>
    public bool BeginAiming(int slot = 0)
    {
        if (_session is null)
        {
            return false;
        }

        // 0 = 放大镜、1 = 声呐扫描（都是"瞄准型"）；2 = 时间冻结是即用型，走 UseTimeFreezeNow
        if (slot is < 0 or > 1)
        {
            Notify(
                NotificationLevel.Warning,
                slot == 2 ? "时间冻结直接点它就行" : "物品栏里没有这一格",
                slot == 2 ? "它是即用型道具，不需要瞄准。" : "物品栏总共三格。",
                "item.empty-slot");
            return false;
        }

        ItemUseCheck check = slot == 1 ? _session.CheckSonarAvailable() : _session.CheckMagnifierAvailable();
        if (check != ItemUseCheck.Ok)
        {
            Notify(NotificationLevel.Warning, $"现在不能用{(slot == 1 ? "声呐扫描" : "放大镜")}", ItemUseCheckText.Describe(check), $"item.blocked.{slot}");
            return false;
        }

        _aiming = true;
        _aimSlot = slot;
        _hoverIndex = -1;
        AimingChanged?.Invoke(this, EventArgs.Empty);
        NotifyQuiet(
            NotificationLevel.Info,
            slot == 1 ? "声呐瞄准中" : "放大镜瞄准中",
            slot == 1
                ? "点击棋盘任意位置扫描那个 3×3（只报雷数）· 右键取消"
                : "点击棋盘任意位置使用放大镜 · 右键棋盘取消",
            $"item.aiming.{slot}");
        RefreshMagnifierPreview();
        RequestRedraw();
        return true;
    }

    /// <summary>刷新"即将透视的区域"（右侧信息面板用，§5.6）。</summary>
    private void RefreshMagnifierPreview()
    {
        if (_session is null || !_aiming || !_session.Board.IsValidIndex(_hoverIndex))
        {
            if (MagnifierPreview is null)
            {
                return;
            }

            MagnifierPreview = null;
            MagnifierPreviewChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        MagnifierPreview = _session.PreviewMagnifier(_hoverIndex);
        MagnifierPreviewChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>退出瞄准态（不消耗道具）。</summary>
    public void CancelAiming() => ExitAiming(notify: true);

    /// <summary>
    /// 退出瞄准态。<paramref name="notify"/> 为 false 时不发"已取消"通知——
    /// 用在"使用失败"这条路径上，否则玩家会先收到一条"已取消"再收到失败原因。
    ///
    /// <b>这里必须整块重绘，不能只失效"框那一块"。</b>
    /// 蓝色瞄准框（<c>theme.AimingRing</c>）是按<b>当时的视口</b>画出来的，
    /// 而它在可见期间视口会被改好几次：右键物品栏会让右侧预览面板弹出，
    /// 棋盘控件随之从 649 宽缩到 369 宽并触发重新适应窗口（offset 181 → 41）；
    /// 透视成功后 <see cref="ZoomToCell"/> 又会把视口挪到别处。
    /// 用"当前视口"去算该失效哪一块，算出来的是<b>新位置</b>，
    /// 而框的旧像素留在旧位置上——那块区域再没人重绘，屏幕上就留下一个蓝色线框残影
    /// （玩家报的 bug：用了放大镜之后还有蓝色线框）。
    /// 整块重绘是这条路径上唯一不会漏的做法，而且它只在退出瞄准时发生一次。
    /// </summary>
    private void ExitAiming(bool notify)
    {
        if (!_aiming)
        {
            return;
        }

        // 整块重绘：退出瞄准后那一圈蓝框必须被彻底擦掉。
        // 框是按"当时的视口"画的，而预览面板的弹出/收起会在瞄准期间多次改变视口，
        // 所以这里不做局部失效，直接整块重绘，保证没有任何蓝色像素留下。
        _hoverIndex = -1;
        _aiming = false;
        MagnifierPreview = null;
        MagnifierPreviewChanged?.Invoke(this, EventArgs.Empty);
        AimingChanged?.Invoke(this, EventArgs.Empty);

        if (notify)
        {
            NotifyQuiet(NotificationLevel.Info, "已取消", "已退出放大镜瞄准态。", "magnifier.cancelled");
        }

        RequestRedraw();
    }

    /// <summary>在指定格使用声呐扫描：只报 3×3 里的雷数，不改变棋盘状态。</summary>
    private void UseSonarAt(int index)
    {
        if (_session is null)
        {
            return;
        }

        SonarScanResult scan = _session.ScanWithSonar(index);
        if (!scan.Success)
        {
            ExitAiming(notify: false);
            Notify(NotificationLevel.Warning, "声呐扫描用不了", ItemUseCheckText.Describe(scan.Check), "sonar.rejected");
            return;
        }

        ExitAiming(notify: false);
        PlaySound(SoundEffect.Chord);

        Board board = _session.Board;
        int size = (scan.Radius * 2) + 1;
        Notify(
            NotificationLevel.Success,
            MineSweeper.Core.Localization.Strings.Get("item.sonar.title", scan.MineCount),
            MineSweeper.Core.Localization.Strings.Get("item.sonar.body", board.YOf(scan.CenterIndex) + 1, board.XOf(scan.CenterIndex) + 1, size, scan.MineCount, scan.RemainingCount),
            "sonar.result");

        // 蓝色瞄准框退出后就不在了，玩家很容易忘记"刚扫的是哪一块"。
        // 所以把结果抛给窗口，由它弹一个**需要手动确认**的小窗口把区域写清楚（Toast 照旧保留）。
        SonarUsed?.Invoke(this, scan);

        BoardStateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>时间冻结（即用型道具）：立刻暂停倒计时。返回是否成功。</summary>
    internal bool UseTimeFreezeNow()
    {
        if (_session is null)
        {
            return false;
        }

        TimeFreezeResult freeze = _session.FreezeTimer();
        if (!freeze.Started)
        {
            Notify(NotificationLevel.Warning, "现在不能用时间冻结", ItemUseCheckText.Describe(freeze.Check), "freeze.rejected");
            return false;
        }

        PlaySound(SoundEffect.Chord);
        Notify(
            NotificationLevel.Success,
            MineSweeper.Core.Localization.Strings.Get("item.freeze.title", freeze.FrozenSeconds.ToString("F0")),
            MineSweeper.Core.Localization.Strings.Get("item.freeze.body", freeze.FrozenSeconds.ToString("F0"), freeze.RemainingCount),
            "freeze.result");

        BoardStateChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }
    private void TryUseMagnifier(int index)
    {
        if (_session is null)
        {
            return;
        }

        if (_aimSlot == 1)
        {
            UseSonarAt(index);
            return;
        }

        MagnifierUseResult result = _session.UseMagnifier(index);

        if (!result.Success)
        {
            ExitAiming(notify: false);
            Notify(NotificationLevel.Warning, "无法使用放大镜", result.Reason ?? "无法使用放大镜。", "magnifier.rejected");
            return;
        }

        ExitAiming(notify: false);

        MagnifierReveal reveal = result.Reveal!;

        // §5.6：大棋盘下自动放大到可读格宽
        ZoomToCell(index);

        // §5.4 的视觉反馈：结算瞬间画一圈高亮、1 秒淡出（由 AdvanceHighlight 逐帧推进）
        _highlightCenter = index;
        _highlightStartedAt = DateTimeOffset.UtcNow;

        Notify(
            NotificationLevel.Success,
            "透视完成",
            MineSweeper.Core.Localization.Strings.Get("item.magnifier.body", reveal.AffectedCount, reveal.MineCount, reveal.EmptyCount),
            "magnifier.used");

        // §4.5：只重绘受影响的矩形，不做全屏刷新
        InvalidateCells(reveal.Cells.Select(c => c.Index).ToArray());
        InvalidateCellShape(index, _session.MagnifierShape);
        BoardStateChanged?.Invoke(this, EventArgs.Empty);
    }

    // ------------------------------------------------------------ 输入

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_session is null)
        {
            return;
        }


        var point = e.GetCurrentPoint(_root);
        Point position = point.Position;
        int index = HitTest(position);



        // 左右键同时按下 = chord（§3.1）。
        // 这个判定必须放在右键分支之前：右键分支命中就 return（去插旗），
        // 放在它后面就永远不可达——那样"左右键同按"会变成插旗而不是 chord。
        if (point.Properties.IsLeftButtonPressed && point.Properties.IsRightButtonPressed)
        {
            if (_aiming)
            {
                // 瞄准态下同按也按取消处理，避免误插旗
                ExitAiming(notify: true);
            }
            else
            {
                TryChord(index);
            }

            e.Handled = true;
            return;
        }

        if (point.Properties.IsMiddleButtonPressed)
        {
            _middlePressed = true;
            _middleDownPoint = position;
            _middleDownCell = index;

            // 中键同时支持"平移"和"chord"：按下是先记录，松开时若没移动过就当 chord
            _panning = false;
            _panStartPoint = position;
            _panStartOffsetX = _offsetX;
            _panStartOffsetY = _offsetY;
            e.Handled = true;
            return;
        }

        if (point.Properties.IsRightButtonPressed)
        {
            if (LogInput)
            {
                MineSweeper.App.Services.AppPaths.Log(
                    $"[INPUT] 右键 index={index} 视口 cell={_cellSize:F2} offset=({_offsetX:F1},{_offsetY:F1}) " +
                    $"控件={_root.ActualWidth:F0}×{_root.ActualHeight:F0} 棋盘={_session?.Board.Width}×{_session?.Board.Height}");
            }

            if (_aiming)
            {
                // 瞄准态下右键棋盘 = 取消（§5.3）
                CancelAiming();
            }
            else
            {
                ToggleFlagAt(index);
            }

            e.Handled = true;
            return;
        }

        if (point.Properties.IsLeftButtonPressed)
        {
            // 记下按下的位置：触屏要靠它区分"点击"和"拖拽"
            _leftDownPoint = position;
            _leftDownValid = true;


            // 全新的左键按下：把上一次可能残留的平移/中键状态清掉（中键还按着的情况除外）
            if (!point.Properties.IsMiddleButtonPressed)
            {
                _panning = false;
                _middlePressed = false;
            }

            // 鼠标与笔：**按下就生效**，而且用"按下的位置"判定格子。
            //
            // 原来是等松手才判定、并且用松手时的坐标——这有两个后果，玩家看到的就是
            // "点了没反应"或者"翻开了别的格子"：
            //   ① 手一抖，松手时指针已经在隔壁格上了 → 翻开的是隔壁那格；
            //   ② 抖动超过 8 DIP 就被当成拖拽，直接不处理 → 点了完全没反应。
            // 原版扫雷是按下即翻开，这里改回原版手感；阈值判定只留给触屏。
            if (e.Pointer.PointerDeviceType != PointerDeviceType.Touch)
            {
                _leftDownValid = false;

                // 双击 = chord：**只由这里判定**。
                // 条件全部是"看得见的事实"：第二次按下落在同一格、且那一格已经翻开。
                // 未翻开的格子（含 0 格、空白区域）永远不会走到 chord —— 单击照常翻开。
                if (IsChordSecondPress(index))
                {
                    TryChord(index);
                }
                else
                {
                    ActivateCell(index);
                }

                RememberLeftPress(index);
                e.Handled = true;
                return;
            }

            // 触屏：手指很难完全不动，留到松手时按阈值判定（见 OnPointerReleased）
            e.Handled = true;
        }
    }

    /// <summary>
    /// 点击诊断（环境变量 <c>SAOLEI_INPUT=1</c>）：把"点在哪、算成哪一格、当时视口是什么"
    /// 写进 app.log。平时不打开；一旦出现"点的位置和翻开的格子对不上"，这是唯一能查的证据。
    /// </summary>
    private static readonly bool LogInput =
        Environment.GetEnvironmentVariable("SAOLEI_INPUT") == "1";

    /// <summary>左键落在某一格上该做的事：瞄准态用放大镜，否则翻开。</summary>
    private void ActivateCell(int index)
    {
        if (LogInput)
        {
            MineSweeper.App.Services.AppPaths.Log(
                $"[INPUT] index={index} 视口 cell={_cellSize:F2} offset=({_offsetX:F1},{_offsetY:F1}) " +
                $"控件={_root.ActualWidth:F0}×{_root.ActualHeight:F0} 棋盘={_session?.Board.Width}×{_session?.Board.Height} " +
                $"该格状态={DescribeCellStateForInput(index)}");
        }

        if (index < 0)
        {
            return;
        }

        if (_aiming)
        {
            TryUseMagnifier(index);
            return;
        }

        // 诊断用：记录这一下"翻开"前后的已翻开格数。
        // 单击已翻开的格子必须**一格都不多开**（chord 只归双击/中键/双键），
        // 有了这两个数就能从 app.log 直接看出有没有误触展开。
        int revealedBefore = _session?.RevealedCount ?? 0;
        RevealAt(index);

        if (LogInput)
        {
            MineSweeper.App.Services.AppPaths.Log(
                $"[INPUT]   本次翻开结果：{revealedBefore} → {_session?.RevealedCount ?? 0} 格" +
                $"（Δ={(_session?.RevealedCount ?? 0) - revealedBefore}），状态={_session?.Status}");
        }
    }

    /// <summary>诊断用：把某一格的可读状态写给 <c>SAOLEI_INPUT=1</c> 的日志。</summary>
    private string DescribeCellStateForInput(int index)
    {
        if (_session is null || !_session.Board.IsValidIndex(index))
        {
            return "棋盘外";
        }

        Board board = _session.Board;
        if (board.IsRevealed(index))
        {
            return $"已翻开·数字{board.GetAdjacentMines(index)}";
        }

        if (board.IsFlagged(index))
        {
            return "已插旗";
        }

        return board.IsQuestionMarked(index) ? "问号" : "未翻开";
    }

    /// <summary>
    /// 仅供 <c>SAOLEI_LEVELS=clicksweep</c> 自检：按当前视口算出某一格中心的控件坐标（DIP）。
    /// </summary>
    internal bool TryGetCellCenterForProbe(int index, out double x, out double y)
    {
        x = 0;
        y = 0;
        if (_session is null || !_session.Board.IsValidIndex(index))
        {
            return false;
        }

        Board board = _session.Board;
        x = _offsetX + ((board.XOf(index) + 0.5) * _cellSize);
        y = _offsetY + ((board.YOf(index) + 0.5) * _cellSize);
        return true;
    }

    /// <summary>仅供自检：该格的可读状态 + 邻雷数。</summary>
    internal string ProbeCellInfo(int index)
    {
        if (_session is null)
        {
            return "无效";
        }

        Board board = _session.Board;
        bool revealed = board.IsRevealed(index);
        return $"状态={DescribeCellStateForInput(index)} 邻雷={(revealed ? board.GetAdjacentMines(index).ToString() : "-")}";
    }

    /// <summary>仅供自检：等价于"鼠标在这一点按下了左键"（走真实命中测试 + 真实左键入口）。</summary>
    internal void ClickAtForProbe(double x, double y)
        => ActivateCell(HitTest(new Point(x, y)));

    /// <summary>
    /// 仅供自检：等价于"鼠标在这一点**双击**"。走的是和真人完全相同的入口：
    /// 两次左键按下（第二次会被 <see cref="IsChordSecondPress"/> 识别成双击 → chord）。
    /// </summary>
    internal void DoubleClickAtForProbe(double x, double y)
    {
        int index = HitTest(new Point(x, y));

        // 第一次按下：正常左键
        ActivateCell(index);
        RememberLeftPress(index);

        // 第二次按下：同一格 + 已翻开 → 判定为双击 → chord
        if (IsChordSecondPress(index))
        {
            TryChord(index);
        }

        RememberLeftPress(index);
    }

    /// <summary>
    /// 是否属于"双击已翻开格"的第二次按下（chord 的唯一触发入口）。
    ///
    /// 三个条件缺一不可：上一次按下是**同一格**、两次间隔在系统双击时间内、该格**已翻开**。
    /// 未翻开的格一律返回 false，所以空白区域/未点过的区域永远不会被 chord 碰到。
    /// </summary>
    private bool IsChordSecondPress(int index)
    {
        if (index < 0 || _session is null)
        {
            return false;
        }

        if (_lastLeftPressIndex != index || _lastLeftPressAt == default)
        {
            return false;
        }

        if ((DateTimeOffset.UtcNow - _lastLeftPressAt).TotalMilliseconds > DoubleClickIntervalMs)
        {
            return false;
        }

        return _session.Board.IsRevealed(index);
    }

    private void RememberLeftPress(int index)
    {
        _lastLeftPressIndex = index;
        _lastLeftPressAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// 系统双击判定时间（毫秒）。默认取 Windows 的 <c>GetDoubleClickTime()</c>；
    /// 取不到时退回 500ms。设成 0 可以彻底关掉"双击 chord"（自检用得上）。
    /// </summary>
    public int DoubleClickIntervalMs { get; set; } = ResolveDoubleClickInterval();

    private static int ResolveDoubleClickInterval()
    {
        try
        {
            int ms = GetDoubleClickTime();
            return ms > 0 ? ms : 500;
        }
        catch (Exception)
        {
            return 500;
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int GetDoubleClickTime();

    /// <summary>右键三态循环（§3.1）。已翻开的格右键无操作。</summary>
    private void ToggleFlagAt(int index)
    {
        if (_session is null || index < 0)
        {
            return;
        }

        if (_session.Board.IsRevealed(index))
        {
            return;
        }

        FlagResult flag = _session.ToggleFlag(index);
        if (flag.IsBlocked)
        {
            return;
        }

        InvalidateLastChange();
        PlaySound(SoundEffect.Flag);

        if (flag.MagnifierGranted)
        {
            Notify(
                NotificationLevel.Success,
                "攒到一次道具奖励",
                $"扫雷数够了：可以在下面的弹窗里从三个道具中挑一个。",
                "item.reward-earned");
        }

        BoardStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_session is null)
        {
            return;
        }

        var point = e.GetCurrentPoint(_root);
        Point position = point.Position;

        if (_panning)
        {
            // 自愈：万一没收到松手事件（拖到控件外松手等），这里发现按键其实已经不在了就收尾，
            // 不然 _panning 会一直挂着，把后续所有左键都吃掉。
            bool stillHeld = point.Properties.IsMiddleButtonPressed;

            if (!stillHeld)
            {
                ResetInputState();
                return;
            }

            PanBy(
                (float)(position.X - _panStartPoint.X),
                (float)(position.Y - _panStartPoint.Y));

            _panStartPoint = position;
            _panStartOffsetX = _offsetX;
            _panStartOffsetY = _offsetY;
            return;
        }

        if (_middlePressed &&
            (Math.Abs(position.X - _middleDownPoint.X) > 4 || Math.Abs(position.Y - _middleDownPoint.Y) > 4))
        {
            // 中键按下后发生了移动 → 认定是平移
            _panning = true;
            _middlePressed = false;
            PanBy(
                (float)(position.X - _panStartPoint.X),
                (float)(position.Y - _panStartPoint.Y));

            _panStartPoint = position;
            return;
        }

        int index = HitTest(position);
        _mouseCell = index;

        if (_aiming && index != _hoverIndex)
        {
            int previous = _hoverIndex;
            _hoverIndex = index;
            InvalidateCells(previous >= 0 ? new[] { previous } : Array.Empty<int>());
            InvalidateCells(index >= 0 ? new[] { index } : Array.Empty<int>());
            RefreshMagnifierPreview();
        }
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_session is null)
        {
            return;
        }

        var point = e.GetCurrentPoint(_root);
        PointerUpdateKind kind = point.Properties.PointerUpdateKind;

        // 平移结束：只收尾，不触发任何棋盘动作
        if (_panning)
        {
            _panning = false;
            _middlePressed = false;
            return;
        }

        // 中键单击 = chord（中键拖拽已在 PointerMoved 里升级为平移）
        if (_middlePressed && kind == PointerUpdateKind.MiddleButtonReleased)
        {
            _middlePressed = false;
            TryChord(_middleDownCell);
            return;
        }

        _middlePressed = false;

        // 鼠标与笔已经在 PointerPressed 里生效了（按下即翻开，用按下的坐标）。
        // 这里剩下的只有触屏那条"松手判定"路径。
        if (e.Pointer.PointerDeviceType != PointerDeviceType.Touch)
        {
            _leftDownValid = false;
            return;
        }

        // 注意：松手时 IsLeftButtonPressed 已经变成 false，
        // 所以这里必须用 PointerUpdateKind 判断"刚刚松开的是哪个键"。
        if (kind != PointerUpdateKind.LeftButtonReleased)
        {
            _leftDownValid = false;
            return;
        }

        // 触屏手势（捏合/拖动）刚做完，不把这次松手当成点击
        if (_manipulating)
        {
            _leftDownValid = false;
            return;
        }

        // 拖拽超过阈值也不当成点击：触屏上手指很难完全不动
        if (_leftDownValid)
        {
            double dx = point.Position.X - _leftDownPoint.X;
            double dy = point.Position.Y - _leftDownPoint.Y;
            _leftDownValid = false;

            if (Math.Sqrt((dx * dx) + (dy * dy)) > 8d)
            {
                return;
            }
        }

        // 触屏松手时也用**按下的那个格子**，避免手指抬起时滑到隔壁格
        ActivateCell(HitTest(_leftDownPoint));
    }

    /// <summary>
    /// 翻开一格（鼠标左键与键盘 Enter 共用同一条路径，避免两条路行为不一致）。
    /// </summary>
    private void RevealAt(int index)
    {
        if (_session is null || index < 0)
        {
            return;
        }

        RevealResult reveal = _session.Reveal(index);

        if (reveal.IsBlocked)
        {
            // 点已翻开的格子：**什么都不做**。
            //
            // 这里原来是"等价于双击 chord"，那是错的：原版扫雷里单击已翻开的数字格是静默的。
            // 因为左键是"按下即生效"，单击就走一遍 chord 会让玩家点一下已翻开的区域
            // 就把周围格子展开一片（数字与周围旗数相符时；插错旗的场合还会连带炸雷）。
            //
            // 实测证据（逐格左击扫描）：旧行为下一次扫描里 chord 被触发 16 次（=点到的数字格数），
            // 日志里能看到成串的 `chord 未执行：周围旗数与数字不符`；修复后一次都不再触发。
            //
            // 现在 chord 一共只有三个入口（全部要求"已翻开"）：
            //   ① 双击 = 第二次左键按下，由 IsChordSecondPress 判定（OnPointerPressed）
            //   ② 中键单击（OnPointerReleased 的中键分支）
            //   ③ 左右键同按（OnPointerPressed 的双键分支）
            // 双击的第二次按下**不会**经过这里：IsChordSecondPress 为真时直接走 TryChord。
            if (reveal.Reason is not null)
            {
                // 原版扫雷对这种"点了没反应"是完全静默的（§0 要求 100% 还原），
                // 而且点到自己刚翻开的格子是最常见的误操作——弹通知只会变成噪音。
                // 所以这里只写日志备查，不弹窗。
                NotifyQuiet(NotificationLevel.Warning, "这一格不能翻开", reveal.Reason, "board.reveal-blocked");
            }

            return;
        }

        RepaintAfterAction();
        BoardStateChanged?.Invoke(this, EventArgs.Empty);
        NotifyGameEndIfAny();

        // 音效按结果分三种：踩雷 / 胜利 / 普通翻开
        PlaySound(reveal.Outcome switch
        {
            RevealOutcome.Exploded => SoundEffect.Explosion,
            RevealOutcome.Won => SoundEffect.Victory,
            _ => SoundEffect.Reveal,
        });
    }

    /// <summary>本局刚结束时发一条对应级别的通知（胜利 / 踩雷）。</summary>
    private void NotifyGameEndIfAny()
    {
        if (_session is null)
        {
            return;
        }

        if (_session.Status == GameStatus.Won)
        {
            Notify(NotificationLevel.Success, MineSweeper.Core.Localization.Strings.Get("toast.win.title"), MineSweeper.Core.Localization.Strings.Get("toast.win.body", _session.ElapsedSecondsInt), "game.won");
        }
        else if (_session.Status == GameStatus.Lost)
        {
            Notify(NotificationLevel.Error, "踩到雷了", "本局失败，已展示全部雷位。", "game.lost");
        }
    }

    private void OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (_aiming && _hoverIndex >= 0)
        {
            int previous = _hoverIndex;
            _hoverIndex = -1;
            InvalidateCells(new[] { previous });
        }
    }

    private void OnDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        int index = HitTest(e.GetPosition(_root));

        // §1.5 的复位：双击棋盘"空白处"（不是任何格子）→ 适应窗口
        if (index < 0)
        {
            FitToWindow();
            NotifyQuiet(NotificationLevel.Info, "已适应窗口", "双击空白处可以复位视图。", "view.fitted");
        }

        // 注意：**双击落在格子上时这里什么都不做**。chord 改由"第二次左键按下"自己判定
        // （见 OnPointerPressed 里的 IsChordSecondPress），这样：
        //   ① 触发条件完全明确 —— 第二次按下必须落在**同一格**、且该格**已翻开**；
        //   ② 不再依赖 WinUI 的 DoubleTapped 时序（它的去抖/判定窗口不受我们控制）；
        //   ③ 自检里连按两次就走同一条路径，能被逐格扫描覆盖。
    }

    private void TryChord(int index)
    {
        if (_session is null || index < 0)
        {
            return;
        }

        if (LogInput)
        {
            Board b = _session.Board;
            MineSweeper.App.Services.AppPaths.Log(
                $"[CHORD] 双击已翻开格 index={index} 状态={DescribeCellStateForInput(index)} " +
                $"周围旗数={b.CountNeighborsWith(index, CellFlags.IsFlagged)} " +
                $"邻雷={b.GetAdjacentMines(index)} 已翻开={_session.RevealedCount}");
        }

        ChordResult chord = _session.Chord(index);

        if (chord.IsBlocked)
        {
            if (chord.Reason is not null && _session.Board.IsRevealed(index) && _session.Board.GetAdjacentMines(index) == 0)
            {
                // 0 格 chord 本就没有意义，静默忽略，避免刷通知
                return;
            }

            if (chord.Reason is not null)
            {
                // 只在"确实是一个数字格、只是旗数还没对上"时提示。
                // 邻雷=0 的空白格被双击属于无意义操作（Core 也会拒绝），静默忽略，
                // 否则玩家在空白区域随手双击就会被一串"chord 未执行"刷屏。
                if (_session.Board.GetAdjacentMines(index) >= 1)
                {
                    Notify(NotificationLevel.Warning, "chord 未执行", chord.Reason, "board.chord-blocked");
                }
            }

            return;
        }

        RepaintAfterAction();
        BoardStateChanged?.Invoke(this, EventArgs.Empty);
        ChordResolved?.Invoke(this, chord.OpenedCount);

        if (chord.Outcome == ChordOutcome.Exploded)
        {
            PlaySound(SoundEffect.Explosion);
            Notify(NotificationLevel.Error, "旗子插错了位置", "chord 踩到了没插旗的雷，本局失败。", "board.chord-exploded");
            return;
        }

        PlaySound(chord.Outcome == ChordOutcome.Won ? SoundEffect.Victory : SoundEffect.Chord);
    }

    // ------------------------------------------------------------ 滚轮（鼠标）
    //
    // 键盘已经彻底不参与游戏操作了，这里读取 Ctrl / Shift 只是**滚轮的修饰键**：
    // Ctrl+滚轮 缩放、Shift+滚轮 横向、滚轮 纵向。它们不会触发任何棋盘动作。

    private static bool IsControlDown() => IsKeyDown(VirtualKey.Control);

    private static bool IsShiftDown() => IsKeyDown(VirtualKey.Shift);

    private static bool IsKeyDown(VirtualKey key)
        => (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(key)
            & Windows.UI.Core.CoreVirtualKeyStates.Down) == Windows.UI.Core.CoreVirtualKeyStates.Down;

    private void OnPointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(_root);
        int delta = point.Properties.MouseWheelDelta;

        if (IsControlDown())
        {
            ZoomAt(point.Position, delta > 0 ? 1.15f : 1f / 1.15f);
        }
        else if (IsShiftDown())
        {
            PanBy(delta > 0 ? -90f : 90f, 0f);
        }
        else
        {
            PanBy(0f, delta > 0 ? -90f : 90f);
        }

        e.Handled = true;
    }

    // ------------------------------------------------------------ 无障碍（§3.2）

    /// <summary>棋盘的无障碍节点（自绘棋盘必须自己提供，这是方案 A 的既定代价）。</summary>
    protected override AutomationPeer OnCreateAutomationPeer() => new BoardViewAutomationPeer(this);

    /// <summary>本局是否还在进行（无障碍节点据此判断"能不能按"）。</summary>
    internal bool IsGameActiveForAutomation => _session is { IsFinished: false };

    /// <summary>
    /// 棋盘的无障碍名称：盘面摘要（尺寸 + 剩余雷数 + 胜负状态）。
    /// 键盘操作已整个移除，所以这里不再有"当前焦点格"这一段。
    /// </summary>
    internal string BuildBoardAutomationName()
    {
        if (_session is null)
        {
            return "扫雷棋盘（未开局）";
        }

        Board board = _session.Board;
        string summary = $"扫雷棋盘 {board.Width}行×{board.Height}列，剩余 {_session.RemainingMines} 雷";

        summary += _session.Status switch
        {
            GameStatus.Won => "，已胜利",
            GameStatus.Lost => "，已失败",
            _ => string.Empty,
        };

        return summary;
    }

    /// <summary>棋盘的操作说明（屏幕阅读器读帮助时用）。</summary>
    internal string BuildBoardAutomationHelpText()
        => "鼠标左键翻开，右键插旗（旗→问号→空），在已翻开的数字上左键等同于 chord；" +
           "右键点击物品栏图标进入放大镜瞄准态；Ctrl 加滚轮缩放，Shift 加滚轮横向滚动，" +
           "中键拖拽平移，双击棋盘空白处适应窗口。本棋盘只支持鼠标操作，不响应键盘。" +
           "每格的完整状态会作为名称读出。";

    /// <summary>
    /// 为一格生成可读描述，例如"第3行 第5列，已翻开，数字 2"。
    /// 这是文档 §3.2 明确要求屏幕阅读器能读出的信息。
    /// </summary>
    internal string DescribeCellForAutomation(int index)
    {
        if (_session is null)
        {
            return "棋盘尚未开始";
        }

        Board board = _session.Board;
        if (!board.IsValidIndex(index))
        {
            return "棋盘之外";
        }

        string position = $"第{board.YOf(index) + 1}行 第{board.XOf(index) + 1}列";
        bool revealed = board.IsRevealed(index);
        bool lost = _session.Status == GameStatus.Lost;

        if (board.IsFlagged(index))
        {
            return $"{position}，已插旗";
        }

        if (board.IsQuestionMarked(index))
        {
            return $"{position}，问号标记";
        }

        if (revealed)
        {
            if (board.IsMine(index))
            {
                return $"{position}，踩到的雷";
            }

            byte adjacent = board.GetAdjacentMines(index);
            return adjacent == 0
                ? $"{position}，已翻开，空"
                : $"{position}，已翻开，数字 {adjacent}";
        }

        if (board.IsItemRevealed(index))
        {
            if (board.IsMine(index))
            {
                return $"{position}，已透视：雷";
            }

            byte adjacent = board.GetAdjacentMines(index);
            return adjacent == 0
                ? $"{position}，已透视：空"
                : $"{position}，已透视：数字 {adjacent}";
        }

        if (lost && board.IsMine(index))
        {
            return $"{position}，雷";
        }

        return $"{position}，未翻开";
    }

    /// <summary>
    /// 生成可见区域的虚拟格无障碍节点。
    /// 设上限是必须的：1000×1000 棋盘有 100 万格，全部铺进无障碍树会把辅助技术卡死。
    /// </summary>
    internal List<AutomationPeer>? BuildCellAutomationPeers()
    {
        if (_session is null)
        {
            return null;
        }

        const int maxCells = 400;

        BoardViewport viewport = BuildViewport();
        var peers = new List<AutomationPeer>();

        for (int row = viewport.FirstRow; row <= viewport.LastRow && peers.Count < maxCells; row++)
        {
            for (int col = viewport.FirstCol; col <= viewport.LastCol && peers.Count < maxCells; col++)
            {
                peers.Add(new BoardCellAutomationPeer(this, _session.Board.Index(col, row)));
            }
        }

        return peers.Count == 0 ? null : peers;
    }

    /// <summary>某一格在无障碍坐标系里的矩形。</summary>
    internal Rect GetCellAutomationBounds(int index)
    {
        if (_session is null || !_session.Board.IsValidIndex(index))
        {
            return default;
        }

        BoardViewport viewport = BuildViewport();
        Rect rect = viewport.CellRect(_session.Board.XOf(index), _session.Board.YOf(index));

        try
        {
            // 与 FrameworkElementAutomationPeer 的做法一致：换算到 XAML 根坐标，
            // 由框架再转成屏幕坐标。
            return TransformToVisual(null).TransformBounds(rect);
        }
        catch (Exception)
        {
            return rect;
        }
    }

    // ------------------------------------------------------------ 命中测试与重绘

    /// <summary>屏幕坐标 → 格索引；不在任何格上时返回 -1。</summary>
    public int HitTest(Point position)
    {
        if (_session is null || _cellSize <= 0)
        {
            return -1;
        }

        Board board = _session.Board;

        int col = (int)Math.Floor((position.X - _offsetX) / _cellSize);
        int row = (int)Math.Floor((position.Y - _offsetY) / _cellSize);

        if (col < 0 || row < 0 || col >= board.Width || row >= board.Height)
        {
            return -1;
        }

        return board.Index(col, row);
    }

    /// <summary>请求整体重绘。</summary>
    public void RequestRedraw() => _canvas.Invalidate();

    /// <summary>只重绘若干格（§4.5：不要为了几格刷新整个屏幕）。</summary>
    public void InvalidateCells(params int[] indexes)
    {
        if (_session is null || indexes.Length == 0)
        {
            return;
        }

        Board board = _session.Board;
        BoardViewport viewport = BuildViewport();

        foreach (int index in indexes)
        {
            if (!board.IsValidIndex(index))
            {
                continue;
            }

            Rect rect = viewport.ClampToControl(viewport.CellRectForInvalidate(board.XOf(index), board.YOf(index), 1));
            if (rect.Width > 0 && rect.Height > 0)
            {
                _canvas.Invalidate(rect);
            }
        }
    }

    /// <summary>
    /// 动作结束后决定重绘范围（§4.5）。
    ///
    /// 这里修掉的是原实现最严重的一处 bug：翻开之后只失效了<b>被点击的那一格</b>，
    /// 而一次 <see cref="GameSession.Reveal"/> 可能通过洪水填充翻开一整片——
    /// 于是屏幕上只开一格、其余已经逻辑翻开的格子仍画成未翻开，
    /// 玩家再点它们会被"该格已翻开"拒绝，在玩家看来就是"点了完全没反应"。
    ///
    /// 现在的规则：胜利/失败要展示整体状态（失败要摊开全盘雷位）→ 整屏重绘；
    /// 其余情况按 Core 回传的改动区间精确重绘。
    /// </summary>
    private void RepaintAfterAction()
    {
        if (_session is null)
        {
            return;
        }

        if (_session.Status is GameStatus.Won or GameStatus.Lost)
        {
            RequestRedraw();
            return;
        }

        InvalidateLastChange();
    }

    /// <summary>
    /// 按 Core 记录的"本次操作改动过的格索引区间"重绘。
    /// 区间是真实改动集合的超集（洪水填充的索引区间天然连续），多画一点总是安全的。
    /// 返回是否真的重绘了东西。
    /// </summary>
    private bool InvalidateLastChange()
    {
        if (_session is null)
        {
            return false;
        }

        if (!_session.ConsumeLastAffectedRange(out int first, out int last))
        {
            return false;
        }

        Board board = _session.Board;
        int x0 = board.XOf(first);
        int y0 = board.YOf(first);
        int x1 = board.XOf(last);
        int y1 = board.YOf(last);

        InvalidateCellRange(Math.Min(x0, x1), Math.Min(y0, y1), Math.Max(x0, x1), Math.Max(y0, y1));
        return true;
    }

    /// <summary>
    /// 供 UI 定时器在分帧展开推进后调用：重绘最近一次改动区域。
    /// 返回 false 表示没有可重绘的区间，调用方可以退回整屏重绘。
    /// </summary>
    public bool RepaintLastChangedArea() => InvalidateLastChange();

    /// <summary>重绘"格矩形 + 一圈余量"，保证格子描边与数字不被裁掉。</summary>
    private void InvalidateCellRange(int col0, int row0, int col1, int row1)
    {
        if (_session is null || _cellSize <= 0)
        {
            return;
        }

        BoardViewport viewport = BuildViewport();
        var rect = new Rect(
            viewport.OffsetX + ((col0 - 1) * viewport.CellSize),
            viewport.OffsetY + ((row0 - 1) * viewport.CellSize),
            (col1 - col0 + 3) * viewport.CellSize,
            (row1 - row0 + 3) * viewport.CellSize);

        rect = viewport.ClampToControl(rect);
        if (rect.Width > 0 && rect.Height > 0)
        {
            _canvas.Invalidate(rect);
        }
    }

    /// <summary>
    /// 重绘"以某格为锚点、按形状覆盖"的那一片区域（放大镜透视结果与高亮都用它）。
    ///
    /// 用形状而不是半径：v1.4 的 2×2 是"点中的格子当左上角"，
    /// 按半径居中算会漏掉右下角那一格，导致高亮框边上留残影。
    /// </summary>
    private void InvalidateCellShape(int anchorIndex, MineSweeper.Core.Items.AreaShape shape)
    {
        if (_session is null || !_session.Board.IsValidIndex(anchorIndex))
        {
            return;
        }

        Board board = _session.Board;
        int x = board.XOf(anchorIndex);
        int y = board.YOf(anchorIndex);

        int x0 = x - shape.AnchorX;
        int y0 = y - shape.AnchorY;

        // 贴边时形状会被裁切，但至少要包含被点中的那一格
        InvalidateCellRange(
            Math.Clamp(Math.Min(x0, x), 0, board.Width - 1),
            Math.Clamp(Math.Min(y0, y), 0, board.Height - 1),
            Math.Clamp(Math.Max(x0 + shape.Width - 1, x), 0, board.Width - 1),
            Math.Clamp(Math.Max(y0 + shape.Height - 1, y), 0, board.Height - 1));
    }

    // ------------------------------------------------------------ 透视高亮的淡出

    /// <summary>当前高亮强度（1 → 0）；没有高亮时为 0。</summary>
    public float HighlightAlpha
    {
        get
        {
            if (_highlightCenter < 0)
            {
                return 0f;
            }

            double elapsed = (DateTimeOffset.UtcNow - _highlightStartedAt).TotalSeconds;
            return elapsed >= HighlightDuration.TotalSeconds
                ? 0f
                : (float)(1.0 - (elapsed / HighlightDuration.TotalSeconds));
        }
    }

    /// <summary>当前需要高亮的中心格（-1 表示无）。</summary>
    public int HighlightCenter => _highlightCenter;

    /// <summary>
    /// 由 UI 定时器逐帧驱动，让 §5.4 要求的"结算瞬间高亮框 1 秒淡出"真的动起来。
    /// 返回 true 表示还有高亮在衰减（UI 需要继续重绘）。
    /// </summary>
    public bool AdvanceHighlight()
    {
        if (_highlightCenter < 0)
        {
            return false;
        }

        int center = _highlightCenter;
        bool stillActive = HighlightAlpha > 0f;

        if (!stillActive)
        {
            _highlightCenter = -1;
        }

        InvalidateCellShape(center, _session?.MagnifierShape ?? MineSweeper.Core.Items.AreaShape.TopLeft(2, 2));
        return stillActive;
    }

    private BoardViewport BuildViewport()
    {
        float width = (float)_root.ActualWidth;
        float height = (float)_root.ActualHeight;

        if (_session is null || _cellSize <= 0 || width <= 0 || height <= 0)
        {
            return new BoardViewport(BoardTheme.DefaultCellSize, 0, 0, width, height, 0, -1, 0, -1);
        }

        Board board = _session.Board;

        int firstCol = Math.Max(0, (int)Math.Floor((-_offsetX) / _cellSize));
        int lastCol = Math.Min(board.Width - 1, (int)Math.Floor((width - _offsetX) / _cellSize));
        int firstRow = Math.Max(0, (int)Math.Floor((-_offsetY) / _cellSize));
        int lastRow = Math.Min(board.Height - 1, (int)Math.Floor((height - _offsetY) / _cellSize));

        return new BoardViewport(_cellSize, _offsetX, _offsetY, width, height, firstCol, lastCol, firstRow, lastRow);
    }

    private void OnCreateResources(CanvasVirtualControl sender, Microsoft.Graphics.Canvas.UI.CanvasCreateResourcesEventArgs args)
    {
        // 本项目全部为矢量自绘，没有位图资源需要预载（§1.2.1 ④：自绘路线无第三方资源版权问题）
    }

    private void OnRegionsInvalidated(CanvasVirtualControl sender, CanvasRegionsInvalidatedEventArgs args)
    {
        if (_session is null)
        {
            return;
        }

        BoardViewport viewport = BuildViewport();

        // CanvasVirtualControl 按区域给绘制会话：只为真正失效的矩形开一个 session，
        // 这样"用放大镜只重绘 3×3"这类小范围更新不会拖累整屏（§4.5）。
        foreach (Rect region in args.InvalidatedRegions)
        {
            using (CanvasDrawingSession drawingSession = sender.CreateDrawingSession(region))
            {
                BoardRenderer.Draw(
                    drawingSession,
                    _canvas,
                    _theme,
                    _session,
                    viewport,
                    _aiming ? _hoverIndex : _mouseCell,
                    _aiming,
                    _highlightCenter,
                    HighlightAlpha,
                    _heatmapEnabled);
            }
        }
    }
}
