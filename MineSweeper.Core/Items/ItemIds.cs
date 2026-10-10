namespace MineSweeper.Core.Items;

/// <summary>
/// 三种道具的稳定 Id（持久化用，**不要改**）。
///
/// v0.6 起道具从一个变成三个：放大镜（原有）、声呐扫描 / 时间冻结（新增）。
/// 物品栏三格正好一格一种，与 §5.2 的"三格"设计一致。
/// </summary>
public static class ItemIds
{
    public const string Magnifier = "magnifier";

    public const string Sonar = "sonar";

    public const string Freeze = "freeze";

    /// <summary>全部道具（顺序 = 物品栏从左到右，也是"三选一"弹窗的顺序）。</summary>
    public static readonly IReadOnlyList<string> All = new[] { Magnifier, Sonar, Freeze };

    /// <summary>道具显示名。</summary>
    public static string DisplayNameOf(string itemId) => itemId switch
    {
        Magnifier => "放大镜",
        Sonar => "声呐扫描",
        Freeze => "时间冻结",
        _ => itemId,
    };

    /// <summary>道具用途的一句话说明（"三选一"弹窗里给玩家看的）。</summary>
    public static string DescriptionOf(string itemId) => itemId switch
    {
        Magnifier => "透视 3×3 区域，把格子直接翻开（不消耗本局步数）。",
        Sonar => "选一个 3×3 区域，只告诉你里面有**几颗雷**，不告诉你位置。",
        Freeze => "倒计时暂停 20 秒，计时门槛按暂停后的时间算。",
        _ => string.Empty,
    };
}