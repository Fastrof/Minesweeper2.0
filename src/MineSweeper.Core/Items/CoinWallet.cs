namespace MineSweeper.Core.Items;

/// <summary>
/// 金币钱包（v1.4 商店机制）。
///
/// 规则（需求原文）：
/// <list type="bullet">
/// <item>每扫出一个雷 <b>+1 金币</b>（"扫出"= 正确地插旗标记一颗雷，与原有的正确标记计数同一事件）。</item>
/// <item><b>每局重新开始时清零</b>；关卡模式**每到新的一关也清零**。</item>
/// <item>金币买到的道具进**本局临时库存**，随金币一起清零，不写进账号存档。</item>
/// </list>
///
/// 所以这个对象挂在 <see cref="GameSession"/> 上而不是 <c>PlayerProgress</c> 上 ——
/// 它天生就是"一局生命周期"的数据，不该跨局保留，也就不该有存档字段与迁移逻辑。
/// </summary>
public sealed class CoinWallet
{
    /// <summary>每扫出一颗雷获得的金币。</summary>
    public const int CoinsPerMine = 1;

    /// <summary>放大镜售价（需求：30 金币）。</summary>
    public const int MagnifierPrice = 30;

    /// <summary>声呐扫描售价（需求：20 金币）。</summary>
    public const int SonarPrice = 20;

    /// <summary>时间冻结售价（需求：20 金币）。</summary>
    public const int FreezePrice = 20;

    /// <summary>当前持币数。</summary>
    public int Coins { get; private set; }

    /// <summary>本局累计赚到的金币（只增不减），供结算/成就展示用。</summary>
    public int EarnedTotal { get; private set; }

    /// <summary>本局花掉的金币总数，供结算展示用。</summary>
    public int SpentTotal { get; private set; }

    /// <summary>
    /// 某个道具的**默认**售价；不认识的 Id 返回 0（调用方按"买不到"处理）。
    ///
    /// 注意这只是默认值：商店的实际售价允许通过 <c>config.json</c> 的 <c>shop.*.price</c> 改，
    /// 所以扣款必须用调用方拿到的价格（见 <see cref="TrySpend(int)"/>），不要再用这个常量判断。
    /// 保留它是为了让"默认价格表"仍有一个单一出处（测试与文案也引用它）。
    /// </summary>
    public static int PriceOf(string itemId) => itemId switch
    {
        ItemIds.Magnifier => MagnifierPrice,
        ItemIds.Sonar => SonarPrice,
        ItemIds.Freeze => FreezePrice,
        _ => 0,
    };

    /// <summary>能不能买得起（按调用方给定的价格）。</summary>
    public bool CanAfford(int price) => price > 0 && Coins >= price;

    /// <summary>扫出一颗雷：加 <see cref="CoinsPerMine"/> 个金币。</summary>
    public void EarnForMine()
    {
        Coins += CoinsPerMine;
        EarnedTotal += CoinsPerMine;
    }

    /// <summary>
    /// 按给定价格扣钱。买不起返回 false 且不动钱包 —— 调用方据此提示"金币不足"。
    ///
    /// <b>价格由调用方传入</b>（商店取 <c>ItemConfig.ShopPriceOf</c>）：
    /// 曾经这里按道具 Id 查硬编码常量扣款，而"能不能买"用的是可改的配置价，
    /// 玩家把 config.json 的价格改高之后就会出现"显示 50、实扣 30"的错账。
    /// 现在价格只有一个来源，钱包只负责记账。
    /// </summary>
    public bool TrySpend(int price)
    {
        if (price <= 0 || Coins < price)
        {
            return false;
        }

        Coins -= price;
        SpentTotal += price;
        return true;
    }

    /// <summary>清零（新局 / 新关卡开始时调用）。</summary>
    public void Reset()
    {
        Coins = 0;
        EarnedTotal = 0;
        SpentTotal = 0;
    }
}
