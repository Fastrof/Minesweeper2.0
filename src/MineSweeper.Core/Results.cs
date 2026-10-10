namespace MineSweeper.Core;

/// <summary>
/// 一次翻开动作的结果（§3.1）。
/// </summary>
public enum RevealOutcome
{
    /// <summary>被拒绝：已翻开、已插旗、或本局已结束。</summary>
    Blocked = 0,

    /// <summary>翻开了格子（可能连带展开一片区域）。</summary>
    Revealed = 1,

    /// <summary>踩雷，本局失败。</summary>
    Exploded = 2,

    /// <summary>这一步让本局达成胜利。</summary>
    Won = 3,
}

/// <summary>翻开结果。</summary>
public readonly record struct RevealResult(RevealOutcome Outcome, int Index, int RevealedCount, string? Reason)
{
    public bool IsBlocked => Outcome == RevealOutcome.Blocked;
}

/// <summary>插旗/问号三态循环的结果（§0 第 1 条）。</summary>
public enum FlagOutcome
{
    /// <summary>被拒绝（已翻开，或本局已结束）。</summary>
    Blocked = 0,

    /// <summary>空 → 旗。</summary>
    Flagged = 1,

    /// <summary>旗 → 问号。</summary>
    QuestionMarked = 2,

    /// <summary>问号 → 空。</summary>
    Cleared = 3,
}

/// <summary>插旗结果。<see cref="MagnifierGranted"/> 表示这一步正好触发了放大镜发放（§5.2）。</summary>
public readonly record struct FlagResult(FlagOutcome Outcome, int Index, int RemainingMines, bool MagnifierGranted, bool CountedAsCorrectFlag, string? Reason)
{
    public bool IsBlocked => Outcome == FlagOutcome.Blocked;
}

/// <summary>chord 结果（§2.4）。</summary>
public enum ChordOutcome
{
    /// <summary>被拒绝：不是已翻开的数字格，或旗数与数字不符。</summary>
    Blocked = 0,

    /// <summary>已结算（可能一个格子都没开，例如周围全部已翻开）。</summary>
    Resolved = 1,

    /// <summary>结算过程中踩雷（旗插错位置，原版行为，必须保留）。</summary>
    Exploded = 2,

    /// <summary>结算后本局胜利。</summary>
    Won = 3,
}

/// <summary>chord 结果。</summary>
public readonly record struct ChordResult(ChordOutcome Outcome, int Index, int OpenedCount, int? ExplodedIndex, string? Reason)
{
    public bool IsBlocked => Outcome == ChordOutcome.Blocked;
}

/// <summary>道具是否可用（§5.3）。</summary>
public enum ItemUseCheck
{
    /// <summary>可用。</summary>
    Ok = 0,

    /// <summary>本局未启用道具系统（纯经典模式，§5.5 ⑤）。</summary>
    ItemsNotAvailable = 1,

    /// <summary>物品栏里没有这个道具。</summary>
    NotInInventory = 2,

    /// <summary>首点之前禁止使用（§5.5 ①：此时棋盘上还没有雷）。</summary>
    BeforeFirstClick = 3,

    /// <summary>本局已结束。</summary>
    GameFinished = 4,

    /// <summary>目标区域在棋盘之外。</summary>
    OutOfBounds = 5,

    /// <summary>目标 3×3 区域已全部被透视过，用了也是浪费（§5.3，不消耗道具）。</summary>
    AllAlreadyRevealed = 6,

    /// <summary>计时还没开始（首点之前冻结没有意义）。</summary>
    TimerNotRunning = 7,

    /// <summary>已经在冻结中，叠加一层没有意义（不消耗道具）。</summary>
    AlreadyFrozen = 8,
}

/// <summary>声呐扫描的结果（§5.6 的同类信息展示，但只给数量、不给位置）。</summary>
public readonly record struct SonarScanResult(
    int CenterIndex,
    int Radius,
    int MineCount,
    bool Success,
    ItemUseCheck Check,
    int RemainingCount)
{
    /// <summary>被拒绝时用。</summary>
    public static SonarScanResult Rejected(ItemUseCheck check)
        => new(-1, 0, 0, false, check, 0);
}

/// <summary>时间冻结的结果。</summary>
public readonly record struct TimeFreezeResult(
    bool Started,
    double FrozenSeconds,
    ItemUseCheck Check,
    int RemainingCount);