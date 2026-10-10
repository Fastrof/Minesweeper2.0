using System.Text.Json.Serialization;

namespace MineSweeper.Core.History;

/// <summary>把"刚打完的这一局"并进历史时用的摘要。</summary>
public readonly record struct GameSummary(
    DateTimeOffset EndedAtUtc,
    double Seconds,
    int MinesSwept,
    int CellsRevealed,
    bool Won,
    bool LevelCleared);

/// <summary>
/// 历史记录（history.json）：从玩家**第一次真正开始扫雷**（第一次翻开格子）记起，
/// 累计扫了多少颗雷、总共玩了多久、打了多少局等。
///
/// <b>它只活在这一个 json 里</b>：文件丢了/删了，历史就从头开始 —— 这正是需求要的行为。
/// 另外它是**纯新增**存档，与 progress/records/settings/levelprogress 各自独立，
/// 任何一个丢了都只影响自己。字段都是加/减都不会崩的简单类型，所以没做 schema 迁移。
/// </summary>
public sealed class PlayHistory
{
    /// <summary>当前 schema 版本。</summary>
    public const int CurrentSchemaVersion = 1;

    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    /// <summary>第一次开始扫雷的时刻（第一次翻开格子）。null = 还没有任何记录。</summary>
    [JsonPropertyName("firstSweptAtUtc")]
    public DateTimeOffset? FirstSweptAtUtc { get; set; }

    /// <summary>最近一次玩的时间。</summary>
    [JsonPropertyName("lastPlayedAtUtc")]
    public DateTimeOffset? LastPlayedAtUtc { get; set; }

    /// <summary>累计扫到的雷数（正确插旗，含普通模式与关卡）。</summary>
    [JsonPropertyName("totalMinesSwept")]
    public long TotalMinesSwept { get; set; }

    /// <summary>累计用时（秒）。只统计真正在玩的时间：冻结道具暂停的那段不算。</summary>
    [JsonPropertyName("totalPlayedSeconds")]
    public double TotalPlayedSeconds { get; set; }

    /// <summary>累计对局数（每次"第一次翻开"算一局）。</summary>
    [JsonPropertyName("totalGames")]
    public int TotalGames { get; set; }

    /// <summary>累计胜利局数。</summary>
    [JsonPropertyName("totalWins")]
    public int TotalWins { get; set; }

    /// <summary>累计翻开的格数。</summary>
    [JsonPropertyName("totalCellsRevealed")]
    public long TotalCellsRevealed { get; set; }

    /// <summary>累计通关的关卡数。</summary>
    [JsonPropertyName("totalLevelsCleared")]
    public int TotalLevelsCleared { get; set; }

    /// <summary>是否已经有记录（用来区分"新玩家"与"玩过但都是 0"）。</summary>
    [JsonIgnore]
    public bool HasAnyRecord => FirstSweptAtUtc is not null || TotalGames > 0;

    public static PlayHistory CreateDefault() => new();

    /// <summary>
    /// 一局开始了：记下"第一次开始扫雷"的时刻（只认第一次），并累计对局数。
    /// </summary>
    public void BeginGame(DateTimeOffset startedAtUtc)
    {
        FirstSweptAtUtc ??= startedAtUtc;
        TotalGames++;
    }

    /// <summary>把一局的成果并进历史。</summary>
    public void RecordGame(in GameSummary summary)
    {
        LastPlayedAtUtc = summary.EndedAtUtc;
        TotalPlayedSeconds += Math.Max(0d, summary.Seconds);
        TotalMinesSwept += Math.Max(0, summary.MinesSwept);
        TotalCellsRevealed += Math.Max(0, summary.CellsRevealed);

        if (summary.Won)
        {
            TotalWins++;
        }

        if (summary.LevelCleared)
        {
            TotalLevelsCleared++;
        }
    }

    /// <summary>把不合理的手改数值夹回可显示范围（负数字段一律归零）。</summary>
    public void Normalize()
    {
        TotalMinesSwept = Math.Max(0, TotalMinesSwept);
        TotalCellsRevealed = Math.Max(0, TotalCellsRevealed);
        TotalGames = Math.Max(0, TotalGames);
        TotalWins = Math.Clamp(TotalWins, 0, TotalGames);
        TotalLevelsCleared = Math.Max(0, TotalLevelsCleared);
        TotalPlayedSeconds = double.IsFinite(TotalPlayedSeconds) ? Math.Max(0d, TotalPlayedSeconds) : 0d;
    }

    /// <summary>把秒数写成"1 小时 05 分 07 秒"这种人话（历史弹窗用）。</summary>
    public static string FormatDuration(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds <= 0)
        {
            return "0 秒";
        }

        long total = (long)Math.Floor(seconds);
        long hours = total / 3600;
        long minutes = (total % 3600) / 60;
        long secs = total % 60;

        if (hours > 0)
        {
            return $"{hours} 小时 {minutes:D2} 分 {secs:D2} 秒";
        }

        return minutes > 0 ? $"{minutes} 分 {secs:D2} 秒" : $"{secs} 秒";
    }
}