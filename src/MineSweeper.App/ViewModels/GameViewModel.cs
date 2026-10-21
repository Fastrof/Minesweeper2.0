using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MineSweeper.App.Services;
using MineSweeper.Core;
using MineSweeper.Core.Items;
using MineSweeper.Core.Levels;
using MineSweeper.Core.Localization;
using MineSweeper.Core.Notifications;
using MineSweeper.Core.Persistence;
using Windows.ApplicationModel.DataTransfer;

namespace MineSweeper.App.ViewModels;

/// <summary>
/// 开一局需要的选项。抽成对象是为了不再往方法上挂 8 个位置参数
/// （那种签名最容易出现"两个 bool 传反了"却编译通过的问题）。
/// </summary>
/// <param name="Mode">模式（§5.5 ⑤）。</param>
/// <param name="FirstClickOpening">首点是否用扩展安全区（A-2）。</param>
/// <param name="PauseTimerOnFocusLoss">失焦是否暂停计时（A-4）。</param>
/// <param name="LockedBoard">是否锁定盘面（§2.6）。</param>
public sealed record GameStartOptions(
    GameMode Mode,
    bool FirstClickOpening,
    bool PauseTimerOnFocusLoss,
    bool LockedBoard);

/// <summary>
/// 主视图模型（§1.3 ViewModels/，用 CommunityToolkit.Mvvm 的源生成器）。
/// 状态栏文案、物品栏计数与进度条都在这里算，View 只负责显示。
/// 属性一律用 partial property（WinRT 场景下字段式写法会产生 AOT 警告 MVVMTK0045）。
///
/// 所有"要告诉玩家的事"都通过 <see cref="INotificationSink"/> 出去（当前落到 Windows Toast），
/// ViewModel 不再持有任何"提示条文案"状态。
/// </summary>
public partial class GameViewModel : ObservableObject
{
    /// <summary>单帧用于推进分帧展开的时间片（毫秒）。</summary>
    private const int FloodFrameBudgetMs = 8;

    private readonly AppStorage _storage;
    private readonly INotificationSink _notifier;
    private bool _recordSubmitted;
    private LevelDefinition? _currentLevel;

    public GameViewModel(AppStorage storage, INotificationSink notifier)
    {
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _notifier = notifier ?? throw new ArgumentNullException(nameof(notifier));

        // 注意：这里刻意<b>不</b>调用 EnsureInitialGift。
        // 首次赠送只在"本局启用道具"时发放（§5.2 与 §5.5 ⑤），由 GameSession 创建时决定；
        // 如果在构造 ViewModel 时就送掉，还没开局就已经把这个"只送一次"的标记消耗掉了。
        Inventory = new ItemInventory(storage.Progress, storage.Config);

        RemainingText = "000";
        TotalMinesText = "10";
        TimerText = "000";
        SmileyGlyph = "🙂";
        StatusText = Strings.Get("status.ready");
        MagnifierCountText = "×0";
        SonarCountText = "×0";
        FreezeCountText = "×0";
        SeedText = "-";
        ModeText = "经典 + 道具";
        LevelGoalText = string.Empty;
    }

    /// <summary>剩余雷数（位数自适应，§4.2）。</summary>
    [ObservableProperty]
    public partial string RemainingText { get; set; }

    /// <summary>总雷数。</summary>
    [ObservableProperty]
    public partial string TotalMinesText { get; set; }

    /// <summary>计时（§4.3）。</summary>
    [ObservableProperty]
    public partial string TimerText { get; set; }

    /// <summary>表情按钮（§4.4 四种状态）。</summary>
    [ObservableProperty]
    public partial string SmileyGlyph { get; set; }

    /// <summary>状态文案。</summary>
    [ObservableProperty]
    public partial string StatusText { get; set; }

    /// <summary>物品栏数量角标（§5.3）。</summary>
    [ObservableProperty]
    public partial string MagnifierCountText { get; set; }

    /// <summary>声呐扫描持有量（物品栏第 2 格，v0.6）。</summary>
    [ObservableProperty]
    public partial string SonarCountText { get; set; }

    /// <summary>时间冻结持有量（物品栏第 3 格，v0.6）。</summary>
    [ObservableProperty]
    public partial string FreezeCountText { get; set; }

    /// <summary>声呐扫描现在能不能用（物品栏第 2 格是否点亮）。</summary>
    [ObservableProperty]
    public partial bool IsSonarUsableNow { get; set; }

