namespace MineSweeper.Core.Items;

/// <summary>
/// 金币钱包（v1.4 商店机制；v1.5.1 改为跨局累计）。
///
/// 规则：
/// <list type="bullet">
/// <item>每扫出一个雷 <b>+1 金币</b>（"扫出"= 正确地插旗标记一颗雷，与原有的正确标记计数同一事件）。</item>
/// <item><b>金币跨局累计</b>：余额存放在 <see cref="PlayerProgress.Coins"/>（progress.json），
///       新局 / 新关卡不再清零；旧存档没有 coins 字段时迁移为 0。</item>
/// <item>商店买到的道具直接进账号物品栏（<see cref="ItemInventory.ClaimPurchase"/>），也跨局保留。</item>
/// </list>
///
/// 钱包本身仍然"一局一个"：它只是账号余额的读写视图，同时记录<b>本局</b>赚/花了多少
/// （<see cref="EarnedTotal"/> / <see cref="SpentTotal"/>）供展示。
/// </summary>
public sealed class CoinWallet
{
    /// <summary>每扫出一颗雷获得的金币。</summary>
    public const int CoinsPerMine = 1;

    /// <summary>账号金币的安全上限（避免手改存档/极端累计溢出）。</summary>
    public const int MaxCoins = 9_999_999;

    /// <summary>放大镜售价（需求：30 金币）。</summary>
    public const int MagnifierPrice = 30;

    /// <summary>声呐扫描售价（需求：20 金币）。</summary>
    public const int SonarPrice = 20;

    /// <summary>时间冻结售价（需求：20 金币）。</summary>
    public const int FreezePrice = 20;

    private readonly PlayerProgress? _progress;
    private int _localCoins;

    /// <param name="progress">
    /// 账号进度（有它时金币跨局累计）。纯经典/隐藏关这种没有物品栏的会话传 null，
    /// 金币只在本局存在（那些模式没有商店，且隐藏关本来就不落盘）。
    /// </param>
    public CoinWallet(PlayerProgress? progress = null)
    {
        _progress = progress;
    }

    /// <summary>当前持币数（有账号进度时就是跨局累计余额）。</summary>
    public int Coins => _progress?.Coins ?? _localCoins;

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

    /// <summary>扫出一颗雷：加 <see cref="CoinsPerMine"/> 个金币（写进账号余额或本局钱包）。</summary>
    public void EarnForMine()
    {
        if (_progress is not null)
        {
            _progress.Coins = Math.Min(MaxCoins, _progress.Coins + CoinsPerMine);
        }
        else
        {
            _localCoins = Math.Min(MaxCoins, _localCoins + CoinsPerMine);
        }

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

        if (_progress is not null)
        {
            _progress.Coins -= price;
        }
        else
        {
            _localCoins -= price;
        }

        SpentTotal += price;
        return true;
    }

    /// <summary>
    /// 只清空"本局赚/花"统计，**不动账号余额**（v1.5.1：金币已跨局累计）。
    /// 新局本来就会 new 一个钱包，所以正常路径不需要调用它；保留给测试/复用场景。
    /// </summary>
    public void Reset()
    {
        EarnedTotal = 0;
        SpentTotal = 0;
    }
}
