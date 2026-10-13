namespace MineSweeper.Core.Achievements;

/// <summary>
/// 累计统计（App 层从 PlayHistory / LevelProgress / GameRecords 汇总后喂进来）。
///
/// 与 <see cref="AchievementFacts"/> 分开的原因：这一半是**跨局的账**，
/// 每局都要重新汇总（比如读 PlayHistory 的 TotalMinesSwept、LevelProgress 的星数）；
/// 另一半是"这一局发生了什么"。规则层只做纯计算，不去碰存档。
/// </summary>
/// <param name="MinesSwept">累计扫到的雷数（正确插旗）。</param>
/// <param name="Wins">累计胜利局数。</param>
/// <param name="Games">累计对局数。</param>
/// <param name="ClearedLevels">累计通关的关卡数（去重）。</param>
/// <param name="TotalStars">累计星数（满星 = 108）。</param>
/// <param name="BestBeginnerSeconds">初级最快用时；没有记录为 null。</param>
/// <param name="BestIntermediateSeconds">中级最快用时；没有记录为 null。</param>
/// <param name="BestExpertSeconds">高级最快用时；没有记录为 null。</param>
public sealed record AchievementStats(
    long MinesSwept,
    int Wins,
    int Games,
    int ClearedLevels,
    int TotalStars,
    double? BestBeginnerSeconds,
    double? BestIntermediateSeconds,
    double? BestExpertSeconds);

/// <summary>
/// 这一次「发生了什么」——全部是显式数值，纯数据，便于单测。
///
/// 刻意不用 <c>GameSession</c>：规则层要能在没有棋盘、没有时钟的情况下被断言，
/// 而且"这一局"的口径（比如 WrongFlags 怎么数）由 App 层决定，
/// 将来加新玩法也不用改这里的判定代码。
/// </summary>
/// <param name="Won">本局是否胜利。</param>
/// <param name="ElapsedSeconds">本局用时（秒，已扣掉时间冻结）。</param>
/// <param name="Width">盘面宽。</param>
/// <param name="Height">盘面高。</param>
/// <param name="MineCount">雷数。</param>
/// <param name="RevealedCells">本局翻开的格数。</param>
/// <param name="WrongFlags">本局插错的旗数（插在非雷格上、且到结束时仍然插着的旗）。</param>
/// <param name="UsedAnyItem">本局是否用过任意道具。</param>
/// <param name="MaxChordOpened">本局单次 chord 展开的最大格数。</param>
/// <param name="LevelId">关卡 Id；非关卡局为 null。</param>
/// <param name="LevelStars">本局拿到的星数（0 = 没通关）。</param>
/// <param name="UsedMagnifier">是否用过放大镜。</param>
/// <param name="UsedSonar">是否用过声呐扫描。</param>
/// <param name="UsedFreeze">是否用过时间冻结。</param>
/// <param name="IsStandardDifficulty">是否标准三级难度之一（"高级通关"这类成就的判定要用）。</param>
/// <param name="Stats">跨局累计统计。</param>
public sealed record AchievementFacts(
    bool Won,
    double ElapsedSeconds,
    int Width,
    int Height,
    int MineCount,
    int RevealedCells,
    int WrongFlags,
    bool UsedAnyItem,
    int MaxChordOpened,
    string? LevelId,
    int LevelStars,
    bool UsedMagnifier,
    bool UsedSonar,
    bool UsedFreeze,
    bool IsStandardDifficulty,
    AchievementStats Stats);

/// <summary>
/// 本次新解锁的一条成就（定义 + 达成时的进度/目标，界面直接拿去弹通知）。
/// </summary>
public sealed record AchievementUnlock(AchievementDefinition Definition, long Progress, long Target);

/// <summary>
/// 成就判定规则（纯函数，全部可单测）。
///
/// <b>判定口径（写在这里，改规则前先看这一段）：</b>
/// <list type="bullet">
/// <item><b>高级通关</b> = <c>Won &amp;&amp; IsStandardDifficulty &amp;&amp; Width==30 &amp;&amp; Height==16 &amp;&amp; MineCount==99</c>。
/// 要求 <see cref="AchievementFacts.IsStandardDifficulty"/> 是为了把"自定义的 30×16/99 雷"挡在外面——
/// 自定义盘允许放宽到任何尺寸/密度，不挡的话"高级"这两个字就名存实亡了。</item>
/// <item><b>速度类只在 Won 时判定</b>，取 <see cref="AchievementFacts.ElapsedSeconds"/>；
/// 另外允许用 <see cref="AchievementStats"/> 里的历史最快成绩补判（v1.1 之前的老玩家
/// 已经跑出过更快的成绩，不该逼他们重刷一遍）。</item>
/// <item><b>"不用道具通关高级"</b> = 高级通关 &amp;&amp; !UsedAnyItem。</item>
/// <item><b>"无插错旗"</b> = 高级通关 &amp;&amp; WrongFlags==0。</item>
/// <item><b>"大棋盘"</b> = Won &amp;&amp; Width*Height &gt;= 1000。</item>
/// <item><b>累计类</b>读 <see cref="AchievementStats"/>；<b>星数类</b>读 Stats.TotalStars / LevelStars。</item>
    /// <item><b>隐藏成就</b>：标题只显示「???」，判定与文案都不透露任何提示。</item>