    /// <summary>时间冻结现在能不能用（物品栏第 3 格是否点亮）。</summary>
    [ObservableProperty]
    public partial bool IsFreezeUsableNow { get; set; }

    /// <summary>
    /// 此刻放大镜是否真的能使用：模式启用道具、物品栏里还有、已经首点、本局未结束（§5.3 / §5.5 ①）。
    /// 物品栏图标的不透明度直接绑这个属性——首点之前和数量为 0 是同一套"置灰"视觉。
    /// </summary>
    [ObservableProperty]
    public partial bool IsMagnifierUsableNow { get; set; }

    /// <summary>本局种子（§2.6）。</summary>
    [ObservableProperty]
    public partial string SeedText { get; set; }

    /// <summary>当前模式文案（§5.5 ⑤）。</summary>
    [ObservableProperty]
    public partial string ModeText { get; set; }

    /// <summary>当前对局。</summary>
    public GameSession? Session { get; private set; }

    /// <summary>物品栏（§5.2）。</summary>
    public ItemInventory Inventory { get; }

    /// <summary>存档与配置。</summary>
    public AppStorage Storage => _storage;

    /// <summary>按参数开一局；参数非法时返回 false 并给出报错文案（§0.1）。</summary>
    public bool StartNewGame(
        GameStartOptions options,
        int width,
        int height,
        int mines,
        int? seed,
        string? difficultyId,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(options);

        int actualSeed = seed ?? GameSession.GenerateSeed();

        var settings = new GameSettings
        {
            Mode = options.Mode,
            FirstClickOpening = options.FirstClickOpening,
            PauseTimerOnFocusLoss = options.PauseTimerOnFocusLoss,

            // v0.6：普通模式与关卡模式一致 —— 每扫够阈值都弹"三选一"补给
            ChoiceRewards = true,
        };

        // 锁定盘面（§2.6）：雷位与首点都由 seed 决定，创建时就直接替玩家翻开首点
        GameSession? session;
        bool created = options.LockedBoard
            ? GameSession.TryCreateLocked(width, height, mines, actualSeed, settings, out session, out error, Inventory, SystemClock.Instance, difficultyId)
            : GameSession.TryCreate(width, height, mines, actualSeed, settings, out session, out error, Inventory, SystemClock.Instance, difficultyId);

        if (!created)
        {
            return false;
        }

        Session = session!;
        _currentLevel = null;   // 普通开局要退出关卡模式
        _recordSubmitted = false;
        Refresh();
        return true;
    }

    /// <summary>按经典三级难度开一局。</summary>
    public void StartPreset(DifficultyPreset preset, GameStartOptions options, int? seed = null)
        => StartNewGame(
            options,
            preset.Width,
            preset.Height,
            preset.MineCount,
            seed,
            preset.Id,
            out _);

    /// <summary>按快照重开同一盘面（§6.3）。</summary>
    public void AttachReplay(GameSession replay)
    {
        ArgumentNullException.ThrowIfNull(replay);
        Session = replay;
        _currentLevel = null;
        _recordSubmitted = false;
        Refresh();
    }

    /// <summary>
    /// 按分享串复现同一盘面（§6.3）。
    /// 分享串里带了 seed + 首点 + 是否用扩展安全区，所以复现是精确的，不依赖本机设置。
    /// </summary>
    public bool TryStartFromShareCode(string? shareCode, GameMode mode, out string? error)
    {
        if (!GameSession.TryReplayShareCode(shareCode, mode, out GameSession? session, out error, Inventory, SystemClock.Instance))
        {
            return false;
        }

        Session = session!;
        _currentLevel = null;
        _recordSubmitted = false;
        Refresh();
        return true;
    }

