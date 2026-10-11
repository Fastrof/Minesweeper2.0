using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using MineSweeper.App.Rendering;
using MineSweeper.App.Services;
using MineSweeper.App.ViewModels;
using MineSweeper.App.Views;
using MineSweeper.Core;
using MineSweeper.Core.Audio;
using MineSweeper.Core.History;
using MineSweeper.Core.Items;
using MineSweeper.Core.Persistence;
using MineSweeper.Core.Levels;
using MineSweeper.Core.Notifications;
using MineSweeper.Core.Settings;
using MineSweeper.Core.Themes;
using Windows.Graphics;

namespace MineSweeper.App;

/// <summary>
/// 主窗口：状态栏（§4.4）、物品栏（§5.3）、设置与难度入口，以及计时器与分帧推进的驱动。
///
/// 窗口里<b>没有</b>任何提示条：所有"要告诉玩家的事"都通过 <see cref="ToastNotifier"/>
/// 以 Windows 通知发出，级别由消息产生方决定。
/// </summary>
public sealed partial class MainWindow : Window
{
    private readonly AppStorage _storage;
    private readonly ToastNotifier _notifier;
    private readonly BoardView _board = new();
    private readonly SoundPlayer _sound;
    private readonly DispatcherQueueTimer _clock;

    private int _lastMagnifierCount;
    private bool _gameOverHandled;

    // ---------------------------------------------------------------- 历史记录（v0.6）

    /// <summary>历史记录存档。它只活在 %LOCALAPPDATA%\MineSweeper2\history.json 里：删掉就从头开始。</summary>
    private readonly HistoryStore _historyStore = new(AppPaths.HistoryFile);

    private PlayHistory _history = PlayHistory.CreateDefault();

    /// <summary>本局真正开始的时刻（第一次翻开格子）；null = 还没开始。</summary>
    private DateTimeOffset? _sessionStartedAt;

    /// <summary>本局开始时的"正确标记"基线（算本局扫了几颗雷用）。</summary>
    private int _correctFlagsAtSessionStart;

    /// <summary>本局是否已经并进历史（防止重复累计）。</summary>
    private bool _historyRecordedForSession;

    /// <summary>本局关卡是否已经结算过（防止同一局重复记成绩）。</summary>
    private bool _levelSettled;

    /// <summary>本局关卡的结算是否**完整走完**（见 SettleLevel 末尾的说明）。</summary>
    private bool _levelSettleCompleted;

    /// <summary>本局关卡里玩家是否真的动过手（挡"开局即胜利"这种不算数的胜利）。</summary>
    private bool _levelPlayerActed;

    /// <summary>
    /// 设置控件是否已经可以响应变更。
    ///
    /// 初始为 false，构造完成前一律忽略——这一点必须小心：XAML 里
    /// <c>IsOn="True"</c> / <c>Value="70"</c> 这类赋值会在 <c>InitializeComponent()</c>
    /// <b>解析期间</b>就触发 Toggled/ValueChanged，而那时 <c>_sound</c> 还没赋值，
    /// 处理这些事件会直接抛异常把启动打崩（实测踩到过）。
    /// </summary>
    private bool _settingsReady;

    /// <summary>是否还在等"设备就绪后跑一次性能自检"。</summary>
    private bool _perfProbePending;