/// </list>
/// 所有进度都<b>只增不减</b>：新一局更慢、星星变少、重开一局，都不会把已解锁的成就是否解锁或进度回退。
/// </summary>
public static class AchievementRules
{
    /// <summary>空统计（当调用方递进来的 Stats 是 null 时兜底，绝不抛异常）。</summary>
    private static readonly AchievementStats EmptyStats = new(0, 0, 0, 0, 0, null, null, null);

    /// <summary>
    /// 纯函数：根据事实与已有进度，算出**本次新解锁**的成就，并把进度/解锁时间写回 <paramref name="progress"/>。
    ///
    /// 约定：
    /// <list type="bullet">
    /// <item>已解锁的不重复返回（所以返回值可以直接当"弹通知"的队列）</item>
    /// <item>进度只增不减（例如新一局用时更长，速度类成就的进度不会从 1 退回 0）</item>
    /// <item>解锁时间写回记录（<see cref="AchievementRecord.UnlockedAtUtc"/> 取第一次解锁的时刻）</item>
    /// <item>遍历顺序 = 成就表顺序；<see cref="AchievementIds.ItemAllThree"/> 就靠这个顺序
    /// 读前面三条单件道具成就的进度来累加"用过几种"，所以<b>不要</b>把它的判定挪到前面去</item>
    /// </list>
    /// <paramref name="facts"/> / <paramref name="progress"/> 为 null 时返回空列表，不抛异常。
    /// </summary>
    public static IReadOnlyList<AchievementUnlock> Evaluate(
        AchievementFacts facts, AchievementProgress progress, DateTimeOffset nowUtc)
    {
        if (facts is null || progress is null)
        {
            return Array.Empty<AchievementUnlock>();
        }

        var unlocks = new List<AchievementUnlock>();

        foreach (AchievementDefinition definition in AchievementCatalog.All)
        {
            long value = Math.Clamp(ProgressFor(definition, facts, progress), 0L, definition.Target);

            AchievementRecord record = progress.GetOrCreate(definition.Id);
            if (value > record.Progress)
            {
                record.Progress = value;
            }

            if (!record.Unlocked && record.Progress >= definition.Target)
            {
                record.Unlocked = true;
                record.UnlockedAtUtc ??= nowUtc;
                unlocks.Add(new AchievementUnlock(definition, record.Progress, definition.Target));
                continue;
            }

            // 手改过的存档可能出现"已解锁但没有时间"，补一个，免得界面显示空白
            if (record.Unlocked && record.UnlockedAtUtc is null)
            {
                record.UnlockedAtUtc = nowUtc;
            }
        }

        return unlocks;
    }

