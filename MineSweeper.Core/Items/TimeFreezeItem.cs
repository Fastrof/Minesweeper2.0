namespace MineSweeper.Core.Items;

/// <summary>
/// 时间冻结（v0.6 新增道具）：让倒计时暂停 <see cref="Seconds"/> 秒（默认 20）。
///
/// 实现要点：冻结期间 <see cref="GameSession.ElapsedSeconds"/> **不再增长**，
/// 所以计时显示、星级判定（目标/挑战时间都按用时算）都会自动跟着"暂停"，不需要各处特判。
/// 冻结只影响"用时"，不影响棋盘状态与胜负判定。
/// </summary>
public sealed class TimeFreezeItem : IItem
{
    public const string ItemId = ItemIds.Freeze;

    /// <summary>默认冻结时长（秒）——需求里写的就是 20 秒。</summary>
    public const int DefaultSeconds = 20;

    public TimeFreezeItem(int seconds = DefaultSeconds)
        => Seconds = Math.Clamp(seconds, 1, 600);

    public TimeFreezeItem(ItemConfig? config)
        : this(config?.FreezeSeconds ?? DefaultSeconds)
    {
    }

    public string Id => ItemId;

    public string DisplayName => "时间冻结";

    /// <summary>冻结时长（秒）。</summary>
    public int Seconds { get; }

    public ItemUseCheck Check(GameSession session, int targetIndex)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (!session.Settings.ItemsAvailable || session.Inventory is null)
        {
            return ItemUseCheck.ItemsNotAvailable;
        }

        if (session.Status is GameStatus.Won or GameStatus.Lost)
        {
            return ItemUseCheck.GameFinished;
        }

        // 计时还没开始（首点之前）冻结没有意义，也不该消耗道具
        if (session.Status != GameStatus.InProgress)
        {
            return ItemUseCheck.TimerNotRunning;
        }

        if (session.IsTimerFrozen)
        {
            return ItemUseCheck.AlreadyFrozen;
        }

        if (session.Inventory.CountOf(Id) <= 0)
        {
            return ItemUseCheck.NotInInventory;
        }

        return ItemUseCheck.Ok;
    }
}