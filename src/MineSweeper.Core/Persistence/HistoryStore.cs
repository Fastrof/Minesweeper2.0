using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MineSweeper.Core.History;

namespace MineSweeper.Core.Persistence;

/// <summary>history.json 的加载结局。</summary>
public enum HistoryLoadStatus
{
    /// <summary>正常读取。</summary>
    Loaded = 0,

    /// <summary>文件不存在：全新开始（玩家第一次玩，或者把 data\ 删了）。</summary>
    CreatedNew = 1,

    /// <summary>JSON 损坏：原文件已备份，历史从零重建。</summary>
    RecoveredFromCorrupt = 2,

    /// <summary>schemaVersion 过新：拒绝读取（已备份，且不覆盖原文件）。</summary>
    RejectedVersionTooNew = 3,
}

/// <summary>历史记录加载结果。</summary>
public sealed record HistoryLoadResult(
    PlayHistory History,
    HistoryLoadStatus Status,
    string? BackupPath,
    string? Message)
{
    /// <summary>是否属于"要提示玩家"的异常情况。</summary>
    public bool HasWarning => Status is HistoryLoadStatus.RecoveredFromCorrupt or HistoryLoadStatus.RejectedVersionTooNew;
}

/// <summary>
/// 历史记录（history.json）的读写。行为与其它存档一致：
/// 原子写盘、损坏备份重建、版本过新拒读且不覆盖原文件。
///
/// **文件不存在/被删掉 = 历史从头开始**，这是需求明确要求的行为，不是缺陷。
/// </summary>
public sealed class HistoryStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public HistoryStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Path = path;
    }

    /// <summary>history.json 的完整路径。</summary>
    public string Path { get; }

    /// <summary>
    /// 本次 Load 是否因"版本过新"拒读过这个文件（此时 <see cref="Save"/> 被禁用）。
    ///
    /// 只拒读不禁写是不够的：App 用全新历史继续跑，退出时 Save 会把更新版本写的文件覆盖掉。
    /// </summary>
    public bool SaveBlocked => _versionTooNew;

    // Load 在 RejectedVersionTooNew 分支置位；下一个 Load 重新判定
    private bool _versionTooNew;

    /// <summary>加载历史；任何异常都降级成"全新历史 + 可展示的提示"。</summary>
    public HistoryLoadResult Load()
    {
        // 重新加载以本次读到的结果为准（文件可能已经被换回旧版本）
        _versionTooNew = false;

        if (!File.Exists(Path))
        {
            return new HistoryLoadResult(PlayHistory.CreateDefault(), HistoryLoadStatus.CreatedNew, null, null);
        }

        string json;
        try
        {
            json = File.ReadAllText(Path, Encoding.UTF8);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Recover($"读不到文件（{ex.Message}）");
        }

        PlayHistory? loaded;
        try
        {
            loaded = JsonSerializer.Deserialize<PlayHistory>(json, SerializerOptions);
        }
        catch (JsonException ex)
        {
            return RecoverFromCorrupt($"内容不是合法 JSON（{ex.Message}）");
        }

        if (loaded is null)
        {
            return RecoverFromCorrupt("文件里没有任何内容");
        }

        if (loaded.SchemaVersion > PlayHistory.CurrentSchemaVersion)
        {
            string? backup = Backup();

            // 拒读之后还要禁写：否则关窗时的 Save 会把新版文件覆盖掉
            _versionTooNew = true;

            return new HistoryLoadResult(
                PlayHistory.CreateDefault(),
                HistoryLoadStatus.RejectedVersionTooNew,
                backup,
                $"历史记录的版本（{loaded.SchemaVersion}）比本程序支持的（{PlayHistory.CurrentSchemaVersion}）新，已备份原文件、不覆盖它。");
        }

        loaded.Normalize();
        loaded.SchemaVersion = PlayHistory.CurrentSchemaVersion;
        return new HistoryLoadResult(loaded, HistoryLoadStatus.Loaded, null, null);
    }

    /// <summary>原子写盘。</summary>
    public void Save(PlayHistory history)
    {
        ArgumentNullException.ThrowIfNull(history);

        // 版本过新被拒读时禁止写盘：内存里的全新历史只是本次运行的临时态，
        // 写回磁盘会把更新版本的存档直接抹掉（见 SaveBlocked）
        if (_versionTooNew)
        {
            return;
        }

        history.Normalize();
        AtomicFile.WriteAllText(Path, JsonSerializer.Serialize(history, SerializerOptions));
    }

    /// <summary>
    /// 读不到文件（被占用 / 没权限）时的降级：备份一份并返回默认历史，但**不重建**原文件。
    ///
    /// 这种失败可能只是临时的（别的进程正开着），文件内容未必坏；贸然覆盖反而会毁掉还能恢复的数据。
    /// </summary>
    private HistoryLoadResult Recover(string reason)
    {
        string? backup = Backup();
        return new HistoryLoadResult(
            PlayHistory.CreateDefault(),
            HistoryLoadStatus.RecoveredFromCorrupt,
            backup,
            $"历史记录读取失败（{reason}），已备份原文件（如有），本次历史从零开始且不会覆盖原文件。");
    }

    /// <summary>
    /// 内容损坏时的降级：先备份原文件，再重建一份默认 history.json。
    ///
    /// 只备份不重建的话损坏文件会一直留在原处：每次启动都再备份一份（corrupt 文件越攒越多），
    /// 玩家也永远回不到正常状态。这一点与其它存档的 RecoverFromCorrupt 保持一致。
    /// 重建失败只影响下次启动是否还走损坏分支，不能影响本次返回默认历史。
    /// </summary>
    private HistoryLoadResult RecoverFromCorrupt(string reason)
    {
        string? backup = Backup();

        PlayHistory fresh = PlayHistory.CreateDefault();
        try
        {
            Save(fresh);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Core 层没有日志：本次照常按默认历史跑，下次启动再尝试重建
        }

        return new HistoryLoadResult(
            fresh,
            HistoryLoadStatus.RecoveredFromCorrupt,
            backup,
            $"{reason}，原文件已备份到 {(backup is null ? "（备份失败）" : System.IO.Path.GetFileName(backup))}，历史已重建为零。");
    }

    private string? Backup()
    {
        try
        {
            if (!File.Exists(Path))
            {
                return null;
            }

            string backup = $"{System.IO.Path.ChangeExtension(Path, null)}.corrupt.{DateTime.Now:yyyyMMddHHmmss}.json";
            File.Copy(Path, backup, overwrite: true);
            return backup;
        }
        catch (Exception)
        {
            return null;
        }
    }
}