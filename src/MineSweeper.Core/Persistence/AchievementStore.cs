using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MineSweeper.Core.Achievements;

namespace MineSweeper.Core.Persistence;

/// <summary>achievements.json 的加载结局（与 progress / levelprogress 一致）。</summary>
public enum AchievementLoadStatus
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

/// <summary>成就进度加载结果。</summary>
public sealed record AchievementLoadResult(
    AchievementProgress Progress,
    AchievementLoadStatus Status,
    string? BackupPath,
    string? Message)
{
    /// <summary>是否属于"要提示玩家"的异常情况。</summary>
    public bool HasWarning => Status is AchievementLoadStatus.RecoveredFromCorrupt or AchievementLoadStatus.RejectedVersionTooNew;
}

/// <summary>
/// 成就进度（achievements.json）的读写。
///
/// 行为与 <see cref="ProgressStore"/> / <see cref="LevelProgressStore"/> 完全一致，
/// 三条硬性要求都照搬（这是玩家"几十小时才攒出来"的东西，不能因为写盘时机不对就报废）：
/// 1. 原子写盘（<see cref="AtomicFile"/>：先写 .tmp 再替换，替换前原文件始终完整）
/// 2. 损坏 → 备份成 achievements.corrupt.&lt;时间戳&gt;.json 再用全新进度重建，不静默丢弃
/// 3. schemaVersion 高于当前 → 备份成 achievements.unsupported.&lt;时间戳&gt;.json 并<b>拒绝读取</b>，
/// 而且<b>绝不覆盖原文件</b>（用户可能只是临时降级运行）
/// </summary>
public sealed class AchievementStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public AchievementStore(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        Path = filePath;
    }

    /// <summary>achievements.json 的完整路径。</summary>
    public string Path { get; }

    /// <summary>
    /// 本次 Load 是否因"版本过新"拒读过这个文件（此时 <see cref="Save"/> 被禁用）。
    ///
    /// 只拒读不禁写是不够的：App 用全新进度继续跑，退出时 Save 会把更新版本写的文件覆盖掉。
    /// </summary>
    public bool SaveBlocked => _versionTooNew;

    // Load 在 RejectedVersionTooNew 分支置位；下一个 Load 重新判定
    private bool _versionTooNew;

    /// <summary>加载成就进度；任何异常情况都降级为全新进度 + 返回可展示的提示。</summary>
    public AchievementLoadResult Load()
    {
        // 重新加载以本次读到的结果为准（文件可能已经被换回旧版本）
        _versionTooNew = false;

        if (!File.Exists(Path))
        {
            AchievementProgress created = AchievementProgress.CreateDefault();
            Save(created);
            return new AchievementLoadResult(created, AchievementLoadStatus.CreatedNew, null, null);
        }

        string json;
        try
        {
            json = File.ReadAllText(Path, Encoding.UTF8);
        }
        catch (IOException ex)
        {
            return new AchievementLoadResult(
                AchievementProgress.CreateDefault(),
                AchievementLoadStatus.RecoveredFromCorrupt,
                null,
                $"成就进度无法读取：{ex.Message}");
        }

        AchievementProgress? loaded;
        try
        {
            loaded = JsonSerializer.Deserialize<AchievementProgress>(json, SerializerOptions);
        }
        catch (JsonException ex)
        {
            return RecoverFromCorrupt($"成就进度不是合法 JSON：{ex.Message}");
        }

        if (loaded is null)
        {
            return RecoverFromCorrupt("成就进度内容为空。");
        }

        if (loaded.SchemaVersion > AchievementProgress.CurrentSchemaVersion)
        {
            string backup = MakeBackupPath("unsupported");
            TryCopy(Path, backup);

            // 拒读之后还要禁写：否则关窗时的 Save 会把新版文件覆盖掉
            _versionTooNew = true;

            // 刻意不覆盖原文件：这是更新版本写的存档，降级运行不该把它毁掉
            return new AchievementLoadResult(
                AchievementProgress.CreateDefault(),
                AchievementLoadStatus.RejectedVersionTooNew,
                backup,
                $"成就进度来自更新版本（schemaVersion={loaded.SchemaVersion}），已备份到 {System.IO.Path.GetFileName(backup)}，本次以全新进度运行且不会覆盖它。");
        }

        AchievementProgress migrated = AchievementProgress.Migrate(loaded);
        migrated.Normalize();
        return new AchievementLoadResult(migrated, AchievementLoadStatus.Loaded, null, null);
    }

    /// <summary>
    /// 原子写盘。写之前先规范化，免得把重复 Id / 负进度这类手改垃圾一直带着往下传。
    /// </summary>
    public void Save(AchievementProgress progress)
    {
        ArgumentNullException.ThrowIfNull(progress);

        // 版本过新被拒读时禁止写盘：内存里的全新进度只是本次运行的临时态，
        // 写回磁盘会把更新版本的存档直接抹掉（见 SaveBlocked）
        if (_versionTooNew)
        {
            return;
        }

        progress.Normalize();

        AtomicFile.WriteAllText(Path, JsonSerializer.Serialize(progress, SerializerOptions));
    }

    private AchievementLoadResult RecoverFromCorrupt(string message)
    {
        string backup = MakeBackupPath("corrupt");
        TryCopy(Path, backup);

        AchievementProgress fresh = AchievementProgress.CreateDefault();
        Save(fresh);

        return new AchievementLoadResult(
            fresh,
            AchievementLoadStatus.RecoveredFromCorrupt,
            backup,
            $"{message} 原文件已备份到 {System.IO.Path.GetFileName(backup)}，成就进度已重置。");
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
            // 备份失败不应该阻断游戏启动
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
