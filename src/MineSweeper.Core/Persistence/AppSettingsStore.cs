using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MineSweeper.Core.Settings;

namespace MineSweeper.Core.Persistence;

/// <summary>settings.json 的加载结局。</summary>
public enum SettingsLoadStatus
{
    /// <summary>正常读取。</summary>
    Loaded = 0,

    /// <summary>文件不存在，已按默认值新建。</summary>
    CreatedNew = 1,

    /// <summary>JSON 损坏：原文件已备份，用默认值重建。</summary>
    RecoveredFromCorrupt = 2,

    /// <summary>schemaVersion 高于本程序支持：拒绝读取（已备份，且不覆盖原文件）。</summary>
    RejectedVersionTooNew = 3,
}

/// <summary>加载结果。</summary>
public sealed record SettingsLoadResult(AppSettings Settings, SettingsLoadStatus Status, string? BackupPath, string? Message)
{
    /// <summary>是否属于"要提示玩家"的异常情况。</summary>
    public bool HasWarning => Status is SettingsLoadStatus.RecoveredFromCorrupt or SettingsLoadStatus.RejectedVersionTooNew;
}

/// <summary>
/// 玩家设置的读写（音效 + 主题）。行为与 <see cref="ProgressStore"/> 完全一致：
/// 原子写盘、损坏备份重建、版本过新拒读且不覆盖。
/// </summary>
public sealed class AppSettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public AppSettingsStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Path = path;
    }

    /// <summary>settings.json 的完整路径。</summary>
    public string Path { get; }

    /// <summary>
    /// 本次 Load 是否因"版本过新"拒读过这个文件（此时 <see cref="Save"/> 被禁用）。
    ///
    /// 只做到"拒读"还不够：App 拿到默认设置后会继续跑，退出时照样 Save，
    /// 更新版本写的文件还是会被这份默认值覆盖——所以拒读必须同时禁写。
    /// </summary>
    public bool SaveBlocked => _versionTooNew;

    // Load 在 RejectedVersionTooNew 分支置位；下一个 Load 重新判定
    private bool _versionTooNew;

    /// <summary>加载设置；任何异常情况都降级为默认值 + 返回可展示的提示。</summary>
    public SettingsLoadResult Load()
    {
        // 重新加载以本次读到的结果为准（文件可能已经被换回旧版本）
        _versionTooNew = false;

        if (!File.Exists(Path))
        {
            AppSettings created = AppSettings.CreateDefault();
            Save(created);
            return new SettingsLoadResult(created, SettingsLoadStatus.CreatedNew, null, null);
        }

        string json;
        try
        {
            json = File.ReadAllText(Path, Encoding.UTF8);
        }
        catch (IOException ex)
        {
            return new SettingsLoadResult(AppSettings.CreateDefault(), SettingsLoadStatus.RecoveredFromCorrupt, null, $"设置无法读取：{ex.Message}");
        }

        AppSettings? loaded;
        try
        {
            loaded = JsonSerializer.Deserialize<AppSettings>(json, SerializerOptions);
        }
        catch (JsonException ex)
        {
            return RecoverFromCorrupt($"设置内容不是合法 JSON：{ex.Message}");
        }

        if (loaded is null)
        {
            return RecoverFromCorrupt("设置内容为空。");
        }

        if (loaded.SchemaVersion > AppSettings.CurrentSchemaVersion)
        {
            string backup = MakeBackupPath("unsupported");
            TryCopy(Path, backup);

            // 拒读之后还要禁写：否则关窗时的 Save 会把新版文件覆盖掉
            _versionTooNew = true;

            // 与 progress.json 一样：更新版本写的文件，降级运行不能毁掉
            return new SettingsLoadResult(
                AppSettings.CreateDefault(),
                SettingsLoadStatus.RejectedVersionTooNew,
                backup,
                $"设置来自更新版本（schemaVersion={loaded.SchemaVersion}），已备份到 {System.IO.Path.GetFileName(backup)}，本次以默认设置运行且不会覆盖它。");
        }

        AppSettings migrated = AppSettings.Migrate(loaded);
        migrated.Normalize();
        return new SettingsLoadResult(migrated, SettingsLoadStatus.Loaded, null, null);
    }

    /// <summary>原子写盘。</summary>
    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        // 版本过新被拒读时禁止写盘：内存里的默认设置只是本次运行的临时态，
        // 写回磁盘会把更新版本的文件直接抹掉（见 SaveBlocked）
        if (_versionTooNew)
        {
            return;
        }

        settings.Normalize();
        AtomicFile.WriteAllText(Path, JsonSerializer.Serialize(settings, SerializerOptions));
    }

    private SettingsLoadResult RecoverFromCorrupt(string message)
    {
        string backup = MakeBackupPath("corrupt");
        TryCopy(Path, backup);

        AppSettings fresh = AppSettings.CreateDefault();
        Save(fresh);

        return new SettingsLoadResult(
            fresh,
            SettingsLoadStatus.RecoveredFromCorrupt,
            backup,
            $"{message} 原文件已备份到 {System.IO.Path.GetFileName(backup)}，设置已重置。");
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
