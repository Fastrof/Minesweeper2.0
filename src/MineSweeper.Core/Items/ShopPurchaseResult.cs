namespace MineSweeper.Core.Items;

/// <summary>商店购买的结果分类（v1.4）。UI 按这个决定文案与按钮状态。</summary>
public enum ShopPurchaseOutcome
{
    /// <summary>买成功了。</summary>
    Bought = 0,

    /// <summary>金币不够（最常见，UI 显示"还差 N 个"）。</summary>
    NotEnoughCoins = 1,

    /// <summary>该道具这一格已经满了，买进去会溢出，所以不扣钱。</summary>
    InventoryFull = 2,

    /// <summary>本局不启用道具（纯经典模式），商店不卖。</summary>
    ItemsNotAvailable = 3,

    /// <summary>本局已结束。</summary>
    GameFinished = 4,

    /// <summary>不认识的商品 Id。</summary>
    UnknownItem = 5,
}

/// <summary>
/// 一次商店购买的结果。
/// 刻意把**当前金币 / 当前数量**一起带出来：UI 不需要再回头读一遍状态，也就不容易出现
/// "扣了钱但界面没刷新"这类不一致。
/// </summary>
public sealed record ShopPurchaseResult(
    ShopPurchaseOutcome Outcome,
    string ItemId,
    int Price,
    int CoinsAfter,
    int CountAfter,
    string? Reason)
{
    /// <summary>是否买成功。</summary>
    public bool Bought => Outcome == ShopPurchaseOutcome.Bought;

    /// <summary>差多少金币（买得起时为 0）。</summary>
    public int Shortfall => Outcome == ShopPurchaseOutcome.NotEnoughCoins
        ? Math.Max(0, Price - CoinsAfter)
        : 0;

    public static ShopPurchaseResult Rejected(
        ShopPurchaseOutcome outcome,
        string itemId,
        int coins,
        int price) => new(outcome, itemId, price, coins, 0, TextOf(outcome));

    /// <summary>失败原因的默认文案（不依赖本地化，便于 Core 单测断言）。</summary>
    public static string TextOf(ShopPurchaseOutcome outcome) => outcome switch
    {
        ShopPurchaseOutcome.Bought => "购买成功。",
        ShopPurchaseOutcome.NotEnoughCoins => "金币不足。",
        ShopPurchaseOutcome.InventoryFull => "这一格已经满了。",
        ShopPurchaseOutcome.ItemsNotAvailable => "本局不启用道具。",
        ShopPurchaseOutcome.GameFinished => "本局已结束。",
        _ => "不认识的道具。",
    };
}