    /// <summary>刷新全部状态栏文案。</summary>
    public void Refresh()
    {
        if (Session is null)
        {
            return;
        }

        GameSession session = Session;
        int digits = Math.Max(3, session.Board.MineCount.ToString().Length);

        TotalMinesText = session.Board.MineCount.ToString();
        RemainingText = session.RemainingMines.ToString($"D{digits}");
        TimerText = FormatSeconds(session.ElapsedSecondsInt);

        // 关卡模式读关卡自己的物品栏（数量由关卡给，不动账号库存）
        ItemInventory active = ActiveInventory;
        MagnifierCountText = $"×{active.MagnifierCount}";

        // 格子亮不亮只看"还有没有货 + 这局还能不能用道具"。
        // 不再因为"还没翻开第一格""计时没开始"就变灰 —— 那样玩家会以为道具根本没解锁；
        // 真的不能用的原因（首点前 / 计时没开始 / 局已结束）由点击时的提示说明。
        bool bagUsable = session.Settings.ItemsAvailable && !session.IsFinished;
        IsMagnifierUsableNow = bagUsable && active.MagnifierCount > 0;
        SonarCountText = $"×{active.SonarCount}";
        FreezeCountText = $"×{active.FreezeCount}";
        IsSonarUsableNow = bagUsable && active.SonarCount > 0;
        IsFreezeUsableNow = bagUsable && active.FreezeCount > 0;

        SmileyGlyph = session.Status switch
        {
            GameStatus.Won => "😎",
            GameStatus.Lost => "😵",
            _ => "🙂",
        };

        ModeText = IsLevelRun
            ? $"关卡 {_currentLevel!.DisplayId}"
            : session.Settings.Mode switch
            {
                GameMode.ClassicPure => Strings.Get("mode.classicPure"),
                GameMode.ClassicWithItems => Strings.Get("mode.name.classicWithItems"),
                _ => Strings.Get("mode.name.custom"),
            };

        SeedText = session.Seed.ToString();

        StatusText = session.Status switch
        {
            GameStatus.Won => IsLevelRun
                ? Strings.Get("flow.winLevel", FormatSeconds(session.ElapsedSecondsInt))
                : Strings.Get("flow.win", FormatSeconds(session.ElapsedSecondsInt)),
            GameStatus.Lost => IsLevelRun
                ? Strings.Get(_currentLevel!.IsSecret ? "status.levelFailedRandom" : "status.levelFailed")
                : Strings.Get("status.defeat"),
            GameStatus.InProgress => IsLevelRun
                ? Strings.Get("mode.level", _currentLevel!.DisplayName)
                // v1.4：每 20 雷补给已取消，状态行改为显示本局金币（商店经济）
                : Strings.Get("mode.coins", session.Coins.Coins),
            _ => session.Settings.ItemsAvailable
                ? Strings.Get("status.readyHint")
                : Strings.Get("status.readyHint"),
        };

        RefreshLevelGoal();
    }

    /// <summary>
    /// 定时器回调：推进分帧展开与计时显示。返回 true 表示棋盘需要重绘。
    ///
    /// 展开按<b>时间片</b>推进而不是"每次定时器只推一批"：定时器是 16ms 一跳，
    /// 每跳最多花 <see cref="FloodFrameBudgetMs"/> 毫秒连续推进，于是
    /// 1000×1000 的展开几百毫秒就完成，而不是被定时器间隔拖成好几秒。
    /// </summary>
    public bool Tick()
    {
        if (Session is null)
        {
            return false;
        }

        bool redraw = false;

        if (Session.HasPendingFlood)
        {
            Stopwatch budget = Stopwatch.StartNew();
            do
            {
                Session.ContinuePendingFlood(GameSession.FloodChunkPerFrame);
                redraw = true;
            }
            while (Session.HasPendingFlood && budget.ElapsedMilliseconds < FloodFrameBudgetMs);
        }

        string timer = FormatSeconds(Session.ElapsedSecondsInt);
        if (timer != TimerText)
        {
            TimerText = timer;

            // 关卡目标条上的"现在通关能拿几星"要跟着秒数实时变
            if (IsLevelRun)
            {
                RefreshLevelGoal();
            }
        }

        if (Session.Status is GameStatus.Won or GameStatus.Lost)
        {
            Refresh();
        }

        return redraw;
    }

    /// <summary>本局结束时提交记录并落盘（§5.5 ⑥ / §6.2），结果以通知告诉玩家。</summary>
    public void SubmitRecordIfNeeded()
    {
        // 关卡是独立玩法：不进最快记录表，也不动跨局进度（成绩写进 levelprogress.json）
        if (IsLevelRun)
        {
            return;
        }

        if (Session is null || Session.Status != GameStatus.Won || _recordSubmitted)
        {
            return;
        }

        // "重打这一盘"是练习局：玩家已经看过这一局的雷位，成绩没有可比性，所以不进最快记录表。
        // 想正常计成绩请用失败弹窗里的「换一张新盘」。这里明说一句，
        // 免得玩家以为"我明明通关了却没进榜"是坏了。
        if (IsPracticeRun)
        {
            _recordSubmitted = true;
            _notifier.Publish(GameNotification.QuietInfo(
                "练习局 · 不计最快记录",
                Strings.Get("flow.practiceTime", Session.ElapsedSeconds.ToString("F1")),
                "record.practice"));
            return;
        }

        _recordSubmitted = true;

        RecordSubmitResult result = _storage.RecordsStore.Submit(Session);
        _storage.RecordsStore.Save(_storage.RecordsStore.Records);
        _storage.ProgressStore.Save(_storage.Progress);

        if (result.IsNewRecord)
        {
            _notifier.Publish(GameNotification.Success(
                "新纪录！",
                $"{ModeText} · {Session.ElapsedSeconds:F1} 秒（表：{result.Table}）",
                "record.new"));
        }
        else
        {
            _notifier.Publish(GameNotification.QuietInfo(
                "本局成绩",
                Strings.Get("flow.notRecord", Session.ElapsedSeconds.ToString("F1")),
                "record.not-improved"));
        }
    }

