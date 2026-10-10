using MineSweeper.Core.Items;

namespace MineSweeper.Core;

/// <summary>
/// 单局状态机（§1.3）：进行中/胜利/失败、计时、剩余雷数，以及道具的使用入口。
///
/// 这一层是 Core 的门面：UI 只调用 <see cref="Reveal"/>、<see cref="ToggleFlag"/>、
/// <see cref="Chord"/>、<see cref="UseMagnifier"/> 与 <see cref="ContinuePendingFlood"/>，
/// 不需要知道洪水填充、布雷、胜负判定的细节。
///
/// 关键实现约束：
/// - 胜利判定用计数器 O(1)，绝不每步遍历棋盘（§2.5）
/// - 大范围展开用 <see cref="ContinuePendingFlood"/> 分帧推进，避免一次点击卡住 UI（§2.3）
/// - 计时器从第一次"翻开"开始（§0 第 6 条）
/// - 随机数只服务于布雷，且由 seed 决定，因此"同 seed + 同首点 = 同盘面"（§2.6）
/// </summary>
public sealed class GameSession
{
    /// <summary>单次展开的最大格数，超过就留给 UI 分帧继续（§2.3）。</summary>
    public const int DefaultFloodChunkSize = 50_000;

    /// <summary>
    /// 分帧推进时单帧最多展开的格数。配合 UI 的时间片预算（见 <c>GameViewModel.Tick</c>），
    /// 让 1000×1000 这种量级的展开在几百毫秒内完成，而不是被 200ms 定时器拖成好几秒。
    /// </summary>
    public const int FloodChunkPerFrame = 20_000;

    private readonly IClock _clock;
    private readonly Random _random;
    private readonly MagnifierItem _magnifier;
    private readonly SonarScanItem _sonar;
    private readonly TimeFreezeItem _freeze;

    /// <summary>已经冻结掉的累计时长（要从未计时里扣掉）。</summary>
    private TimeSpan _frozenTotal;

    /// <summary>当前这一次冻结的起点（null = 没在冻结）。</summary>
    private DateTimeOffset? _freezeStartedAt;
    private readonly List<int> _chordTargets = new(8);

    private FloodFillOperation? _pendingFill;
    private TimeSpan _accumulated;
    private DateTimeOffset? _runningSince;
    private bool _timerStopped;

    /// <summary>最近一次操作真正改动过的格索引区间（含首尾），供 UI 精确重绘。</summary>
    private int _affectedMinIndex = int.MaxValue;

    private int _affectedMaxIndex = -1;

    /// <summary>被放大镜透视过的格子及其时间（用于可选的"几秒后恢复迷雾"，§5.4 备选）。</summary>
    private readonly Dictionary<int, DateTimeOffset> _itemRevealTimes = new();

    /// <summary>透视信息保留时长（秒）；0 表示永久保留。</summary>
    private readonly int _itemRevealFadeSeconds;

    private GameSession(Board board, GameSettings settings, ItemInventory? inventory, IClock clock, int seed, string? difficultyId, bool locked = false)
    {
        Board = board;
        Settings = settings;
        Inventory = inventory;
        _clock = clock;
        Seed = seed;
        DifficultyId = difficultyId;
        Locked = locked;
        _random = new Random(seed);
        _magnifier = new MagnifierItem(inventory is null ? MagnifierItem.DefaultRadius : inventory.Config.MagnifierRadius);
        _sonar = new SonarScanItem(inventory?.Config);
        _freeze = new TimeFreezeItem(inventory?.Config);
        _itemRevealFadeSeconds = inventory?.Config.MagnifierRevealFadeSeconds ?? 0;
        Status = GameStatus.NotStarted;
    }

    /// <summary>棋盘。</summary>
    public Board Board { get; }

    /// <summary>本局设置。</summary>
    public GameSettings Settings { get; }

    /// <summary>跨局进度 / 物品栏；纯经典模式也可能传入（计数照走，但不发放，见 §5.5 ⑤）。</summary>
    public ItemInventory? Inventory { get; }

    /// <summary>本局随机种子。</summary>
    public int Seed { get; }

    /// <summary>
    /// 是否"锁定盘面"模式（§2.6）：雷位在开局时就按 seed 定好，与玩家首点无关。
    /// 普通模式下同 seed 换首点会得到另一张盘，锁定模式则同 seed 必然同盘。
    /// </summary>
    public bool Locked { get; }

    /// <summary>难度预设 Id（自定义模式为 null）。</summary>
    public string? DifficultyId { get; }

    /// <summary>当前状态。</summary>
    public GameStatus Status { get; private set; }

    /// <summary>已翻开的格数（§2.5 的 O(1) 胜利判定的依据）。</summary>
    public int RevealedCount { get; private set; }

