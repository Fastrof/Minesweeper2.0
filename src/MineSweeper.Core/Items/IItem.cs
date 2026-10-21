namespace MineSweeper.Core.Items;

/// <summary>
/// 道具接口（§1.3：Items/IItem.cs）。
/// 道具只提供信息或操作优势，具体"能不能用/用了之后发生什么"由 Core 决定，
/// UI 只负责把 <see cref="ItemUseCheck"/> 翻译成提示文案。
/// </summary>
public interface IItem
{
    /// <summary>稳定标识（持久化用，不要改）。</summary>
    string Id { get; }

    /// <summary>显示名。</summary>
    string DisplayName { get; }

    /// <summary>本局当前状态下，能否对目标格使用。</summary>
    ItemUseCheck Check(GameSession session, int targetIndex);
}

/// <summary>把 <see cref="ItemUseCheck"/> 翻成给玩家看的中文原因（§5.3 的提示条）。</summary>
public static class ItemUseCheckText
{
    public static string Describe(ItemUseCheck check) => check switch
    {
        ItemUseCheck.Ok => string.Empty,
        ItemUseCheck.ItemsNotAvailable => "经典模式不含道具。",
        ItemUseCheck.NotInInventory => "物品栏里没有这个道具了。",
        ItemUseCheck.BeforeFirstClick => "先翻开第一格才能使用道具（此时棋盘上还没有雷）。",
        ItemUseCheck.GameFinished => "本局已经结束。",
        ItemUseCheck.OutOfBounds => "目标区域超出棋盘范围。",
        ItemUseCheck.AllAlreadyRevealed => "该区域已经透视过了。",
        ItemUseCheck.TimerNotRunning => "倒计时还没开始，冻结没有意义。",
            ItemUseCheck.AlreadyFrozen => "已经在冻结中，等它结束再用吧。",
            ItemUseCheck.FloodPending => "上一片区域还在展开，请稍候。",
        _ => "无法使用。",
    };
}