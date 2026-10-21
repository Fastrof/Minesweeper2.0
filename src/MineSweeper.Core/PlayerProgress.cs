using System.Text.Json.Serialization;

namespace MineSweeper.Core;

/// <summary>
/// 跨局进度（§5.7 / §6.1）：正确标记计数、道具持有量。
/// 字段名用文档里的驼峰键名，方便直接对照 progress.json 排查问题。
/// </summary>
public sealed class PlayerProgress
{
    /// <summary>当前 schema 版本。改结构时 +1 并补迁移函数（§6.1）。</summary>
    public const int CurrentSchemaVersion = 1;

    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    /// <summary>账号级累计的"正确标记"数（跨局不清零，§5.2）。</summary>
    [JsonPropertyName("correctMineFlagsTotal")]
    public int CorrectMineFlagsTotal { get; set; }

    /// <summary>当前持有的放大镜数量。</summary>
    [JsonPropertyName("magnifierCount")]
    public int MagnifierCount { get; set; }

    /// <summary>当前持有的声呐扫描数量（v0.6 新增）。</summary>
    [JsonPropertyName("sonarCount")]
    public int SonarCount { get; set; }

    /// <summary>当前持有的时间冻结数量（v0.6 新增）。</summary>
    [JsonPropertyName("freezeCount")]
    public int FreezeCount { get; set; }

    /// <summary>历史上发放过的奖励次数（含首赠之外的每一次"三选一"，战绩展示用）。</summary>
    [JsonPropertyName("magnifierGranted")]
    public int MagnifierGranted { get; set; }

    /// <summary>
    /// 【已废弃】攒着还没领的"三选一"奖励次数。
    ///
    /// v1.4 起奖励机制被金币商店取代，这个字段不再被读取。
    /// **刻意保留**而不是删掉：老存档里还写着这个键，删掉字段会让它变成未知键，
    /// 虽然能忽略、但会让"读进来再写回去"悄悄丢数据；留着并只做兼容是最稳的。
    /// </summary>
    [JsonPropertyName("pendingRewards")]
    public int PendingRewards { get; set; }

    /// <summary>是否已经发过"首次进入赠送"的那一个（只送一次）。</summary>
    [JsonPropertyName("initialGiftGranted")]
    public bool InitialGiftGranted { get; set; }

    /// <summary>默认进度。</summary>
    public static PlayerProgress CreateDefault() => new();
}