    /// <summary>已插旗的格数（问号不计入）。</summary>
    public int FlagCount { get; private set; }

    /// <summary>踩到的雷的格索引（失败后展示全盘雷位用）。</summary>
    public int? ExplodedIndex { get; private set; }

    /// <summary>本局是否使用过道具（§5.5 ⑥：记录要分表）。</summary>
    public bool ItemUsed { get; private set; }

    /// <summary>剩余雷数 = 总雷数 − 已插旗数（§0 第 5 条）。</summary>
    public int RemainingMines => Board.MineCount - FlagCount;

    /// <summary>本局是否已结束。</summary>
    public bool IsFinished => Status is GameStatus.Won or GameStatus.Lost;

    /// <summary>是否还有未完成的展开（UI 需要继续 pump）。</summary>
    public bool HasPendingFlood => _pendingFill is { IsComplete: false };

    /// <summary>透视半径（跟随配置，默认 1 即 3×3）。</summary>
    public int MagnifierRadius => _magnifier.Radius;

    /// <summary>
    /// 取走"最近一次操作实际改动过的格索引区间"并清空记录。
    ///
    /// UI 必须靠它来决定重绘范围（§4.5）：一次 <see cref="Reveal"/> 可能翻开一整片，
    /// 只失效被点击那一格会让屏幕上根本不显示展开结果；而每次都整屏重绘又会浪费。
    /// 区间是真实改动集合的<b>超集</b>（多画一点永远是对的），所以用两个 int 就够，
    /// 不需要为百万格展开收集格集合。
    /// </summary>
    public bool ConsumeLastAffectedRange(out int firstIndex, out int lastIndex)
    {
        firstIndex = _affectedMinIndex;
        lastIndex = _affectedMaxIndex;

        bool any = _affectedMaxIndex >= _affectedMinIndex;
        _affectedMinIndex = int.MaxValue;
        _affectedMaxIndex = -1;
        return any;
    }

    private void ResetAffectedRange()
    {
        _affectedMinIndex = int.MaxValue;
        _affectedMaxIndex = -1;
    }

    private void TrackAffected(int index)
    {
        if (index < _affectedMinIndex)
        {
            _affectedMinIndex = index;
        }

        if (index > _affectedMaxIndex)
        {
            _affectedMaxIndex = index;
        }
    }

    private void TrackAffected(FloodFillOperation fill)
    {
        if (!fill.HasRevealedAnything)
        {
            return;
        }

        TrackAffected(fill.MinRevealedIndex);
        TrackAffected(fill.MaxRevealedIndex);
    }

    /// <summary>已用秒数（浮点，UI 自行取整展示）。</summary>
    public double ElapsedSeconds
    {
        get
        {
            SyncFreeze();

            TimeSpan total = _accumulated;
            if (_runningSince is { } started)
            {
                total += _clock.UtcNow - started;
            }

            // 冻结期间计时不前进：把"已冻结 + 正在冻结的这一段"整体扣掉。
            TimeSpan frozen = _frozenTotal;
            if (_freezeStartedAt is { } active)
            {
                frozen += ClampFreezeSpan(_clock.UtcNow - active);
            }

            total -= frozen;

            return Math.Max(0d, total.TotalSeconds);
        }
    }

    /// <summary>已用整数秒（§4.2：超过 999 秒继续按秒显示）。</summary>
    public int ElapsedSecondsInt => (int)Math.Floor(ElapsedSeconds);

    /// <summary>生成一个可分享的种子（正数，便于口头/文字传递）。</summary>
    public static int GenerateSeed() => Random.Shared.Next(1, int.MaxValue);

    // ---------------------------------------------------------------- 创建

    /// <summary>按参数创建一局。参数非法时返回 false，并给出可直接展示的报错（§0.1）。</summary>
    public static bool TryCreate(
        int width,
        int height,
        int mineCount,
        int seed,
        GameSettings settings,
        out GameSession? session,
        out string? error,
        ItemInventory? inventory = null,
        IClock? clock = null,
        string? difficultyId = null)
    {
        session = null;

        if (settings is null)
        {
            error = "缺少本局设置。";
            return false;
        }

        if (!GameLimits.ValidateParameters(width, height, mineCount, out error))
        {
            return false;
        }

        var board = new Board(width, height, mineCount, seed);
        session = new GameSession(board, settings, inventory, clock ?? SystemClock.Instance, seed, difficultyId);
        GrantInitialGiftIfItemsAvailable(session);
        error = null;
        return true;
    }