    /// <summary>算出某条成就"当前的总进度"（不是本局的增量，所以进度天然只增不减）。</summary>
    private static long ProgressFor(
        AchievementDefinition definition, AchievementFacts facts, AchievementProgress progress)
    {
        AchievementStats? stats = facts.Stats;
        stats ??= EmptyStats;

        bool anyItemUsed = facts.UsedAnyItem || facts.UsedMagnifier || facts.UsedSonar || facts.UsedFreeze;
        bool levelCleared = !string.IsNullOrWhiteSpace(facts.LevelId) && facts.LevelStars > 0;
        bool expertWin = IsExpertWin(facts);

        // 本局这一场"至少算一次"：Stats 由 App 层汇总，汇总时机可能在本局入账之前/之后，
        // 取 max 而不是相加 —— 相加会在"已经含本局"时提前解锁，取 max 最多晚一局。
        long winProgress = Math.Max(stats.Wins, facts.Won ? 1 : 0);
        long levelProgress = Math.Max(stats.ClearedLevels, levelCleared ? 1 : 0);

        return definition.Id switch
        {
            // -------------------------------------------------- 累计
            AchievementIds.Win1 => winProgress,
            AchievementIds.Wins10 => winProgress,
            AchievementIds.Wins50 => winProgress,
            AchievementIds.Mines100 => Math.Max(0L, stats.MinesSwept),
            AchievementIds.Mines1000 => Math.Max(0L, stats.MinesSwept),
            AchievementIds.Mines10000 => Math.Max(0L, stats.MinesSwept),
            AchievementIds.Games100 => Math.Max(0, stats.Games),

            // -------------------------------------------------- 速度（只在胜利局判定）
            AchievementIds.SpeedBeginner10 => Speed(facts, DifficultyPreset.Beginner, stats.BestBeginnerSeconds, 10d),
            AchievementIds.SpeedBeginner30 => Speed(facts, DifficultyPreset.Beginner, stats.BestBeginnerSeconds, 30d),
            AchievementIds.SpeedIntermediate60 => Speed(facts, DifficultyPreset.Intermediate, stats.BestIntermediateSeconds, 60d),
            AchievementIds.SpeedIntermediate120 => Speed(facts, DifficultyPreset.Intermediate, stats.BestIntermediateSeconds, 120d),
            AchievementIds.SpeedExpert180 => Speed(facts, DifficultyPreset.Expert, stats.BestExpertSeconds, 180d),
            AchievementIds.SpeedExpert300 => Speed(facts, DifficultyPreset.Expert, stats.BestExpertSeconds, 300d),

            // -------------------------------------------------- 关卡
            AchievementIds.LevelFirstClear => levelProgress,
            AchievementIds.LevelCleared6 => levelProgress,
            AchievementIds.LevelCleared18 => levelProgress,
            AchievementIds.LevelCleared36 => levelProgress,
            AchievementIds.Stars36 => Math.Max(0, stats.TotalStars),
            AchievementIds.Stars108 => Math.Max(0, stats.TotalStars),
            AchievementIds.LevelThreeStars => facts.LevelStars >= 3 ? 1L : 0L,

            // -------------------------------------------------- 道具
            AchievementIds.ItemFirstUse => anyItemUsed ? 1L : 0L,
            AchievementIds.ItemMagnifier => facts.UsedMagnifier ? 1L : 0L,
            AchievementIds.ItemSonar => facts.UsedSonar ? 1L : 0L,
            AchievementIds.ItemFreeze => facts.UsedFreeze ? 1L : 0L,

            // "三种都用过"是跨局累加：单看这一局最多只有一种，靠前面三条单件成就的进度求和
            AchievementIds.ItemAllThree =>
                (UsedBefore(facts.UsedMagnifier, progress, AchievementIds.ItemMagnifier) ? 1 : 0)
                + (UsedBefore(facts.UsedSonar, progress, AchievementIds.ItemSonar) ? 1 : 0)
                + (UsedBefore(facts.UsedFreeze, progress, AchievementIds.ItemFreeze) ? 1 : 0),

            AchievementIds.ExpertNoItem => expertWin && !anyItemUsed ? 1L : 0L,

            // -------------------------------------------------- 挑战
            // chord 属于"操作成就"，不要求赢：一次漂亮的连锁展开本身就算数
            AchievementIds.Chord50 => facts.MaxChordOpened >= 50 ? 1L : 0L,
            AchievementIds.Board1000 => facts.Won && (long)facts.Width * facts.Height >= 1000 ? 1L : 0L,
            AchievementIds.ExpertFlawless => expertWin && facts.WrongFlags == 0 ? 1L : 0L,

            // -------------------------------------------------- 隐藏（只看数量，不看怎么触发的）
            AchievementIds.HiddenChord100 => Math.Clamp(facts.MaxChordOpened, 0, 100),
            AchievementIds.HiddenSpeedIntermediate30 => Speed(facts, DifficultyPreset.Intermediate, stats.BestIntermediateSeconds, 30d),

            // 不认识的 Id（成就表加过又删掉、或来自更新版本）：进度保持原样
            _ => 0L,
        };
    }

    /// <summary>高级通关的判定口径（见类注释）。</summary>
    private static bool IsExpertWin(AchievementFacts facts)
    {
        DifficultyPreset expert = DifficultyPreset.Expert;
        return facts.Won
            && facts.IsStandardDifficulty
            && facts.Width == expert.Width
            && facts.Height == expert.Height
            && facts.MineCount == expert.MineCount;
    }

    /// <summary>
    /// 速度类判定：本局必须在对应难度的标准盘面上<b>赢了</b>，且用时不超过门槛。
    /// <paramref name="recordedBest"/> 是历史最快成绩（可为 null），达标也算——
    /// 存档里早就有更快成绩的老玩家不用为了成就再刷一遍。
    /// </summary>
    private static long Speed(
        AchievementFacts facts, DifficultyPreset preset, double? recordedBest, double threshold)
    {
        if (!facts.Won
            || facts.Width != preset.Width
            || facts.Height != preset.Height
            || facts.MineCount != preset.MineCount)
        {
            return 0L;
        }

        if (double.IsFinite(facts.ElapsedSeconds) && facts.ElapsedSeconds >= 0 && facts.ElapsedSeconds <= threshold)
        {
            return 1L;
        }

        return recordedBest is { } best && double.IsFinite(best) && best >= 0 && best <= threshold ? 1L : 0L;
    }

    /// <summary>这一局用过的道具，或者之前某一局已经用过（进度写在对应的单件成就上）。</summary>
    private static bool UsedBefore(bool usedNow, AchievementProgress progress, string itemAchievementId)
        => usedNow || progress.ProgressOf(itemAchievementId) > 0;
}
