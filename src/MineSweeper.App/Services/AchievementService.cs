using MineSweeper.Core;
using MineSweeper.Core.Achievements;
using MineSweeper.Core.History;
using MineSweeper.Core.Levels;
using MineSweeper.Core.Persistence;

namespace MineSweeper.App.Services;

/// <summary>
/// 成就服务（v1.1）：把"这一局发生了什么"翻译成 Core 认识的事实，算解锁、落盘、抛出事件。
///
/// 分工：
/// <list type="bullet">
/// <item>判定规则全在 Core 的 <c>AchievementRules</c>（纯函数、有单测）—— 这里只做数据搬运。</item>
/// <item>落盘走 <c>AchievementStore</c>（原子写盘 + 损坏降级），失败只写日志，绝不影响游戏。</item>
/// <item>UI 只订阅 <see cref="Unlocked"/>，不关心判定细节。</item>
/// </list>
/// </summary>
internal sealed class AchievementService
{
    private readonly AchievementStore _store;

    public AchievementService(AchievementStore store, AchievementProgress progress)
    {
        _store = store;
        Progress = progress;
    }

    /// <summary>当前进度（含已解锁、各成就的进度值）。</summary>
    public AchievementProgress Progress { get; }

    /// <summary>本次新解锁了哪些成就（可能一次多个）。</summary>
    public event EventHandler<IReadOnlyList<AchievementUnlock>>? Unlocked;

    /// <summary>评估一次事实，返回本次新解锁的成就（顺便落盘 + 抛事件）。</summary>
    public IReadOnlyList<AchievementUnlock> Report(AchievementFacts facts)
    {
        IReadOnlyList<AchievementUnlock> unlocks = AchievementRules.Evaluate(facts, Progress, DateTimeOffset.UtcNow);

        // 进度数据（累计扫雷数、单局最快等）也要落盘，否则成就面板里的进度条永远是 0。
        // 代价是一次原子写，只发生在"一局结束"这个低频时刻。
        Progress.Normalize();
        Save();

        if (unlocks.Count > 0)
        {
            foreach (AchievementUnlock unlock in unlocks)
            {
                AppPaths.Log($"[成就] 解锁 {unlock.Definition.Id}「{unlock.Definition.Title}」" +
                             $"（{unlock.Progress}/{unlock.Target}，+{unlock.Definition.Points} 点）");
            }

            Unlocked?.Invoke(this, unlocks);
        }

        return unlocks;
    }

    /// <summary>落盘。失败只写日志（成就丢了比游戏崩了轻得多）。</summary>
    public void Save()
    {
        try
        {
            _store.Save(Progress);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppPaths.Log($"[成就] 落盘失败：{ex.GetType().Name}：{ex.Message}");
        }
    }

    /// <summary>
    /// 汇总累计统计。
    ///
    /// 最快用时**两张表一起看**：v0.6 起界面恒为"经典 + 道具"，
    /// 所以纯手工表（Classic）在实际使用中基本是空的，只读一张表会让速度类成就永远解不开。
    /// </summary>
    public static AchievementStats BuildStats(PlayHistory history, LevelProgress levels, GameRecords records)
        => new(
            MinesSwept: history.TotalMinesSwept,
            Wins: history.TotalWins,
            Games: history.TotalGames,
            ClearedLevels: levels.ClearedCount,
            TotalStars: levels.TotalStars,
            BestBeginnerSeconds: BestSeconds(records, DifficultyPreset.Beginner.Id),
            BestIntermediateSeconds: BestSeconds(records, DifficultyPreset.Intermediate.Id),
            BestExpertSeconds: BestSeconds(records, DifficultyPreset.Expert.Id));

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
        int width = session?.Board.Width ?? 0;
        int height = session?.Board.Height ?? 0;
        int mineCount = session?.Board.MineCount ?? 0;

        return new AchievementFacts(
            Won: won,
            ElapsedSeconds: session?.ElapsedSeconds ?? 0d,
            Width: width,
            Height: height,
            MineCount: mineCount,
            RevealedCells: session?.RevealedCount ?? 0,
            WrongFlags: wrongFlags,
            UsedAnyItem: usedAnyItem,
            MaxChordOpened: maxChordOpened,
            LevelId: levelId,
            LevelStars: levelStars,
            UsedMagnifier: usedMagnifier,
            UsedSonar: usedSonar,
            UsedFreeze: usedFreeze,
            IsStandardDifficulty: IsStandard(width, height, mineCount),
            Stats: stats);
    }

    /// <summary>是否标准三级难度之一（9×9/10、16×16/40、30×16/99）——"高级通关"这类成就按它判定。</summary>
    private static bool IsStandard(int width, int height, int mineCount)
        => (width == DifficultyPreset.Beginner.Width && height == DifficultyPreset.Beginner.Height
            && mineCount == DifficultyPreset.Beginner.MineCount)
           || (width == DifficultyPreset.Intermediate.Width && height == DifficultyPreset.Intermediate.Height
               && mineCount == DifficultyPreset.Intermediate.MineCount)
           || (width == DifficultyPreset.Expert.Width && height == DifficultyPreset.Expert.Height
               && mineCount == DifficultyPreset.Expert.MineCount);

    /// <summary>统计一局里插错的旗（插在非雷格上的旗）。胜利时自动插的旗都是对的，所以不影响结果。</summary>
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
