using System.Text.Json.Serialization;

namespace MineSweeper.Core.Achievements;

/// <summary>
/// 单条成就的玩家进度（achievements.json 里的一条）。
///
/// 只存"事实"（做到了哪一步、什么时候解锁），不存标题/点数这类定义数据——
/// 定义随时可以改文案、调门槛，而玩家的存档不受影响。
/// </summary>
public sealed class AchievementRecord
{
    /// <summary>成就 Id（对应 <see cref="AchievementDefinition.Id"/>，大小写不敏感地匹配）。</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    /// <summary>是否已解锁。解锁由 <see cref="AchievementRules.Evaluate"/> 单向推进，不会退回。</summary>
    [JsonPropertyName("unlocked")]
    public bool Unlocked { get; set; }

    /// <summary>解锁时刻（UTC）；未解锁为 null。</summary>
    [JsonPropertyName("unlockedAtUtc")]
    public DateTimeOffset? UnlockedAtUtc { get; set; }

    /// <summary>当前进度值（进度条用）：目标为 1 的成就只会是 0/1。</summary>
    [JsonPropertyName("progress")]
    public long Progress { get; set; }
}

/// <summary>
/// 成就进度（achievements.json）。
///
/// 与 progress.json / levelprogress.json 一样是**独立存档**：文件丢了只丢成就，
/// 不影响胜负记录与关卡进度。保护措施也一致：原子写盘、损坏备份重建、schemaVersion 过新拒读。
///
/// 未知 Id 会被保留（见 <see cref="Normalize"/>）：这样降级运行读到新版本写的成就时，
/// 只是"当前版本不认识它"，而不是把玩家在新版本里的进度删掉。
/// </summary>
public sealed class AchievementProgress
{
    /// <summary>当前 schema 版本。改结构时 +1 并补迁移函数。</summary>
    public const int CurrentSchemaVersion = 1;

    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    [JsonPropertyName("records")]
    public List<AchievementRecord> Records { get; set; } = new();

    /// <summary>默认（全新）进度。</summary>
    public static AchievementProgress CreateDefault() => new();

    /// <summary>取某条成就记录；没有返回 null。null/空 Id 一律返回 null。</summary>
    public AchievementRecord? Find(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        string trimmed = id.Trim();
        foreach (AchievementRecord record in Records)
        {
            if (record is not null && string.Equals(record.Id, trimmed, StringComparison.OrdinalIgnoreCase))
            {
                return record;
            }
        }

        return null;
    }

    /// <summary>某条成就是否已解锁。</summary>
    public bool IsUnlocked(string? id) => Find(id)?.Unlocked == true;

    /// <summary>某条成就的当前进度值；没有记录返回 0。</summary>
    public long ProgressOf(string? id) => Find(id)?.Progress ?? 0;

    /// <summary>
    /// 取某条成就记录，没有就建一条。
    ///
    /// <b>空 Id 不抛异常</b>：返回一条"游离"的临时记录（不会进 <see cref="Records"/>）。
    /// 调用方（<see cref="AchievementRules.Evaluate"/>）只会传成就表里的 Id，
    /// 所以这是给"外部误传"兜底，宁可写不进去也不要让存档读写路径抛异常。
    /// 未知但非空的 Id 会照常建记录，和 <see cref="Normalize"/> 的"未知 Id 保留"一致。
    /// </summary>
    public AchievementRecord GetOrCreate(string id)
    {
        AchievementRecord? existing = Find(id);
        if (existing is not null)
        {
            return existing;
        }

        if (string.IsNullOrWhiteSpace(id))
        {
            return new AchievementRecord();
        }

        var created = new AchievementRecord { Id = id.Trim() };
        Records.Add(created);
        return created;
    }

    /// <summary>
    /// 已解锁成就数（只统计成就表里存在的 Id；由记录算出来，不进存档）。
    /// 用集合去重：手改过的存档可能留下重复 Id，计数不能因此虚高。
    /// </summary>
    [JsonIgnore]
    public int UnlockedCount
    {
        get
        {
            int count = 0;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (AchievementRecord record in Records)
            {
                if (record is null || !record.Unlocked || string.IsNullOrWhiteSpace(record.Id))
                {
                    continue;
                }

                if (AchievementCatalog.Find(record.Id) is null)
                {
                    continue;
                }

                if (seen.Add(record.Id))
                {
                    count++;
                }
            }

            return count;
        }
    }

    /// <summary>已解锁成就的点数之和（由 <see cref="Records"/> 与成就表算出，不进存档）。</summary>
    [JsonIgnore]
    public int TotalPoints
    {
        get
        {
            int total = 0;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (AchievementRecord record in Records)
            {
                if (record is null || !record.Unlocked || string.IsNullOrWhiteSpace(record.Id))
                {
                    continue;
                }

                AchievementDefinition? definition = AchievementCatalog.Find(record.Id);
                if (definition is null || !seen.Add(record.Id))
                {
                    continue;
                }

                total += definition.Points;
            }

            return total;
        }
    }

    /// <summary>
    /// 规范化：丢掉空 Id、合并重复 Id（取"已解锁/较大进度/最早解锁时间"）、把负进度归零、
    /// 按成就表顺序重排。手改坏了文件顶多丢几条进度，不会让整个成就系统崩掉。
    ///
    /// 未知 Id（不在成就表里）<b>保留在最后</b>：它可能来自更新版本，
    /// 现在不认识就先原样存着，等玩家升回去时进度还在。
    /// </summary>
    public void Normalize()
    {
        Records ??= new List<AchievementRecord>();

        var merged = new Dictionary<string, AchievementRecord>(StringComparer.OrdinalIgnoreCase);
        foreach (AchievementRecord record in Records)
        {
            if (record is null || string.IsNullOrWhiteSpace(record.Id))
            {
                continue;
            }

            record.Id = record.Id.Trim();
            record.Progress = Math.Max(0L, record.Progress);

            // 没解锁就不该有解锁时间（手改/半途写入都可能留下这种组合）
            if (!record.Unlocked)
            {
                record.UnlockedAtUtc = null;
            }

            if (!merged.TryGetValue(record.Id, out AchievementRecord? kept))
            {
                merged[record.Id] = record;
                continue;
            }

            // 合并：解锁状态取或、进度取大、解锁时间取最早（第一次解锁的时刻才有意义）
            kept.Unlocked |= record.Unlocked;
            kept.Progress = Math.Max(kept.Progress, record.Progress);
            kept.UnlockedAtUtc = Earliest(kept.UnlockedAtUtc, record.UnlockedAtUtc);

            if (!kept.Unlocked)
            {
                kept.UnlockedAtUtc = null;
            }
        }

        Records.Clear();
        foreach (AchievementDefinition definition in AchievementCatalog.All)
        {
            if (merged.Remove(definition.Id, out AchievementRecord? known))
            {
                // 统一成成就表里的大小写，避免 "win1" / "Win1" 两条并存
                known.Id = definition.Id;
                Records.Add(known);
            }
        }

        foreach (AchievementRecord leftover in merged.Values)
        {
            Records.Add(leftover);
        }
    }

    /// <summary>低版本迁移（当前只有 v1，留好扩展点）。</summary>
    public static AchievementProgress Migrate(AchievementProgress loaded)
    {
        ArgumentNullException.ThrowIfNull(loaded);
        loaded.SchemaVersion = CurrentSchemaVersion;
        return loaded;
    }

    private static DateTimeOffset? Earliest(DateTimeOffset? left, DateTimeOffset? right)
    {
        if (left is null)
        {
            return right;
        }

        if (right is null)
        {
            return left;
        }

        return left <= right ? left : right;
    }
}
