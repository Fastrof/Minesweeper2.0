using MineSweeper.Core;
using MineSweeper.Core.Achievements;
using MineSweeper.Core.Audio;
using MineSweeper.Core.History;
using MineSweeper.Core.Items;
using MineSweeper.Core.Levels;
using MineSweeper.Core.Notifications;
using MineSweeper.Core.Persistence;

namespace MineSweeper.App.Linux.Services;

/// <summary>
/// 单局编排（Linux 端的 "MainWindow 业务部分"）。
///
/// 设计目标：把 Windows 端 <c>MainWindow.xaml.cs</c> 里散落的业务逻辑收拢到一处，
/// 让窗口只负责"显示 + 输入转发"。规则全部在 Core，这里只做：
///   - 开局/重开/换关
///   - 玩家动作的转发与副作用（音效、结算、成就、记录、历史）
///   - 分帧展开的节拍
///
/// 与 Windows 端的口径完全一致：
///   - 练习局（重打这一盘）/ 无猜局 / 用过提示的局 **不计最快记录**；
///   - 关卡成绩只写 levelprogress.json，不进最快记录表；
///   - 历史累计只在"玩家真的操作过"时记账。
/// </summary>
public sealed class GameCoordinator
{
    private readonly Storage _storage;
    private readonly SoundService _sound;
    private readonly NotificationService _notifier;
    private readonly AchievementService _achievements;

    public GameCoordinator(
        Storage storage,
        SoundService sound,
        NotificationService notifier,
        AchievementService achievements)
    {
        _storage = storage;
        _sound = sound;
        _notifier = notifier;
        _achievements = achievements;
    }

    // ---------------------------------------------------------------- 当前局

    public GameSession? Session { get; private set; }

    /// <summary>当前关卡（普通局为 null）。</summary>
    public LevelDefinition? Level { get; private set; }

    /// <summary>当前难度预设（普通局有效；关卡局沿用上一档）。</summary>
    public DifficultyPreset Preset { get; private set; } = DifficultyPreset.Beginner;

    /// <summary>是否是"重打这一盘"的练习局（不计最快记录）。</summary>
    public bool PracticeRun { get; private set; }

    public bool LevelSession => Level is not null;

    /// <summary>本局是否已经有玩家操作（决定历史是否记账）。</summary>
    public bool PlayerActed { get; private set; }

    /// <summary>本局是否已经结算过（防重复）。</summary>
    public bool Settled { get; private set; }

    /// <summary>本局最大一次 chord 翻开的格数（成就用）。</summary>
    public int MaxChordOpened { get; private set; }

    /// <summary>本局开始时间（UTC）。</summary>
    public DateTimeOffset? StartedUtc { get; private set; }

    /// <summary>最近一次关卡结算结果（关卡局用）。</summary>
    public LevelOutcome? LastLevelOutcome { get; private set; }

    /// <summary>最近一次最快记录提交结果（普通局用）。</summary>
    public RecordSubmitResult? LastRecordResult { get; private set; }

    // ---------------------------------------------------------------- 事件

    /// <summary>新开一局（窗口重建棋盘与状态栏）。</summary>
    public event EventHandler? NewSession;

    /// <summary>任意状态变化（刷新状态栏 / 重绘棋盘）。</summary>
    public event EventHandler? StateChanged;

    /// <summary>需要弹给玩家的通知。</summary>
    public event EventHandler<GameNotification>? NotificationRequested;

    /// <summary>需要播放的音效。</summary>
    public event EventHandler<SoundEffect>? SoundRequested;

    /// <summary>一局结束（胜或负）。</summary>
    public event EventHandler<GameSession>? GameFinished;

    /// <summary>关卡结算完成。</summary>
    public event EventHandler<LevelOutcome>? LevelFinished;

    // ---------------------------------------------------------------- 开局