    /// <summary>
    /// 锁定盘面模式创建一局（§2.6）：先按 seed 布雷、再取安全首点。
    ///
    /// <b>开局不替玩家翻开首点</b>——整盘保持未翻开、计时为 0，等玩家自己点第一下。
    /// 首点安全由 <see cref="Reveal"/> 保证：锁定盘面的首次翻开会被钉到
    /// <see cref="Board.FirstClickIndex"/> 这格安全点上（点哪里都一样）。
    ///
    /// 早先这里的做法是"创建时就直接 Reveal 首点"，好处是玩家一打开就能看到一片空地，
    /// 但副作用是"还没操作就已经走了一步"：状态栏直接变成进行中、计时随时钟走，
    /// 关卡模式下体验尤其突兀。玩家报的就是这个。
    /// </summary>
    public static bool TryCreateLocked(
        int width,
        int height,
        int mineCount,
        int seed,
        GameSettings settings,
        out GameSession? session,
        out string? error,
        ItemInventory? inventory = null,
        IClock? clock = null,
        string? difficultyId = null)
    {
        session = null;

        if (settings is null)
        {
            error = "缺少本局设置。";
            return false;
        }

        if (!GameLimits.ValidateParameters(width, height, mineCount, out error))
        {
            return false;
        }

        var board = new Board(width, height, mineCount, seed);
        if (!BoardGenerator.TryPlaceMinesLocked(board, settings, new Random(seed), out error))
        {
            return false;
        }

        var created = new GameSession(board, settings, inventory, clock ?? SystemClock.Instance, seed, difficultyId, locked: true);
        GrantInitialGiftIfItemsAvailable(created);

        // 雷位已经按 seed 定好（盘面仍是"同一关永远同一张"），但**不在这里翻开首点**：
        // 交给玩家自己的第一次点击（见 Reveal 里那段锁定盘面的首点钉定）。
        session = created;
        error = null;
        return true;
    }

    /// <summary>按经典三级难度创建一局。</summary>
    public static GameSession CreateFromPreset(
        DifficultyPreset preset,
        int seed,
        GameSettings settings,
        ItemInventory? inventory = null,
        IClock? clock = null)
    {
        ArgumentNullException.ThrowIfNull(preset);

        if (!TryCreate(preset.Width, preset.Height, preset.MineCount, seed, settings, out GameSession? session, out string? error, inventory, clock, preset.Id))
        {
            throw new InvalidOperationException($"创建对局失败：{error}");
        }

        return session!;
    }

    /// <summary>按快照重开同一盘面（§6.3）。</summary>
    public static GameSession CreateReplay(
        BoardSnapshot snapshot,
        GameSettings settings,
        ItemInventory? inventory = null,
        IClock? clock = null,
        string? difficultyId = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(settings);

        var board = new Board(snapshot.Width, snapshot.Height, snapshot.MineCount, snapshot.Seed);
        if (!BoardGenerator.TryPlaceMinesAt(board, snapshot.FirstClickIndex, snapshot.MineIndexes, out string? error))
        {
            throw new InvalidOperationException($"快照还原失败：{error}");
        }

        board.UsedExtendedSafeZone = snapshot.UsedExtendedSafeZone;

        var session = new GameSession(board, settings, inventory, clock ?? SystemClock.Instance, snapshot.Seed, difficultyId, snapshot.Locked);
        GrantInitialGiftIfItemsAvailable(session);
        return session;
    }

    /// <summary>
    /// 首次进入游戏赠送 1 个放大镜（§5.2）。
    /// 只在<b>本局启用道具</b>时赠送：纯经典模式是"无道具、无物品栏"（§5.5 ⑤），
    /// 如果在那里也把赠送标记掉，玩家切到"经典 + 道具"时反而拿不到本该属于他的那一个。
    /// </summary>
    private static void GrantInitialGiftIfItemsAvailable(GameSession session)
    {
        if (session.Settings.ItemsAvailable)
        {
            session.Inventory?.EnsureInitialGift();
        }
    }

