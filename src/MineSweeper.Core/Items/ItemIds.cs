using MineSweeper.Core.Localization;

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

    /// <summary>全部道具（顺序 = 物品栏从左到右，也是商店里的商品顺序）。</summary>
    public static readonly IReadOnlyList<string> All = new[] { Magnifier, Sonar, Freeze };

    /// <summary>道具显示名（走语言表，中英切换后商店/Toast 不会出现中英混排）。</summary>
    public static string DisplayNameOf(string itemId) => itemId switch
    {
        Magnifier => Strings.Get("item.magnifier"),
        Sonar => Strings.Get("item.sonar"),
        Freeze => Strings.Get("item.freeze"),
        _ => itemId,
    };

    /// <summary>道具用途的一句话说明（同 DisplayNameOf，走语言表；商店 tooltip 直接显示）。</summary>
    public static string DescriptionOf(string itemId) => itemId switch
    {
        Magnifier => Strings.Get("item.magnifier.desc"),
        Sonar => Strings.Get("item.sonar.desc"),
        Freeze => Strings.Get("item.freeze.desc"),
        _ => string.Empty,
    };
}