    public MainWindow(ToastNotifier notifier)
    {
        _notifier = notifier ?? throw new ArgumentNullException(nameof(notifier));

        _storage = AppPaths.CreateStorage();
        ViewModel = new GameViewModel(_storage, _notifier);

        // x:Bind 在 InitializeComponent 里求值，ViewModel 必须先就位
        InitializeComponent();

        // 音效播放器：目录不存在时会由代码合成 5 个 wav 进去（零素材）
        _sound = new SoundPlayer(AppPaths.SoundsDirectory);

        Title = "扫雷 2.0";
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1180, 900));

        // 任务栏 / 标题栏图标。exe 里已经嵌了同名图标（csproj 的 ApplicationIcon），
        // 这里再显式设一次，保证 unpackaged 运行、以及从别的目录启动时也一致。
        try
        {
            if (System.IO.File.Exists(AppPaths.AppIconFile))
            {
                AppWindow.SetIcon(AppPaths.AppIconFile);
            }
        }
        catch (Exception ex)
        {
            AppPaths.Log($"设置窗口图标失败：{ex.GetType().Name}：{ex.Message}");
        }

        BoardHost.Children.Add(_board);

        // 把"棋盘这一行真正有多高"告诉棋盘。必须主动告知，不能只靠 MaxHeight/Clip：
        // 棋盘里是 Win2D 画布，会一直画到自己的内容边界，限制控件尺寸挡不住它，
        // 结果就是小盘（被 MaxFitCellSize=44 放大后）盖住底部的按钮与关卡导航行。
        void TellBoardItsHeight()
        {
            if (BoardHost.ActualHeight <= 0)
            {
                return;
            }

            _board.AvailableHeight = BoardHost.ActualHeight;
            _board.FitToWindow();
        }

        BoardHost.SizeChanged += (_, _) => TellBoardItsHeight();
        _board.NotificationRequested += OnBoardNotificationRequested;
        _board.SoundRequested += OnBoardSoundRequested;
        _board.BoardStateChanged += OnBoardStateChanged;
        _board.MagnifierPreviewChanged += OnMagnifierPreviewChanged;
        _board.SonarUsed += OnSonarUsed;

        // 16ms 一跳：既刷新计时显示，也作为分帧展开的推进节拍。
        // 真正的限速在 GameViewModel.Tick 的时间片里，所以这里不需要再放大间隔。
        _clock = DispatcherQueue.CreateTimer();
        _clock.Interval = TimeSpan.FromMilliseconds(16);
        _clock.Tick += OnClockTick;
        _clock.Start();

        Activated += OnWindowActivated;
        Closed += OnWindowClosed;


        // 主题与音效：先把设置应用到渲染器与播放器，再开第一局
        ApplySettings();


        // §5.5 ⑤ 默认开局：纯经典模式（无道具、可进最快记录）
        StartPreset(DifficultyPreset.Beginner);
        _notifier.Publish(GameNotification.QuietInfo(
            "扫雷 2.0 已就绪",
            "左键翻开 · 右键插旗（旗→问号→空）· 双击数字格 chord。首点保证安全。",
            "app.ready"));

        // 构建标识：排查"改了没生效 / 跑的到底是哪一份 exe"这类问题时的第一手证据。
        // 带上 exe 的时间戳，一眼就能看出跑的是不是刚编译的那份。
        AppPaths.Log($"[构建] v{AboutInfo.Version} · exe={BuildStampOfThisProcess()}");

        // 启动铃声：和原版扫雷一样，进游戏先响一声（v0.8）。
        // 放在"第一局已经开好"之后，避免和开局音效抢同一个音频设备。
        PlayStartupSound("程序启动");

        // 构造全部完成：从这里开始，设置控件的变更才是"玩家真的改了设置"
        _settingsReady = true;

        // 窗口变宽变窄要重排关卡列表（列数跟着可用宽度走，见 LevelListColumns）。
        // 注意两点，否则会自己绕成死循环：
        //   ① 只在"跨过列数阈值"时才重建（记住上一次用的列数），不是每次尺寸变化都重建；
        //   ② 重建会改变子元素 → 又触发 SizeChanged，所以排到下一轮 DispatcherQueue 再执行。
        LevelListPanel.SizeChanged += (_, _) =>
        {
            if (_levelCheckMode is not null || _levelListRefreshQueued)
            {
                return;
            }

            if (LevelPanel.Visibility == Visibility.Visible && LevelListColumns() != _levelListColumns)
            {
                _levelListRefreshQueued = true;
                DispatcherQueue.TryEnqueue(() =>
                {
                    _levelListRefreshQueued = false;
                    RefreshLevelList();
                });
            }
        };

        HistoryLoadResult historyLoad = _historyStore.Load();
        _history = historyLoad.History;
        AppPaths.Log($"[历史] 读取完成：状态={historyLoad.Status}，已扫 {_history.TotalMinesSwept} 颗，累计用时 {PlayHistory.FormatDuration(_history.TotalPlayedSeconds)}。");
        if (historyLoad.HasWarning)
        {
            AppPaths.Log($"[历史] 注意：{historyLoad.Message}");
        }

        StartPresetCheckIfRequested();
        StartToastBurstCheckIfRequested();
        StartItemCheckIfRequested();
        ClearStaleNotificationsAsync();
        StartPerfProbeIfRequested();
        StartLevelCheckIfRequested();

        StartNotifyCheckIfRequested();
        StartAboutCheckIfRequested();
        StartNewGameCheckIfRequested();
    }

    /// <summary>视图模型（XAML 里 x:Bind 用）。</summary>
    public GameViewModel ViewModel { get; }

    // ------------------------------------------------------------ 通知

    private void OnBoardNotificationRequested(object? sender, GameNotification notification)
        => _notifier.Publish(notification);

    private void OnBoardSoundRequested(object? sender, SoundEffect effect) => _sound.Play(effect);

    // ------------------------------------------------------------ 性能自检（文档 M2 验收标准）

    /// <summary>
    /// 启动时带 SAOLEI_PERF=1 就自动跑一次完整自检，结果写进 %LOCALAPPDATA%\MineSweeper2\app.log。
    /// 保留这个开关是因为"1000×1000 缩放到全景卡不卡"只能实测，命令行走一遍最省事。
    /// </summary>
    private void StartPerfProbeIfRequested()
        => _perfProbePending = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SAOLEI_PERF"));

    private void OnPerfProbeClick(object sender, RoutedEventArgs e) => RunPerfProbe(full: false);

    private void RunPerfProbe(bool full)
    {
        try
        {
            RenderPerfReport report = full
                ? RenderPerfProbe.Run(_board.ResourceCreator, 1200f, 800f, System.IO.Path.Combine(AppPaths.DataDirectory, "perf"))
                : RenderPerfProbe.RunQuick(_board.ResourceCreator, 1200f, 800f);

            AppPaths.Log(report.ToLogText());

            RenderPerfSample worst = report.FrameSamples.Count > 0
                ? report.FrameSamples.OrderByDescending(s => s.MsPerFrame).First()
                : default;

            _notifier.Publish(GameNotification.Info(
                "性能自检完成",
                report.FrameSamples.Count > 0
                    ? $"最慢档位 {worst.MsPerFrame:F1} ms/帧（{worst.VisibleCells:N0} 格可见），详情见 %LOCALAPPDATA%\\MineSweeper2\\app.log。"
                    : "详情见 %LOCALAPPDATA%\\MineSweeper2\\app.log。",
                "perf.done"));
        }
        catch (Exception ex)
        {
            AppPaths.Log($"性能自检失败：{ex.GetType().Name}：{ex.Message}");
            _notifier.Publish(GameNotification.Error("性能自检失败", ex.Message, "perf.failed"));
        }
    }

    // ------------------------------------------------------------ 主题与音效设置

    /// <summary>把 settings.json 里的设置应用到渲染器、播放器与设置控件。</summary>
    private void ApplySettings()
    {
        AppSettings settings = _storage.Settings;

        // 读设置会写控件值，控件值变化又会触发"改设置"事件；这里临时关掉，读完再恢复
        bool previous = _settingsReady;
        _settingsReady = false;
        try
        {
            if (ThemeCombo.Items.Count == 0)
            {
                foreach (BoardPalette palette in BoardPalette.BuiltIn)
                {
                    ThemeCombo.Items.Add(palette.DisplayName);
                }
            }

            _board.ApplyTheme(BoardTheme.FromPalette(settings.ResolvePalette()));
            _board.ApplyHeatmap(settings.ShowMineDensityHeatmap);

            AudioToggle.IsOn = settings.AudioEnabled;
            VolumeSlider.Value = Math.Round(settings.AudioVolume * 100);
            _sound.Apply(settings.AudioEnabled, settings.AudioVolume);

            HeatmapToggle.IsOn = settings.ShowMineDensityHeatmap;
            LockedBoardToggle.IsOn = settings.LockedBoard;

            int index = IndexOfTheme(settings.ThemeId);
            ThemeCombo.SelectedIndex = index >= 0 ? index : 0;
        }
        finally
        {
            _settingsReady = previous;
        }
    }

    private static int IndexOfTheme(string? themeId)
    {
        if (string.IsNullOrWhiteSpace(themeId))
        {
            return 0;
        }

        for (int i = 0; i < BoardPalette.BuiltIn.Count; i++)
        {
            if (string.Equals(BoardPalette.BuiltIn[i].Id, themeId, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    private void SaveSettings() => _storage.SettingsStore.Save(_storage.Settings);

    private void OnThemeChanged(object sender, Microsoft.UI.Xaml.Controls.SelectionChangedEventArgs e)
    {
        if (!_settingsReady)
        {
            return;
        }

        int index = ThemeCombo.SelectedIndex;
        if (index < 0 || index >= BoardPalette.BuiltIn.Count)
        {
            return;
        }

        BoardPalette palette = BoardPalette.BuiltIn[index];
        _storage.Settings.ThemeId = palette.Id;

        // 换主题时清掉逐项颜色覆盖：那些覆盖是给上一套主题调的，留着会让新主题看起来是坏的
        _storage.Settings.ThemeOverrides.Clear();

        _board.ApplyTheme(BoardTheme.FromPalette(_storage.Settings.ResolvePalette()));
        SaveSettings();

        _notifier.Publish(GameNotification.QuietInfo("已切换主题", palette.DisplayName, "settings.theme"));
    }

    private void OnAudioToggled(object sender, RoutedEventArgs e)
    {
        if (!_settingsReady)
        {
            return;
        }

        _storage.Settings.AudioEnabled = AudioToggle.IsOn;
        _sound.Apply(_storage.Settings.AudioEnabled, _storage.Settings.AudioVolume);
        SaveSettings();
    }

    private void OnVolumeChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (!_settingsReady)
        {
            return;
        }

        _storage.Settings.AudioVolume = Math.Clamp(e.NewValue / 100d, 0d, 1d);
        _sound.Apply(_storage.Settings.AudioEnabled, _storage.Settings.AudioVolume);
        SaveSettings();
    }

    private void OnPreviewSoundClick(object sender, RoutedEventArgs e) => _sound.Play(SoundEffect.Reveal);

    /// <summary>
    /// 播放"启动铃声"（v0.8）。
    ///
    /// 用在两个时机：① 程序启动；② 玩家点初级/中级/高级切换难度时。
    /// 关卡局、按种子复现、点笑脸重开等**不算"换盘面"**，所以不响——
    /// 那些操作本身已经有翻开/结算音效，再叠一声启动音只会吵。
    /// </summary>
    private void PlayStartupSound(string reason)
    {
        _sound.Play(SoundEffect.Startup);
        AppPaths.Log($"[音效] 播放启动铃声（{reason}）。");
    }

    private void OnHeatmapToggled(object sender, RoutedEventArgs e)
    {
        if (!_settingsReady)
        {
            return;
        }

        _storage.Settings.ShowMineDensityHeatmap = HeatmapToggle.IsOn;
        _board.ApplyHeatmap(HeatmapToggle.IsOn);
        SaveSettings();
    }

    private void OnLockedBoardToggled(object sender, RoutedEventArgs e)
    {
        if (!_settingsReady)
        {
            return;
        }

        _storage.Settings.LockedBoard = LockedBoardToggle.IsOn;
        SaveSettings();

        _notifier.Publish(GameNotification.QuietInfo(
            LockedBoardToggle.IsOn ? "已开启锁定盘面" : "已关闭锁定盘面",
            LockedBoardToggle.IsOn
                ? "之后开的每一局都先按种子布雷再取安全首点：同一个种子必定是同一张盘。"
                : "回到普通模式：同一个种子换个首点会是另一张盘。",
            "settings.locked"));
    }

    /// <summary>放大镜信息面板（§5.6 备选）：列出即将透视的 3×3 区域的坐标与内容。</summary>
    private void OnMagnifierPreviewChanged(object? sender, EventArgs e)
    {
        MagnifierReveal? preview = _board.MagnifierPreview;

        MagnifierPreviewText.Text = preview is not null && ViewModel.Session is not null
            ? preview.ToPanelText(ViewModel.Session.Board)
            : string.Empty;

        MagnifierPanel.Visibility = string.IsNullOrEmpty(MagnifierPreviewText.Text)
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    // ------------------------------------------------------------ 游戏生命周期

    private void StartPreset(DifficultyPreset preset)
    {
        ViewModel.StartPreset(preset, CurrentOptions(), TryReadSeed());

        AttachSession();
        _notifier.Publish(GameNotification.QuietInfo(
            $"新局：{preset.DisplayName}",
            $"{preset.Width}×{preset.Height} / {preset.MineCount} 雷 · {ViewModel.ModeText}",
            "game.new"));
    }

    private void AttachSession()
    {
        // 新的一局要开始了：上一局如果没走完结算，先把它的用时/成果并进历史
        FlushHistoryForCurrentSession(sessionEnded: true);

        _sessionStartedAt = null;
        _correctFlagsAtSessionStart = ViewModel.ActiveInventory.Progress.CorrectMineFlagsTotal;
        _historyRecordedForSession = false;
        _gameOverHandled = false;
        _levelSettled = false;
        _levelSettleCompleted = false;
        _levelPlayerActed = false;
        _lastMagnifierCount = ViewModel.Inventory.MagnifierCount;
        UpdateLevelGoalBar();

        // 新的一局开始了：把上一局的结算卡片与踩雷窗口都收掉，并刷新关卡面板的前后跳按钮
        HideSettlementOverlay();
        HideDefeatOverlay();
        SonarOverlay.Visibility = Visibility.Collapsed;
        RefreshItemChoiceOverlay();
        UpdateMagnifierSlot();   // 新局开始：三格道具的亮度按"有没有货 + 这局还能不能用"重算
        RefreshLevelList();

        if (ViewModel.Session is null)
        {
            return;
        }

        // 窗口跟着棋盘走：9×9 不再开出一个空荡荡的大窗口，大棋盘也能一眼看全
        FitWindowToBoard(ViewModel.Session);

        _board.AttachSession(ViewModel.Session);
        UpdateMagnifierSlot();
        OnMagnifierPreviewChanged(this, EventArgs.Empty);
    }

    /// <summary>
    /// 当前进程跑的 exe 的构建标识（版本 + 文件时间 + 大小）。
    /// 用来回答"我改了到底有没有生效"——对比源码时间与 exe 时间即可。
    /// </summary>
    private static string BuildStampOfThisProcess()
    {
        try
        {
            string path = Environment.ProcessPath ?? string.Empty;
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                return "(取不到路径)";
            }

            var info = new FileInfo(path);
            string exeVersion = System.Reflection.Assembly
                .GetEntryAssembly()?.GetName().Version?.ToString() ?? "?";
            return $"{info.Name} {info.LastWriteTime:yyyy-MM-dd HH:mm:ss} {info.Length / 1024}KB · " +
                   $"程序集版本={exeVersion}";
        }
        catch (Exception ex)
        {
            return $"(读取失败 {ex.GetType().Name})";
        }
    }

    /// <summary>当前界面上选定的开局选项（§5.5 ⑤ / A-2 / A-4 / §2.6）。</summary>
    private GameStartOptions CurrentOptions() => new(
        // v0.6：道具不再需要玩家在设置里开关 —— 统一按"经典 + 道具"开局
        GameMode.ClassicWithItems,
        FirstClickOpening: FirstClickOpeningSwitch.IsOn,
        PauseTimerOnFocusLoss: PauseTimerSwitch.IsOn,
        LockedBoard: LockedBoardToggle.IsOn);

    private int? TryReadSeed()
        => int.TryParse(SeedBox.Text?.Trim(), out int seed) ? seed : null;

    private void OnClockTick(DispatcherQueueTimer sender, object args)
    {
        // 性能自检要等 Win2D 设备就绪（棋盘已经画过一帧）
        if (_perfProbePending && _board.IsDeviceReady)
        {
            _perfProbePending = false;
            RunPerfProbe(full: true);
        }

        if (ViewModel.Tick())
        {
            // 分帧展开推进了：优先按改动区间精确重绘，拿不到区间就整屏兜底
            if (!_board.RepaintLastChangedArea())
            {
                _board.RequestRedraw();
            }
        }

        // §5.4 的透视高亮靠这里逐帧衰减，动画才动得起来（内部自己负责失效重绘）
        _board.AdvanceHighlight();

        // §5.4 备选：透视信息到期后恢复迷雾（默认永久保留，配 config.json 才生效）
        if (ViewModel.Session?.ExpireItemReveals() == true)
        {
            _board.RepaintLastChangedArea();
        }

        if (ViewModel.Session is { Status: GameStatus.Won or GameStatus.Lost } && !_gameOverHandled)
        {
            _gameOverHandled = true;
            HandleGameOver();
            _board.RequestRedraw();
        }

        UpdateMagnifierSlot();

        // 关卡模式自检（环境变量 SAOLEI_LEVELS，仅用于无法注入输入时的验证）
        AdvanceLevelCheck();

        // 通知精简自检（环境变量 SAOLEI_NOTIFY）
        if (_notifyCheckPending && _board.IsDeviceReady)
        {
            _notifyCheckPending = false;
            RunNotifyCheck();
        }

        // 通知"替换而不是排队"自检（环境变量 SAOLEI_TOASTBURST）
        if (_toastBurstPending && _board.IsDeviceReady)
        {
            _toastBurstPending = false;
            RunToastBurstCheckAsync();
        }

        // 模拟"点笑脸重开"（环境变量 SAOLEI_NEWGAME）：验证"重开沿用当前难度"与"最大化不被还原"
        // 道具自检（环境变量 SAOLEI_ITEMS）
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SAOLEI_HISTORY")) && _board.IsDeviceReady && !_historyCheckDone)
        {
            _historyCheckDone = true;
            RunHistoryCheck();
        }

        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SAOLEI_FLAG20")) && _board.IsDeviceReady && !_flag20Done)
        {
            _flag20Done = true;
            RunFlag20Check();
        }

        if (_itemCheckPending && _board.IsDeviceReady)
        {
            RunItemCheck();
        }

        // v0.8 新弹窗自检（环境变量 SAOLEI_UI=sonar|defeat）
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SAOLEI_UI")) && _board.IsDeviceReady && !_uiCheckDone)
        {
            _uiCheckDone = true;
            RunUiCheck();
        }

        if (_newGameCheckPending && _board.IsDeviceReady && DateTimeOffset.Now >= _newGameCheckDueAt)
        {
            _newGameCheckPending = false;

            if (AppWindow.Presenter is OverlappedPresenter presenter)
            {
                AppPaths.Log($"[NEWGAME] 重开前窗口状态 = {presenter.State}");
            }

            AppPaths.Log($"[NEWGAME] 重开前 = {DescribeCurrentGame()}");

            // 走的就是小黄脸按钮那条路径，不是另写一份逻辑
            RestartCurrentGame();

            AppPaths.Log($"[NEWGAME] 重开后 = {DescribeCurrentGame()}");

            if (AppWindow.Presenter is OverlappedPresenter after)
            {
                AppPaths.Log($"[NEWGAME] 重开后窗口状态 = {after.State}");
            }
        }

        // 关于页面自检（环境变量 SAOLEI_ABOUT）
        if (_aboutCheckMode is not null && _board.IsDeviceReady)
        {
            string aboutMode = _aboutCheckMode;
            _aboutCheckMode = null;
            RunAboutCheck(aboutMode);
        }
    }

    private void OnBoardStateChanged(object? sender, EventArgs e)
    {
        _levelPlayerActed = true;

        // 第一次翻开 = "第一次开始扫雷"：记下起点、基线，并累计一次对局
        if (ViewModel.Session is { Status: GameStatus.InProgress } started && _sessionStartedAt is null)
        {
            _sessionStartedAt = DateTimeOffset.UtcNow;
            _correctFlagsAtSessionStart = ViewModel.ActiveInventory.Progress.CorrectMineFlagsTotal;
            _history.BeginGame(_sessionStartedAt.Value);
            _historyStore.Save(_history);
            UpdateItemSlots();
        }
        ViewModel.Refresh();
        UpdateMagnifierSlot();
        // 攒够一颗雷就可能多一次奖励：这里必须刷新，否则"自动弹出三选一"就成了空话
        RefreshItemChoiceOverlay();

        // 道具数量一变就立刻落盘（三种都盯；关卡里用掉的也算，因为 v0.6 起两者共用同一份存档）
        int bagTotal = ViewModel.Inventory.MagnifierCount + ViewModel.Inventory.SonarCount + ViewModel.Inventory.FreezeCount;
        if (bagTotal != _lastMagnifierCount)
        {
            _lastMagnifierCount = bagTotal;
            ViewModel.SaveProgress();
        }

        if (ViewModel.Session is { Status: GameStatus.Won or GameStatus.Lost } && !_gameOverHandled)
        {
            _gameOverHandled = true;
            HandleGameOver();
        }
    }

    /// <summary>一局结束后的收尾：关卡局走关卡结算，普通局走记录提交。</summary>
    private void HandleGameOver()
    {
        if (ViewModel.IsLevelRun)
        {
            SettleLevel();
            FlushHistoryForCurrentSession(sessionEnded: true);
            return;
        }

        ViewModel.SubmitRecordIfNeeded();
        FlushHistoryForCurrentSession(sessionEnded: true);

        // 普通局踩雷：弹出"重开这一盘 / 退出游戏"的小窗口（v0.8）。
        // 关卡局有自己的结算卡片（失败时也会提示重开/返回），所以不叠加两层。
        if (ViewModel.Session is { Status: GameStatus.Lost })
        {
            ShowDefeatOverlay();
        }
    }

    // ------------------------------------------------------------ 踩雷失败弹窗（v0.8）

    /// <summary>踩雷后弹出的小窗口：让玩家直接选"重新开始这一盘"或"退出游戏"。</summary>
    private void ShowDefeatOverlay()
    {
        DefeatSummary.Text =
            $"{ViewModel.Session?.Board.Width}×{ViewModel.Session?.Board.Height} · " +
            $"{ViewModel.Session?.Board.MineCount} 雷 · 用时 {ViewModel.Session?.ElapsedSecondsInt ?? 0} 秒\n" +
            "重新开始会换一张新盘。";

        DefeatRetryButton.Content = "重新开始这一盘";
        DefeatOverlay.Visibility = Visibility.Visible;
        AppPaths.Log("[DEFEAT] 已弹出失败窗口（重新开始这一盘 / 退出游戏）");
    }

    private void HideDefeatOverlay() => DefeatOverlay.Visibility = Visibility.Collapsed;

    private void OnDefeatRetryClick(object sender, RoutedEventArgs e)
    {
        HideDefeatOverlay();

        // 沿用笑脸按钮那条路径：关卡局重开同一关，普通局沿用**这一局真实生效的**盘面参数。
        RestartCurrentGame();
        AppPaths.Log("[DEFEAT] 玩家选择：重新开始这一盘");
    }

    private void OnDefeatExitClick(object sender, RoutedEventArgs e)
    {
        AppPaths.Log("[DEFEAT] 玩家选择：退出游戏");
        Close();
    }

    // ------------------------------------------------------------ 声呐结果弹窗（v0.8）

    /// <summary>
    /// 声呐扫描成功后弹"需要手动确认"的小窗口。
    /// 蓝色瞄准框一退出就没了，这张卡片负责让玩家事后还能看清"刚扫的是哪一块、几颗雷"。
    /// </summary>
    private void OnSonarUsed(object? sender, SonarScanResult scan)
    {
        if (ViewModel.Session is not { } session)
        {
            return;
        }

        Board board = session.Board;
        int size = (scan.Radius * 2) + 1;
        int centerRow = board.YOf(scan.CenterIndex) + 1;
        int centerCol = board.XOf(scan.CenterIndex) + 1;
        int fromRow = centerRow - scan.Radius;
        int toRow = centerRow + scan.Radius;
        int fromCol = centerCol - scan.Radius;
        int toCol = centerCol + scan.Radius;

        SonarTitle.Text = scan.MineCount == 0
            ? "声呐扫描：这一片是空的"
            : $"声呐扫描：这一片有 {scan.MineCount} 颗雷";

        // 把"哪几行哪几列"写清楚——这正是玩家事后最容易忘的信息
        SonarArea.Text =
            $"区域：第 {fromRow}~{toRow} 行 × 第 {fromCol}~{toCol} 列（{size}×{size}）\n" +
            $"中心：第 {centerRow} 行 第 {centerCol} 列\n" +
            "只报数量，不显示雷的位置。";

        SonarRemaining.Text = $"剩余声呐扫描 ×{scan.RemainingCount}";
        SonarOverlay.Visibility = Visibility.Visible;
    }

    private void OnSonarCloseClick(object sender, RoutedEventArgs e)
        => SonarOverlay.Visibility = Visibility.Collapsed;

    private void OnWindowActivated(object sender, WindowActivatedEventArgs args)
    {
        bool active = args.WindowActivationState != WindowActivationState.Deactivated;
        // 注意：这里**不再**把键盘焦点交给棋盘。棋盘现在完全只认鼠标，
        // 不给它焦点也就不会出现那个蓝色的焦点小方框（见 README §6.1）。
        ViewModel.Session?.SetWindowFocused(active);
    }

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        CloseAboutWindows();
        ViewModel.SaveProgress();
        SaveSettings();
        _sound.Dispose();
        AppPaths.Log("窗口关闭，进度与设置已落盘。");
    }

    // ------------------------------------------------------------ 状态栏 / 物品栏

    /// <summary>
    /// 物品栏图标置灰（§5.3 / §5.5 ①）。
    /// 直接绑"此刻是否真的能用"：数量为 0、模式不含道具、首点之前、本局已结束时都是同一套置灰视觉。
    /// </summary>
    private void UpdateMagnifierSlot()
    {
        double opacity = ViewModel.IsMagnifierUsableNow ? 1.0 : 0.4;
        if (Math.Abs(MagnifierSlot.Opacity - opacity) > 0.001)
        {
            MagnifierSlot.Opacity = opacity;
        }
    }

    // ------------------------------------------------------------ 事件处理

    private void OnSmileyClick(object sender, RoutedEventArgs e) => RestartCurrentGame();

    /// <summary>
    /// 小黄脸 = "重开这一局"：**沿用当前这一局的参数**重新开始。
    ///
    /// 以前这里写死的是 <c>StartPreset(DifficultyPreset.Beginner)</c>，
    /// 于是玩 16×16 / 40 雷失败后点笑脸会直接掉回 9×9 / 10 雷（玩家报的 bug）。
    ///
    /// 规则：
    /// <list type="bullet">
    /// <item>关卡局 → 重开同一关（同一张盘，方便刷成绩）。</item>
    /// <item>普通局 → 沿用当前这一局的盘面尺寸、雷数、模式与"锁定盘面"开关；
    /// 种子取种子框里的值，留空就是一张新的随机盘（和再按一次难度按钮的行为一致）。</item>
    /// </list>
    /// </summary>
    private void RestartCurrentGame()
    {
        // 关卡局：重开这一关
        if (ViewModel.CurrentLevel is { } level)
        {
            StartLevel(level);
            return;
        }

        if (ViewModel.Session is not { } previous)
        {
            // 还没有任何对局（理论上不该发生）：退回初级
            StartPreset(DifficultyPreset.Beginner);
            return;
        }

        // 用**这一局真实生效的参数**，而不是界面控件当前的值：
        // 玩家可能在开局之后又改了模式/锁定开关，那些改动不该影响"重开这一局"。
        var options = new GameStartOptions(
            previous.Settings.Mode,
            previous.Settings.FirstClickOpening,
            previous.Settings.PauseTimerOnFocusLoss,
            previous.Locked);

        int width = previous.Board.Width;
        int height = previous.Board.Height;
        int mines = previous.Board.MineCount;
        string? difficultyId = previous.DifficultyId;

        if (!ViewModel.StartNewGame(options, width, height, mines, TryReadSeed(), difficultyId, out string? error))
        {
            _notifier.Publish(GameNotification.Error("重开失败", error ?? "无法按当前参数重新开局。", "game.restart-failed"));
            return;
        }

        AttachSession();
        _notifier.Publish(GameNotification.QuietInfo(
            $"重开：{width}×{height} / {mines} 雷",
            $"沿用上一局的难度与模式 · {ViewModel.ModeText}",
            "game.restart"));
    }

    /// <summary>把"当前这一局是什么"写成一行日志（自检用）。</summary>
    private string DescribeCurrentGame()
    {
        if (ViewModel.CurrentLevel is { } level)
        {
            return $"关卡 {level.DisplayId} {level.BoardText}";
        }

        if (ViewModel.Session is not { } session)
        {
            return "（无对局）";
        }

        return $"{session.Board.Width}×{session.Board.Height} / {session.Board.MineCount} 雷" +
               $" · 模式={session.Settings.Mode} · 锁定={session.Locked} · 难度={session.DifficultyId ?? "自定义"}";
    }

    // 初级 / 中级 / 高级：这三个按钮既是"换盘面"，也当作"启动"——
    // 按需求，切换雷数时同样要响启动铃声。
    private void OnBeginnerClick(object sender, RoutedEventArgs e) => SwitchPreset(DifficultyPreset.Beginner);

    private void OnIntermediateClick(object sender, RoutedEventArgs e) => SwitchPreset(DifficultyPreset.Intermediate);

    private void OnExpertClick(object sender, RoutedEventArgs e) => SwitchPreset(DifficultyPreset.Expert);

    /// <summary>切换难度：开新局 + 启动铃声（v0.8）。</summary>
    private void SwitchPreset(DifficultyPreset preset)
    {
        StartPreset(preset);
        PlayStartupSound($"切换到{preset.DisplayName}");
    }

    private void OnSettingsToggled(object sender, RoutedEventArgs e)
        => SettingsPanel.Visibility = SettingsToggle.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;


    private void OnCustomStartClick(object sender, RoutedEventArgs e)
    {
        int width = double.IsNaN(WidthBox.Value) ? 30 : (int)WidthBox.Value;
        int height = double.IsNaN(HeightBox.Value) ? 16 : (int)HeightBox.Value;
        int mines = double.IsNaN(MinesBox.Value) ? 99 : (int)MinesBox.Value;

        GameStartOptions options = CurrentOptions();
        if (options.Mode != GameMode.Custom)
        {
            options = options with { Mode = GameMode.Custom };
        }

        if (!ViewModel.StartNewGame(options, width, height, mines, TryReadSeed(), difficultyId: null, out string? error))
        {
            // §0.1：参数非法时明确报错并拒绝开局，不做静默退化
            _notifier.Publish(GameNotification.Error("开局失败", error ?? "参数不合法。", "game.custom-failed"));
            return;
        }

        AttachSession();
        _notifier.Publish(GameNotification.QuietInfo(
            "新局：自定义",
            $"{width}×{height} / {mines} 雷（道具默认开启{(ViewModel.Session!.Locked ? " · 锁定盘面" : string.Empty)}）",
            "game.custom"));
    }

    private void OnReplaySeedClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Session is null)
        {
            return;
        }

        GameSession current = ViewModel.Session;

        // 有种子 → 用"同种子 + 同首点"复现；没有种子 → 直接按快照重开同一盘面（§6.3）
        if (TryReadSeed() is int seed && current.Board.MinesPlaced)
        {
            var options = new GameStartOptions(
                current.Settings.Mode,
                current.Settings.FirstClickOpening,
                current.Settings.PauseTimerOnFocusLoss,
                current.Locked);

            if (!ViewModel.StartNewGame(
                    options,
                    current.Board.Width,
                    current.Board.Height,
                    current.Board.MineCount,
                    seed,
                    current.DifficultyId,
                    out string? error))
            {
                _notifier.Publish(GameNotification.Error("开局失败", error ?? "参数不合法。", "game.replay-seed-failed"));
                return;
            }

            // 普通模式：同一首点 → 同一盘面（§2.6）；锁定模式在创建时已经翻开过首点
            if (!current.Locked)
            {
                ViewModel.Session!.Reveal(current.Board.FirstClickIndex);
            }

            AttachSession();
            _notifier.Publish(GameNotification.QuietSuccess(
                "已复现盘面",
                current.Locked
                    ? $"锁定盘面 · 种子 {seed}（盘面与首点都由种子决定）。"
                    : $"种子 {seed} + 首点 {current.Board.FirstClickIndex}。",
                "game.replay-seed"));
            return;
        }

        if (!current.Board.MinesPlaced)
        {
            _notifier.Publish(GameNotification.Warning(
                "还不能重开本局",
                "先翻开第一格（布雷后才有盘面可以重开）。",
                "game.replay-too-early"));
            return;
        }

        try
        {
            BoardSnapshot snapshot = current.CaptureSnapshot();
            GameSession replay = GameSession.CreateReplay(
                snapshot,
                current.Settings,
                ViewModel.Inventory,
                SystemClock.Instance,
                current.DifficultyId);

            ViewModel.AttachReplay(replay);
            AttachSession();
            _notifier.Publish(GameNotification.QuietSuccess(
                "已重开本局",
                "雷位与首点完全相同。",
                "game.replay-snapshot"));
        }
        catch (InvalidOperationException ex)
        {
            _notifier.Publish(GameNotification.Error("重开失败", ex.Message, "game.replay-snapshot-failed"));
        }
    }

    /// <summary>按分享串复现同一盘面（§6.3 的"好友之间分享同一盘"）。</summary>
    private void OnOpenShareCodeClick(object sender, RoutedEventArgs e)
    {
        GameMode mode = CurrentOptions().Mode;

        if (!ViewModel.TryStartFromShareCode(ShareCodeBox.Text, mode, out string? error))
        {
            _notifier.Publish(GameNotification.Error("分享串无法使用", error ?? "分享串不合法。", "share.open-failed"));
            return;
        }

        AttachSession();
        _notifier.Publish(GameNotification.QuietSuccess(
            "已按分享串复现盘面",
            $"{ViewModel.Session!.Board.Width}×{ViewModel.Session.Board.Height} / {ViewModel.Session.Board.MineCount} 雷 · 种子 {ViewModel.Session.Seed}"
                + (ViewModel.Session.Locked ? " · 锁定盘面" : string.Empty),
            "share.opened"));
    }

    private void OnFitClick(object sender, RoutedEventArgs e) => _board.FitToWindow();

    /// <summary>右键物品栏槽位进入瞄准态（§5.3）。目前只有 0 号槽有道具。</summary>
    private void OnMagnifierRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        e.Handled = true;
        _board.BeginAiming(0);
    }

    /// <summary>右键第 2 / 3 个槽位（§3.2 的预留位）：明确告诉玩家还没道具。</summary>
    /// <summary>物品栏第 2 / 3 格：1 = 声呐扫描（瞄准型），2 = 时间冻结（即用型）。</summary>
    private void OnSlotRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        e.Handled = true;
        ActivateSlot(SlotIndexOf(sender));
    }

    private void OnSlotHolding(object sender, HoldingRoutedEventArgs e)
    {
        if (e.HoldingState != HoldingState.Started)
        {
            return;
        }

        e.Handled = true;
        ActivateSlot(SlotIndexOf(sender));
    }

    private static int SlotIndexOf(object sender)
        => sender is FrameworkElement { Tag: string tag } && int.TryParse(tag, out int slot) ? slot : -1;

    private void ActivateSlot(int slot)
    {
        if (slot == 2)
        {
            _board.UseTimeFreezeNow();
            return;
        }

        if (slot == 1)
        {
            _board.BeginAiming(1);
            return;
        }
    }

    private void OnEmptySlotRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        e.Handled = true;
        _board.BeginAiming(1);
    }

    /// <summary>触屏长按物品栏图标进入瞄准态（§5.3 的备用入口，无右键设备）。</summary>
    private void OnMagnifierHolding(object sender, HoldingRoutedEventArgs e)
    {
        if (e.HoldingState != HoldingState.Started)
        {
            return;
        }

        e.Handled = true;
        _board.BeginAiming(0);
    }

    /// <summary>触屏长按空槽位：同样给出"这个槽位还没有道具"的反馈。</summary>
    private void OnEmptySlotHolding(object sender, HoldingRoutedEventArgs e)
    {
        if (e.HoldingState != HoldingState.Started)
        {
            return;
        }

        e.Handled = true;
        ActivateSlot(SlotIndexOf(sender));
    }

    private void OnUnusedEmptySlotHolding(object sender, HoldingRoutedEventArgs e)
    {
        if (e.HoldingState != HoldingState.Started)
        {
            return;
        }

        e.Handled = true;
        _board.BeginAiming(1);
    }

    // ------------------------------------------------------------ 关卡模式
    //
    // 关卡制的数据与规则都在 Core（LevelTable / LevelRules / LevelProgress，可全量单测），
    // 这里只负责：把关卡列表画出来、开始某一关、以及在结算时把结果落盘并通知玩家。

    private void OnLevelsClick(object sender, RoutedEventArgs e)
    {
        RefreshLevelList();
        LevelPanel.Visibility = Visibility.Visible;
    }

    private void OnCloseLevelPanelClick(object sender, RoutedEventArgs e)
        => LevelPanel.Visibility = Visibility.Collapsed;

    private void OnLevelButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: LevelDefinition level })
        {
            StartLevel(level);
        }
    }

    // ------------------------------------------------------------ 上一关 / 下一关（游戏内）
    //
    // 这两个按钮现在直接放在游戏界面里（不再在关卡面板上），所以锚点就是**正在进行的这一关**：
    // 普通局/隐藏关不显示它们（普通局没有"上一关"这个概念；隐藏关不在 18 关链条里）。

    /// <summary>前后跳转围绕的那一关 = 正在进行的这一关；不是主线关卡局时返回 null。</summary>
    private LevelDefinition? NavigationAnchorLevel()
        => ViewModel.IsLevelRun && ViewModel.CurrentLevel is { IsSecret: false } running ? running : null;

    /// <summary>锚点关卡的上一关；已经是第一关（或隐藏关）时返回 null。</summary>
    private LevelDefinition? PreviousLevelOf(LevelDefinition anchor)
    {
        if (anchor.IsSecret || anchor.Index <= 0)
        {
            return null;
        }

        // 能走到 anchor 说明 anchor 已解锁，所以它的上一关必然也解锁了
        return LevelTable.At(anchor.Index - 1);
    }

    /// <summary>锚点关卡的下一关；不存在或<b>还没解锁</b>时返回 null（按钮据此置灰）。</summary>
    private LevelDefinition? NextLevelOf(LevelDefinition anchor)
    {
        if (anchor.IsSecret)
        {
            return null;
        }

        LevelDefinition? next = LevelTable.Next(anchor);
        return next is not null && LevelRules.IsUnlocked(_storage.LevelProgress, next) ? next : null;
    }

    /// <summary>
    /// 刷新游戏内"上一关 / 下一关"两个按钮：可见性 + 可用性 + 提示。
    /// 非关卡局直接整组藏起来（普通局里放这俩没有意义）。
    /// </summary>
    private void UpdateLevelNavigation()
    {
        LevelDefinition? anchor = NavigationAnchorLevel();

        Visibility visibility = anchor is null ? Visibility.Collapsed : Visibility.Visible;
        LevelNavRow.Visibility = visibility;

        if (anchor is null)
        {
            return;
        }

        LevelDefinition? previous = PreviousLevelOf(anchor);
        PreviousLevelButton.IsEnabled = previous is not null;
        ToolTipService.SetToolTip(
            PreviousLevelButton,
            previous is not null ? $"回到 {previous.DisplayName}" : "已经是第一关了");

        LevelDefinition? next = NextLevelOf(anchor);
        NextLevelButton.IsEnabled = next is not null;
        ToolTipService.SetToolTip(
            NextLevelButton,
            next is not null
                ? $"进入 {next.DisplayName}"
                : LevelTable.Next(anchor) is null ? "已经是最后一关了" : "通关本关后解锁");
    }

    private void OnPreviousLevelClick(object sender, RoutedEventArgs e)
    {
        LevelDefinition? anchor = NavigationAnchorLevel();
        LevelDefinition? previous = anchor is null ? null : PreviousLevelOf(anchor);
        if (previous is null)
        {
            return;
        }

        // 直接换关，不打开关卡面板：这两个按钮就是给"连着往下打"用的
        StartLevel(previous);
    }

    private void OnNextLevelClick(object sender, RoutedEventArgs e)
    {
        LevelDefinition? anchor = NavigationAnchorLevel();
        LevelDefinition? next = anchor is null ? null : NextLevelOf(anchor);
        if (next is null)
        {
            // 按钮本来就该是灰的；万一走到这里也把"为什么不能进"说清楚
            _notifier.Publish(GameNotification.Warning(
                "下一关还没解锁",
                "先通关当前这一关才能进下一关。",
                "level.next-locked"));
            return;
        }

        StartLevel(next);
    }

    /// <summary>重建关卡列表（每次打开面板时重建，这样星数与解锁状态一定是最新的）。</summary>
    private void RefreshLevelList()
    {
        LevelProgress progress = _storage.LevelProgress;

        LevelSummaryText.Text =
            $"已通关 {progress.ClearedCount} / {LevelTable.Count} 关 · " +
            $"星数 {progress.TotalStars} / {LevelTable.Count * LevelRules.MaxStars}";

        UpdateLevelNavigation();

        LevelListPanel.Children.Clear();

        int columns = LevelListColumns();
        _levelListColumns = columns;
        for (int chapter = 1; chapter <= LevelTable.ChapterCount; chapter++)
        {
            IReadOnlyList<LevelDefinition> levels = LevelTable.Chapter(chapter);
            int cleared = 0;
            foreach (LevelDefinition level in levels)
            {
                if (progress.IsCleared(level.Id))
                {
                    cleared++;
                }
            }

            LevelListPanel.Children.Add(new TextBlock
            {
                Text = $"第 {chapter} 章 · {levels[0].ChapterName}（已通关 {cleared} / {levels.Count}）",
                FontSize = 15,
            });

            var grid = new Grid { ColumnSpacing = 8, RowSpacing = 8 };
            for (int column = 0; column < columns; column++)
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            }

            int rowCount = ((levels.Count + columns - 1) / columns);
            for (int row = 0; row < rowCount; row++)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            }

            for (int i = 0; i < levels.Count; i++)
            {
                Button button = BuildLevelButton(progress, levels[i]);
                Grid.SetRow(button, i / columns);
                Grid.SetColumn(button, i % columns);
                grid.Children.Add(button);
            }

            LevelListPanel.Children.Add(grid);
        }

        // 隐藏关：只有本次运行触发过彩蛋之后才会出现在这里（之前连名字都不显示）
        if (progress.SecretUnlocked && _secretLevel is not null)
        {
            LevelDefinition secret = _secretLevel;

            LevelListPanel.Children.Add(new TextBlock
            {
                Text = $"{LevelTable.SecretChapterName}（{secret.BoardText}）",
                FontSize = 15,
            });

            var secretGrid = new Grid { ColumnSpacing = 8, RowSpacing = 8 };
            for (int column = 0; column < columns; column++)
            {
                secretGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            }

            secretGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            Button secretButton = BuildLevelButton(progress, secret);
            Grid.SetRow(secretButton, 0);
            Grid.SetColumn(secretButton, 0);
            secretGrid.Children.Add(secretButton);

            LevelListPanel.Children.Add(secretGrid);
        }
    }

    /// <summary>
    /// 关卡列表排几列。
    ///
    /// 面板工具条（标题 + 汇总 + 上一关/下一关 + 返回）会占掉一部分宽度，
    /// 窗口被拖窄时固定三列会把卡片挤成"每行只放得下两个、第三个跑到下一行"的怪样子，
    /// 所以按面板实际可用宽度决定列数。
    /// </summary>
    private int LevelListColumns()
    {
        if (_levelListColumns > 0)
        {
            return _levelListColumns;
        }

        double available = LevelListPanel.ActualWidth;

        // 面板还没排版时（ActualWidth 为 0）按窗口宽度估算，够宽就三列
        if (available <= 1)
        {
            available = Math.Max(0, RootGrid.ActualWidth - 32);
        }

        // 一张卡片至少要 ~210 DIP 才不至于把"9×9 / 10 雷 · ★★★ · 最好 0.0s"折成好几行
        return available >= 3 * 210 ? 3 : 2;
    }

    /// <summary>本次布局用了几个列（0 = 还没算过）。用来避免每次尺寸变化都重建列表。</summary>
    private int _levelListColumns;

    /// <summary>是否已经排了一个"重排列数"的延迟任务，避免重复排队。</summary>
    private bool _levelListRefreshQueued;

    private Button BuildLevelButton(LevelProgress progress, LevelDefinition level)
    {
        bool unlocked = LevelRules.IsUnlocked(progress, level);
        int stars = progress.StarsOf(level.Id);
        double? best = progress.BestSecondsOf(level.Id);

        var content = new StackPanel { Spacing = 2 };
        content.Children.Add(new TextBlock
        {
            Text = unlocked ? level.DisplayName : $"🔒 {level.DisplayId} {level.Name}",
            FontSize = 14,
        });

        string detail = unlocked
            ? $"{level.BoardText} · {LevelRules.StarsText(stars)}" +
              (best is { } seconds ? $" · 最好 {seconds:F1}s" : string.Empty)
            : "通关上一关后解锁";
        content.Children.Add(new TextBlock
        {
            Text = detail,
            FontSize = 11,
            Opacity = 0.75,
            TextWrapping = TextWrapping.Wrap,
        });

        if (unlocked)
        {
            content.Children.Add(new TextBlock
            {
                Text = level.GoalText,
                FontSize = 11,
                Opacity = 0.6,
                TextWrapping = TextWrapping.Wrap,
            });
        }

        var button = new Button
        {
            Tag = level,
            Content = content,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            IsEnabled = unlocked,
        };

        button.Click += OnLevelButtonClick;
        return button;
    }

    /// <summary>开始某一关。用的是锁定盘面，所以同一关永远是同一张盘（可重试、可刷星）。</summary>
    private void StartLevel(LevelDefinition level)
    {
        if (!ViewModel.StartLevel(level, out string? error))
        {
            _notifier.Publish(GameNotification.Error("关卡无法开始", error ?? "未知原因。", "level.start-failed"));
            return;
        }

        LevelPanel.Visibility = Visibility.Collapsed;
        AttachSession();

        _notifier.Publish(GameNotification.QuietInfo(
            $"关卡 {level.DisplayId}：{level.Name}",
            $"{level.BoardText} · {level.GoalText} · 同一关盘面固定，失败可原样重试。",
            "level.start"));
    }

    /// <summary>
    /// 关卡结算：评星 → 更新解锁 → 落盘 → 通知。
    /// 成绩只写 levelprogress.json，<b>不进最快记录表、也不动跨局进度</b>。
    /// </summary>
    private void SettleLevel()
    {
        if (_levelSettled ||
            ViewModel.Session is not { } session ||
            ViewModel.CurrentLevel is not { } level)
        {
            return;
        }

        _levelSettled = true;

        bool won = session.Status == GameStatus.Won;

        // 防御：锁定盘面创建时会替玩家翻开首点，极小概率一开局就把安全格翻完。
        // 那种"胜利"不是玩家打出来的，不能算通关、更不能解锁下一关。
        if (won && !_levelPlayerActed)
        {
            won = false;
            AppPaths.Log("[LEVEL] 检测到开局即胜利（玩家未操作），按未通关处理，不解锁下一关。");
        }

        LevelOutcome outcome = LevelRules.Settle(_storage.LevelProgress, level, won, session.ElapsedSeconds);
        _storage.LevelStore.Save(_storage.LevelProgress);

        if (won)
        {
            _notifier.Publish(GameNotification.Success(
                outcome.FirstClear ? "关卡通关！" : "关卡完成",
                outcome.Summary,
                "level.cleared"));
        }
        else
        {
            _notifier.Publish(GameNotification.Error("关卡失败", outcome.Summary, "level.failed"));
        }

        // 通关就弹结算小卡片（失败不弹：Toast 已经说清楚了，而且失败会反复发生，
        // 每次都拦一个弹窗会把"重试点一下就好"的节奏打断）。
        if (won)
        {
            _lastLevelOutcome = outcome;
            ShowSettlementOverlay(outcome);
        }

        // 结算**整体做完**的标志。自检用 `_gameOverHandled` 判断"游戏结束处理过没有"，
        // 但那个标志是在调用 HandleGameOver **之前**置位的；万一这一次结算中途异常退出，
        // 自检就会读到"标志为真但弹窗还没弹"，把半成品状态当成最终结果误报。
        // 所以额外给一个"结算真的走完了"的标志，自检只认它。
        _levelSettleCompleted = true;

        UpdateLevelGoalBar();
        ViewModel.RefreshLevelGoal();
    }

    // ------------------------------------------------------------ 历史记录（v0.6）

    /// <summary>
    /// 把"当前这一局"并进历史。<paramref name="countAsGame"/> 为 true 时会累计对局数
    /// （第一次翻开格子的时候调一次）；本局成果只在第一次调用时累计，之后重复调用无效。
    /// </summary>
    private void FlushHistoryForCurrentSession(bool sessionEnded)
    {
        if (ViewModel.Session is not { } session || _historyRecordedForSession)
        {
            return;
        }

        // 本局一次都没翻开过就不并入：否则启动时会凭空记一局，
        // 而且那一刻基线（_correctFlagsAtSessionStart）还是 0，会把账号累计的扫雷数
        // 整个当成"本局扫的"重复累加进历史。
        if (_sessionStartedAt is null)
        {
            return;
        }

        ItemInventory inventory = ViewModel.ActiveInventory;
        AppPaths.Log($"[ITEMCHECK] 开局物品栏：镜×{inventory.MagnifierCount} 呐×{inventory.SonarCount} 冰×{inventory.FreezeCount}");
        int minesSwept = Math.Max(0, inventory.Progress.CorrectMineFlagsTotal - _correctFlagsAtSessionStart);

        _history.RecordGame(new GameSummary(
            DateTimeOffset.UtcNow,
            session.ElapsedSeconds,
            minesSwept,
            session.RevealedCount,
            Won: session.Status == GameStatus.Won,
            LevelCleared: ViewModel.CurrentLevel is not null && session.Status == GameStatus.Won));

        _historyRecordedForSession = true;
        _historyStore.Save(_history);

        AppPaths.Log(
            $"[历史] 本局并入：翻开 {session.RevealedCount} 格 · 扫雷 {minesSwept} 颗 · 用时 {session.ElapsedSeconds:F1}s；" +
            $"累计 {_history.TotalGames} 局 / {_history.TotalMinesSwept} 颗 / {PlayHistory.FormatDuration(_history.TotalPlayedSeconds)}。");
    }

    /// <summary>打开历史记录弹窗。</summary>
    private void OnHistoryClick(object sender, RoutedEventArgs e) => ShowHistoryOverlay();

    private void OnHistoryCloseClick(object sender, RoutedEventArgs e)
        => HistoryOverlay.Visibility = Visibility.Collapsed;

    private void ShowHistoryOverlay()
    {
        HistoryRangeText.Text = _history.FirstSweptAtUtc is { } first
            ? $"第一次扫雷：{first.LocalDateTime:yyyy-MM-dd HH:mm}" +
              (_history.LastPlayedAtUtc is { } last ? $"　·　最近一次：{last.LocalDateTime:yyyy-MM-dd HH:mm}" : string.Empty)
            : "还没有记录 —— 从你第一次翻开格子的那一刻起，这里开始记录。";

        HistoryTotalsText.Text =
            $"累计扫雷：{_history.TotalMinesSwept} 颗\n" +
            $"累计用时：{PlayHistory.FormatDuration(_history.TotalPlayedSeconds)}";

        HistoryGamesText.Text =
            $"对局 {_history.TotalGames} 局（胜 {_history.TotalWins}）\n" +
            $"累计翻开 {_history.TotalCellsRevealed} 格　·　通关关卡 {_history.TotalLevelsCleared} 个";

        HistoryNoteText.Text =
            "这些数据只存在 %LOCALAPPDATA%\\MineSweeper2\\history.json 这一个文件里（第一次启动会把旧的 data\\ 搬过来）：" +
            "把它（或整个 data\\ 文件夹）删掉，历史就从零重新开始。";

        HistoryOverlay.Visibility = Visibility.Visible;
    }

    /// <summary>时间冻结格：点一下就冻。</summary>
    private void OnFreezeSlotTapped(object sender, TappedRoutedEventArgs e)
    {
        e.Handled = true;
        _board.UseTimeFreezeNow();
    }

    /// <summary>刷新三格道具的"能不能用"外观（暗 = 用不了/没有）。</summary>
    private void UpdateItemSlots()
    {
        SonarSlot.Opacity = ViewModel.IsSonarUsableNow ? 1d : 0.35d;
        FreezeSlot.Opacity = ViewModel.IsFreezeUsableNow ? 1d : 0.35d;
    }

    // ------------------------------------------------------------ 道具自检（SAOLEI_ITEMS=1）

    private bool _itemCheckPending;
    private bool _uiCheckDone;

    /// <summary>
    /// v0.8 新弹窗自检：<c>SAOLEI_UI=sonar</c> 验声呐结果弹窗（需手动确认），
    /// <c>SAOLEI_UI=defeat</c> 验踩雷弹窗（重新开始 / 退出）。
    /// 走的是**和真人完全一样**的代码路径（真实的瞄准 + 真实的点击 / 真实的踩雷结算）。
    /// </summary>
    private void RunUiCheck()
    {
        string mode = Environment.GetEnvironmentVariable("SAOLEI_UI")?.Trim().ToLowerInvariant() ?? string.Empty;

        if (mode == "defeat")
        {
            RunDefeatUiCheck();
            return;
        }

        if (mode == "difficulty")
        {
            RunDifficultySoundCheck();
            return;
        }

        if (mode == "exit")
        {
            RunExitCheck();
            return;
        }

        if (mode == "play")
        {
            RunItemPlaySetup();
            return;
        }

        RunSonarUiCheck();
    }

    /// <summary>
    /// <c>SAOLEI_UI=play</c>：把游戏摆成"**可直接手动试声呐**"的状态，然后停在这里不自动跑完。
    ///
    /// 为什么要它：默认开局是纯经典模式（道具被设计为不可用），而"首次赠送"只发一次，
    /// 老存档里 `initialGiftGranted` 早就是 true —— 于是玩家想手动试声呐时会发现槽位是灰的、
    /// 点了只回一句"数量不足"。这个开关直接把三种道具补满并留在游戏画面里，省掉找设置/刷奖励的功夫。
    /// </summary>
    private void RunItemPlaySetup()
    {
        if (!ViewModel.StartNewGame(
                new GameStartOptions(GameMode.ClassicWithItems, FirstClickOpening: true, PauseTimerOnFocusLoss: false, LockedBoard: false),
                9,
                9,
                10,
                seed: null,
                difficultyId: null,
                out string? modeError))
        {
            AppPaths.Log($"[UICHECK] 开带道具的局失败：{modeError}");
            return;
        }

        AttachSession();

        if (ViewModel.Session is not { } session)
        {
            return;
        }

        // 补满三种道具（只改内存里的这一局，落盘由正常的保存流程负责）
        ItemInventory inventory = ViewModel.ActiveInventory;
        inventory.Progress.MagnifierCount = inventory.MaxHold;
        inventory.Progress.SonarCount = inventory.MaxHold;
        inventory.Progress.FreezeCount = inventory.MaxHold;
        ViewModel.Refresh();
        UpdateMagnifierSlot();

        // 先开一格完成布雷：声呐要求"首点之后"才能用（否则区域里还没有雷）
        _board.ClickAtForProbe(0, 0);
        session.Reveal(session.Board.Index(4, 4));
        ViewModel.Refresh();
        UpdateMagnifierSlot();

        AppPaths.Log(
            $"[UICHECK] 已摆好声呐测试局面：模式={ViewModel.ModeText} · " +
            $"盘面={session.Board.Width}×{session.Board.Height} · 已翻开={session.RevealedCount} 格 · " +
            $"镜×{inventory.MagnifierCount} 呐×{inventory.SonarCount} 冰×{inventory.FreezeCount} · " +
            $"声呐现在可用={ViewModel.IsSonarUsableNow}（期望 True）");
        AppPaths.Log("[UICHECK] 手动测试：右键（或长按）📡 槽位进入瞄准 → 左键点棋盘任意 3×3 中心 → 应弹出结果卡片");
    }

    /// <summary>
    /// 验证"退出游戏"按钮真的能关掉窗口（v0.8）。
    /// 覆盖层是 Border 而不是 ContentDialog，理论上不该拦住 Close()，但这是玩家点下去必须生效的动作，
    /// 所以留一条自检：日志打出"正在退出"，随后进程若消失就说明 Close() 生效了。
    /// </summary>
    private void RunExitCheck()
    {
        GameSession session = ViewModel.Session!;
        session.Reveal(session.Board.Index(4, 4));
        session.Reveal(session.Board.GetMineIndexes()[0]);
        OnBoardStateChanged(_board, EventArgs.Empty);

        AppPaths.Log($"[UICHECK] 退出前：失败弹窗可见={DefeatOverlay.Visibility}（期望 Visible）");

        OnDefeatExitClick(this, new RoutedEventArgs());

        // 注意：Close() 是**异步**的，玩家点下去之后窗口才会真正关掉，
        // 所以这一行一定会先写出来。要判断"到底关没关"，看进程有没有退出（见 README 的自检说明）。
        AppPaths.Log("[UICHECK] 已调用退出（随后应以进程消失为准）");
    }

    /// <summary>
    /// 验证"切换难度也响启动铃声"（v0.8）。走的是初级/中级/高级三个按钮的处理函数，
    /// 不是直接调 PlayStartupSound —— 验的正是"按钮确实接上了"。
    /// </summary>
    private void RunDifficultySoundCheck()
    {
        var e = new RoutedEventArgs();

        AppPaths.Log($"[UICHECK] 切换难度前：盘面={ViewModel.Session?.Board.Width}×{ViewModel.Session?.Board.Height}");

        // 三个按钮依次点一遍，每次都应留下一条启动铃声日志 + 开一局新的
        OnIntermediateClick(this, e);
        AppPaths.Log(
            $"[UICHECK] 点『中级』后：盘面={ViewModel.Session?.Board.Width}×{ViewModel.Session?.Board.Height}" +
            $"（期望 16×16）· 状态={ViewModel.Session?.Status}");

        OnExpertClick(this, e);
        AppPaths.Log(
            $"[UICHECK] 点『高级』后：盘面={ViewModel.Session?.Board.Width}×{ViewModel.Session?.Board.Height}" +
            $"（期望 30×16）· 状态={ViewModel.Session?.Status}");

        OnBeginnerClick(this, e);
        AppPaths.Log(
            $"[UICHECK] 点『初级』后：盘面={ViewModel.Session?.Board.Width}×{ViewModel.Session?.Board.Height}" +
            $"（期望 9×9）· 状态={ViewModel.Session?.Status}");
    }

    private void RunSonarUiCheck()
    {
        // 默认开局是纯经典（道具被禁止），先换成"经典 + 道具"
        if (!ViewModel.StartNewGame(
                new GameStartOptions(GameMode.ClassicWithItems, FirstClickOpening: true, PauseTimerOnFocusLoss: false, LockedBoard: false),
                9,
                9,
                10,
                seed: null,
                difficultyId: null,
                out string? modeError))
        {
            AppPaths.Log($"[UICHECK] 开带道具的局失败：{modeError}");
            return;
        }

        AttachSession();

        if (ViewModel.Session is not { } session)
        {
            return;
        }

        // 发一个声呐；先手动开一格完成布雷（声呐要求首点之后才能用）
        ViewModel.ActiveInventory.Progress.SonarCount = 1;
        session.Reveal(session.Board.Index(4, 4));
        ViewModel.Refresh();
        UpdateMagnifierSlot();

        AppPaths.Log($"[UICHECK] 声呐前：弹窗可见={SonarOverlay.Visibility}（期望 Collapsed）· 库存={ViewModel.Inventory.SonarCount}");

        // 真实路径：进瞄准态 → 在棋盘中心放下去
        if (!_board.BeginAiming(1))
        {
            AppPaths.Log("[UICHECK] ✗ 进不了声呐瞄准态");
            return;
        }

        int center = session.Board.Index(4, 4);
        if (!_board.TryGetCellCenterForProbe(center, out double cx, out double cy))
        {
            AppPaths.Log("[UICHECK] ✗ 取不到中心格坐标");
            return;
        }

        _board.ClickAtForProbe(cx, cy);

        AppPaths.Log(
            $"[UICHECK] 声呐后：弹窗可见={SonarOverlay.Visibility}（期望 Visible）· " +
            $"标题=『{SonarTitle.Text}』· 区域=『{SonarArea.Text.Replace("\n", " / ")}』· " +
            $"剩余=『{SonarRemaining.Text}』· 剩余声呐={ViewModel.Inventory.SonarCount}");

        // 手动确认后必须收起
        OnSonarCloseClick(this, new RoutedEventArgs());
        AppPaths.Log($"[UICHECK] 点『知道了』之后：弹窗可见={SonarOverlay.Visibility}（期望 Collapsed）");
    }

    private void RunDefeatUiCheck()
    {
        GameSession session = ViewModel.Session!;

        // 真实路径：手动开一格完成布雷，然后踩一颗雷（走的是和玩家一样的 Reveal）
        session.Reveal(session.Board.Index(4, 4));
        int mine = session.Board.GetMineIndexes()[0];
        RevealResult hit = session.Reveal(mine);

        AppPaths.Log($"[UICHECK] 踩雷：outcome={hit.Outcome} 状态={session.Status}");

        // 走真实的状态变化链路（和玩家踩雷时一样：事件 → OnBoardStateChanged → HandleGameOver）
        OnBoardStateChanged(_board, EventArgs.Empty);

        AppPaths.Log(
            $"[UICHECK] 失败弹窗：可见={DefeatOverlay.Visibility}（期望 Visible）· " +
            $"摘要=『{DefeatSummary.Text.Replace("\n", " / ")}』· " +
            $"重开按钮=『{DefeatRetryButton.Content}』");

        // 点"重新开始这一盘"：应当收起弹窗并开一局新的
        OnDefeatRetryClick(this, new RoutedEventArgs());

        AppPaths.Log(
            $"[UICHECK] 点『重新开始这一盘』之后：弹窗可见={DefeatOverlay.Visibility}（期望 Collapsed）· " +
            $"新局状态={ViewModel.Session?.Status} · 已翻开={ViewModel.Session?.RevealedCount}（期望 0）");
    }

    private bool _historyCheckDone;
    private bool _flag20Done;

    private void StartItemCheckIfRequested()
    {
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SAOLEI_ITEMS")))
        {
            _itemCheckPending = true;
        }
    }

    /// <summary>
    /// 自检（SAOLEI_FLAG20=1）：在**普通模式**下连插 20 面正确的旗，
    /// 验证"每扫 20 颗雷自动弹出三选一"—— 这里**故意不手工刷新弹窗**，
    /// 所以日志里的 Visible 只可能来自棋盘 BoardStateChanged 的自动链路。
    /// </summary>
    private void RunFlag20Check()
    {
        // 初级盘只有 10 颗雷、插不满 20，所以先换一局 30×16/99 的普通局
        if (!ViewModel.StartNewGame(
                new GameStartOptions(GameMode.ClassicWithItems, FirstClickOpening: true, PauseTimerOnFocusLoss: false, LockedBoard: false),
                30,
                16,
                99,
                seed: null,
                difficultyId: null,
                out string? modeError))
        {
            AppPaths.Log($"[FLAG20] 开 30×16/99 失败：{modeError}");
            return;
        }

        AttachSession();

        if (ViewModel.Session is not { } session)
        {
            return;
        }

        session.Reveal(session.Board.Index(4, 4));

        int[] mines = session.Board.GetMineIndexes();
        int placed = Math.Min(20, mines.Length);
        for (int i = 0; i < placed; i++)
        {
            session.ToggleFlag(mines[i]);
        }

        // Core 直调不会触发 BoardView 的 BoardStateChanged（6 个触发点都在真实输入路径里），
        // 所以这里显式走一遍"事件 -> 处理函数"，验的正是处理函数里那行 RefreshItemChoiceOverlay()。
        OnBoardStateChanged(_board, EventArgs.Empty);

        AppPaths.Log(
            $"[FLAG20] 模式={(ViewModel.IsLevelRun ? "关卡" : "普通")} · 插了 {placed} 面正确的旗 · " +
            $"攒下奖励={ViewModel.ActiveInventory.PendingRewards} · 弹窗可见={ItemChoiceOverlay.Visibility}（期望 Visible，且没手工刷新）");
    }

    private void RunHistoryCheck()
    {
        _history.BeginGame(DateTimeOffset.UtcNow.AddHours(-3));
        _history.RecordGame(new GameSummary(DateTimeOffset.UtcNow, 3725d, 137, 2048, Won: true, LevelCleared: true));
        _history.RecordGame(new GameSummary(DateTimeOffset.UtcNow, 65d, 9, 88, Won: false, LevelCleared: false));

        ShowHistoryOverlay();

        AppPaths.Log(
            $"[HISTORYCHECK] 弹窗可见={HistoryOverlay.Visibility}（期望 Visible）· 首次扫雷={_history.FirstSweptAtUtc:u} · " +
            $"扫雷={_history.TotalMinesSwept} 颗 · 用时={PlayHistory.FormatDuration(_history.TotalPlayedSeconds)} · " +
            $"对局={_history.TotalGames} 胜={_history.TotalWins} 翻开={_history.TotalCellsRevealed} 关卡={_history.TotalLevelsCleared}");
    }

    private void RunItemCheck()
    {
        _itemCheckPending = false;

        // 默认开局是"纯经典"（道具在该模式下被设计禁止），所以先换成"经典 + 道具"再测
        if (!ViewModel.StartNewGame(
                new GameStartOptions(GameMode.ClassicWithItems, FirstClickOpening: true, PauseTimerOnFocusLoss: false, LockedBoard: false),
                9,
                9,
                10,
                seed: null,
                difficultyId: null,
                out string? modeError))
        {
            AppPaths.Log($"[ITEMCHECK] 开带道具的局失败：{modeError}");
            return;
        }

        AttachSession();

        if (ViewModel.Session is not { } session)
        {
            return;
        }

        // 先记一笔开局时的真实库存（证明"初次赠送三种各 2 个"确实落到了存档里）

        // 每种道具各发一个，然后各用一次，最后逼出一个"三选一"奖励弹窗
        ItemInventory inventory = ViewModel.ActiveInventory;
        inventory.Progress.SonarCount = 1;
        inventory.Progress.FreezeCount = 1;
        inventory.Progress.MagnifierCount = Math.Max(1, inventory.Progress.MagnifierCount);

        session.Reveal(session.Board.Index(4, 4));

        SonarScanResult scan = session.ScanWithSonar(session.Board.Index(4, 4));
        AppPaths.Log($"[ITEMCHECK] 声呐：成功={scan.Success} 区域={((scan.Radius * 2) + 1)}×{((scan.Radius * 2) + 1)} 雷数={scan.MineCount} 剩余={scan.RemainingCount}");

        TimeFreezeResult freeze = session.FreezeTimer();
        AppPaths.Log($"[ITEMCHECK] 冻结：启动={freeze.Started} 秒数={freeze.FrozenSeconds} 冻结中={session.IsTimerFrozen} 剩余={freeze.RemainingCount}");

        inventory.Progress.PendingRewards = 1;
        ViewModel.Refresh();
        UpdateMagnifierSlot();
        UpdateItemSlots();
        RefreshItemChoiceOverlay();
        AppPaths.Log($"[ITEMCHECK] 三选一弹窗：可见={ItemChoiceOverlay.Visibility}（期望 Visible）");
    }
    // ------------------------------------------------------------ "三选一"奖励弹窗（v0.6）

    /// <summary>
    /// 按攒下的奖励次数刷新"三选一"弹窗：每扫到 RewardThreshold（默认 20）颗雷攒一次，
    /// 玩家挑一个道具进物品栏；某个道具满了就把它禁用（奖励不会被白扣，换一个即可）。
    /// </summary>
    private void RefreshItemChoiceOverlay()
    {
        if (ViewModel.Session is null)
        {
            ItemChoiceOverlay.Visibility = Visibility.Collapsed;
            return;
        }

        ItemInventory inventory = ViewModel.ActiveInventory;
        if (inventory.PendingRewards <= 0)
        {
            ItemChoiceOverlay.Visibility = Visibility.Collapsed;
            return;
        }

        ItemChoiceTitle.Text = "选一个道具";
        ItemChoiceHint.Text =
            $"每扫到 {inventory.RewardThreshold} 颗雷攒一次奖励：从下面三个里挑一个。" +
            "三格道具各自最多存 3 个，满了就先挑别的。";
        ItemChoiceRemaining.Text = $"还攒着 {inventory.PendingRewards} 次奖励";

        ConfigureChoiceButton(ChoiceMagnifierButton, inventory, ItemIds.Magnifier);
        ConfigureChoiceButton(ChoiceSonarButton, inventory, ItemIds.Sonar);
        ConfigureChoiceButton(ChoiceFreezeButton, inventory, ItemIds.Freeze);

        ItemChoiceOverlay.Visibility = Visibility.Visible;
    }

    private static void ConfigureChoiceButton(Button button, ItemInventory inventory, string itemId)
    {
        button.Content = $"{ItemIds.DisplayNameOf(itemId)}（已有 {inventory.CountOf(itemId)} / {inventory.MaxHold}）";
        button.IsEnabled = !inventory.IsFull(itemId);
        ToolTipService.SetToolTip(button, ItemIds.DescriptionOf(itemId));
    }

    private void OnItemChoiceMagnifier(object sender, RoutedEventArgs e) => ClaimItemReward(ItemIds.Magnifier);

    private void OnItemChoiceSonar(object sender, RoutedEventArgs e) => ClaimItemReward(ItemIds.Sonar);

    private void OnItemChoiceFreeze(object sender, RoutedEventArgs e) => ClaimItemReward(ItemIds.Freeze);

    private void ClaimItemReward(string itemId)
    {
        if (ViewModel.Session is null)
        {
            return;
        }

        if (!ViewModel.Session.TryClaimReward(itemId, out ItemGrantResult result) || !result.Granted)
        {
            _notifier.Publish(GameNotification.Warning(
                "这个道具已经满了",
                "换一个吧 —— 奖励不会因为选了满的道具而丢掉。",
                "item.claim-full"));
            return;
        }

        AppPaths.Log($"[道具] 领取奖励：{ItemIds.DisplayNameOf(itemId)}（现在 {result.CountAfter} 个）。");

        _notifier.Publish(GameNotification.Success(
            $"获得道具：{ItemIds.DisplayNameOf(itemId)}",
            result.InventoryFull
                ? $"{ItemIds.DisplayNameOf(itemId)} 已经存满（{ViewModel.ActiveInventory.MaxHold} 个）。"
                : ItemIds.DescriptionOf(itemId),
            "item.claimed"));

        ViewModel.Refresh();
        UpdateMagnifierSlot();
        RefreshItemChoiceOverlay();
    }
    // ------------------------------------------------------------ 胜利结算小弹窗

    /// <summary>最近一次通关的结算结果（弹窗按钮要用它判断"有没有下一关"）。</summary>
    private LevelOutcome? _lastLevelOutcome;

    /// <summary>弹出胜利结算小卡片：星级 + 用时 + 最好成绩 + 刚解锁的下一关。</summary>
    private void ShowSettlementOverlay(LevelOutcome outcome)
    {
        LevelDefinition level = outcome.Level;

        SettlementTitle.Text = outcome.FirstClear ? "🎉 通关！" : "本局完成";
        SettlementStars.Text = outcome.StarsText;

        var lines = new List<string>
        {
            $"关卡 {level.DisplayId}：{level.Name}（{level.BoardText}）",
            $"本局用时 {outcome.Seconds:F1} 秒　·　最好成绩 {outcome.BestSeconds:F1} 秒",
        };

        if (outcome.IsNewBest)
        {
            lines.Add("🏆 刷新了本关最好成绩");
        }

        SettlementSummary.Text = string.Join(Environment.NewLine, lines);

        // 提示区：优先说"解锁了什么"，没有新解锁就说清楚下一步能干什么
        if (outcome.UnlockedNext is { } unlocked)
        {
            SettlementHint.Text = $"已解锁 {unlocked.DisplayName}，可以直接进下一关。";
        }
        else if (NextLevelOf(level) is { } next)
        {
            SettlementHint.Text = $"下一关是 {next.DisplayName}。";
        }
        else
        {
            SettlementHint.Text = level.IsSecret
                ? "隐藏关不计入关卡进度，关掉游戏就消失。"
                : "这已经是最后一关了 —— 回来刷满三星吧。";
        }

        // "下一关"只在真的有下一关可进时才可点
        SettlementNextButton.IsEnabled = NextLevelOf(level) is not null;

        SettlementOverlay.Visibility = Visibility.Visible;
        RefreshLevelList();
    }

    private void HideSettlementOverlay()
        => SettlementOverlay.Visibility = Visibility.Collapsed;

    private void OnSettlementCloseClick(object sender, RoutedEventArgs e)
        => HideSettlementOverlay();

    /// <summary>结算卡片上的"重开这一关"：和主界面小黄脸走同一条路径（关卡局重开同一关）。</summary>
    private void OnSettlementRetryClick(object sender, RoutedEventArgs e)
    {
        HideSettlementOverlay();
        RefreshItemChoiceOverlay();
        RestartCurrentGame();
    }

    /// <summary>
    /// 结算卡片上的"下一关"。到这里说明这一关已经通关并落盘，
    /// 所以下一关必然是解锁状态（<see cref="NextLevelOf"/> 自己会再校验一次）。
    /// </summary>
    private void OnSettlementNextClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.CurrentLevel is not { } level || NextLevelOf(level) is not { } next)
        {
            return;
        }

        HideSettlementOverlay();
        RefreshItemChoiceOverlay();
        StartLevel(next);
    }

    /// <summary>关卡目标条只在关卡模式显示。</summary>
    // ------------------------------------------------------------ 关于游戏 / 关于作者
    //
    // 两个页面都用**独立的固定尺寸小窗口**打开：尺寸对齐 Windows 的"关于 Windows"，
    // 并且不可缩放、不可最大化、不可最小化（见 Views/AboutWindowChrome.cs）。
    //
    // 每个窗口里各藏了一个彩蛋，刻意不做任何提示：
    //   关于游戏 → 连点「版本号」10 下
    //   关于作者 → 连点「主开发头像」3 下
    // 触发逻辑在各自窗口的 code-behind 里，这里只负责开窗。

    /// <summary>本次运行摇出来的隐藏关（雷数与种子随机，关掉游戏就没了）。</summary>
    private LevelDefinition? _secretLevel;

    private AboutGameWindow? _aboutGameWindow;
    private AboutAuthorsWindow? _aboutAuthorsWindow;
    private string? _aboutCheckMode;

    private void OnAboutGameClick(object sender, RoutedEventArgs e)
    {
        if (_aboutGameWindow is null)
        {
            _aboutGameWindow = new AboutGameWindow(_sound, _notifier);
            _aboutGameWindow.Closed += (_, _) => _aboutGameWindow = null;
        }

        _aboutGameWindow.Activate();
    }

    private void OnAboutAuthorsClick(object sender, RoutedEventArgs e)
    {
        if (_aboutAuthorsWindow is null)
        {
            _aboutAuthorsWindow = new AboutAuthorsWindow(_sound, _notifier, UnlockSecretLevel);
            _aboutAuthorsWindow.Closed += (_, _) => _aboutAuthorsWindow = null;
        }

        _aboutAuthorsWindow.Activate();
    }

    /// <summary>
    /// 解锁隐藏关（由"关于作者"里 Shinichi 的大彩蛋通过回调触发）并立刻落盘。
    ///
    /// 隐藏关不在 18 关的链条里，所以这里只改一个标志位；关卡面板每次打开都会重建，
    /// 下次打开时最下面就会多出一组"隐藏关卡"。
    /// </summary>
    private void UnlockSecretLevel()
    {
        if (_secretLevel is not null)
        {
            return;   // 本次运行内已经触发过，幂等
        }

        // 每次触发都重新摇：雷数 101..150、种子也随机 → 盘面每次都不一样
        _secretLevel = LevelTable.CreateSecret();
        _storage.LevelProgress.SecretUnlocked = true;

        // 刻意**不落盘**：隐藏关只在本次运行内有效，关掉游戏就消失（存档里也不留痕迹）
        AppPaths.Log(
            $"[LEVEL] 已解锁隐藏关卡 {_secretLevel.DisplayName}（{_secretLevel.BoardText}，种子 {_secretLevel.Seed}）" +
            "；本次运行内有效，不落盘。");
    }

    /// <summary>
    /// 让主窗口跟着棋盘尺寸走：棋盘多大就给多大，最多不超过屏幕可用区域的 94%。
    ///
    /// 以前窗口是写死的 1180×900：玩 9×9 时右边和下面空出一大片；玩 1920×1080 的自定义盘
    /// 又装不下。现在按"棋盘格宽 26 DIP + 界面装饰"算一个目标尺寸再夹到屏幕里。
    /// </summary>
    private void FitWindowToBoard(GameSession session)
    {
        try
        {
            // 窗口处于最大化 / 最小化（不是"还原"）状态时**绝对不要动它的尺寸**：
            // 否则玩家点一下笑脸重开，最大化就会被还原掉——这是玩家报上来的 bug。
            if (AppWindow.Presenter is OverlappedPresenter state &&
                state.State != OverlappedPresenterState.Restored)
            {
                return;
            }

            const double CellSize = 26;      // 与 BoardView 的默认格宽一致
            const double ChromeWidth = 150;  // 左右边框 + 边距 + 物品栏

            // 状态栏 + 关卡目标条 + **三行按钮**（难度/关卡模式、上一关下一关、设置那一行）+ 内外边距。
            //
            // 这个数必须跟着底部控件一起改：主界面第 1 行的高度是 *（吃掉剩余空间），
            // 所以"界面装饰"算少了不会把棋盘压小，而是把**底部控件按顺序挤出窗口可视区**。
            // 实测：只给两行按钮的预算（300）时，第三行（关卡导航）完全看不见 ——
            // 而且它是被"压到高度 0"而不是消失，很难一眼看出来。
            // 量法：SAOLEI_LEVELS=layout，或把窗口拉大后量"最后一行控件的底边 + 边距"。
            const double ChromeHeight = 380;

            double targetWidth = (session.Board.Width * CellSize) + ChromeWidth;
            double targetHeight = (session.Board.Height * CellSize) + ChromeHeight;

            double scale = GetWindowScale();
            DisplayArea area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
            double maxWidth = (area.WorkArea.Width / scale) * 0.94;
            double maxHeight = (area.WorkArea.Height / scale) * 0.94;

            // 下限要能装下状态栏与两行按钮，否则界面会被裁掉
            targetWidth = Math.Clamp(targetWidth, 700, maxWidth);
            targetHeight = Math.Clamp(targetHeight, 540, maxHeight);

            AppWindow.Resize(new SizeInt32(
                (int)Math.Round(targetWidth * scale),
                (int)Math.Round(targetHeight * scale)));
        }
        catch (Exception ex)
        {
            AppPaths.Log($"按棋盘调整窗口尺寸失败：{ex.GetType().Name}：{ex.Message}");
        }
    }

    /// <summary>取窗口缩放比例。用 GetDpiForWindow 而不是 XamlRoot——构造期拿不到 XamlRoot。</summary>
    private double GetWindowScale()
    {
        try
        {
            uint dpi = GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this));
            if (dpi > 0)
            {
                return dpi / 96.0;
            }
        }
        catch (Exception)
        {
            // 落到 XamlRoot 兜底
        }

        double fallback = Content?.XamlRoot?.RasterizationScale ?? 1.0;
        return fallback > 0 ? fallback : 1.0;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint hwnd);

    /// <summary>主窗口关闭时把两个关于窗口一起关掉，别留下孤儿窗口。</summary>
    private void CloseAboutWindows()
    {
        try
        {
            _aboutGameWindow?.Close();
        }
        catch (Exception)
        {
            // 关闭阶段出错就算了
        }

        try
        {
            _aboutAuthorsWindow?.Close();
        }
        catch (Exception)
        {
        }
    }

    // ------------------------------------------------------------ 关于窗口自检（环境变量 SAOLEI_ABOUT）

    private void StartAboutCheckIfRequested()
    {
        string? mode = Environment.GetEnvironmentVariable("SAOLEI_ABOUT");
        if (!string.IsNullOrWhiteSpace(mode))
        {
            _aboutCheckMode = mode.Trim().ToLowerInvariant();
        }
    }

    /// <summary>
    /// SAOLEI_ABOUT = game / authors / game-egg / authors-egg。
    /// 彩蛋没法从外面点，所以带 -egg 的模式会调窗口里的 ForceUnlockEggForCheck()，
    /// 它内部走的是和玩家连点**完全一样**的 TryUnlockEgg()。
    /// </summary>
    private void RunAboutCheck(string mode)
    {
        bool authors = mode.StartsWith("authors", StringComparison.Ordinal);

        if (authors)
        {
            OnAboutAuthorsClick(this, new RoutedEventArgs());
        }
        else
        {
            OnAboutGameClick(this, new RoutedEventArgs());
        }

        if (mode.EndsWith("-tooearly", StringComparison.Ordinal))
        {
            // 只点 Shinichi、不点 Fastrof：应当只出小彩蛋，且隐藏关不解锁
            _aboutAuthorsWindow?.ForceTooEarlyForCheck();
            RefreshLevelList();
            LevelPanel.Visibility = Visibility.Visible;
            AppPaths.Log($"[ABOUTCHECK] 顺序不对时：关卡按钮 {CountLevelButtons()} 个（期望 18 = 没有隐藏关）");
        }
        else if (mode.EndsWith("-bigegg-panel", StringComparison.Ordinal))
        {
            // 同一个进程里：先触发大彩蛋，再打开关卡面板数按钮
            _aboutAuthorsWindow?.ForceUnlockEggForCheck(big: true);
            RefreshLevelList();
            LevelPanel.Visibility = Visibility.Visible;
            AppPaths.Log($"[ABOUTCHECK] 大彩蛋之后：关卡按钮 {CountLevelButtons()} 个（期望 19 = 18 主线 + 1 隐藏）");
        }
        else if (mode.EndsWith("-bigegg", StringComparison.Ordinal))
        {
            _aboutAuthorsWindow?.ForceUnlockEggForCheck(big: true);
        }
        else if (mode.EndsWith("-egg", StringComparison.Ordinal))
        {
            if (authors)
            {
                _aboutAuthorsWindow?.ForceUnlockEggForCheck();
            }
            else
            {
                _aboutGameWindow?.ForceUnlockEggForCheck();
            }
        }

        AppPaths.Log(
            $"[ABOUTCHECK] mode={mode} · 关于游戏窗口={(_aboutGameWindow is null ? "未开" : "已开")} · " +
            $"关于作者窗口={(_aboutAuthorsWindow is null ? "未开" : "已开")} · 仓库地址='{AboutInfo.RepositoryText}'");
    }
    /// <summary>关卡目标条只在关卡模式显示。</summary>
    private void UpdateLevelGoalBar()
        => LevelGoalBar.Visibility = ViewModel.IsLevelRun ? Visibility.Visible : Visibility.Collapsed;

    // ------------------------------------------------------------ 关卡模式自检
    //
    // 关卡模式的关键路径（面板构建 → 开始关卡 → 通关结算 → 解锁 → 失败重试）
    // 需要真的"玩完一局"才能走到。这台机器上合成输入送不进应用（前台锁定/UIPI），
    // 所以用环境变量 SAOLEI_LEVELS 启动自检：它直接调 Core 的 Reveal 把一局打通/打输，
    // 剩下的（状态机 → HandleGameOver → 结算 → 落盘 → 刷新面板）走的是<b>和真人玩完全一样</b>的代码路径。

    private string? _levelCheckMode;
    private int _levelCheckStage;
    private int _levelCheckButtons;
    private int _levelCheckBoardHash;

    /// <summary>SAOLEI_LEVELS=layout 用：逐个高度试到"三段都不被压扁"为止。</summary>
    private int _layoutProbeHeight = 500;
    private double _layoutProbeScale = 1.0;

    /// <summary>
    /// 启动时清掉上一次会话遗留的通知。
    ///
    /// **为什么必须做**：Toast 是<b>排队</b>的——每条都要等前一条消失。旧版本每条通知各占一个槽位，
    /// 玩几局就会在操作中心里攒下一串没看完的通知；之后所有新通知都得排在它们后面，
    /// 玩家感觉"通知跟不上操作，还得等前一条关掉"。所以启动先清空一次。
    /// </summary>
    private async void ClearStaleNotificationsAsync()
    {
        try
        {
            await Microsoft.Windows.AppNotifications.AppNotificationManager.Default.RemoveAllAsync();
            AppPaths.Log("[通知] 已清掉上一次会话遗留的通知（避免新通知排在它们后面）。");
        }
        catch (Exception ex)
        {
            AppPaths.Log($"[通知] 清理遗留通知失败（不影响游戏）：{ex.GetType().Name}：{ex.Message}");
        }
    }

    // ------------------------------------------------------------ 通知"替换而不是排队"自检
    //
    // SAOLEI_TOASTBURST=1：先发一条 Warning，隔 400ms 再发一条 Error，然后查操作中心剩几条。
    // 共用同一个 Tag/Group 时应当只剩 1 条（= 后发的那条替换掉了先发的），而不是 2 条排队。

    private bool _toastBurstPending;

    private void StartToastBurstCheckIfRequested()
    {
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SAOLEI_TOASTBURST")))
        {
            _toastBurstPending = true;
        }
    }

    private async void RunToastBurstCheckAsync()
    {
        _notifier.Publish(GameNotification.Warning(
            "chord 未执行",
            "第一条：如果通知会排队，它会占着位置不放。",
            "toastburst.first"));

        await Task.Delay(400);

        _notifier.Publish(GameNotification.Error(
            "踩雷失败",
            "第二条：这一条应当立刻顶掉上一条并响铃。",
            "toastburst.second"));

        await Task.Delay(1500);

        try
        {
            IList<Microsoft.Windows.AppNotifications.AppNotification> all =
                await Microsoft.Windows.AppNotifications.AppNotificationManager.Default.GetAllAsync();

            var mine = new List<Microsoft.Windows.AppNotifications.AppNotification>();
            foreach (Microsoft.Windows.AppNotifications.AppNotification item in all)
            {
                if (item.Tag == ToastNotifier.NotificationSlotTag && item.Group == ToastNotifier.NotificationSlotGroup)
                {
                    mine.Add(item);
                }
            }

            AppPaths.Log($"[TOASTBURST] 操作中心共 {all.Count} 条，其中属于本作槽位（{ToastNotifier.NotificationSlotTag}/{ToastNotifier.NotificationSlotGroup}）的有 {mine.Count} 条");

            foreach (Microsoft.Windows.AppNotifications.AppNotification item in mine)
            {
                string payload = item.Payload ?? string.Empty;
                AppPaths.Log($"[TOASTBURST]   id={item.Id} 是后发的那条={payload.Contains("第二条", StringComparison.Ordinal)}");
            }

            AppPaths.Log(mine.Count <= 1
                ? "[TOASTBURST] 结论：只占一个槽位 → 新通知替换旧的，不会排队 ✓"
                : "[TOASTBURST] 结论：本作通知出现多条 → 仍在排队 ✗");
        }
        catch (Exception ex)
        {
            AppPaths.Log($"[TOASTBURST] 查询失败：{ex.GetType().Name}：{ex.Message}");
        }
    }

    // ------------------------------------------------------------ 指定难度开局自检
    //
    // SAOLEI_PRESET=beginner|intermediate|expert 启动时直接开对应难度，
    // 也可以给自定义参数：SAOLEI_PRESET=30x20/120。
    // 用于验证"笑脸重开会沿用当前难度"这类需求（否则无输入环境里只能开到默认的初级）。

    private void StartPresetCheckIfRequested()
    {
        string? value = Environment.GetEnvironmentVariable("SAOLEI_PRESET");
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        string key = value.Trim().ToLowerInvariant();

        // 自定义参数写法：宽x高/雷数
        if (key.Contains('x') && key.Contains('/'))
        {
            string[] sizeAndMines = key.Split('/');
            string[] size = sizeAndMines[0].Split('x');

            if (size.Length == 2 &&
                int.TryParse(size[0], out int customWidth) &&
                int.TryParse(size[1], out int customHeight) &&
                int.TryParse(sizeAndMines[1], out int customMines))
            {
                if (ViewModel.StartNewGame(
                        CurrentOptions(),
                        customWidth,
                        customHeight,
                        customMines,
                        TryReadSeed(),
                        difficultyId: null,
                        out string? customError))
                {
                    AttachSession();
                    AppPaths.Log($"[PRESETCHECK] 已按 SAOLEI_PRESET 开自定义局：{DescribeCurrentGame()}");
                }
                else
                {
                    AppPaths.Log($"[PRESETCHECK] SAOLEI_PRESET 自定义局参数非法：{customError}");
                }

                return;
            }
        }

        DifficultyPreset preset = key switch
        {
            "intermediate" or "middle" => DifficultyPreset.Intermediate,
            "expert" or "hard" => DifficultyPreset.Expert,
            _ => DifficultyPreset.Beginner,
        };

        StartPreset(preset);
        AppPaths.Log($"[PRESETCHECK] 已按 SAOLEI_PRESET 开局：{DescribeCurrentGame()}");
    }

    // ------------------------------------------------------------ 重开自检
    //
    // "窗口最大化时点笑脸会把最大化还原掉"这条没法在无输入环境里点笑脸，
    // 所以用 SAOLEI_NEWGAME=1 启动：等设备就绪后调一次 StartPreset，
    // 并记录重开前后的窗口状态（[NEWGAME] 行）。

    private bool _newGameCheckPending;

    private DateTimeOffset _newGameCheckDueAt;

    private void StartNewGameCheckIfRequested()
    {
        string? value = Environment.GetEnvironmentVariable("SAOLEI_NEWGAME");
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        // 值 = 延迟秒数（默认 5 秒）。留出时间让外部脚本"先把窗口最大化"，
        // 这样才能验证"重开不会把最大化还原掉"。
        double delay = double.TryParse(value, out double seconds) && seconds > 0 ? seconds : 5;
        _newGameCheckDueAt = DateTimeOffset.Now.AddSeconds(delay);
        _newGameCheckPending = true;
    }

    // ------------------------------------------------------------ 通知精简自检
    //
    // 换主题、开关设置、新开一局以前都会弹 Toast。这类动作没法在无输入环境里触发，
    // 所以用 SAOLEI_NOTIFY=1 启动自检：把它们逐个做一遍，然后报告"新增弹窗几条"。

    private bool _notifyCheckPending;

    private void StartNotifyCheckIfRequested()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SAOLEI_NOTIFY")))
        {
            return;
        }

        _notifyCheckPending = true;
    }

    private void RunNotifyCheck()
    {
        int toastsBefore = _notifier.ShownTotal;
        int quietBefore = _notifier.QuietTotal;

        // 1) 换主题（以前会弹"已切换主题"）
        ThemeCombo.SelectedIndex = ThemeCombo.SelectedIndex == 1 ? 0 : 1;

        // 2) 开关设置（以前会弹"已开启/关闭锁定盘面"）
        LockedBoardToggle.IsOn = !LockedBoardToggle.IsOn;
        HeatmapToggle.IsOn = !HeatmapToggle.IsOn;

        // 3) 新开一局（以前会弹"新局：初级"）
        StartPreset(DifficultyPreset.Beginner);

        AppPaths.Log(
            $"[NOTIFYCHECK] 换主题 + 开关设置 + 新开一局：新增弹窗 {_notifier.ShownTotal - toastsBefore} 条" +
            $"（期望 0），新增只入日志 {_notifier.QuietTotal - quietBefore} 条（期望 3）");
        AppPaths.Log($"[NOTIFYCHECK] 累计：弹窗 {_notifier.ShownTotal} 条 · 只入日志 {_notifier.QuietTotal} 条");
    }

    private void StartLevelCheckIfRequested()
    {
        string? mode = Environment.GetEnvironmentVariable("SAOLEI_LEVELS");
        if (string.IsNullOrWhiteSpace(mode))
        {
            return;
        }

        _levelCheckMode = mode.Trim().ToLowerInvariant();

        if (_levelCheckMode == "panel")
        {
            // 跑完整套自检，但停在"关卡面板打开"那一步（便于截图核对）
            _levelCheckStage = 1;
            return;
        }

        if (_levelCheckMode == "ingame")
        {
            // 直接进第 1 关并**保持游戏画面**：用来核对游戏内的"上一关 / 下一关"按钮
            StartLevel(LevelTable.First);
            _levelCheckStage = 0;
            return;
        }

        if (_levelCheckMode == "layout")
        {
            // 量"最小合适窗口高度"：把窗口逐档加高，直到底部的操作区不再被压扁
            StartLevel(LevelTable.First);
            _layoutProbeScale = GetWindowScale();
            _layoutProbeHeight = 460;
            ApplyLayoutProbeHeight();
            _levelCheckStage = 200;
            return;
        }

        if (_levelCheckMode == "play")
        {
            // 直接进第 1 关，供截图（关卡目标条）
            StartLevel(LevelTable.First);
            return;
        }

        if (_levelCheckMode == "secret")
        {
            // 直接摇一个隐藏关并进去，供截图
            UnlockSecretLevel();
            if (_secretLevel is not null)
            {
                StartLevel(_secretLevel);
            }

            return;
        }

        // check：逐帧推进的端到端自检
        // clicksweep：临时自检 —— 逐格左击扫描，检查"单击已翻开格是否误触展开"
        if (_levelCheckMode == "clicksweep")
        {
            StartPreset(DifficultyPreset.Beginner);
            _levelCheckStage = 900;
            return;
        }

        _levelCheckStage = 1;
    }

    // ------------------------------------------------------------ 临时自检：单击行为扫描
    //
    // 用一个真实的 9×9 盘，先左击一格开出一片，然后**逐格**走一遍左键入口。
    // 任何"该格已翻开且邻雷>=1，但已翻开格数增加了"都是 bug；
    // 已翻开的 0 格不可能 chord（Core 会拒绝），所以那种 0 变化是正常的。
    private int _clickSweepStage;
    private int _clickSweepBad;
    private int _sweepQuietBaseline;
    private DateTimeOffset _clickSweepArmAt;

    private void AdvanceClickSweep()
    {
        if (!_board.IsDeviceReady)
        {
            return;
        }

        if (_clickSweepStage == 0)
        {
            _clickSweepStage = 1;
            _clickSweepArmAt = DateTimeOffset.UtcNow;
            return;
        }

        if ((DateTimeOffset.UtcNow - _clickSweepArmAt).TotalMilliseconds < 400)
        {
            return;
        }

        _clickSweepArmAt = DateTimeOffset.UtcNow;

        if (_clickSweepStage == 1)
        {
            GameSession session = ViewModel.Session!;
            Board board = session.Board;

            // 第一次左击：正常开一片（用棋盘中心附近的一点）
            if (_board.TryGetCellCenterForProbe(board.Index(board.Width / 2, board.Height / 2), out double fx, out double fy))
            {
                _board.ClickAtForProbe(fx, fy);
            }

            _sweepQuietBaseline = session.RevealedCount;
            AppPaths.Log(
                $"[CLICKSWEEP] 第一次左击后：已翻开={session.RevealedCount} 格，状态={session.Status}，" +
                $"盘面={board.Width}×{board.Height}，雷={board.MineCount}，" +
                $"挂起展开={session.HasPendingFlood}");
            _clickSweepStage = 15;
            return;
        }

        // 关键观测：点一次之后**不再有任何输入**，看棋盘会不会自己继续展开。
        // 分帧洪水填充是异步推进的，如果它在点击返回之后还继续动棋盘，
        // 玩家看到的就是"我只点了一下，别的地方自己开了"。
        if (_clickSweepStage == 15)
        {
            GameSession session = ViewModel.Session!;
            AppPaths.Log(
                $"[CLICKSWEEP] 静置 3 秒后：已翻开={session.RevealedCount} 格" +
                $"（点击时 {_sweepQuietBaseline}，Δ={session.RevealedCount - _sweepQuietBaseline}），" +
                $"状态={session.Status}，挂起展开={session.HasPendingFlood}");

            if (session.RevealedCount != _sweepQuietBaseline)
            {
                AppPaths.Log(
                    $"[CLICKSWEEP] ✗ BUG：没有任何输入，棋盘自己又多开了 " +
                    $"{session.RevealedCount - _sweepQuietBaseline} 格 —— 分帧展开在点击之后仍在继续动棋盘");
            }

            _clickSweepStage = 2;
            return;
        }

        if (_clickSweepStage == 2)
        {
            GameSession session = ViewModel.Session!;
            Board board = session.Board;
            _clickSweepBad = 0;
            int revealedCellsClicked = 0;
            int numberCellsClicked = 0;

            // 先扫描一遍"双击已翻开格"（玩家报的场景：双击空白区域会连带展开）
            for (int i = 0; i < board.CellCount; i++)
            {
                if (session.Status is GameStatus.Won or GameStatus.Lost)
                {
                    break;
                }

                if (!board.IsRevealed(i))
                {
                    continue;
                }

                int adjacent = board.GetAdjacentMines(i);
                int before = session.RevealedCount;
                string info = _board.ProbeCellInfo(i);

                if (!_board.TryGetCellCenterForProbe(i, out double dx, out double dy))
                {
                    continue;
                }

                _board.DoubleClickAtForProbe(dx, dy);
                int dDelta = session.RevealedCount - before;
                bool dLost = session.Status == GameStatus.Lost;

                // 判据：双击"已翻开的 0 格（空白区域）"绝不该展开任何格子（原版 chord 对 0 格无意义）
                if (adjacent == 0 && dDelta != 0)
                {
                    _clickSweepBad++;
                    AppPaths.Log(
                        $"[CLICKSWEEP] ✗ BUG：双击已翻开【空白格】index={i} 却展开了 {dDelta} 格 —— {info}");
                }

                if (adjacent == 0 || dDelta != 0 || dLost)
                {
                    AppPaths.Log(
                        $"[CLICKSWEEP] 双击已翻开格 index={i}：{info}，已翻开 {before}→{session.RevealedCount}" +
                        $"（Δ={dDelta}）{(dLost ? " ← 炸了" : string.Empty)}");
                }
            }

            AppPaths.Log(
                $"[CLICKSWEEP] 双击扫描完成：空白格误展开={_clickSweepBad}（期望 0），本局状态={session.Status}");

            // 再扫描一遍"单击已翻开格"（确认单击始终是静默 no-op）
            int singleBad = 0;
            for (int i = 0; i < board.CellCount; i++)
            {
                if (session.Status is GameStatus.Won or GameStatus.Lost)
                {
                    break;
                }

                if (!board.IsRevealed(i))
                {
                    continue;
                }

                int adjacent = board.GetAdjacentMines(i);
                int before = session.RevealedCount;
                string info = _board.ProbeCellInfo(i);

                if (!_board.TryGetCellCenterForProbe(i, out double cx, out double cy))
                {
                    continue;
                }

                _board.ClickAtForProbe(cx, cy);
                int delta = session.RevealedCount - before;
                bool nowLost = session.Status == GameStatus.Lost;

                revealedCellsClicked++;
                if (adjacent >= 1)
                {
                    numberCellsClicked++;
                }

                if (adjacent >= 1 && (delta != 0 || nowLost))
                {
                    singleBad++;
                    AppPaths.Log($"[CLICKSWEEP] ✗ BUG：单击已翻开数字格 index={i} 连带展开了（Δ={delta}） —— {info}");
                }
            }

            AppPaths.Log(
                $"[CLICKSWEEP] 单击扫描：共 {revealedCellsClicked} 个已翻开格（数字格 {numberCellsClicked}），" +
                $"误触展开={singleBad}（期望 0）");
            AppPaths.Log(
                $"[CLICKSWEEP] 结论：双击空白格误展开={_clickSweepBad}（期望 0）、单击误触展开={singleBad}（期望 0）");
            _clickSweepStage = 0;
            _levelCheckStage = 0;
        }
    }

    private void AdvanceLevelCheck()
    {
        if (_levelCheckStage == 0)
        {
            return;
        }

        if (_levelCheckStage == 900)
        {
            AdvanceClickSweep();
            return;
        }

        if (_levelCheckStage == 200)
        {
            AdvanceLayoutProbe();
            return;
        }

        // 等 Win2D 设备就绪再动手（否则第一帧还没画，面板/目标条的可见性判断不准）
        if (!_board.IsDeviceReady && _levelCheckStage == 1)
        {
            return;
        }

        LevelDefinition level = LevelTable.First;

        switch (_levelCheckStage)
        {
            case 1:
                OnLevelsClick(this, new RoutedEventArgs());
                _levelCheckButtons = CountLevelButtons();
                AppPaths.Log($"[LEVELCHECK] 面板构建完成：关卡按钮 {_levelCheckButtons} 个（期望 {LevelTable.Count}），章节标题 {LevelListPanel.Children.Count} 个顶层元素");

                // 面板上**不该**再有"上一关 / 下一关"（它们已经挪进游戏界面了）。
                // 这里顺便守一下：面板里的按钮只有"返回游戏"那一个。
                AppPaths.Log(
                    $"[LEVELCHECK] 关卡面板工具条按钮数={CountPanelToolbarButtons()}（期望 1 —— 只剩『返回游戏』）");

                // 还没进关卡局：游戏内的前后跳按钮应当整组藏起来
                AppPaths.Log(
                    $"[LEVELCHECK] 未进关卡时游戏内前后跳按钮：导航行={LevelNavRow.Visibility}（期望 Collapsed）");

                StartLevel(level);
                GameSession session = ViewModel.Session!;
                _levelCheckBoardHash = HashMines(session);
                AppPaths.Log(
                    $"[LEVELCHECK] 已开始 {level.DisplayName}：锁定={session.Locked}, seed={session.Seed}, " +
                    $"盘面={session.Board.Width}×{session.Board.Height}, 雷={session.Board.GetMineIndexes().Length}, " +
                    $"状态={session.Status}, 已翻开={session.RevealedCount}, 首点已翻开={session.Board.IsRevealed(session.Board.FirstClickIndex)}, " +
                    $"雷位指纹={_levelCheckBoardHash}, 关卡道具={ViewModel.ActiveInventory.MagnifierCount}, " +
                    $"目标条={LevelGoalBar.Visibility}, 目标文案={(ViewModel.LevelGoalText.Length > 0 ? "有" : "空")}");

                // 进入关卡局之后：整组按钮应当出现，可用性按当前进度算（不是写死）
                AppPaths.Log(
                    $"[LEVELCHECK] 进入 {level.DisplayName} 后游戏内前后跳按钮：导航行={LevelNavRow.Visibility}" +
                    $"（期望 Visible），上一关可用={PreviousLevelButton.IsEnabled}，下一关可用={NextLevelButton.IsEnabled}");

                LogLayoutMetrics();
                _levelCheckStage = 2;
                break;

            case 2:
                // 造一次"通关"：把所有非雷格翻开（走的是 GameSession 的正式 API）
                RevealAllSafeCells();
                GameSession afterReveal = ViewModel.Session!;
                AppPaths.Log(
                    $"[LEVELCHECK] 造通关明细：已翻开={afterReveal.RevealedCount} / 安全格应为 " +
                    $"{afterReveal.Board.CellCount - afterReveal.Board.MineCount}，状态={afterReveal.Status}，" +
                    $"还有挂起展开={afterReveal.HasPendingFlood}");
                _levelCheckStage = 3;
                break;

            case 3:
                // 等 Tick 里的 HandleGameOver 先结算（评星 / 解锁 / 落盘）再往下走。
                // 不在这里干等的话，下面一句 StartLevel 会在同一帧把刚赢的那局换掉，
                // 结算就永远轮不到它——表现是"状态=Won，但进度里没记录"。
                // 注意要等的是**结算整个走完**（`_levelSettleCompleted`），不是"游戏结束被处理过"：
                // 后者在调用 HandleGameOver 之前就置位了，万一一帧里结算中途异常退出，
                // 就会把"标志为真、弹窗还没弹"这种半成品状态读成最终结果（曾经误报过一次）。
                if (!_gameOverHandled || !_levelSettleCompleted)
                {
                    return;
                }

                AppPaths.Log(
                    $"[LEVELCHECK] 造通关后：状态={ViewModel.Session!.Status}, 用时={ViewModel.Session.ElapsedSeconds:F1}s, " +
                    $"进度文件存在={File.Exists(AppPaths.LevelProgressFile)}");
                AppPaths.Log($"[LEVELCHECK] 通关后的进度：{DescribeLevelProgress()}");

                // 胜利结算小弹窗应当已经弹出，且内容与这一局一致
                AppPaths.Log(
                    $"[LEVELCHECK] 结算弹窗：可见={SettlementOverlay.Visibility}（期望 Visible），" +
                    $"标题=\"{SettlementTitle.Text}\", 星级=\"{SettlementStars.Text}\", " +
                    $"下一关按钮可用={SettlementNextButton.IsEnabled}（期望 True —— 刚解锁 1-2）");
                AppPaths.Log($"[LEVELCHECK] 结算弹窗正文：{SettlementSummary.Text.Replace(Environment.NewLine, " / ")}");
                AppPaths.Log($"[LEVELCHECK] 结算弹窗提示：{SettlementHint.Text}");

                // 通关后前后跳按钮应当都活了
                RefreshLevelList();
                AppPaths.Log(
                    $"[LEVELCHECK] 前后跳按钮（通关 1-1 后）：上一关可用={PreviousLevelButton.IsEnabled}（期望 False），" +
                    $"下一关可用={NextLevelButton.IsEnabled}（期望 True）");

                // 重开同一关，检查盘面是否完全一致（关卡制的核心承诺）
                StartLevel(level);
                int again = HashMines(ViewModel.Session!);
                AppPaths.Log(
                    $"[LEVELCHECK] 重开同一关：雷位指纹={again}, 与首次一致={again == _levelCheckBoardHash}, " +
                    $"尝试次数={_storage.LevelProgress.Find(level.Id)?.Attempts}，" +
                    $"结算弹窗已收起={SettlementOverlay.Visibility == Visibility.Collapsed}");
                _levelCheckStage = 4;
                break;

            case 4:
                // 造一次"失败"：点一颗雷
                RevealOneMine();
                _levelCheckStage = 5;
                break;

            case 5:
                AppPaths.Log(
                    $"[LEVELCHECK] 造失败后：状态={ViewModel.Session!.Status}, 星数未变={_storage.LevelProgress.StarsOf(level.Id)}，" +
                    $"结算弹窗可见={SettlementOverlay.Visibility}（期望 Collapsed —— 失败不弹结算）");
                AppPaths.Log($"[LEVELCHECK] 失败后的进度：{DescribeLevelProgress()}");

                // 通关后（还没离开这一局）：游戏内的"下一关"应当亮了
                AppPaths.Log(
                    $"[LEVELCHECK] 通关 1-1 后游戏内前后跳按钮：上一关可用={PreviousLevelButton.IsEnabled}（期望 False —— 第一关），" +
                    $"下一关可用={NextLevelButton.IsEnabled}（期望 True —— 1-2 刚解锁）");

                // 收尾：把面板打开，方便截图看星数与解锁状态
                RefreshLevelList();
                LevelPanel.Visibility = Visibility.Visible;
                AppPaths.Log("[LEVELCHECK] 完成。");

                // panel 模式：停在"关卡面板打开"，供截图核对
                if (_levelCheckMode == "panel")
                {
                    _levelCheckStage = 0;
                    break;
                }

                // 普通 check：再往下走两步，把游戏内的"上一关 / 下一关"按钮真的点一次
                _levelCheckStage = 6;
                break;

            case 6:
                // 点"下一关 ▶"：应当进入当前关卡的下一关（1-1 通关后就是 1-2）
                {
                    LevelDefinition? expectNext = NextLevelOf(ViewModel.CurrentLevel ?? LevelTable.First);
                    OnNextLevelClick(this, new RoutedEventArgs());
                    AppPaths.Log(
                        $"[LEVELCHECK] 点游戏内『下一关』后：当前关卡={ViewModel.CurrentLevel?.DisplayName}" +
                        $"（期望 {expectNext?.DisplayName}），锁定={ViewModel.Session?.Locked}, " +
                        $"已翻开={ViewModel.Session?.RevealedCount}, 关卡面板={LevelPanel.Visibility}（期望 Collapsed —— 不该弹面板）");
                    _levelCheckStage = 7;
                }

                break;

            case 7:
                // 再点游戏内"上一关 ◀"：应当回到刚才那一关，而且盘面还是同一张
                OnPreviousLevelClick(this, new RoutedEventArgs());
                AppPaths.Log(
                    $"[LEVELCHECK] 点游戏内『上一关』后：当前关卡={ViewModel.CurrentLevel?.DisplayName}（期望 {level.DisplayName}），" +
                    $"同盘指纹={HashMines(ViewModel.Session!) == _levelCheckBoardHash}（期望 True —— 回到的还是那张固定的盘）");

                RefreshLevelList();
                LevelPanel.Visibility = Visibility.Visible;
                _levelCheckStage = 0;
                break;
        }
    }

    /// <summary>
    /// 把主界面各段的实际排版高度写进日志（DIP）。
    ///
    /// 为什么要这个：主界面第 1 行是 <c>*</c>，会吃掉剩余空间 —— 所以
    /// <c>FitWindowToBoard</c> 里"界面装饰"的预算算少了，症状不是棋盘变小，
    /// 而是**底部按钮被顶出窗口可视区**（实测踩过：加进关卡导航那一行之后按钮就看不见了）。
    /// 这条日志把 状态栏 / 棋盘区 / 底部操作区 / 窗口客户区 四个高度都报出来，
    /// 一眼就能看出预算差多少。
    /// </summary>
    private void LogLayoutMetrics()
    {
        // 先强制走一次布局：刚把 Collapsed 改成 Visible 的元素，ActualHeight 要等一次
        // 布局过程才会更新，不然量到的还是旧的 0。
        RootGrid.UpdateLayout();

        double statusHeight = RootGrid.RowDefinitions.Count > 0 ? RootGrid.RowDefinitions[0].ActualHeight : 0;
        double boardHeight = RootGrid.RowDefinitions.Count > 1 ? RootGrid.RowDefinitions[1].ActualHeight : 0;
        double bottomHeight = RootGrid.RowDefinitions.Count > 2 ? RootGrid.RowDefinitions[2].ActualHeight : 0;

        AppPaths.Log(
            $"[LAYOUT] 客户区={RootGrid.ActualWidth:F1}×{RootGrid.ActualHeight:F1} DIP · " +
            $"状态栏={statusHeight:F1} · 棋盘区={boardHeight:F1}（棋盘控件={_board.ActualHeight:F1}，BoardHost={BoardHost.ActualHeight:F1}）· " +
            $"底部操作区={bottomHeight:F1}（目标条={LevelGoalBar.ActualHeight:F1}，第一行按钮={LevelsButton.ActualHeight:F1}）· " +
            $"三段之和={statusHeight + boardHeight + bottomHeight:F1}");

        // 根 Grid 四行的真实高度：第 3 行就是关卡导航那一行
        var rows = new List<string>();
        for (int i = 0; i < RootGrid.RowDefinitions.Count; i++)
        {
            rows.Add($"R{i}={RootGrid.RowDefinitions[i].ActualHeight:F1}");
        }

        AppPaths.Log(
            $"[LAYOUT]   根 Grid 各行：{string.Join(" · ", rows)}" +
            $"（定义={string.Join(",", RootGrid.RowDefinitions.Select(r => r.Height.ToString()))}）· " +
            $"关卡导航行 y={NavRowTop():F1} h={LevelNavRow.ActualHeight:F1}");
    }

    /// <summary>关卡导航行相对客户区的位置（只用于布局日志）。</summary>
    private double NavRowTop()
    {
        try
        {
            return LevelNavRow.TransformToVisual(RootGrid)
                .TransformPoint(new Windows.Foundation.Point(0, 0)).Y;
        }
        catch (Exception)
        {
            return -1;
        }
    }

    /// <summary>
    /// 布局探针（SAOLEI_LEVELS=layout）：把窗口从 460 DIP 起逐档加高，
    /// 直到底部操作区**不再被压扁**，把那个高度写进日志。
    ///
    /// 为什么要它：主界面第 1 行是 * 且棋盘会被 MaxFitCellSize 放大，所以
    /// "界面装饰预算算少了"不会让棋盘变小，而是把底部按钮顶出窗口 —— 在外面猜不出来，
    /// 必须在应用里量。
    /// </summary>
    private void AdvanceLayoutProbe()
    {
        if (_levelCheckStage != 200)
        {
            return;
        }

        // 等布局真的量过一遍（ActualHeight 才有效）
        RootGrid.UpdateLayout();

        double bottomHeight = RootGrid.RowDefinitions[2].ActualHeight;
        double firstRowHeight = LevelsButton.ActualHeight;

        // 底部操作区"不被压扁"的判据：它在最底下，压缩必然先压它
        bool bottomOk = bottomHeight >= 110 && RootGrid.RowDefinitions[3].ActualHeight > 0;

        AppPaths.Log(
            $"[LAYOUTPROBE] 窗口高 {_layoutProbeHeight} DIP：客户区={RootGrid.ActualHeight:F1} · " +
            $"状态栏={RootGrid.RowDefinitions[0].ActualHeight:F1} · 棋盘区={RootGrid.RowDefinitions[1].ActualHeight:F1} · " +
            $"底部操作区={bottomHeight:F1} · 第一行按钮高={firstRowHeight:F1} · 达标={bottomOk}");

        LogLayoutMetrics();

        if (bottomOk)
        {
            AppPaths.Log($"[LAYOUTPROBE] 结论：{_layoutProbeHeight} DIP 已经装得下（再往上都是余量）。");
            _levelCheckStage = 0;
            return;
        }

        _layoutProbeHeight += 40;
        if (_layoutProbeHeight > 900)
        {
            AppPaths.Log("[LAYOUTPROBE] 试到 900 DIP 仍不达标。");
            _levelCheckStage = 0;
            return;
        }

        ApplyLayoutProbeHeight();
    }

    private void ApplyLayoutProbeHeight()
        => AppWindow.Resize(new SizeInt32(
            (int)Math.Round(700 * _layoutProbeScale),
            (int)Math.Round(_layoutProbeHeight * _layoutProbeScale)));

    private int CountLevelButtons()
    {
        int count = 0;
        foreach (object child in LevelListPanel.Children)
        {
            if (child is Grid grid)
            {
                foreach (object cell in grid.Children)
                {
                    if (cell is Button)
                    {
                        count++;
                    }
                }
            }
        }

        return count;
    }

    /// <summary>
    /// 关卡面板**工具条**上的按钮数。
    /// 用来守"上一关 / 下一关已经挪进游戏界面、面板上不该再留着它们"这件事。
    /// </summary>
    private int CountPanelToolbarButtons()
    {
        // 工具条是 LevelPanel > Grid > StackPanel（第一个子元素）
        if (LevelPanel.Child is not Grid root || root.Children.Count == 0 ||
            root.Children[0] is not StackPanel toolbar)
        {
            return -1;
        }

        int count = 0;
        foreach (object child in toolbar.Children)
        {
            if (child is Button)
            {
                count++;
            }
        }

        return count;
    }

    private static int HashMines(GameSession session)
    {
        var hash = new HashCode();
        foreach (int index in session.Board.GetMineIndexes())
        {
            hash.Add(index);
        }

        return hash.ToHashCode();
    }

    private void RevealAllSafeCells()
    {
        GameSession session = ViewModel.Session!;

        // 真人点格子时 BoardView 会抛 BoardStateChanged，窗口就是靠它把 _levelPlayerActed
        // 置位的（见 OnBoardStateChanged）。自检直接调 Core 的 Reveal，绕过了那一层，
        // 所以这里要自己补上——否则结算会把这一胜当成"开局即胜利"丢掉、不解锁下一关。
        _levelPlayerActed = true;

        // 第一次翻开 = "第一次开始扫雷"：记下起点、基线，并累计一次对局
        if (ViewModel.Session is { Status: GameStatus.InProgress } started && _sessionStartedAt is null)
        {
            _sessionStartedAt = DateTimeOffset.UtcNow;
            _correctFlagsAtSessionStart = ViewModel.ActiveInventory.Progress.CorrectMineFlagsTotal;
            _history.BeginGame(_sessionStartedAt.Value);
            _historyStore.Save(_history);
            UpdateItemSlots();
        }

        // 第一步必须按玩家真实路径走：锁定盘面（关卡）的首次 Reveal 会被钉到安全点上，
        // 也就是说"点 index=0"并不会真的翻开 index=0 —— 直接照索引顺序循环会漏掉那一格，
        // 于是永远差一格、判不了胜。这里先显式走掉首点，再翻剩下的。
        if (session.Status == GameStatus.NotStarted && session.Locked)
        {
            session.Reveal(session.Board.FirstClickIndex);
            PumpPendingFlood(session);
        }

        for (int index = 0; index < session.Board.Width * session.Board.Height; index++)
        {
            if (session.Board.IsMine(index) || session.Status is GameStatus.Won or GameStatus.Lost)
            {
                continue;
            }

            session.Reveal(index);

            // 一次 Reveal 可能翻开一大片，超过单次上限就挂起等分帧继续（§2.3）。
            // 挂起期间 GameSession 会拒绝后续 Reveal（"上一片区域还在展开"），
            // 所以这里必须像 UI 那样把分帧推进到底，否则整个循环只会翻开第一格。
            PumpPendingFlood(session);
        }
    }

    /// <summary>把挂起的展开一次推完（等价于 UI 定时器反复调 Tick，只是不按时间片限速）。</summary>
    private static void PumpPendingFlood(GameSession session)
    {
        while (session.HasPendingFlood)
        {
            session.ContinuePendingFlood(GameSession.FloodChunkPerFrame);
        }
    }

    private void RevealOneMine()
    {
        GameSession session = ViewModel.Session!;

        // 锁定盘面（关卡）的第一次翻开会被钉到安全点上，所以不能直接点雷——
        // 那样只会被挪到安全格，造不出"失败"。先走一步正常的安全首点把这次钉定用掉。
        if (session.Status == GameStatus.NotStarted && session.Locked)
        {
            session.Reveal(session.Board.FirstClickIndex);
            PumpPendingFlood(session);
        }

        foreach (int index in session.Board.GetMineIndexes())
        {
            session.Reveal(index);
            return;
        }
    }

    private string DescribeLevelProgress()
    {
        LevelProgress progress = _storage.LevelProgress;
        var parts = new List<string>
        {
            $"已通关 {progress.ClearedCount}/{LevelTable.Count}",
            $"星数 {progress.TotalStars}/{LevelTable.Count * LevelRules.MaxStars}",
            $"解锁数 {LevelRules.UnlockedCount(progress)}",
        };

        foreach (LevelDefinition level in LevelTable.All)
        {
            LevelRecord? record = progress.Find(level.Id);
            if (record is not null)
            {
                parts.Add(
                    $"{level.Id}:{(record.Cleared ? "通" : "未")}/★{record.Stars}/最好{record.BestSeconds:F1}s/试{record.Attempts}");
            }
        }

        return string.Join(" | ", parts);
    }
}