    /// <summary>
    /// 按分享串开一局并复现同一盘面（§2.6 / §6.3）。
    ///
    /// 之所以能精确复现：分享串里有 seed、首点索引和"是否用了扩展安全区"，
    /// 而布雷只依赖 (seed, 首点, 安全区半径) 且是确定性的（见 <see cref="BoardGenerator"/>）。
    /// 这里刻意把 <see cref="GameSettings.FirstClickOpening"/> 设为分享串里的取值，
    /// 这样即使本机设置不同，生成出来的也是同一张盘。
    /// </summary>
    public static bool TryReplayShareCode(
        string? shareCode,
        GameMode mode,
        out GameSession? session,
        out string? error,
        ItemInventory? inventory = null,
        IClock? clock = null)
    {
        session = null;

        if (!BoardSnapshot.TryParseShareCode(shareCode, out int width, out int height, out int mines, out int seed, out int firstClick, out bool usedExtendedSafeZone, out bool locked))
        {
            error = "分享串格式不对，应当形如 16x16:40:123456:77:1。";
            return false;
        }

        var settings = new GameSettings
        {
            Mode = mode,
            FirstClickOpening = usedExtendedSafeZone,
        };

        // 锁定盘面：雷位与首点都由 seed 决定，所以重建之后要校验首点确实一致
        if (locked)
        {
            if (!TryCreateLocked(width, height, mines, seed, settings, out GameSession? lockedSession, out error, inventory, clock))
            {
                return false;
            }

            if (lockedSession!.Board.FirstClickIndex != firstClick)
            {
                error = "锁定盘面未能复现（首点与分享串不一致）。";
                return false;
            }

            session = lockedSession;

            // 与非锁定路径保持一致：复现时也把首点翻开，玩家看到的开局与分享方一样。
            // 锁定盘面的首点会被钉到同一个安全点上（见 Reveal），所以这一步的结果是确定的；
            // 少了它，"锁定盘面的分享串"复现出来会是一张全未翻开的盘，和普通分享串行为不一致。
            RevealResult replayReveal = lockedSession.Reveal(firstClick);
            if (replayReveal.IsBlocked)
            {
                session = null;
                error = replayReveal.Reason ?? "首点无法翻开，分享串里记录的盘面复现失败。";
                return false;
            }

            error = null;
            return true;
        }

        if (!TryCreate(width, height, mines, seed, settings, out GameSession? created, out error, inventory, clock, difficultyId: null))
        {
            return false;
        }

        RevealResult reveal = created!.Reveal(firstClick);
        if (reveal.IsBlocked)
        {
            error = reveal.Reason ?? "首点无法翻开，分享串里记录的盘面复现失败。";
            return false;
        }

        if (created.Board.UsedExtendedSafeZone != usedExtendedSafeZone)
        {
            error = "盘面未能完全复现（安全区设置与分享串不一致）。";
            return false;
        }

        session = created;
        error = null;
        return true;
    }

    /// <summary>导出本局盘面快照（未布雷时抛异常）。</summary>
    public BoardSnapshot CaptureSnapshot()
    {
        if (!Board.MinesPlaced)
        {
            throw new InvalidOperationException("尚未布雷（还没点第一格），无法生成盘面快照。");
        }

        return new BoardSnapshot(
            Board.Width,
            Board.Height,
            Board.MineCount,
            Seed,
            Board.FirstClickIndex,
            Board.UsedExtendedSafeZone,
            Board.GetMineIndexes())
        {
            Locked = Locked,
        };
    }

    // ---------------------------------------------------------------- 翻开

    /// <summary>翻开一格（首点会触发布雷与首点安全区，§2.2）。</summary>
    public RevealResult Reveal(int index)
    {
        ResetAffectedRange();

        if (!Board.IsValidIndex(index))
        {
            return new RevealResult(RevealOutcome.Blocked, index, RevealedCount, "索引越界。");
        }

        if (IsFinished)
        {
            return new RevealResult(RevealOutcome.Blocked, index, RevealedCount, "本局已结束。");
        }

        if (HasPendingFlood)
        {
            return new RevealResult(RevealOutcome.Blocked, index, RevealedCount, "上一片区域还在展开，请稍候。");
        }

        if (Board.IsRevealed(index))
        {
            return new RevealResult(RevealOutcome.Blocked, index, RevealedCount, "该格已翻开。");
        }

        if (Board.IsFlagged(index))
        {
            return new RevealResult(RevealOutcome.Blocked, index, RevealedCount, "该格已插旗，需要先取消旗帜。");
        }

        // 锁定盘面（含主线关卡）**开局不替玩家走第一步**：打开时整盘未翻开、计时为 0，
        // 玩家点下第一格才开始。
        //
        // 但锁定盘面的雷位是开局就按 seed 定死的，不能像普通模式那样"围着首点布雷"，
        // 所以这里把首次翻开钉到开局就算好的那格安全点（Board.FirstClickIndex）上：
        // 玩家点哪里都行，结果都是从那格安全地展开，既不会踩雷，盘面也仍然与 seed 一一对应。
        // 返回的 RevealResult.Index 会是**真正被翻开的那一格**，调用方据此就能看出这次被挪动了。
        if (Locked && Status == GameStatus.NotStarted && Board.MinesPlaced && Board.IsValidIndex(Board.FirstClickIndex))
        {
            index = Board.FirstClickIndex;
        }

        if (!Board.MinesPlaced)
        {
            if (!BoardGenerator.TryPlaceMines(Board, index, Settings, _random, out string? placementError))
            {
                return new RevealResult(RevealOutcome.Blocked, index, RevealedCount, placementError);
            }
        }

        if (Status == GameStatus.NotStarted)
        {
            Status = GameStatus.InProgress;
            StartTimer();
        }

        if (Board.IsMine(index))
        {
            Status = GameStatus.Lost;
            ExplodedIndex = index;
            StopTimer();
            return new RevealResult(RevealOutcome.Exploded, index, RevealedCount, null);
        }

        var fill = new FloodFillOperation(Board, index);
        int revealed = fill.Step(DefaultFloodChunkSize);
        RevealedCount += revealed;
        TrackAffected(fill);

        if (!fill.IsComplete)
        {
            _pendingFill = fill;
            return new RevealResult(RevealOutcome.Revealed, index, RevealedCount, "展开范围很大，正在分帧继续。");
        }

        return new RevealResult(CheckWin() ? RevealOutcome.Won : RevealOutcome.Revealed, index, RevealedCount, null);
    }

