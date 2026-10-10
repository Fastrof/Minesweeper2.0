using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MineSweeper.Core.Levels;

namespace MineSweeper.Core.Persistence;

/// <summary>levels 进度的加载结局。</summary>
public enum LevelProgressLoadStatus
{
    /// <summary>正常读取。</summary>
    Loaded = 0,

    /// <summary>文件不存在，已按全新进度新建。</summary>
    CreatedNew = 1,

    /// <summary>JSON 损坏：原文件已备份，用全新进度重建。</summary>
    RecoveredFromCorrupt = 2,

    /// <summary>schemaVersion 高于本程序支持：拒绝读取（已备份，且不覆盖原文件）。</summary>
    RejectedVersionTooNew = 3,
}

/// <summary>关卡进度加载结果。</summary>
public sealed record LevelProgressLoadResult(
    LevelProgress Progress,
    LevelProgressLoadStatus Status,
    string? BackupPath,
    string? Message)
{
    /// <summary>是否属于"要提示玩家"的异常情况。</summary>
    public bool HasWarning => Status is LevelProgressLoadStatus.RecoveredFromCorrupt or LevelProgressLoadStatus.RejectedVersionTooNew;
}

/// <summary>
/// 关卡进度（levelprogress.json）的读写。
/// 行为与 <see cref="ProgressStore"/> / <see cref="AppSettingsStore"/> 一致：
/// 原子写盘、损坏备份重建、版本过新拒读且不覆盖。
/// </summary>
public sealed class LevelProgressStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public LevelProgressStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Path = path;
    }

    /// <summary>levelprogress.json 的完整路径。</summary>
    public string Path { get; }

    /// <summary>加载关卡进度；任何异常情况都降级为全新进度 + 返回可展示的提示。</summary>
    public LevelProgressLoadResult Load()
    {
        if (!File.Exists(Path))
        {
            LevelProgress created = LevelProgress.CreateDefault();
            Save(created);
            return new LevelProgressLoadResult(created, LevelProgressLoadStatus.CreatedNew, null, null);
        }

        string json;
        try
        {
            json = File.ReadAllText(Path, Encoding.UTF8);
        }
        catch (IOException ex)
        {
            return new LevelProgressLoadResult(
                LevelProgress.CreateDefault(),
                LevelProgressLoadStatus.RecoveredFromCorrupt,
                null,
                $"关卡进度无法读取：{ex.Message}");
        }

        LevelProgress? loaded;
        try
        {
            loaded = JsonSerializer.Deserialize<LevelProgress>(json, SerializerOptions);
        }
        catch (JsonException ex)
        {
            return RecoverFromCorrupt($"关卡进度不是合法 JSON：{ex.Message}");
        }

        if (loaded is null)
        {
            return RecoverFromCorrupt("关卡进度内容为空。");
        }

        if (loaded.SchemaVersion > LevelProgress.CurrentSchemaVersion)
        {
            string backup = MakeBackupPath("unsupported");
            TryCopy(Path, backup);

            return new LevelProgressLoadResult(
                LevelProgress.CreateDefault(),
                LevelProgressLoadStatus.RejectedVersionTooNew,
                backup,
                $"关卡进度来自更新版本（schemaVersion={loaded.SchemaVersion}），已备份到 {System.IO.Path.GetFileName(backup)}，本次以全新进度运行且不会覆盖它。");
        }

        LevelProgress migrated = LevelProgress.Migrate(loaded);
        migrated.Normalize();
        return new LevelProgressLoadResult(migrated, LevelProgressLoadStatus.Loaded, null, null);
    }

    /// <summary>
    /// 原子写盘。
    ///
    /// 隐藏关的成绩会被剔除：它是"当次运行内有效"的彩蛋关卡，
    /// 每次触发的盘面都不一样（雷数随机），跨会话比较用时没有意义；
    /// 而且不落盘也意味着存档里不会留下任何"这里有个隐藏关"的痕迹。
    /// </summary>
    public void Save(LevelProgress progress)
    {
        ArgumentNullException.ThrowIfNull(progress);

        progress.Normalize();

        var persisted = new LevelProgress { SchemaVersion = progress.SchemaVersion };
        foreach (LevelRecord record in progress.Records)
        {
            if (!string.Equals(record.LevelId, LevelTable.SecretId, StringComparison.OrdinalIgnoreCase))
            {
                persisted.Records.Add(record);
            }
        }

        AtomicFile.WriteAllText(Path, JsonSerializer.Serialize(persisted, SerializerOptions));
    }

    private LevelProgressLoadResult RecoverFromCorrupt(string message)
    {
        string backup = MakeBackupPath("corrupt");
        TryCopy(Path, backup);

        LevelProgress fresh = LevelProgress.CreateDefault();
        Save(fresh);

        return new LevelProgressLoadResult(
            fresh,
            LevelProgressLoadStatus.RecoveredFromCorrupt,
            backup,
            $"{message} 原文件已备份到 {System.IO.Path.GetFileName(backup)}，关卡进度已重置。");
    }

    private string MakeBackupPath(string kind)
    {
        string directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(Path)) ?? ".";
        string stamp = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss");
        string name = System.IO.Path.GetFileNameWithoutExtension(Path);
        return System.IO.Path.Combine(directory, $"{name}.{kind}.{stamp}.json");
    }

    private static void TryCopy(string source, string destination)
    {
        try
        {
            File.Copy(source, destination, overwrite: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