    /// <summary>开一局普通难度。</summary>
    public GameSession StartPreset(DifficultyPreset preset)
    {
        Preset = preset;
        Level = null;
        PracticeRun = false;

        var settings = BuildSettings(firstClickOpening: _storage.Settings.FirstClickOpening, noGuess: _storage.Settings.NoGuess, mode: GameMode.ClassicWithItems);
        ItemInventory inventory = CreateInventory();

        if (!GameSession.TryCreate(
                preset.Width, preset.Height, preset.MineCount,
                GameSession.GenerateSeed(), settings,
                out GameSession? session, out string? error,
                inventory, SystemClock.Instance, preset.Id)
            || session is null)
        {
            throw new InvalidOperationException(error ?? "开局失败");
        }

        Attach(session);
        _sound.Play(SoundEffect.Startup);
        return session;
    }

    /// <summary>开一关（锁定盘面：雷位固定，首点由 Core 钉到安全格）。</summary>
    public GameSession StartLevel(LevelDefinition level)
    {
        Level = level;
        PracticeRun = false;

        var settings = BuildSettings(
            firstClickOpening: level.FirstClickOpening,
            noGuess: false,
            mode: level.Mode);

        ItemInventory inventory = CreateInventory();

        if (!GameSession.TryCreateLocked(
                level.Width, level.Height, level.MineCount,
                level.Seed, settings,
                out GameSession? session, out string? error,
                inventory, SystemClock.Instance, level.Id)
            || session is null)
        {
            throw new InvalidOperationException(error ?? "关卡开局失败");
        }

        Attach(session);
        _sound.Play(SoundEffect.Startup);
        return session;
    }

    /// <summary>开一局自定义盘（宽/高/雷/种子可选）。</summary>
    public GameSession StartCustom(int width, int height, int mineCount, int? seed)
    {
        Level = null;
        PracticeRun = false;

        var settings = BuildSettings(
            _storage.Settings.FirstClickOpening,
            _storage.Settings.NoGuess,
            GameMode.Custom);

        if (!GameSession.TryCreate(
                width, height, mineCount, seed ?? GameSession.GenerateSeed(), settings,
                out GameSession? session, out string? error,
                CreateInventory(), SystemClock.Instance, null)
            || session is null)
        {
            throw new InvalidOperationException(error ?? "自定义开局失败");
        }

        Attach(session);
        return session;
    }

    /// <summary>按分享串复现一局。</summary>
    public GameSession? StartFromShareCode(string shareCode, out string? error)
    {
        Level = null;
        PracticeRun = false;

        if (!GameSession.TryReplayShareCode(
                shareCode, GameMode.Custom, out GameSession? session, out error,
                CreateInventory(), SystemClock.Instance)
            || session is null)
        {
            return null;
        }

        Attach(session);
        return session;
    }

    /// <summary>当前关卡的上一关（主线才有效；第一关或非关卡局返回 null）。</summary>
    public LevelDefinition? PreviousLevel =>
        Level is { Index: > 0 } ? LevelTable.At(Level.Index - 1) : null;

    /// <summary>当前关卡的下一关（主线才有效；隐藏关 / 最后一关返回 null）。</summary>
    public LevelDefinition? NextLevel => Level is null ? null : LevelTable.Next(Level);

    /// <summary>按当前这一局的参数重开（普通局换新盘 / 关卡局重打同一关）。</summary>
    public GameSession Restart()
    {
        if (Level is not null)
        {
            return StartLevel(Level);
        }

        GameSession? previous = Session;
        if (previous is null)
        {
            return StartPreset(Preset);
        }

        // 沿用上一局的盘面参数与模式（同 Windows 端 RestartCurrentGame）
        var settings = BuildSettings(
            previous.Settings.FirstClickOpening,
            previous.Settings.NoGuess,
            previous.Settings.Mode);

        if (!GameSession.TryCreate(
                previous.Board.Width, previous.Board.Height, previous.Board.MineCount,
                GameSession.GenerateSeed(), settings,
                out GameSession? session, out string? error,
                CreateInventory(), SystemClock.Instance, previous.DifficultyId)
            || session is null)
        {
            throw new InvalidOperationException(error ?? "重开失败");
        }

        Preset = DifficultyPreset.All.FirstOrDefault(p => p.Id == previous.DifficultyId) ?? Preset;
        Attach(session);
        return session;
    }

