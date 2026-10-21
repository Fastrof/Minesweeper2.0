using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MineSweeper.Core.Persistence;

/// <summary>某个难度/盘面的最快记录。</summary>
public sealed record BestTimeEntry(
    [property: JsonPropertyName("seconds")] double Seconds,
    [property: JsonPropertyName("width")] int Width,
    [property: JsonPropertyName("height")] int Height,
    [property: JsonPropertyName("mineCount")] int MineCount,
    [property: JsonPropertyName("itemUsed")] bool ItemUsed,
    [property: JsonPropertyName("achievedAtUtc")] string AchievedAtUtc);

/// <summary>自定义盘面的一局历史（§6.2）。</summary>
public sealed record CustomRecord(
    [property: JsonPropertyName("width")] int Width,
    [property: JsonPropertyName("height")] int Height,
    [property: JsonPropertyName("mineCount")] int MineCount,
    [property: JsonPropertyName("seconds")] double Seconds,
    [property: JsonPropertyName("itemUsed")] bool ItemUsed,
    [property: JsonPropertyName("won")] bool Won,
    [property: JsonPropertyName("seed")] int Seed,
    [property: JsonPropertyName("achievedAtUtc")] string AchievedAtUtc);

/// <summary>
/// 记录文件（§6.2 分表）：
/// classic（纯手工，无道具）/ classicWithItem（经典盘面 + 道具）/ custom（自定义盘面历史）。
/// 带道具的成绩必须与纯手工成绩分表，否则"使用道具换来的时间"会污染排行榜。
/// </summary>
public sealed class GameRecords
{
    /// <summary>当前 schema 版本。</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>自定义历史最多保留多少条（防止无限增长）。</summary>
    public const int MaxCustomRecords = 200;

    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    /// <summary>纯手工经典最快记录：key 为难度 Id（beginner/intermediate/expert）。</summary>
    [JsonPropertyName("classic")]
    public Dictionary<string, BestTimeEntry> Classic { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>经典盘面 + 道具的最快记录（与上面分开，§5.5 ⑥）。</summary>
    [JsonPropertyName("classicWithItem")]
    public Dictionary<string, BestTimeEntry> ClassicWithItem { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>自定义盘面历史（最新的在最前）。</summary>
    [JsonPropertyName("custom")]
    public List<CustomRecord> Custom { get; set; } = new();

    /// <summary>默认空记录。</summary>
    public static GameRecords CreateDefault() => new();
}

/// <summary>提交记录的结果。</summary>
public readonly record struct RecordSubmitResult(bool IsNewRecord, string Table, double? PreviousSeconds, string? Reason)
{
    /// <summary>没进记录时的说明。</summary>
    public static RecordSubmitResult NotSubmitted(string reason) => new(false, "none", null, reason);
}

/// <summary>records.json 的读写（§6.1 / §6.2）。同样带 schemaVersion 与损坏降级。</summary>
public sealed class RecordsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    public RecordsStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Path = path;
    }

    /// <summary>records.json 的完整路径。</summary>
    public string Path { get; }

    /// <summary>
    /// 本次 Load 是否因"版本过新"拒读过这个文件（此时 <see cref="Save"/> 被禁用）。
    ///
    /// records.json 没有 LoadStatus 返回值，"版本过新"以前被当作损坏处理（备份 + 空记录）。
    /// 备份保留没错，但还必须同时禁写：App 拿着空记录继续打，关窗时 Save 会把更新版本
    /// 写的 records.json 覆盖掉，那才是真的把玩家的最快纪录全丢了。
    /// </summary>
    public bool SaveBlocked => _versionTooNew;

    // Load 在检测到 schemaVersion 过新时置位；下一个 Load 重新判定
    private bool _versionTooNew;

