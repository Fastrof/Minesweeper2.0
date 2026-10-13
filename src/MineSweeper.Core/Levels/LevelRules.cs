namespace MineSweeper.Core.Levels;

/// <summary>
/// 一局关卡打完之后的结果（用于结算界面与通知）。
/// </summary>
public readonly record struct LevelOutcome(
    LevelDefinition Level,
    bool Won,
    int Stars,
    double Seconds,
    double BestSeconds,
    bool IsNewBest,
    bool FirstClear,
    LevelDefinition? UnlockedNext)
{
    /// <summary>本局的星数文案（如 ★★☆）。</summary>
    public string StarsText => LevelRules.StarsText(Stars);

    /// <summary>一句话总结，可直接作为通知正文。</summary>
    public string Summary
    {
        get
        {
            if (!Won)
            {
                return MineSweeper.Core.Localization.Strings.Get("toast.levelFailedBody", Seconds.ToString("F1"));
            }

            string text = MineSweeper.Core.Localization.Strings.Get("flow.levelCleared", StarsText, Seconds.ToString("F1"));
            if (BestSeconds > 0)
            {
                text += $"，最好成绩 {BestSeconds:F1} 秒";
            }

            if (UnlockedNext is { } next)
            {
                text += $"；已解锁 {next.DisplayName}";
            }

            return text + "。";
        }
    }
}

/// <summary>
/// 关卡制的判定规则（纯函数，全部可单测）：
/// 星级评定、逐关解锁、以及"打完一局 → 更新进度 + 给出结算结果"。
/// </summary>
public static class LevelRules
{
    /// <summary>满星。</summary>
    public const int MaxStars = 3;

    /// <summary>按通关用时评星：★★★ 挑战时间 / ★★ 目标时间 / ★ 通关。</summary>
    public static int EvaluateStars(LevelDefinition level, double elapsedSeconds)
    {
        ArgumentNullException.ThrowIfNull(level);

        if (elapsedSeconds <= level.ChallengeSeconds)
        {
            return 3;
        }

        return elapsedSeconds <= level.TargetSeconds ? 2 : 1;
    }

    /// <summary>星数文案（★★☆）。</summary>
    public static string StarsText(int stars)
    {
        int clamped = Math.Clamp(stars, 0, MaxStars);
        return new string('★', clamped) + new string('☆', MaxStars - clamped);
    }

    /// <summary>
    /// 某关是否已解锁。
    /// 普通关：第 1 关永远开放，其余需要<b>上一关已通关</b>。
    /// 隐藏关：只看 <see cref="LevelProgress.SecretUnlocked"/>，与 18 关的进度无关。
    /// </summary>
    public static bool IsUnlocked(LevelProgress progress, LevelDefinition level)
    {
        ArgumentNullException.ThrowIfNull(progress);
        ArgumentNullException.ThrowIfNull(level);

        if (level.IsSecret)
        {
            return progress.SecretUnlocked;
        }

        if (level.Index <= 0)
        {
            return true;
        }

        LevelDefinition? previous = LevelTable.At(level.Index - 1);
        return previous is not null && progress.IsCleared(previous.Id);
    }

    /// <summary>已解锁的关卡数（= 已通关数 + 1，封顶在总关卡数）。</summary>
    public static int UnlockedCount(LevelProgress progress)
    {
        ArgumentNullException.ThrowIfNull(progress);
        return Math.Min(LevelTable.Count, progress.ClearedCount + 1);
    }

    /// <summary>下一个"已解锁但还没通关"的关卡（用来做"继续挑战"入口）；全通了返回 null。</summary>
    public static LevelDefinition? NextUnclearedLevel(LevelProgress progress)
    {
        ArgumentNullException.ThrowIfNull(progress);

        foreach (LevelDefinition level in LevelTable.All)
        {
            if (IsUnlocked(progress, level) && !progress.IsCleared(level.Id))
            {
                return level;
            }
        }

        return null;
    }

    /// <summary>
    /// 结算一局：更新进度并返回可直接展示的结果。
    /// 只有"通关"才会推进解锁；失败只累加尝试次数。
    /// </summary>
    public static LevelOutcome Settle(LevelProgress progress, LevelDefinition level, bool won, double elapsedSeconds)
    {
        ArgumentNullException.ThrowIfNull(progress);
        ArgumentNullException.ThrowIfNull(level);

        // 隐藏关不在 18 关的链条里，也没有"下一关"
        LevelDefinition? next = level.IsSecret ? null : LevelTable.Next(level);
        bool nextWasUnlocked = next is not null && IsUnlocked(progress, next);

        LevelRecord record = progress.GetOrCreate(level.Id);
        record.Attempts++;

        int stars = 0;
        bool firstClear = false;
        bool isNewBest = false;

        if (won)
        {
            stars = EvaluateStars(level, elapsedSeconds);

            firstClear = !record.Cleared;
            record.Cleared = true;

            if (record.Stars < stars)
            {
                record.Stars = stars;
            }

            if (record.BestSeconds <= 0 || elapsedSeconds < record.BestSeconds)
            {
                record.BestSeconds = elapsedSeconds;
                isNewBest = true;
            }
        }

        bool nextJustUnlocked = next is not null && !nextWasUnlocked && IsUnlocked(progress, next);

        return new LevelOutcome(
            level,
            won,
            stars,
            elapsedSeconds,
            record.BestSeconds,
            isNewBest,
            firstClear,
            nextJustUnlocked ? next : null);
    }
}
