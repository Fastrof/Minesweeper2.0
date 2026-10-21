using System.Text.Json;
using Xunit;
using MineSweeper.Core.History;
using MineSweeper.Core.Persistence;

namespace MineSweeper.Core.Tests;

/// <summary>
/// 历史记录（history.json）：从第一次扫雷开始累计。
/// **核心要求**：它只活在这一个文件里 —— 文件丢了/删了，历史就从零开始。
/// </summary>
public class HistoryTests
{
    private static GameSummary Summary(
        double seconds = 10d,
        int mines = 3,
        int cells = 20,
        bool won = false,
        bool levelCleared = false)
        => new(DateTimeOffset.Parse("2026-10-10T12:00:00Z"), seconds, mines, cells, won, levelCleared);

    [Fact]
    public void 新玩家没有任何记录()
    {
        PlayHistory history = PlayHistory.CreateDefault();

        Assert.False(history.HasAnyRecord);
        Assert.Null(history.FirstSweptAtUtc);
        Assert.Equal(0, history.TotalGames);
        Assert.Equal(0, history.TotalMinesSwept);
        Assert.Equal(0d, history.TotalPlayedSeconds);
    }

    [Fact]
    public void 第一次扫雷的时刻只记一次_但对局数每局都加()
    {
        PlayHistory history = PlayHistory.CreateDefault();
        var first = DateTimeOffset.Parse("2026-10-10T08:00:00Z");
        var later = DateTimeOffset.Parse("2026-10-11T09:00:00Z");

        history.BeginGame(first);
        history.BeginGame(later);

        Assert.Equal(first, history.FirstSweptAtUtc);
        Assert.Equal(2, history.TotalGames);
    }

    [Fact]
    public void 一局的成果会累加()
    {
        PlayHistory history = PlayHistory.CreateDefault();

        history.RecordGame(Summary(seconds: 30, mines: 5, cells: 40, won: true));
        history.RecordGame(Summary(seconds: 12.5, mines: 2, cells: 10, won: false, levelCleared: true));

        Assert.Equal(42.5d, history.TotalPlayedSeconds, 3);
        Assert.Equal(7, history.TotalMinesSwept);
        Assert.Equal(50, history.TotalCellsRevealed);
        Assert.Equal(1, history.TotalWins);
        Assert.Equal(1, history.TotalLevelsCleared);
        Assert.Equal(DateTimeOffset.Parse("2026-10-10T12:00:00Z"), history.LastPlayedAtUtc);
    }

    [Fact]
    public void 存档往返一致()
    {
        string path = TestPaths.NewFile("history.json");
        var store = new HistoryStore(path);

        PlayHistory history = store.Load().History;
        history.BeginGame(DateTimeOffset.Parse("2026-10-10T08:00:00Z"));
        history.RecordGame(Summary(seconds: 65, mines: 9, cells: 88, won: true, levelCleared: true));
        store.Save(history);

        HistoryLoadResult reloaded = new HistoryStore(path).Load();

        Assert.Equal(HistoryLoadStatus.Loaded, reloaded.Status);
        Assert.Equal(history.FirstSweptAtUtc, reloaded.History.FirstSweptAtUtc);
        Assert.Equal(1, reloaded.History.TotalGames);
        Assert.Equal(9, reloaded.History.TotalMinesSwept);
        Assert.Equal(65d, reloaded.History.TotalPlayedSeconds, 3);
        Assert.Equal(88, reloaded.History.TotalCellsRevealed);
        Assert.Equal(1, reloaded.History.TotalWins);
        Assert.Equal(1, reloaded.History.TotalLevelsCleared);
        Assert.False(File.Exists(path + ".tmp"), "原子写盘不该留下 .tmp");
    }

    /// <summary>需求明确要求：历史数据（文件）丢了，就得从最新的开始。</summary>
    [Fact]
    public void 历史文件被删掉之后从零开始()
    {
        string path = TestPaths.NewFile("history.json");
        var store = new HistoryStore(path);

        PlayHistory history = store.Load().History;
        history.BeginGame(DateTimeOffset.Parse("2026-10-10T08:00:00Z"));
        history.RecordGame(Summary(seconds: 120, mines: 15, cells: 300, won: true));
        store.Save(history);
        Assert.Equal(15, new HistoryStore(path).Load().History.TotalMinesSwept);

        File.Delete(path);

        HistoryLoadResult afterDelete = new HistoryStore(path).Load();
        Assert.Equal(HistoryLoadStatus.CreatedNew, afterDelete.Status);
        Assert.False(afterDelete.History.HasAnyRecord);
        Assert.Equal(0, afterDelete.History.TotalMinesSwept);
        Assert.Equal(0d, afterDelete.History.TotalPlayedSeconds);
    }

    [Fact]
    public void 损坏的历史会备份重建()
    {
        string path = TestPaths.NewFile("history.json");
        File.WriteAllText(path, "{ 这不是 JSON");

        HistoryLoadResult result = new HistoryStore(path).Load();

        Assert.Equal(HistoryLoadStatus.RecoveredFromCorrupt, result.Status);
        Assert.True(result.HasWarning);
        Assert.NotNull(result.BackupPath);
        Assert.True(File.Exists(result.BackupPath!));
        Assert.False(result.History.HasAnyRecord);

        // 损坏分支必须重建一份默认文件（与其它存档一致）：否则原文件一直坏着，
        // 每次启动都会再备份一份 corrupt 文件，玩家也永远回不到正常状态。
        Assert.True(File.Exists(path), "损坏分支应当重建一份默认 history.json。");
        HistoryLoadResult reloaded = new HistoryStore(path).Load();
        Assert.Equal(HistoryLoadStatus.Loaded, reloaded.Status);
        Assert.False(reloaded.History.HasAnyRecord);
    }

    [Fact]
    public void 版本过新时拒读且不覆盖原文件()
    {
        string path = TestPaths.NewFile("history.json");
        var newer = new PlayHistory { SchemaVersion = PlayHistory.CurrentSchemaVersion + 5, TotalMinesSwept = 999 };
        File.WriteAllText(path, JsonSerializer.Serialize(newer));
        string before = File.ReadAllText(path);

        HistoryLoadResult result = new HistoryStore(path).Load();

        Assert.Equal(HistoryLoadStatus.RejectedVersionTooNew, result.Status);
        Assert.Equal(before, File.ReadAllText(path));
        Assert.False(result.History.HasAnyRecord);
    }

    [Fact]
    public void 手改出来的负数会被夹回零()
    {
        var history = new PlayHistory
        {
            TotalMinesSwept = -5,
            TotalCellsRevealed = -1,
            TotalGames = 3,
            TotalWins = 99,
            TotalPlayedSeconds = -12.5,
        };

        history.Normalize();

        Assert.Equal(0, history.TotalMinesSwept);
        Assert.Equal(0, history.TotalCellsRevealed);
        Assert.Equal(3, history.TotalWins);   // 不会超过总局数
        Assert.Equal(0d, history.TotalPlayedSeconds);
    }

    [Theory]
    [InlineData(0, "0 秒")]
    [InlineData(-3, "0 秒")]
    [InlineData(7, "7 秒")]
    [InlineData(65, "1 分 05 秒")]
    [InlineData(3661, "1 小时 01 分 01 秒")]
    [InlineData(7325, "2 小时 02 分 05 秒")]
    public void 时长格式化(double seconds, string expected)
        => Assert.Equal(expected, PlayHistory.FormatDuration(seconds));
}