    /// <summary>加载；文件不存在或损坏时返回空记录（并备份损坏文件）。</summary>
    public GameRecords Load()
    {
        // 重新加载以本次读到的结果为准（文件可能已经被换回旧版本）
        _versionTooNew = false;

        if (!File.Exists(Path))
        {
            return GameRecords.CreateDefault();
        }

        try
        {
            string json = File.ReadAllText(Path, Encoding.UTF8);
            GameRecords? loaded = JsonSerializer.Deserialize<GameRecords>(json, SerializerOptions);

            if (loaded is null)
            {
                BackupCorrupt();
                return GameRecords.CreateDefault();
            }

            if (loaded.SchemaVersion > GameRecords.CurrentSchemaVersion)
            {
                // 过新也按损坏备份一份，但额外记住"拒读过"：这次进程内不再允许写盘
                _versionTooNew = true;
                BackupCorrupt();
                return GameRecords.CreateDefault();
            }

            // 反序列化后字典可能换成默认比较器，这里补回来，保证 key 大小写不敏感
            loaded.Classic = new Dictionary<string, BestTimeEntry>(loaded.Classic ?? new(), StringComparer.OrdinalIgnoreCase);
            loaded.ClassicWithItem = new Dictionary<string, BestTimeEntry>(loaded.ClassicWithItem ?? new(), StringComparer.OrdinalIgnoreCase);
            loaded.Custom ??= new List<CustomRecord>();
            loaded.SchemaVersion = GameRecords.CurrentSchemaVersion;

            return loaded;
        }
        catch (JsonException)
        {
            BackupCorrupt();
            return GameRecords.CreateDefault();
        }
        catch (IOException)
        {
            return GameRecords.CreateDefault();
        }
    }

    /// <summary>
    /// 原子写盘。记录文件被写坏一次就等于所有最快纪录作废，
    /// 所以这里和 progress.json 走同一套原子替换实现（<see cref="AtomicFile"/>），
    /// 不再用"直接覆盖目标文件"的写法。
    /// </summary>
    public void Save(GameRecords records)
    {
        ArgumentNullException.ThrowIfNull(records);

        // 版本过新被拒读时禁止写盘：内存里的空记录只是本次运行的临时态，
        // 写回磁盘会把更新版本的 records.json 直接抹掉（见 SaveBlocked）
        if (_versionTooNew)
        {
            return;
        }

        AtomicFile.WriteAllText(Path, JsonSerializer.Serialize(records, SerializerOptions));
    }

    /// <summary>
    /// 把一局的结果提交进记录表。只有胜利才进表；用道具的局进带道具表（§5.5 ⑥）。
    /// </summary>
    public RecordSubmitResult Submit(GameSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (session.Status != GameStatus.Won)
        {
            return RecordSubmitResult.NotSubmitted("只有胜利的对局才计入记录。");
        }

        double seconds = session.ElapsedSeconds;
        string now = DateTimeOffset.UtcNow.ToString("O");

        if (session.Settings.Mode == GameMode.Custom)
        {
            var record = new CustomRecord(
                session.Board.Width,
                session.Board.Height,
                session.Board.MineCount,
                seconds,
                session.ItemUsed,
                true,
                session.Seed,
                now);

            Records.Custom.Insert(0, record);
            if (Records.Custom.Count > GameRecords.MaxCustomRecords)
            {
                Records.Custom.RemoveRange(GameRecords.MaxCustomRecords, Records.Custom.Count - GameRecords.MaxCustomRecords);
            }

            return new RecordSubmitResult(true, "custom", null, null);
        }

        bool withItem = session.Settings.Mode == GameMode.ClassicWithItems || session.ItemUsed;
        Dictionary<string, BestTimeEntry> table = withItem ? Records.ClassicWithItem : Records.Classic;
        string tableName = withItem ? "classicWithItem" : "classic";
        string key = session.DifficultyId ?? $"{session.Board.Width}x{session.Board.Height}-{session.Board.MineCount}";

        if (table.TryGetValue(key, out BestTimeEntry? previous) && previous.Seconds <= seconds)
        {
            return new RecordSubmitResult(false, tableName, previous.Seconds, "没有超过既有最快记录。");
        }

        table[key] = new BestTimeEntry(
            seconds,
            session.Board.Width,
            session.Board.Height,
            session.Board.MineCount,
            session.ItemUsed,
            now);

        return new RecordSubmitResult(true, tableName, previous?.Seconds, null);
    }

    /// <summary>当前记录（调用前先 <see cref="Load"/>）。</summary>
    public GameRecords Records { get; private set; } = GameRecords.CreateDefault();

    /// <summary>加载并替换当前记录对象。</summary>
    public GameRecords Reload()
    {
        Records = Load();
        return Records;
    }

    private void BackupCorrupt()
    {
        try
        {
            string directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(Path)) ?? ".";
            string stamp = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss");
            string name = System.IO.Path.GetFileNameWithoutExtension(Path);
            File.Copy(Path, System.IO.Path.Combine(directory, $"{name}.corrupt.{stamp}.json"), overwrite: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