    /// <summary>接管一个外部创建好的会话（自定义盘 / 分享串 / 截图测试用）。</summary>
    public void Adopt(GameSession session)
    {
        Level = null;
        PracticeRun = false;
        Attach(session);
    }

    /// <summary>
    /// 「重打这一盘」：用快照精确复现同一张盘（与分享串复现同一条路径），并标记为练习局。
    /// 练习局的成绩不计最快记录（玩家已经看过这局的雷位）。
    /// </summary>
    public GameSession RestartSameBoard()
    {
        GameSession session = RequireSession();

        if (Level is not null)
        {
            return StartLevel(Level);   // 关卡本身就是固定盘，重开同一关即可
        }

        if (!session.Board.MinesPlaced)
        {
            return Restart();           // 还没点第一格，没有快照可用
        }

        Core.BoardSnapshot snapshot = session.CaptureSnapshot();
        GameSession replay = GameSession.CreateReplay(
            snapshot,
            session.Settings,
            CreateInventory(),
            Core.SystemClock.Instance,
            session.DifficultyId);

        Attach(replay);
        PracticeRun = true;             // 必须在 Attach 之后设置
        AppPaths.Log("[开局] 重打这一盘（练习局，不计最快记录）");
        return replay;
    }

    /// <summary>放大镜预览（瞄准时列出区域内容，不消耗道具）。</summary>
    public Core.Items.MagnifierReveal? PreviewMagnifier(int index)
    {
        GameSession? session = Session;
        if (session is null || index < 0)
        {
            return null;
        }

        return session.PreviewMagnifier(index);
    }

