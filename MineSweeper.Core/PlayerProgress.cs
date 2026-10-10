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
    /// 攒着还没领的"三选一"奖励次数（v0.6 新增）。
    /// 纯新增字段且有默认值 0，所以老存档直接读得进来，不需要 +schemaVersion 迁移。
    /// </summary>
    [JsonPropertyName("pendingRewards")]
    public int PendingRewards { get; set; }

    /// <summary>是否已经发过"首次进入赠送"的那一个（只送一次）。</summary>
    [JsonPropertyName("initialGiftGranted")]
    public bool InitialGiftGranted { get; set; }

    /// <summary>默认进度。</summary>
    public static PlayerProgress CreateDefault() => new();
}