    /// <summary>
    /// 本局是不是"重打同一张盘"的练习局。
    ///
    /// 用"当前会话是否就是那个练习会话"来判定，而不是自己维护一个布尔开关：
    /// 这样任何一条开局路径（难度按钮 / 自定义 / 关卡 / 分享串）一换会话，标志就自动失效，
    /// 不必去每个开局方法里补一行复位（少一处迟早会被忘记维护的地方）。
    /// </summary>
    public bool IsPracticeRun => ReferenceEquals(Session, _practiceSession);

    private GameSession? _practiceSession;

    /// <summary>
    /// 用**同一张盘**重开一局（失败弹窗的「重打这一盘」）。
    ///
    /// 走 <see cref="BoardSnapshot"/> 精确复现，而不是"沿用同一个种子"：
    /// 普通模式（未锁定盘面）下雷位由 (seed, 首点) 共同决定，
    /// 只给同一个 seed 会得到另一张盘 —— 这正是"点了重玩这一盘却换了盘"的根因。
    ///
    /// 复现出来的是一局全未翻开、计时归零、道具数量照旧的新局，
    /// 只是**雷位与首点与上一局逐位相同**；因为玩家已经看过这张盘，所以标记成练习局（不进最快记录）。
    /// </summary>
    public bool RestartSameBoard(out string? error)
    {
        error = null;

        if (Session is not { } previous)
        {
            error = "还没有对局可重打。";
            return false;
        }

        if (!previous.Board.MinesPlaced)
        {
            // 还没点第一格时盘面尚未确定（普通模式是首点之后才布雷），没有"同一张盘"可言。
            error = "这一局还没布雷（还没点第一格），没有盘面可复现。";
            return false;
        }

        GameSession replayed;
        try
        {
            BoardSnapshot snapshot = previous.CaptureSnapshot();
            replayed = GameSession.CreateReplay(
                snapshot, previous.Settings, Inventory, SystemClock.Instance, previous.DifficultyId);
        }
        catch (InvalidOperationException ex)
        {
            error = ex.Message;
            return false;
        }

        Session = replayed;
        _practiceSession = replayed;
        _currentLevel = null;
        _recordSubmitted = false;

        Refresh();
        return true;
    }

    /// <summary>把跨局进度落盘（插旗拿到道具、窗口关闭时调用）。</summary>
    public void SaveProgress() => _storage.ProgressStore.Save(_storage.Progress);    /// <summary>本局种子分享串（§6.3）；尚未布雷时返回 null。</summary>
    public string? GetShareCode()
    {
        if (Session is null || !Session.Board.MinesPlaced)
        {
            return null;
        }

        return Session.CaptureSnapshot().ToShareCode();
    }

    // ---------------------------------------------------------------- 关卡模式

    /// <summary>当前正在打的关卡；非关卡模式为 null。</summary>
    public LevelDefinition? CurrentLevel => _currentLevel;

    /// <summary>本局是否关卡模式。</summary>
    public bool IsLevelRun => _currentLevel is not null;

    /// <summary>
    /// 本局实际使用的物品栏。
    /// 关卡模式用的是关卡专用的临时物品栏（数量由关卡给，不动账号库存），
    /// 所以状态栏与物品栏必须读这个，而不是账号物品栏。
    /// </summary>
    public ItemInventory ActiveInventory => Session?.Inventory ?? Inventory;

    /// <summary>关卡目标文案（非关卡模式为空串）。</summary>
    [ObservableProperty]
    public partial string LevelGoalText { get; set; }