    private void Attach(GameSession session)
    {

        Session = session;
        Settled = false;
        PlayerActed = false;
        MaxChordOpened = 0;
        StartedUtc = DateTimeOffset.UtcNow;
        LastLevelOutcome = null;
        LastRecordResult = null;

        _storage.History.BeginGame(StartedUtc.Value);
        AppPaths.Log(
            $"[开局] {session.Board.Width}x{session.Board.Height}/{session.Board.MineCount} " +
            $"seed={session.Seed} level={Level?.Id ?? "-"} mode={session.Settings.Mode}");

        NewSession?.Invoke(this, EventArgs.Empty);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private GameSettings BuildSettings(bool firstClickOpening, bool noGuess, GameMode mode) => new()
    {
        FirstClickOpening = firstClickOpening,
        NoGuess = noGuess,
        PauseTimerOnFocusLoss = _storage.Settings.PauseTimerOnFocusLoss,
        Mode = mode,
    };

    private ItemInventory CreateInventory()
    {
        // v1.5.1 的 Core 去掉了"本局临时库存"（SessionPurchases / ResetSession）：
        // 道具直接存账号（PlayerProgress 的 *Count），买一次就一直有，跨局也不用重置。
        var inventory = new ItemInventory(_storage.Progress, _storage.ItemConfig);
        inventory.EnsureInitialGift();
        return inventory;
    }

    // ---------------------------------------------------------------- 玩家动作

    public RevealResult Reveal(int index)
    {
        GameSession session = RequireSession();
        RevealResult result = session.Reveal(index);

        if (result.Outcome == RevealOutcome.Exploded)
        {
            PlaySound(SoundEffect.Explosion);
        }
        else if (result.Outcome == RevealOutcome.Revealed)
        {
            PlaySound(SoundEffect.Reveal);
        }

        AfterAction();
        return result;
    }

    public FlagResult ToggleFlag(int index)
    {
        GameSession session = RequireSession();
        FlagResult result = session.ToggleFlag(index);

        if (result.Outcome == FlagOutcome.Flagged)
        {
            PlaySound(SoundEffect.Flag);
        }

        AfterAction();
        return result;
    }

    public ChordResult Chord(int index)
    {
        GameSession session = RequireSession();
        ChordResult result = session.Chord(index);

        if (result.OpenedCount > MaxChordOpened)
        {
            MaxChordOpened = result.OpenedCount;
        }

        if (result.Outcome == ChordOutcome.Exploded)
        {
            PlaySound(SoundEffect.Explosion);
        }
        else if (result.OpenedCount > 0)
        {
            PlaySound(SoundEffect.Chord);
        }

        AfterAction();
        return result;
    }

    /// <summary>使用放大镜（瞄准态落子）。</summary>
    public bool UseMagnifier(int index, out string message)
    {
        GameSession session = RequireSession();
        MagnifierUseResult result = session.UseMagnifier(index);
        message = result.Check == ItemUseCheck.Ok
            ? LocalizationService.Get("toast.magnifierDone")
            : LocalizationService.T("item.unavailable", "道具不可用", "Item unavailable");

        if (result.Check == ItemUseCheck.Ok)
        {
            PlaySound(SoundEffect.Reveal);
            AfterAction();
        }

        return result.Check == ItemUseCheck.Ok;
    }

    /// <summary>使用声呐扫描。</summary>
    public SonarScanResult UseSonar(int index)
    {
        GameSession session = RequireSession();
        SonarScanResult result = session.ScanWithSonar(index);

        if (result.Success)
        {
            PlaySound(SoundEffect.Chord);
            AfterAction();
        }

        return result;
    }

    /// <summary>使用时间冻结。</summary>
    public TimeFreezeResult Freeze()
    {
        GameSession session = RequireSession();
        TimeFreezeResult result = session.FreezeTimer();

        if (result.Started)
        {
            PlaySound(SoundEffect.Flag);
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        return result;
    }

    /// <summary>道具售价（来自 config.json，改价后界面与实扣一致）。</summary>
    public int ShopPriceOf(string itemId) => Session?.ShopPriceOf(itemId) ?? 0;

    /// <summary>商店购买。</summary>
    /// <summary>
    /// 商店购买。Core 的 <c>TryBuyItem</c> 在 v1.5.1 已经是"账号钱包 + 账号库存"，
    /// 所以这里只负责调用它、把进度落盘并通知 UI。
    /// </summary>
    public ShopPurchaseResult Buy(string itemId)
    {
        GameSession session = RequireSession();
        ShopPurchaseResult result = session.TryBuyItem(itemId);

        if (result.Bought)
        {
            _storage.SaveProgress();
            AppPaths.Log($"[商店] 购买 {itemId} 花 {result.Price}，账号金币 {result.CoinsAfter}");
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
        return result;
    }

    /// <summary>提示：返回一个确定安全的格（-1 表示推不出）。</summary>
    public int FindHint() => Session?.FindHint() ?? -1;

    /// <summary>标记本局用过提示（之后不计最快记录）。</summary>
    public void MarkHintUsed() => Session?.MarkHintUsed();

    /// <summary>窗口焦点变化（失焦暂停计时）。</summary>
    public void SetWindowFocused(bool focused) => Session?.SetWindowFocused(focused);

    /// <summary>
    /// 节拍：推进分帧展开、刷新计时。返回 true 表示需要重绘。
    /// </summary>
    public bool Tick()
    {
        GameSession? session = Session;
        if (session is null)
        {
            return false;
        }

        bool changed = false;
        if (session.HasPendingFlood)
        {
            session.ContinuePendingFlood(GameSession.FloodChunkPerFrame);
            changed = true;
        }

        if (changed)
        {
            AfterAction();
        }

        return changed;
    }

    private GameSession RequireSession() =>
        Session ?? throw new InvalidOperationException("当前没有进行中的对局");

    private void PlaySound(SoundEffect effect) => SoundRequested?.Invoke(this, effect);

    private void AfterAction()
    {
        GameSession? session = Session;
        if (session is null)
        {
            return;
        }

        if (session.Status is GameStatus.InProgress or GameStatus.Won or GameStatus.Lost)
        {
            PlayerActed = true;
        }

        if (session.IsFinished && !Settled)
        {
            Settle();
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    // ---------------------------------------------------------------- 结算

    private void Settle()
    {
        GameSession session = RequireSession();
        Settled = true;
        bool won = session.Status == GameStatus.Won;

        AppPaths.Log(
            $"[结算] {(won ? "胜" : "负")} {session.ElapsedSeconds:F1}s " +
            $"已翻开={session.RevealedCount} 插旗={session.FlagCount} 提示={session.UsedHint}");

        // 1) 关卡结算（只写 levelprogress.json，不进最快记录表）
        if (Level is not null)
        {
            LevelOutcome outcome = LevelRules.Settle(
                _storage.LevelProgress, Level, won, session.ElapsedSeconds);
            _storage.SaveLevelProgress();
            LastLevelOutcome = outcome;
            LevelFinished?.Invoke(this, outcome);
        }
        else
        {
            // 2) 普通局：最快记录（练习局 / 无猜局 / 用过提示的局不计）
            bool eligible = won
                            && !PracticeRun
                            && !session.Settings.NoGuess
                            && !session.UsedHint;
            LastRecordResult = _storage.SubmitRecord(session, eligible);
        }

        // 3) 历史累计（只在玩家真的操作过时记账）
        if (PlayerActed)
        {
            _storage.History.RecordGame(new GameSummary(
                EndedAtUtc: DateTimeOffset.UtcNow,
                Seconds: session.ElapsedSeconds,
                MinesSwept: session.FlagCount,
                CellsRevealed: session.RevealedCount,
                Won: won,
                LevelCleared: won && Level is not null));
            _storage.SaveHistory();
        }

        // 4) 成就
        int stars = LastLevelOutcome?.Stars ?? 0;
        AchievementFacts facts = AchievementService.BuildFacts(
            session,
            won,
            AchievementService.CountWrongFlags(session),
            session.ItemUsed,
            MaxChordOpened,
            Level?.Id,
            stars,
            session.UsedMagnifier,
            session.UsedSonar,
            session.UsedFreeze,
            _achievements.BuildStats());

        IReadOnlyList<AchievementUnlock> unlocks = _achievements.Report(facts);

        // 5) 通知 + 音效
        if (won)
        {
            PlaySound(SoundEffect.Victory);
        }
        else
        {
            PlaySound(SoundEffect.Explosion);
        }

        string title = won
            ? LocalizationService.T("notify.win.title", "胜利", "Victory")
            : LocalizationService.Get("toast.hitMine.title");
        string message = BuildSettleMessage(session, won, stars, unlocks);
        Raise(won ? GameNotification.Success(title, message, "settle") : GameNotification.Error(title, message, "settle"));

        GameFinished?.Invoke(this, session);
    }

    private string BuildSettleMessage(GameSession session, bool won, int stars, IReadOnlyList<AchievementUnlock> unlocks)
    {
        string time = session.ElapsedSeconds.ToString("F1", System.Globalization.CultureInfo.InvariantCulture);

        if (won)
        {
            if (Level is not null)
            {
                string starsText = LevelRules.StarsText(stars);
                return LocalizationService.Get("notify.level.won", Level.DisplayName, time, starsText);
            }

            return LocalizationService.Get("notify.win.body", time);
        }

        return LocalizationService.Get("toast.hitMine.body");
    }

    /// <summary>发一条通知：先走 NotificationService（写日志 + 尽力发系统通知），再通知 UI 弹窗口内提示。</summary>
    private void Raise(GameNotification notification)
    {
        _notifier.Show(notification);
        NotificationRequested?.Invoke(this, notification);
    }

    /// <summary>
    /// 账号金币（跨局累计）。
    ///
    /// v1.5.1 的 Core 已经把 <see cref="GameSession.Coins"/> 改成
    /// <c>PlayerProgress.Coins</c> 的读写视图（<c>new CoinWallet(inventory.Progress)</c>），
    /// 所以这里直接读钱包即可 —— **不要**再在 App 层做一次入账，否则会重复加钱。
    /// </summary>
    public int AccountCoins => Session?.Coins.Coins ?? _storage.Progress.Coins;

    /// <summary>退出前落盘（设置、进度、关卡、成就、历史）。</summary>
    public void SaveOnExit()
    {
        _storage.SaveSettings();
        _storage.SaveProgress();
        _storage.SaveRecords();
        _storage.SaveLevelProgress();
        _storage.SaveAchievements();
        _storage.SaveHistory();
    }
}
