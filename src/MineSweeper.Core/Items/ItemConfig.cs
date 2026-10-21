using System.Text.Json.Serialization;

namespace MineSweeper.Core.Items;

/// <summary>
/// 道具配置（§5.7）。刻意不写死在代码里，JSON 键名与文档里的配置键一一对应，
/// 这样调手感时只需要改 config.json 而不用重新编译。
/// </summary>
public sealed class ItemConfig
{
    /// <summary>
    /// 配置结构版本。
    ///
    /// 字段变动时 +1，并在 <see cref="Persistence.ItemConfigStore"/> 里补迁移。
    /// v1.4 从 1 提到 2：放大镜从"半径"改成"宽×高"、持有上限 3→9、新增商店价格三个键。
    /// **加这个字段的直接原因**：老玩家的 config.json 里没有新键，
    /// 反序列化时那些键会静默用默认值 —— 但"持有上限"不一样，老文件里写着 3，
    /// 会被原样读进来，于是商店永远买不了东西。
    /// </summary>
    public const int CurrentSchemaVersion = 2;

    /// <summary>
    /// 配置结构版本（见 <see cref="CurrentSchemaVersion"/>）。
    ///
    /// **默认值必须是 0，不能是 CurrentSchemaVersion** —— 老配置文件里根本没有这个键，
    /// 反序列化时若默认成当前版本，"是否需要迁移"的判断会立刻为 false，迁移永远不执行。
    /// </summary>
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; }

    /// <summary>奖励阈值（v1.4 起已不再用于发放道具，只保留给统计展示）。</summary>
    [JsonPropertyName("item.magnifier.threshold")]
    public int MagnifierThreshold { get; set; } = 20;

    /// <summary>
    /// 持有上限的默认值（v1.4 起 9）。
    /// 抽成常量是因为迁移代码要用它：老配置里写着 3，要能识别并提到这个值。
    /// </summary>
    public const int DefaultMaxHold = 9;

    /// <summary>
    /// 持有上限。
    ///
    /// v1.4 从 3 提到 **9**：需求"金币每局清零、买到的道具也随本局清零"意味着
    /// 玩家账号里的存量是**长期累积**的。上限还是 3 的话，老玩家库存早就满了，
    /// 攒到金币也买不了任何东西 —— 商店对他们等于不存在。提高上限让商店真正可用。
    /// </summary>
    [JsonPropertyName("item.magnifier.maxHold")]
    public int MagnifierMaxHold { get; set; } = DefaultMaxHold;

    /// <summary>首次进入游戏赠送数量（§5.2；v0.6 起三种道具各送 2 个）。</summary>
    [JsonPropertyName("item.magnifier.initialGift")]
    public int MagnifierInitialGift { get; set; } = 2;

    /// <summary>声呐扫描的首次赠送数量（v0.6 起默认 2）。</summary>
    [JsonPropertyName("item.sonar.initialGift")]
    public int SonarInitialGift { get; set; } = 2;

    /// <summary>时间冻结的首次赠送数量（v0.6 起默认 2）。</summary>
    [JsonPropertyName("item.freeze.initialGift")]
    public int FreezeInitialGift { get; set; } = 2;

    /// <summary>
    /// 透视范围的宽（格）。v1.4 起默认 **2**（原为 3）—— 需求要求把放大镜削弱成 2×2。
    /// 被点中的格子是这 2×2 的**左上角**。
    /// </summary>
    [JsonPropertyName("item.magnifier.width")]
    public int MagnifierWidth { get; set; } = MagnifierItem.DefaultWidth;

    /// <summary>透视范围的高（格）。v1.4 起默认 **2**。</summary>
    [JsonPropertyName("item.magnifier.height")]
    public int MagnifierHeight { get; set; } = MagnifierItem.DefaultHeight;

    /// <summary>
    /// 透视信息保留时长（秒）。<b>0 = 永久保留</b>，这是默认值，也是 §5.4 硬性约束 3 的取值。
    ///
    /// 文档 §5.4 把这个"5 秒后恢复迷雾"列为备选并注明"两人团队不建议首版做"，
    /// 所以这里做成配置项而不是写死：默认关（守住"已获知的信息不该回收"），需要时改 config.json 即可。
    /// </summary>
    [JsonPropertyName("item.magnifier.fadeSeconds")]
    public int MagnifierRevealFadeSeconds { get; set; }

    /// <summary>声呐扫描半径：1 = 3×3（v0.6 新增道具）。</summary>
    [JsonPropertyName("item.sonar.radius")]
    public int SonarRadius { get; set; } = 1;

    /// <summary>时间冻结时长（秒）：需求里是 20 秒（v0.6 新增道具）。</summary>
    [JsonPropertyName("item.freeze.seconds")]
    public int FreezeSeconds { get; set; } = 20;

    /// <summary>放大镜在商店里的售价（金币，v1.4 商店）。</summary>
    [JsonPropertyName("shop.magnifier.price")]
    public int ShopMagnifierPrice { get; set; } = CoinWallet.MagnifierPrice;

    /// <summary>声呐扫描在商店里的售价（金币）。</summary>
    [JsonPropertyName("shop.sonar.price")]
    public int ShopSonarPrice { get; set; } = CoinWallet.SonarPrice;

    /// <summary>时间冻结在商店里的售价（金币）。</summary>
    [JsonPropertyName("shop.freeze.price")]
    public int ShopFreezePrice { get; set; } = CoinWallet.FreezePrice;

    /// <summary>某个道具的商店售价（跟随配置）。</summary>
    public int ShopPriceOf(string itemId) => itemId switch
    {
        ItemIds.Magnifier => ShopMagnifierPrice,
        ItemIds.Sonar => ShopSonarPrice,
        ItemIds.Freeze => ShopFreezePrice,
        _ => 0,
    };

    /// <summary>
    /// 默认配置。
    ///
    /// 显式把版本号写成当前值：这是"新装玩家第一次落盘的 config.json"。
    /// 如果这里留 0（属性默认值），第一份配置就会带着 schemaVersion=0 落地，
    /// 下次启动又要跑一遍迁移——迁移本身无害，但新玩家的文件不该长得像老文件。
    /// 反序列化旧文件时仍然是 0（那个键不存在就不会走这个工厂方法），迁移照旧执行。
    /// </summary>
    public static ItemConfig CreateDefault() => new() { SchemaVersion = CurrentSchemaVersion };

    /// <summary>把配置夹到合理范围，避免手改 JSON 写出会让游戏崩掉的数值。</summary>
    public void Normalize()
    {
        MagnifierThreshold = Math.Clamp(MagnifierThreshold, 1, 100_000);
        MagnifierMaxHold = Math.Clamp(MagnifierMaxHold, 1, 99);
        MagnifierInitialGift = Math.Clamp(MagnifierInitialGift, 0, MagnifierMaxHold);
        SonarInitialGift = Math.Clamp(SonarInitialGift, 0, MagnifierMaxHold);
        FreezeInitialGift = Math.Clamp(FreezeInitialGift, 0, MagnifierMaxHold);
        MagnifierWidth = Math.Clamp(MagnifierWidth, 1, 9);
        MagnifierHeight = Math.Clamp(MagnifierHeight, 1, 9);
        MagnifierRevealFadeSeconds = Math.Clamp(MagnifierRevealFadeSeconds, 0, 600);
        SonarRadius = Math.Clamp(SonarRadius, 0, 4);
        FreezeSeconds = Math.Clamp(FreezeSeconds, 1, 600);
        ShopMagnifierPrice = Math.Clamp(ShopMagnifierPrice, 1, 100_000);
        ShopSonarPrice = Math.Clamp(ShopSonarPrice, 1, 100_000);
        ShopFreezePrice = Math.Clamp(ShopFreezePrice, 1, 100_000);
    }
}