    /// <summary>
    /// 分帧继续上一次未完成的展开（§2.3）。UI 在 DispatcherQueue 的定时回调里反复调用即可，
    /// 一次点击就不会卡住界面。返回本次翻开的格数。
    /// </summary>
    public int ContinuePendingFlood(int maxCells = DefaultFloodChunkSize)
    {
        if (_pendingFill is null)
        {
            return 0;
        }

        int revealed = _pendingFill.Step(maxCells);
        RevealedCount += revealed;
        TrackAffected(_pendingFill);

        if (_pendingFill.IsComplete)
        {
            _pendingFill = null;
            CheckWin();
        }

        return revealed;
    }

    // ---------------------------------------------------------------- 插旗

    /// <summary>右键三态循环：空 → 旗 → 问号 → 空（§0 第 1 条）。</summary>
    public FlagResult ToggleFlag(int index)
    {
        ResetAffectedRange();

        if (!Board.IsValidIndex(index))
        {
            return new FlagResult(FlagOutcome.Blocked, index, RemainingMines, false, false, "索引越界。");
        }

        if (IsFinished)
        {
            return new FlagResult(FlagOutcome.Blocked, index, RemainingMines, false, false, "本局已结束。");
        }

        if (Board.IsRevealed(index))
        {
            return new FlagResult(FlagOutcome.Blocked, index, RemainingMines, false, false, "已翻开的格子不能插旗。");
        }

        // 三种状态迁移（空 → 旗 → 问号 → 空）都只影响这一格
        TrackAffected(index);

        // 旗 → 问号
        if (Board.IsFlagged(index))
        {
            Board.RemoveFlag(index, CellFlags.IsFlagged);
            Board.AddFlag(index, CellFlags.IsQuestion);
            FlagCount--;
            return new FlagResult(FlagOutcome.QuestionMarked, index, RemainingMines, false, false, null);
        }

        // 问号 → 空
        if (Board.IsQuestionMarked(index))
        {
            Board.RemoveFlag(index, CellFlags.IsQuestion);
            return new FlagResult(FlagOutcome.Cleared, index, RemainingMines, false, false, null);
        }

        // 空 → 旗
        Board.AddFlag(index, CellFlags.IsFlagged);
        FlagCount++;

        bool counted = false;
        bool granted = false;

        // 首点之前布雷尚未发生，此时不可能"插对"（§5.5 ① 的同一个前提）
        if (Board.MinesPlaced && Board.IsMine(index) && !Board.HasFlag(index, CellFlags.FlagCounted))
        {
            Board.AddFlag(index, CellFlags.FlagCounted);
            counted = true;

            if (Inventory is not null)
            {
                // v0.6：跨阈值不再自动发大镜，而是攒下一次"三选一"奖励（由 UI 弹窗领）。
                granted = Inventory.RecordCorrectFlag(Settings.ItemsAvailable, Settings.ChoiceRewards);
            }
        }

        return new FlagResult(FlagOutcome.Flagged, index, RemainingMines, granted, counted, null);
    }

    // ---------------------------------------------------------------- chord

