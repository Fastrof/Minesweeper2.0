using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MineSweeper.Core.Persistence;

/// <summary>progress.json 的加载结局（§6.1）。</summary>
public enum ProgressLoadStatus
{
    /// <summary>正常读取。</summary>
    Loaded = 0,

    /// <summary>文件不存在，已按默认值新建。</summary>
    CreatedNew = 1,

    /// <summary>JSON 损坏：原文件已备份，用默认值重建。</summary>
    RecoveredFromCorrupt = 2,

    /// <summary>schemaVersion 高于本程序支持：拒绝读取（原文件已备份、且不会被覆盖）。</summary>
    RejectedVersionTooNew = 3,
}

/// <summary>加载结果。</summary>
public sealed record ProgressLoadResult(
    PlayerProgress Progress,
    ProgressLoadStatus Status,
    string? BackupPath,
    string? Message)
{
    /// <summary>是否属于"要提示玩家"的异常情况。</summary>
    public bool HasWarning => Status is ProgressLoadStatus.RecoveredFromCorrupt or ProgressLoadStatus.RejectedVersionTooNew;
}

/// <summary>
/// 跨局进度的读写（§6.1）。
///
/// 三条硬性要求，都体现在实现里：
/// 1. 写盘是原子的（先写 .tmp 再替换），避免断电/杀进程写坏存档
/// 2. JSON 损坏 → 备份成 progress.corrupt.&lt;时间戳&gt;.json 再用默认值重建，不静默丢弃
/// 3. schemaVersion 高于当前 → 拒绝读取并备份，且<b>不覆盖</b>原文件（用户可能是降级安装）
/// </summary>
public sealed class ProgressStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public ProgressStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Path = path;
    }

    /// <summary>progress.json 的完整路径。</summary>
    public string Path { get; }

    /// <summary>加载进度；任何异常情况都会降级为默认值 + 返回可展示的提示。</summary>
    public ProgressLoadResult Load()
    {
        if (!File.Exists(Path))
        {
            PlayerProgress created = PlayerProgress.CreateDefault();
            Save(created);
            return new ProgressLoadResult(created, ProgressLoadStatus.CreatedNew, null, null);
        }

        string json;
        try
        {
            json = File.ReadAllText(Path, Encoding.UTF8);
        }
        catch (IOException ex)
        {
            return new ProgressLoadResult(PlayerProgress.CreateDefault(), ProgressLoadStatus.RecoveredFromCorrupt, null, $"存档无法读取：{ex.Message}");
        }

        PlayerProgress? loaded;
        try
        {
            loaded = JsonSerializer.Deserialize<PlayerProgress>(json, SerializerOptions);
        }
        catch (JsonException ex)
        {
            return RecoverFromCorrupt($"存档内容不是合法 JSON：{ex.Message}");
        }

        if (loaded is null)
        {
            return RecoverFromCorrupt("存档内容为空。");
        }

        if (loaded.SchemaVersion > PlayerProgress.CurrentSchemaVersion)
        {
            string backup = MakeBackupPath("unsupported");
            TryCopy(Path, backup);

            // 刻意不覆盖原文件：这是更新版本写的存档，降级运行不应该把它毁掉
            return new ProgressLoadResult(
                PlayerProgress.CreateDefault(),
                ProgressLoadStatus.RejectedVersionTooNew,
                backup,
                $"存档来自更新版本（schemaVersion={loaded.SchemaVersion}），已备份到 {System.IO.Path.GetFileName(backup)}，本次以默认进度运行且不会覆盖它。");
        }

        PlayerProgress migrated = Migrate(loaded);
        return new ProgressLoadResult(migrated, ProgressLoadStatus.Loaded, null, null);
    }

    /// <summary>原子写盘。实现见 <see cref="AtomicFile"/>，与 records.json 共用同一套逻辑。</summary>
    public void Save(PlayerProgress progress)
    {
        ArgumentNullException.ThrowIfNull(progress);

        AtomicFile.WriteAllText(Path, JsonSerializer.Serialize(progress, SerializerOptions));
    }

    /// <summary>把旧版本结构升级到当前结构（§6.1）。当前只有 v1，所以这里是直通。</summary>
    private static PlayerProgress Migrate(PlayerProgress loaded)
    {
        if (loaded.SchemaVersion < PlayerProgress.CurrentSchemaVersion)
        {
            // 将来加字段时在这里补默认值，然后统一把版本号推到当前值
            loaded.SchemaVersion = PlayerProgress.CurrentSchemaVersion;
        }

        return loaded;
    }

    private ProgressLoadResult RecoverFromCorrupt(string message)
    {
        string backup = MakeBackupPath("corrupt");
        TryCopy(Path, backup);

        PlayerProgress fresh = PlayerProgress.CreateDefault();
        Save(fresh);

        return new ProgressLoadResult(
            fresh,
            ProgressLoadStatus.RecoveredFromCorrupt,
            backup,
            $"{message} 原文件已备份到 {System.IO.Path.GetFileName(backup)}，进度已重置。");
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
