using System.Text.Json.Serialization;

namespace MineSweeper.Core.Levels;

/// <summary>单个关卡的战绩（关卡制进度里的一条）。</summary>
public sealed class LevelRecord
{
    [JsonPropertyName("levelId")]
    public string LevelId { get; set; } = string.Empty;

    /// <summary>是否通关过（解锁下一关的唯一依据）。</summary>
    [JsonPropertyName("cleared")]
    public bool Cleared { get; set; }

    /// <summary>历史最好星数（0..3）。</summary>
    [JsonPropertyName("stars")]
    public int Stars { get; set; }

    /// <summary>通关最快用时（秒）；未通关为 0。</summary>
    [JsonPropertyName("bestSeconds")]
    public double BestSeconds { get; set; }

    /// <summary>尝试次数（含失败）。</summary>
    [JsonPropertyName("attempts")]
    public int Attempts { get; set; }
}

/// <summary>
/// 关卡制进度（levelprogress.json）。
///
/// 刻意与 progress.json / records.json 分开：关卡是独立玩法，
/// <b>既不影响账号级的道具进度，也不进最快记录表</b>，这样"还原原版"的那套东西保持干净。
/// 保护措施与其它存档一致：原子写盘、损坏备份重建、schemaVersion 过新拒读。
/// </summary>
public sealed class LevelProgress
{
    /// <summary>当前 schema 版本。改结构时 +1 并补迁移函数。</summary>
    public const int CurrentSchemaVersion = 1;

    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    [JsonPropertyName("records")]
    public List<LevelRecord> Records { get; set; } = new();

    /// <summary>
    /// 隐藏关是否已解锁。默认 false —— 关卡面板里连它的名字都不会出现。
    ///
    /// <b>刻意标了 JsonIgnore：这个状态不落盘。</b>
    /// 隐藏关是"触发一次、当次运行内出现，关掉游戏就消失"的彩蛋，
    /// 所以关掉再开又得重新去发现它（顺带存档里也不会留下任何痕迹）。
    /// </summary>
    [JsonIgnore]
    public bool SecretUnlocked { get; set; }

    /// <summary>默认（全新）进度。</summary>
    public static LevelProgress CreateDefault() => new();

    /// <summary>取某关战绩；从未玩过返回 null。</summary>
    public LevelRecord? Find(string? levelId)
    {
        if (string.IsNullOrEmpty(levelId))
        {
            return null;
        }

        foreach (LevelRecord record in Records)
        {
            if (string.Equals(record.LevelId, levelId, StringComparison.OrdinalIgnoreCase))
            {
                return record;
            }
        }

        return null;
    }

    /// <summary>取某关战绩，没有就建一条。</summary>
    public LevelRecord GetOrCreate(string levelId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(levelId);

        LevelRecord? existing = Find(levelId);
        if (existing is not null)
        {
            return existing;
        }

        var created = new LevelRecord { LevelId = levelId };
        Records.Add(created);
        return created;
    }

    /// <summary>某关是否已通关。</summary>
    public bool IsCleared(string? levelId) => Find(levelId)?.Cleared == true;

    /// <summary>某关的最好星数（0 表示没通关过）。</summary>
    public int StarsOf(string? levelId) => Find(levelId)?.Stars ?? 0;

    /// <summary>某关的最快用时；没有返回 null。</summary>
    public double? BestSecondsOf(string? levelId)
    {
        LevelRecord? record = Find(levelId);
        return record is null || record.BestSeconds <= 0 ? null : record.BestSeconds;
    }

    /// <summary>已通关的关卡数（由记录算出来，不进存档）。</summary>
    [JsonIgnore]
    public int ClearedCount
    {
        get
        {
            int count = 0;
            foreach (LevelDefinition level in LevelTable.All)
            {
                if (IsCleared(level.Id))
                {
                    count++;
                }
            }

            return count;
        }
    }

    /// <summary>已获得的星数合计（满星 = 关卡数 × 3；由记录算出来，不进存档）。</summary>
    [JsonIgnore]
    public int TotalStars
    {
        get
        {
            int total = 0;
            foreach (LevelDefinition level in LevelTable.All)
            {
                total += StarsOf(level.Id);
            }

            return total;
        }
    }

    /// <summary>
    /// 规范化：去掉空 Id、合并重复项（保留更好的成绩）、把数值夹到合法范围。
    /// 手改坏了文件顶多丢几条成绩，不会让整个进度作废。
    /// </summary>
    public void Normalize()
    {
        Records ??= new List<LevelRecord>();

        var merged = new Dictionary<string, LevelRecord>(StringComparer.OrdinalIgnoreCase);
        foreach (LevelRecord record in Records)
        {
            if (record is null || string.IsNullOrWhiteSpace(record.LevelId))
            {
                continue;
            }

            record.LevelId = record.LevelId.Trim();
            record.Stars = Math.Clamp(record.Stars, 0, 3);
            record.Attempts = Math.Max(0, record.Attempts);
            record.BestSeconds = double.IsFinite(record.BestSeconds) && record.BestSeconds > 0
                ? record.BestSeconds
                : 0;

            // 没通关就谈不上星数与成绩
            if (!record.Cleared)
            {
                record.Stars = 0;
                record.BestSeconds = 0;
            }

            if (!merged.TryGetValue(record.LevelId, out LevelRecord? kept))
            {
                merged[record.LevelId] = record;
                continue;
            }

            // 合并：取更好的星数/成绩，尝试次数相加
            kept.Cleared |= record.Cleared;
            kept.Stars = Math.Max(kept.Stars, record.Stars);
            kept.Attempts += record.Attempts;
            if (record.BestSeconds > 0 && (kept.BestSeconds <= 0 || record.BestSeconds < kept.BestSeconds))
            {
                kept.BestSeconds = record.BestSeconds;
            }
        }

        Records.Clear();
        foreach (LevelDefinition level in LevelTable.All)
        {
            if (merged.Remove(level.Id, out LevelRecord? known))
            {
                Records.Add(known);
            }
        }

        // 隐藏关与"未知关卡 Id"的成绩：不在 All 里，所以放在最后保留下来，
        // 这样隐藏关的最好成绩与星数不会因为规范化而丢掉。
        foreach (LevelRecord leftover in merged.Values)
        {
            Records.Add(leftover);
        }
    }

    /// <summary>低版本迁移（当前只有 v1，留好扩展点）。</summary>
    public static LevelProgress Migrate(LevelProgress loaded)
    {
        ArgumentNullException.ThrowIfNull(loaded);
        loaded.SchemaVersion = CurrentSchemaVersion;
        return loaded;
    }
}