    /// <summary>chord：双击已翻开的数字格批量展开（§2.4）。旗插错位置会踩雷，这是原版行为。</summary>
    public ChordResult Chord(int index)
    {
        ResetAffectedRange();

        if (!Board.IsValidIndex(index))
        {
            return new ChordResult(ChordOutcome.Blocked, index, 0, null, "索引越界。");
        }

        if (IsFinished)
        {
            return new ChordResult(ChordOutcome.Blocked, index, 0, null, "本局已结束。");
        }

        if (HasPendingFlood)
        {
            return new ChordResult(ChordOutcome.Blocked, index, 0, null, "上一片区域还在展开，请稍候。");
        }

        if (!Board.IsRevealed(index))
        {
            return new ChordResult(ChordOutcome.Blocked, index, 0, null, "只有已翻开的数字格可以 chord。");
        }

        if (Board.GetAdjacentMines(index) == 0)
        {
            return new ChordResult(ChordOutcome.Blocked, index, 0, null, "该格周围没有雷，chord 无意义。");
        }

        if (Board.CountNeighborsWith(index, CellFlags.IsFlagged) != Board.GetAdjacentMines(index))
        {
            return new ChordResult(ChordOutcome.Blocked, index, 0, null, "周围旗数与数字不符。");
        }

        ChordSolver.CollectTargets(Board, index, _chordTargets);
        if (_chordTargets.Count == 0)
        {
            return new ChordResult(ChordOutcome.Resolved, index, 0, null, null);
        }

        // 复制一份再遍历：展开过程中会改动棋盘状态
        int[] targets = _chordTargets.ToArray();
        int opened = 0;

        foreach (int target in targets)
        {
            if (Board.IsRevealed(target) || Board.IsFlagged(target))
            {
                continue;
            }

            if (Board.IsMine(target))
            {
                Status = GameStatus.Lost;
                ExplodedIndex = target;
                TrackAffected(target);
                StopTimer();
                return new ChordResult(ChordOutcome.Exploded, index, opened, target, "旗子插错了位置。");
            }

            var fill = new FloodFillOperation(Board, target);
            int revealed = fill.Step(DefaultFloodChunkSize);
            RevealedCount += revealed;
            opened += revealed;
            TrackAffected(fill);

            if (!fill.IsComplete)
            {
                _pendingFill = fill;
                return new ChordResult(ChordOutcome.Resolved, index, opened, null, "展开范围很大，正在分帧继续。");
            }
        }

        return new ChordResult(CheckWin() ? ChordOutcome.Won : ChordOutcome.Resolved, index, opened, null, null);
    }

    // ---------------------------------------------------------------- 道具

    /// <summary>
    /// 只看"道具本身此刻能不能用"，不看目标格（UI 用它决定物品栏图标是否置灰）。
    /// </summary>
    public ItemUseCheck CheckMagnifierAvailable()
    {
        ItemUseCheck state = CheckMagnifierState();
        if (state != ItemUseCheck.Ok)
        {
            return state;
        }

        return Inventory!.MagnifierCount <= 0 ? ItemUseCheck.NotInInventory : ItemUseCheck.Ok;
    }

    /// <summary>本局此刻能否对目标格使用放大镜（§5.3）。</summary>
    public ItemUseCheck CheckMagnifier(int targetIndex)
    {
        ItemUseCheck state = CheckMagnifierState();
        if (state != ItemUseCheck.Ok)
        {
            return state;
        }

        // 顺序讲究：既然调用方问的是"这个目标格"，目标非法就先报目标非法，
        // 否则"物品栏空了"会把"越界"这种更具体的错误盖掉。
        if (!Board.IsValidIndex(targetIndex))
        {
            return ItemUseCheck.OutOfBounds;
        }

        if (Inventory!.MagnifierCount <= 0)
        {
            return ItemUseCheck.NotInInventory;
        }

        if (MagnifierItem.AllAffectedAlreadyItemRevealed(Board, targetIndex, _magnifier.Radius))
        {
            return ItemUseCheck.AllAlreadyRevealed;
        }

        return ItemUseCheck.Ok;
    }

    /// <summary>与目标无关的状态检查：本局是否启用道具、是否已结束、是否还没首点。</summary>
    private ItemUseCheck CheckMagnifierState()
    {
        if (!Settings.ItemsAvailable || Inventory is null)
        {
            return ItemUseCheck.ItemsNotAvailable;
        }

        if (IsFinished)
        {
            return ItemUseCheck.GameFinished;
        }

        // 首点之前棋盘上还没有雷，放大镜无内容可透视（§5.5 ① 硬约束）
        if (!Board.MinesPlaced)
        {
            return ItemUseCheck.BeforeFirstClick;
        }

        return ItemUseCheck.Ok;
    }

    /// <summary>使用放大镜透视目标格周围 3×3（§5.4）。</summary>
    public MagnifierUseResult UseMagnifier(int targetIndex)
    {
        ResetAffectedRange();

        ItemUseCheck check = CheckMagnifier(targetIndex);
        if (check != ItemUseCheck.Ok)
        {
            return MagnifierUseResult.Rejected(check, Inventory?.MagnifierCount ?? 0);
        }

        Inventory!.TryConsumeMagnifier();
        ItemUsed = true;

        MagnifierReveal reveal = _magnifier.Apply(Board, targetIndex);

        // 透视会一次性改掉整片区域（半径 2 时是 5×5），范围要按实际覆盖的格算
        DateTimeOffset now = _clock.UtcNow;
        foreach (MagnifierCellInfo cell in reveal.Cells)
        {
            TrackAffected(cell.Index);

            if (_itemRevealFadeSeconds > 0 && !cell.WasAlreadyItemRevealed)
            {
                _itemRevealTimes[cell.Index] = now;
            }
        }

        return MagnifierUseResult.Used(reveal, Inventory.MagnifierCount);
    }