    /// <summary>
    /// 开始一个关卡（关卡制的入口）。
    /// 用锁定盘面创建，所以<b>同一关永远是同一张盘</b>：重试、刷星、分享都是同一张。
    /// </summary>
    public bool StartLevel(LevelDefinition level, out string? error)
    {
        ArgumentNullException.ThrowIfNull(level);

        var settings = new GameSettings
        {
            Mode = level.Mode,
            FirstClickOpening = level.FirstClickOpening,

            // 关卡模式（需求）：每扫够阈值攒一次"三选一"，给玩家挑
            ChoiceRewards = true,

            // 关卡里的失败代价已经够高，不引入"失焦暂停"这种额外规则
            PauseTimerOnFocusLoss = false,
        };

        // 隐藏关用"首点后才布雷"（原版行为）：开局是**全新未翻开**的盘面，
        // 玩家点下第一格才布雷、才计时。
        //
        // 主线关卡则必须用锁定盘面：先按 seed 布雷并定死首点，这样"同一关永远是同一张盘"，
        // 星级用时才有可比性（代价是开局会替玩家翻开首点区域，见 README §6.3）。
        bool created = level.IsSecret
            ? GameSession.TryCreate(
                level.Width,
                level.Height,
                level.MineCount,
                level.Seed,
                settings,
                out GameSession? session,
                out error,
                CreateLevelInventory(level),
                SystemClock.Instance,
                level.Id)
            : GameSession.TryCreateLocked(
                level.Width,
                level.Height,
                level.MineCount,
                level.Seed,
                settings,
                out session,
                out error,
                CreateLevelInventory(level),
                SystemClock.Instance,
                level.Id);

        if (!created)
        {
            return false;
        }

        Session = session!;
        _currentLevel = level;
        _recordSubmitted = false;
        Refresh();
        return true;
    }

    /// <summary>
    /// 关卡用哪套物品栏。<b>v0.6 起与普通模式完全共用同一套</b>（就是账号物品栏 <see cref="Inventory"/>）：
    /// 关卡不再每关另发道具，消耗与获得都记在同一份 progress.json 里。
    ///
    /// 保留这个方法只是为了"关卡与普通局取物品栏"这件事在代码里有一个明确的入口，
    /// 将来若要给某类关卡单独的规则，改这里一处即可。
    /// </summary>
    private ItemInventory CreateLevelInventory(LevelDefinition level) => Inventory;

    /// <summary>
    /// 刷新关卡目标条：实时告诉玩家"现在通关能拿几星"。
    /// 由 <see cref="Tick"/> 每秒调用一次，所以剩余时间的感觉是活的。
    /// </summary>
    public void RefreshLevelGoal()
    {
        if (_currentLevel is null || Session is null)
        {
            LevelGoalText = string.Empty;
            return;
        }

        LevelDefinition level = _currentLevel;

        if (Session.Status is GameStatus.Won or GameStatus.Lost)
        {
            LevelGoalText = $"{level.DisplayName} · {level.BoardText} · 本局结束";
            return;
        }

        int projected = LevelRules.EvaluateStars(level, Session.ElapsedSeconds);
        string stars = LevelRules.StarsText(projected);

            // v0.6：关卡不再自带道具，这里显示玩家当前的三种道具存量
            ItemInventory bag = ActiveInventory;
            string items = $" · 道具 镜×{bag.MagnifierCount} 呐×{bag.SonarCount} 冰×{bag.FreezeCount}";

        LevelGoalText =
            $"{level.DisplayName} · {level.BoardText} · 现在通关可拿 {stars}" +
            Strings.Get("levelDialog.stars", level.TargetSeconds, level.ChallengeSeconds) + items;
    }

    /// <summary>复制本局种子串到剪贴板（§6.3 的"分享同一盘"）。</summary>
    [RelayCommand]
    private void CopyShareCode()
    {
        string? code = GetShareCode();
        if (code is null)
        {
            _notifier.Publish(GameNotification.Warning(
                "还不能分享",
                "先翻开第一格，之后才能生成可分享的种子串。",
                "share.too-early"));
            return;
        }

        try
        {
            var package = new DataPackage();
            package.SetText(code);
            Clipboard.SetContent(package);

            _notifier.Publish(GameNotification.QuietSuccess("已复制种子串", code, "share.copied"));
        }
        catch (Exception ex)
        {
            _notifier.Publish(GameNotification.Error("复制失败", ex.Message, "share.copy-failed"));
        }
    }

    /// <summary>§4.2：经典数码管是 3 位，取消 99 上限后必须自适应位数；超过 999 秒继续按秒显示。</summary>
    private static string FormatSeconds(int seconds)
    {
        string text = seconds.ToString();
        return text.Length >= 3 ? text : text.PadLeft(3, '0');
    }
}
