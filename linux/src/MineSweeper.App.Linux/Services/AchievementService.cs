using MineSweeper.Core;
using MineSweeper.Core.Achievements;
using MineSweeper.Core.History;
using MineSweeper.Core.Levels;
using MineSweeper.Core.Persistence;

namespace MineSweeper.App.Linux.Services;

/// <summary>
/// 成就服务（**移植自** Windows 端 <c>Services/AchievementService.cs</c>）。
///
/// 分工与 Windows 端完全一致：
///   - 判定规则在 Core 的 <c>AchievementRules</c>（纯函数、有单测）；
///   - 这里只做事实装配、落盘、抛事件；
///   - 落盘失败只写日志（成就丢了比游戏崩了轻得多）。
/// </summary>
public sealed class AchievementService
{
    private readonly Storage _storage;

    public AchievementService(Storage storage) => _storage = storage;

    /// <summary>当前进度（含已解锁、各成就进度值）。</summary>
    public AchievementProgress Progress => _storage.Achievements;

    /// <summary>本次新解锁了哪些成就（可能一次多个）。</summary>
    public event EventHandler<IReadOnlyList<AchievementUnlock>>? Unlocked;

    /// <summary>评估一次事实，返回本次新解锁的成就（顺便落盘 + 抛事件）。</summary>
    public IReadOnlyList<AchievementUnlock> Report(AchievementFacts facts)
    {
        IReadOnlyList<AchievementUnlock> unlocks = AchievementRules.Evaluate(
            facts, Progress, DateTimeOffset.UtcNow);

        Progress.Normalize();
        _storage.SaveAchievements();

        if (unlocks.Count > 0)
        {
            foreach (AchievementUnlock unlock in unlocks)
            {
                AppPaths.Log(
                    $"[成就] 解锁 {unlock.Definition.Id}「{unlock.Definition.Title}」" +
                    $"（{unlock.Progress}/{unlock.Target}，+{unlock.Definition.Points} 点）");
            }

            Unlocked?.Invoke(this, unlocks);
        }

        return unlocks;
    }

    /// <summary>累计统计（最快用时两张表一起看）。</summary>
    public AchievementStats BuildStats()
    {
        GameRecords records = _storage.Records;
        LevelProgress levels = _storage.LevelProgress;
        PlayHistory history = _storage.History;

        return new AchievementStats(
            MinesSwept: history.TotalMinesSwept,
            Wins: history.TotalWins,
            Games: history.TotalGames,
            ClearedLevels: levels.ClearedCount,
            TotalStars: levels.TotalStars,
            BestBeginnerSeconds: BestSeconds(records, DifficultyPreset.Beginner.Id),
            BestIntermediateSeconds: BestSeconds(records, DifficultyPreset.Intermediate.Id),
            BestExpertSeconds: BestSeconds(records, DifficultyPreset.Expert.Id));
    }

    private static double? BestSeconds(GameRecords records, string difficultyId)
    {
        double? best = null;

        foreach (Dictionary<string, BestTimeEntry> table in new[] { records.Classic, records.ClassicWithItem })
        {
            if (table.TryGetValue(difficultyId, out BestTimeEntry? entry) && entry is not null &&
                (best is null || entry.Seconds < best))
            {
                best = entry.Seconds;
            }
        }

        return best;
    }

    /// <summary>把一局的现场翻译成 Core 的事实记录。</summary>
    public static AchievementFacts BuildFacts(
        GameSession? session,
        bool won,
        int wrongFlags,
        bool usedAnyItem,
        int maxChordOpened,
        string? levelId,
        int levelStars,
        bool usedMagnifier,
        bool usedSonar,
        bool usedFreeze,
        AchievementStats stats)
    {
        return new AchievementFacts(
            Won: won,
            ElapsedSeconds: session?.ElapsedSeconds ?? 0d,
            Width: session?.Board.Width ?? 0,
            Height: session?.Board.Height ?? 0,
            MineCount: session?.Board.MineCount ?? 0,
            RevealedCells: session?.RevealedCount ?? 0,
            WrongFlags: wrongFlags,
            UsedAnyItem: usedAnyItem,
            MaxChordOpened: maxChordOpened,
            LevelId: levelId,
            LevelStars: levelStars,
            UsedMagnifier: usedMagnifier,
            UsedSonar: usedSonar,
            UsedFreeze: usedFreeze,
            IsStandardDifficulty: IsStandard(
                session?.Board.Width ?? 0, session?.Board.Height ?? 0, session?.Board.MineCount ?? 0),
            Stats: stats);
    }

    private static bool IsStandard(int width, int height, int mineCount)
        => (width == DifficultyPreset.Beginner.Width && height == DifficultyPreset.Beginner.Height
            && mineCount == DifficultyPreset.Beginner.MineCount)
           || (width == DifficultyPreset.Intermediate.Width && height == DifficultyPreset.Intermediate.Height
               && mineCount == DifficultyPreset.Intermediate.MineCount)
           || (width == DifficultyPreset.Expert.Width && height == DifficultyPreset.Expert.Height
               && mineCount == DifficultyPreset.Expert.MineCount);

    /// <summary>统计插错的旗（插在非雷格上的旗）。</summary>
    public static int CountWrongFlags(GameSession? session)
    {
        if (session is null)
        {
            return 0;
        }

        Board board = session.Board;
        int wrong = 0;
        for (int i = 0; i < board.CellCount; i++)
        {
            if (board.IsFlagged(i) && !board.IsMine(i))
            {
                wrong++;
            }
        }

        return wrong;
    }
}