    /// <summary>只读预览（UI 悬停时用，不消耗道具、不改棋盘）。</summary>
    public MagnifierReveal PreviewMagnifier(int targetIndex) => _magnifier.Preview(Board, targetIndex);

    /// <summary>透视信息是否会在将来过期（§5.4 备选：几秒后恢复迷雾）。UI 据此决定要不要继续 tick。</summary>
    public bool HasExpiringItemReveals => _itemRevealFadeSeconds > 0 && _itemRevealTimes.Count > 0;

    /// <summary>透视信息保留时长（秒）；0 表示永久。</summary>
    public int ItemRevealFadeSeconds => _itemRevealFadeSeconds;

    /// <summary>
    /// 结算过期的透视：把超过保留时长的格子上的 <see cref="CellFlags.ItemRevealed"/> 清掉，
    /// 于是渲染器不需要任何改动就会把那几格画回"未翻开"。
    ///
    /// 返回是否真的清掉了东西（true 时 UI 应当重绘；具体范围由
    /// <see cref="ConsumeLastAffectedRange"/> 给出）。
    /// </summary>
    public bool ExpireItemReveals()
    {
        if (_itemRevealFadeSeconds <= 0 || _itemRevealTimes.Count == 0)
        {
            return false;
        }

        DateTimeOffset now = _clock.UtcNow;
        List<int>? expired = null;

        foreach (KeyValuePair<int, DateTimeOffset> pair in _itemRevealTimes)
        {
            if ((now - pair.Value).TotalSeconds >= _itemRevealFadeSeconds)
            {
                (expired ??= new List<int>()).Add(pair.Key);
            }
        }

        if (expired is null)
        {
            return false;
        }

        foreach (int index in expired)
        {
            _itemRevealTimes.Remove(index);
            Board.RemoveFlag(index, CellFlags.ItemRevealed);
            TrackAffected(index);
        }

        return true;
    }

    // ---------------------------------------------------------------- 计时

    /// <summary>
    /// 窗口焦点变化（§A-4）。只有 <see cref="GameSettings.PauseTimerOnFocusLoss"/> 打开时才生效。
    /// </summary>
    public void SetWindowFocused(bool focused)
    {
        if (!Settings.PauseTimerOnFocusLoss || _timerStopped)
        {
            return;
        }

        if (focused)
        {
            if (_runningSince is null && Status == GameStatus.InProgress)
            {
                _runningSince = _clock.UtcNow;
            }
        }
        else if (_runningSince is { } started)
        {
            _accumulated += _clock.UtcNow - started;
            _runningSince = null;
        }
    }

    private void StartTimer()
    {
        if (!_timerStopped && _runningSince is null)
        {
            _runningSince = _clock.UtcNow;
        }
    }

    private void StopTimer()
    {
        if (_runningSince is { } started)
        {
            _accumulated += _clock.UtcNow - started;
            _runningSince = null;
        }

        _timerStopped = true;
    }

    // ---------------------------------------------------------------- 新增道具（v0.6）

    /// <summary>声呐扫描现在能不能用（UI 的瞄准态入口用）。</summary>
    public ItemUseCheck CheckSonarAvailable() => _sonar.Check(this, Board.MinesPlaced ? Board.FirstClickIndex : 0);

    /// <summary>时间冻结现在能不能用。</summary>
    public ItemUseCheck CheckFreezeAvailable() => _freeze.Check(this, -1);

    /// <summary>最近一次声呐扫描的结果（UI 显示用；再扫一次或换一局会被覆盖）。</summary>
    public SonarScanResult? LastSonarScan { get; private set; }

    /// <summary>当前是否在冻结中（会先把到期的冻结结算掉）。</summary>
    public bool IsTimerFrozen
    {
        get
        {
            SyncFreeze();
            return _freezeStartedAt is not null;
        }
    }

    /// <summary>本次冻结还剩几秒（没在冻结时为 0）。</summary>
    public double FrozenRemainingSeconds
    {
        get
        {
            SyncFreeze();
            if (_freezeStartedAt is not { } started)
            {
                return 0d;
            }

            return Math.Max(0d, _freeze.Seconds - (_clock.UtcNow - started).TotalSeconds);
        }
    }

