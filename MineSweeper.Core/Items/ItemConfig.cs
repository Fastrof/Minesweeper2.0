using System.Text.Json.Serialization;

namespace MineSweeper.Core.Items;

/// <summary>
/// 道具配置（§5.7）。刻意不写死在代码里，JSON 键名与文档里的配置键一一对应，
/// 这样调手感时只需要改 config.json 而不用重新编译。
/// </summary>
public sealed class ItemConfig
{
    /// <summary>奖励阈值：每累计这么多个"正确标记"（= 扫到一颗雷）给一次"三选一"奖励（§5.2）。</summary>
    [JsonPropertyName("item.magnifier.threshold")]
    public int MagnifierThreshold { get; set; } = 20;

    /// <summary>持有上限：物品栏三格（§5.2）。</summary>
    [JsonPropertyName("item.magnifier.maxHold")]
    public int MagnifierMaxHold { get; set; } = 3;

    /// <summary>首次进入游戏赠送数量（§5.2；v0.6 起三种道具各送 2 个）。</summary>
    [JsonPropertyName("item.magnifier.initialGift")]
    public int MagnifierInitialGift { get; set; } = 2;

    /// <summary>声呐扫描的首次赠送数量（v0.6 起默认 2）。</summary>
    [JsonPropertyName("item.sonar.initialGift")]
    public int SonarInitialGift { get; set; } = 2;

    /// <summary>时间冻结的首次赠送数量（v0.6 起默认 2）。</summary>
    [JsonPropertyName("item.freeze.initialGift")]
    public int FreezeInitialGift { get; set; } = 2;

    /// <summary>透视半径：1 = 3×3。抽出来是为了以后做 5×5 的"大放大镜"不用改代码。</summary>
    [JsonPropertyName("item.magnifier.radius")]
    public int MagnifierRadius { get; set; } = 1;

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

    /// <summary>默认配置。</summary>
    public static ItemConfig CreateDefault() => new();

    /// <summary>把配置夹到合理范围，避免手改 JSON 写出会让游戏崩掉的数值。</summary>
    public void Normalize()
    {
        MagnifierThreshold = Math.Clamp(MagnifierThreshold, 1, 100_000);
        MagnifierMaxHold = Math.Clamp(MagnifierMaxHold, 1, 3);
        MagnifierInitialGift = Math.Clamp(MagnifierInitialGift, 0, MagnifierMaxHold);
        SonarInitialGift = Math.Clamp(SonarInitialGift, 0, MagnifierMaxHold);
        FreezeInitialGift = Math.Clamp(FreezeInitialGift, 0, MagnifierMaxHold);
        MagnifierRadius = Math.Clamp(MagnifierRadius, 0, 4);
        MagnifierRevealFadeSeconds = Math.Clamp(MagnifierRevealFadeSeconds, 0, 600);
        SonarRadius = Math.Clamp(SonarRadius, 0, 4);
        FreezeSeconds = Math.Clamp(FreezeSeconds, 1, 600);
    }
}
