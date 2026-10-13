using System.Text;
using System.Text.Json;
using MineSweeper.Core.Persistence;

// 刻意声明在 Updates 而不是 Persistence：整个"检查更新"模块（ReleaseJson / AppVersion /
// UpdateState / UpdateStateStore）对 App 层是一个整体，UpdateChecker 只 using 这一个命名空间。
// 文件仍按工程约定放在 Persistence 目录下（存档实现都在这里，方便与其它 store 对照）。
namespace MineSweeper.Core.Updates;

/// <summary>updatestate.json 的加载结局。</summary>
public enum UpdateStateLoadStatus
{
    /// <summary>正常读取。</summary>
    Loaded = 0,

    /// <summary>文件不存在，已按默认状态新建。</summary>
    CreatedNew = 1,

    /// <summary>JSON 损坏：原文件已备份，用默认状态重建。</summary>
    RecoveredFromCorrupt = 2,

    /// <summary>schemaVersion 高于本程序支持：拒绝读取（已备份，且不覆盖原文件）。</summary>
    RejectedVersionTooNew = 3,
}

/// <summary>更新状态加载结果。</summary>
public sealed record UpdateStateLoadResult(
    UpdateState State,
    UpdateStateLoadStatus Status,
    string? BackupPath,
    string? Message)
{
    /// <summary>是否属于"要提示玩家"的异常情况。</summary>
    public bool HasWarning => Status is UpdateStateLoadStatus.RecoveredFromCorrupt or UpdateStateLoadStatus.RejectedVersionTooNew;
}

/// <summary>
/// 「检查更新」状态（updatestate.json）的读写。
/// 行为与 <see cref="ProgressStore"/> / <see cref="LevelProgressStore"/> 完全一致：
/// 走 <see cref="AtomicFile"/> 原子写盘、JSON 损坏时备份重建、schemaVersion 过新时拒读且不覆盖原文件。
///
/// 这份状态丢了只是"下次启动可能会多查一次更新"，所以任何异常都降级成默认值即可，
/// 绝不能因为更新检查出问题而拦住程序启动。
/// </summary>
public sealed class UpdateStateStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public UpdateStateStore(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        Path = filePath;
    }

    /// <summary>updatestate.json 的完整路径。</summary>
    public string Path { get; }

    /// <summary>加载更新状态；任何异常情况都降级为默认状态 + 返回可展示的提示。</summary>
    public UpdateStateLoadResult Load()
    {
        if (!File.Exists(Path))
        {
            UpdateState created = UpdateState.CreateDefault();
            Save(created);
            return new UpdateStateLoadResult(created, UpdateStateLoadStatus.CreatedNew, null, null);
        }

        string json;
        try
        {
            json = File.ReadAllText(Path, Encoding.UTF8);
        }
        catch (IOException ex)
        {
            return new UpdateStateLoadResult(
                UpdateState.CreateDefault(),
                UpdateStateLoadStatus.RecoveredFromCorrupt,
                null,
                $"更新状态无法读取：{ex.Message}");
        }

        UpdateState? loaded;
        try
        {
            loaded = JsonSerializer.Deserialize<UpdateState>(json, SerializerOptions);
        }
        catch (JsonException ex)
        {
            return RecoverFromCorrupt($"更新状态不是合法 JSON：{ex.Message}");
        }

        if (loaded is null)
        {
            return RecoverFromCorrupt("更新状态内容为空。");
        }

        if (loaded.SchemaVersion > UpdateState.CurrentSchemaVersion)
        {
            string backup = MakeBackupPath("unsupported");
            TryCopy(Path, backup);

            // 刻意不覆盖原文件：这是更新版本写的状态，降级运行不该把它毁掉
            return new UpdateStateLoadResult(
                UpdateState.CreateDefault(),
                UpdateStateLoadStatus.RejectedVersionTooNew,
                backup,
                $"更新状态来自更新版本（schemaVersion={loaded.SchemaVersion}），已备份到 {System.IO.Path.GetFileName(backup)}，本次以默认状态运行且不会覆盖它。");
        }

        return new UpdateStateLoadResult(Migrate(loaded), UpdateStateLoadStatus.Loaded, null, null);
    }

    /// <summary>原子写盘。实现见 <see cref="AtomicFile"/>，与其它存档共用同一套逻辑。</summary>
    public void Save(UpdateState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        AtomicFile.WriteAllText(Path, JsonSerializer.Serialize(state, SerializerOptions));
    }

    /// <summary>把旧结构升级到当前结构。当前只有 v1，所以只是把版本号推到当前值。</summary>
    private static UpdateState Migrate(UpdateState loaded)
    {
        if (loaded.SchemaVersion < UpdateState.CurrentSchemaVersion)
        {
            // 将来加字段时在这里补默认值，然后统一把版本号推到当前值
            loaded.SchemaVersion = UpdateState.CurrentSchemaVersion;
        }

        return loaded;
    }

    private UpdateStateLoadResult RecoverFromCorrupt(string message)
    {
        string backup = MakeBackupPath("corrupt");
        TryCopy(Path, backup);

        UpdateState fresh = UpdateState.CreateDefault();
        Save(fresh);

        return new UpdateStateLoadResult(
            fresh,
            UpdateStateLoadStatus.RecoveredFromCorrupt,
            backup,
            $"{message} 原文件已备份到 {System.IO.Path.GetFileName(backup)}，更新状态已重置。");
    }

    private string MakeBackupPath(string kind)
    {
        string directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(Path)) ?? ".";
        string stamp = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss", System.Globalization.CultureInfo.InvariantCulture);
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