    /// <summary>
    /// 声呐扫描：只报出以 <paramref name="targetIndex"/> 为中心的区域里有几颗雷，**不改变棋盘状态**。
    /// 不翻开、不插旗、不影响胜负，也不进"已获知信息"（同一个区域可以重复扫，只是浪费道具）。
    /// </summary>
    public SonarScanResult ScanWithSonar(int targetIndex)
    {
        ItemUseCheck check = _sonar.Check(this, targetIndex);
        if (check != ItemUseCheck.Ok || Inventory is null)
        {
            return SonarScanResult.Rejected(check);
        }

        int mines = _sonar.CountMines(this, targetIndex);
        Inventory.TryConsume(SonarScanItem.ItemId);

        LastSonarScan = new SonarScanResult(
            targetIndex, _sonar.Radius, mines, true, ItemUseCheck.Ok, Inventory.SonarCount);

        return LastSonarScan.Value;
    }

    /// <summary>时间冻结：让倒计时暂停若干秒（默认 20）。只影响用时，不影响棋盘。</summary>
    public TimeFreezeResult FreezeTimer()
    {
        ItemUseCheck check = _freeze.Check(this, -1);
        if (check != ItemUseCheck.Ok || Inventory is null)
        {
            return new TimeFreezeResult(false, 0d, check, Inventory?.FreezeCount ?? 0);
        }

        Inventory.TryConsume(TimeFreezeItem.ItemId);
        _freezeStartedAt = _clock.UtcNow;

        return new TimeFreezeResult(true, _freeze.Seconds, ItemUseCheck.Ok, Inventory.FreezeCount);
    }

    /// <summary>领取一次"三选一"奖励：把选中的道具放进物品栏。</summary>
    public bool TryClaimReward(string itemId, out ItemGrantResult result)
    {
        if (Inventory is null)
        {
            result = new ItemGrantResult(false, false, 0);
            return false;
        }

        result = Inventory.ClaimReward(itemId);
        return result.Granted;
    }

    /// <summary>
    /// 冻结状态自愈：把已经到期的冻结折进 <see cref="ElapsedSeconds"/> 用的累计量。
    /// 不做这一步的话，"冻结了一段时间但还没被任何人结算"会让用时越扣越多（甚至变成负数）。
    /// 单次扣除上限就是一次冻结的时长，钟表跳变也不会把用时扣崩。
    /// </summary>
    /// <summary>单次冻结能被扣掉的时长上限 = 一次冻结的时长（防御钟表跳变）。</summary>
    private TimeSpan ClampFreezeSpan(TimeSpan span)
    {
        TimeSpan max = TimeSpan.FromSeconds(_freeze.Seconds);
        if (span > max)
        {
            return max;
        }

        return span < TimeSpan.Zero ? TimeSpan.Zero : span;
    }

    private void SyncFreeze()
    {
        if (_freezeStartedAt is not { } started)
        {
            return;
        }

        TimeSpan span = _clock.UtcNow - started;
        bool finished = Status is GameStatus.Won or GameStatus.Lost;
        bool expired = span.TotalSeconds >= _freeze.Seconds;

        if (!finished && !expired)
        {
            return;
        }

        _frozenTotal += ClampFreezeSpan(span);
        _freezeStartedAt = null;
    }

    // ---------------------------------------------------------------- 内部

    /// <summary>O(1) 胜利判定（§2.5）：已翻开格数 == 总线 − 雷数。</summary>
    private bool CheckWin()
    {
        if (Status == GameStatus.InProgress && RevealedCount >= Board.CellCount - Board.MineCount)
        {
            Status = GameStatus.Won;
            FlagAllRemainingMines();
            StopTimer();
            return true;
        }

        return false;
    }

    /// <summary>
    /// 胜利时把还没插旗的雷自动插上旗（问号也换成旗）。
    ///
    /// 为什么需要这一步：扫雷的胜利条件是"翻开所有非雷格"，**并不要求玩家把雷标出来**，
    /// 所以原本赢了之后画面上还会留着一堆没翻开的格子。玩家看到的是
    /// "还有雷没扫完"却已经判胜、下一关还解锁了 —— 很自然会认为关卡逻辑坏了。
    /// 原版扫雷在赢的那一刻也是把剩下的雷标出来的，所以这既是修 bug，也是还原原版表现。
    /// 顺带把剩余雷数归零（<see cref="RemainingMines"/> 由 <see cref="FlagCount"/> 推出）。
    /// </summary>
    private void FlagAllRemainingMines()
    {
        for (int index = 0; index < Board.CellCount; index++)
        {
            if (!Board.IsMine(index) || Board.IsFlagged(index))
            {
                continue;
            }

            Board.RemoveFlag(index, CellFlags.IsQuestion);
            Board.AddFlag(index, CellFlags.IsFlagged);
            FlagCount++;
        }
    }
}
