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

    /// <summary>加载历史；任何异常都降级成"全新历史 + 可展示的提示"。</summary>
    public HistoryLoadResult Load()
    {
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
            return Recover($"内容不是合法 JSON（{ex.Message}）");
        }

        if (loaded is null)
        {
            return Recover("文件里没有任何内容");
        }

        if (loaded.SchemaVersion > PlayHistory.CurrentSchemaVersion)
        {
            string? backup = Backup();
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

        history.Normalize();
        AtomicFile.WriteAllText(Path, JsonSerializer.Serialize(history, SerializerOptions));
    }

    private HistoryLoadResult Recover(string reason)
    {
        string? backup = Backup();
        return new HistoryLoadResult(
            PlayHistory.CreateDefault(),
            HistoryLoadStatus.RecoveredFromCorrupt,
            backup,
            $"历史记录读取失败（{reason}），已备份原文件，历史从零重新开始。");
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