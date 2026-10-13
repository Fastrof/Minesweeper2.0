using MineSweeper.Core.Achievements;
using MineSweeper.Core.Persistence;
using Xunit;

namespace MineSweeper.Core.Tests;

/// <summary>
/// 成就系统（成就表 + 判定规则 + 进度存档）。
///
/// 成就表那几个用例是<b>守门人</b>：31 条成就是手写常量，Id 打错、点数配不平、
/// 隐藏成就多写一条剧透文案，只有这些用例能挡住。
/// 判定类的用例全部走 <see cref="AchievementRules.Evaluate"/>（纯函数，不碰存档、不需要棋盘），
/// 所以门槛的"正好等于"与"差一点"这两种边界可以被精确钉住。
/// </summary>
public sealed class AchievementTests
{
    /// <summary>判定用的固定时刻（解锁时间写回要能断言，所以不能取当前时间）。</summary>
    private static readonly DateTimeOffset Now = new(2026, 2, 1, 12, 0, 0, TimeSpan.Zero);

    // ------------------------------------------------------------ 构造辅助

    private static AchievementStats Stats(
        long minesSwept = 0,
        int wins = 0,
        int games = 0,
        int clearedLevels = 0,
        int totalStars = 0,
        double? bestBeginner = null,
        double? bestIntermediate = null,
        double? bestExpert = null)
        => new(minesSwept, wins, games, clearedLevels, totalStars, bestBeginner, bestIntermediate, bestExpert);

    /// <summary>默认是"初级盘面的普通一局"，各项都能单独覆盖，用例才读得懂。</summary>
    private static AchievementFacts Facts(
        bool won = false,
        double elapsed = 0d,
        int width = 9,
        int height = 9,
        int mines = 10,
        int revealed = 0,
        int wrongFlags = 0,
        bool usedAnyItem = false,
        int maxChord = 0,
        string? levelId = null,
        int levelStars = 0,
        bool magnifier = false,
        bool sonar = false,
        bool freeze = false,
        bool standard = true,
        AchievementStats? stats = null)
        => new(
            won, elapsed, width, height, mines, revealed, wrongFlags, usedAnyItem, maxChord,
            levelId, levelStars, magnifier, sonar, freeze, standard, stats ?? Stats());

    /// <summary>高级（30×16 / 99 雷）标准盘面的一局。</summary>
    private static AchievementFacts ExpertFacts(
        bool won = true,
        double elapsed = 200d,
        int wrongFlags = 0,
        bool usedAnyItem = false,
        bool standard = true,
        AchievementStats? stats = null)
        => Facts(
            won: won,
            elapsed: elapsed,
            width: 30,
            height: 16,
            mines: 99,
            wrongFlags: wrongFlags,
            usedAnyItem: usedAnyItem,
            standard: standard,
            stats: stats);

    private static IReadOnlyList<AchievementUnlock> Evaluate(
        AchievementFacts facts, AchievementProgress progress, DateTimeOffset? now = null)
        => AchievementRules.Evaluate(facts, progress, now ?? Now);

    private static bool Unlocked(IReadOnlyList<AchievementUnlock> unlocks, string id)
    {
        foreach (AchievementUnlock unlock in unlocks)
        {
            if (string.Equals(unlock.Definition.Id, id, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    // ------------------------------------------------------------ 成就表

    [Fact]
    public void 成就数量在二十八到三十二之间()
    {
        Assert.InRange(AchievementCatalog.Count, 28, 32);
        Assert.Equal(AchievementCatalog.All.Count, AchievementCatalog.Count);
        Assert.Equal(AchievementIds.All.Count, AchievementCatalog.Count);
    }

    [Fact]
    public void 成就Id唯一且与常量表顺序一致()
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < AchievementCatalog.Count; i++)
        {
            AchievementDefinition definition = AchievementCatalog.All[i];

            Assert.True(ids.Add(definition.Id), $"成就 Id 重复：{definition.Id}");
            Assert.False(string.IsNullOrWhiteSpace(definition.Id));
            Assert.Equal(definition.Id, definition.Id.Trim());

            // 常量表的顺序就是成就面板的顺序，两张表必须一一对应
            Assert.Equal(AchievementIds.All[i], definition.Id);
        }
    }

    [Fact]
    public void 每条成就的目标值为正且点数与文案合法()
    {
        foreach (AchievementDefinition definition in AchievementCatalog.All)
        {
            Assert.True(definition.Target > 0, $"{definition.Id} 的目标值必须为正。");
            Assert.True(definition.Points > 0, $"{definition.Id} 的点数必须为正。");
            Assert.False(string.IsNullOrWhiteSpace(definition.Title), $"{definition.Id} 缺少标题。");
            Assert.False(string.IsNullOrWhiteSpace(definition.Description), $"{definition.Id} 缺少说明。");
        }
    }

    [Fact]
    public void 成就点合计正好一千()
    {
        int sum = 0;
        foreach (AchievementDefinition definition in AchievementCatalog.All)
        {
            sum += definition.Points;
        }

        Assert.Equal(1000, sum);
        Assert.Equal(1000, AchievementCatalog.TotalPoints);
    }

    /// <summary>五个分类都得有成就，否则成就面板会出现空分组。</summary>
    [Fact]
    public void 每个分类都至少有一条成就()
    {
        foreach (AchievementCategory category in Enum.GetValues<AchievementCategory>())
        {
            int count = 0;
            foreach (AchievementDefinition definition in AchievementCatalog.All)
            {
                if (definition.Category == category)
                {
                    count++;
                }
            }

            Assert.True(count > 0, $"{category} 分类下一条成就都没有。");
        }
    }

    [Fact]
    public void 隐藏成就恰好两个且文案不剧透()
    {
        var hidden = new List<AchievementDefinition>();
        foreach (AchievementDefinition definition in AchievementCatalog.All)
        {
            if (definition.Hidden)
            {
                hidden.Add(definition);
            }
        }

        Assert.Equal(2, hidden.Count);

        // 守住项目的彩蛋红线（HANDOFF.md：彩蛋"没有提示、没有成就、没有存档记录"）：
        // v1.1 的两个隐藏成就与彩蛋无关，用普通条件判定 —— 这里继续拦"文案里出现彩蛋线索"，
        // 同时要求隐藏成就必须有正的目标值与点数，否则界面上会多出一条永远解不开、也显示不出进度的死项。
        string[] forbidden = { "彩蛋", "触发", "点击", "输入", "按", "秘密", "坐标", "关于", "头像", "版本号" };
        foreach (AchievementDefinition definition in hidden)
        {
            Assert.True(definition.Target > 0, $"{definition.Id} 的目标值必须为正");
            Assert.True(definition.Points > 0, $"{definition.Id} 的点数必须为正");

            foreach (string word in forbidden)
            {
                Assert.DoesNotContain(word, definition.Title);
                Assert.DoesNotContain(word, definition.Description);
            }
        }
    }

    [Fact]
    public void 按Id查找成就时大小写不敏感()
    {
        Assert.Equal(AchievementIds.Win1, AchievementCatalog.Find("WIN1")!.Id);
        Assert.Equal(AchievementIds.Win1, AchievementCatalog.Find("  win1  ")!.Id);
        Assert.Null(AchievementCatalog.Find("没有这个成就"));
        Assert.Null(AchievementCatalog.Find(null));
        Assert.Null(AchievementCatalog.Find("   "));
    }

    // ------------------------------------------------------------ 累计进度

    [Fact]
    public void 首胜解锁第一滴血()
    {
        var progress = AchievementProgress.CreateDefault();

        IReadOnlyList<AchievementUnlock> unlocks = Evaluate(Facts(won: true, elapsed: 42), progress);

        Assert.True(Unlocked(unlocks, AchievementIds.Win1));
        Assert.False(Unlocked(unlocks, AchievementIds.Wins10));
        Assert.Equal(1L, progress.ProgressOf(AchievementIds.Win1));
    }

    [Fact]
    public void 累计胜场达标时解锁十胜()
    {
        var progress = AchievementProgress.CreateDefault();

        IReadOnlyList<AchievementUnlock> unlocks = Evaluate(Facts(stats: Stats(wins: 10)), progress);

        Assert.True(Unlocked(unlocks, AchievementIds.Wins10));
        Assert.False(Unlocked(unlocks, AchievementIds.Wins50));
        Assert.Equal(10L, progress.ProgressOf(AchievementIds.Wins50));
    }

    [Fact]
    public void 累计扫雷达标时解锁千雷斩()
    {
        var progress = AchievementProgress.CreateDefault();

        IReadOnlyList<AchievementUnlock> unlocks = Evaluate(Facts(stats: Stats(minesSwept: 1000)), progress);

        Assert.True(Unlocked(unlocks, AchievementIds.Mines100));
        Assert.True(Unlocked(unlocks, AchievementIds.Mines1000));
        Assert.False(Unlocked(unlocks, AchievementIds.Mines10000));
    }

    [Fact]
    public void 累计对局一百局时解锁常驻雷场()
    {
        var progress = AchievementProgress.CreateDefault();

        Assert.False(Unlocked(Evaluate(Facts(stats: Stats(games: 99)), progress), AchievementIds.Games100));

        // 进度是"当前总量"，所以下一局跨过门槛就解锁（不需要凑够一局一格的增量）
        Assert.True(Unlocked(Evaluate(Facts(stats: Stats(games: 100)), progress), AchievementIds.Games100));
    }

    // ------------------------------------------------------------ 速度（含边界）

    [Fact]
    public void 初级正好十秒解锁闪电初心()
    {
        var progress = AchievementProgress.CreateDefault();

        IReadOnlyList<AchievementUnlock> unlocks = Evaluate(Facts(won: true, elapsed: 10.0), progress);

        Assert.True(Unlocked(unlocks, AchievementIds.SpeedBeginner10));
        Assert.True(Unlocked(unlocks, AchievementIds.SpeedBeginner30));
    }

    [Fact]
    public void 初级十点零一秒不解锁闪电初心()
    {
        var progress = AchievementProgress.CreateDefault();

        IReadOnlyList<AchievementUnlock> unlocks = Evaluate(Facts(won: true, elapsed: 10.01), progress);

        Assert.False(Unlocked(unlocks, AchievementIds.SpeedBeginner10));
        Assert.True(Unlocked(unlocks, AchievementIds.SpeedBeginner30));
    }

    [Fact]
    public void 中级的速度门槛按中级盘面判定()
    {
        var progress = AchievementProgress.CreateDefault();

        IReadOnlyList<AchievementUnlock> unlocks = Evaluate(
            Facts(won: true, elapsed: 60.0, width: 16, height: 16, mines: 40), progress);

        Assert.True(Unlocked(unlocks, AchievementIds.SpeedIntermediate60));
        Assert.True(Unlocked(unlocks, AchievementIds.SpeedIntermediate120));
        Assert.False(Unlocked(unlocks, AchievementIds.SpeedExpert180));
    }

    [Fact]
    public void 高级正好一百八十秒解锁雷场疾风()
    {
        var progress = AchievementProgress.CreateDefault();

        IReadOnlyList<AchievementUnlock> unlocks = Evaluate(ExpertFacts(elapsed: 180.0), progress);

        Assert.True(Unlocked(unlocks, AchievementIds.SpeedExpert180));
        Assert.True(Unlocked(unlocks, AchievementIds.SpeedExpert300));
    }

    [Fact]
    public void 高级三百秒是最后一档速度成就()
    {
        var progress = AchievementProgress.CreateDefault();

        Assert.False(Unlocked(Evaluate(ExpertFacts(elapsed: 300.01), progress), AchievementIds.SpeedExpert300));
        Assert.True(Unlocked(Evaluate(ExpertFacts(elapsed: 300.0), progress), AchievementIds.SpeedExpert300));
    }

    [Fact]
    public void 未胜利时不判速度类成就()
    {
        var progress = AchievementProgress.CreateDefault();

        // 用时再快也不算：速度成就的前提是"这一局赢了"
        IReadOnlyList<AchievementUnlock> unlocks = Evaluate(Facts(won: false, elapsed: 1.0), progress);

        foreach (AchievementDefinition definition in AchievementCatalog.All)
        {
            if (definition.Category == AchievementCategory.Speed)
            {
                Assert.False(Unlocked(unlocks, definition.Id), $"{definition.Id} 不该在失败局解锁。");
                Assert.Equal(0L, progress.ProgressOf(definition.Id));
            }
        }
    }

    [Fact]
    public void 速度成就只认对应难度的盘面()
    {
        var progress = AchievementProgress.CreateDefault();

        // 中级盘面赢了 5 秒：只能拿中级的速度成就，不能算初级
        IReadOnlyList<AchievementUnlock> unlocks = Evaluate(
            Facts(won: true, elapsed: 5.0, width: 16, height: 16, mines: 40), progress);

        Assert.True(Unlocked(unlocks, AchievementIds.SpeedIntermediate60));
        Assert.False(Unlocked(unlocks, AchievementIds.SpeedBeginner10));
        Assert.False(Unlocked(unlocks, AchievementIds.SpeedExpert180));
    }

    [Fact]
    public void 历史最快成绩达标也能解锁速度成就()
    {
        var progress = AchievementProgress.CreateDefault();

        // v1.1 之前的老玩家存档里早就有更快的成绩，不该逼他们重刷一遍
        IReadOnlyList<AchievementUnlock> unlocks = Evaluate(
            Facts(won: true, elapsed: 40.0, stats: Stats(bestBeginner: 9.5)), progress);

        Assert.True(Unlocked(unlocks, AchievementIds.SpeedBeginner10));
    }

    // ------------------------------------------------------------ 关卡

    [Fact]
    public void 第一次通关关卡解锁踏上征途()
    {
        var progress = AchievementProgress.CreateDefault();

        IReadOnlyList<AchievementUnlock> unlocks = Evaluate(
            Facts(won: true, elapsed: 30, levelId: "1-1", levelStars: 2), progress);

        Assert.True(Unlocked(unlocks, AchievementIds.LevelFirstClear));
        Assert.False(Unlocked(unlocks, AchievementIds.LevelCleared6));
    }

    [Fact]
    public void 关卡没通关时不算通关数()
    {
        var progress = AchievementProgress.CreateDefault();

        // 关卡局但输了：LevelStars 为 0，不该算一次通关
        IReadOnlyList<AchievementUnlock> unlocks = Evaluate(
            Facts(won: false, levelId: "1-1", levelStars: 0), progress);

        Assert.False(Unlocked(unlocks, AchievementIds.LevelFirstClear));
    }

    [Fact]
    public void 累计通关三十六关解锁全关制霸()
    {
        var progress = AchievementProgress.CreateDefault();

        IReadOnlyList<AchievementUnlock> unlocks = Evaluate(Facts(stats: Stats(clearedLevels: 36)), progress);

        Assert.True(Unlocked(unlocks, AchievementIds.LevelCleared6));
        Assert.True(Unlocked(unlocks, AchievementIds.LevelCleared18));
        Assert.True(Unlocked(unlocks, AchievementIds.LevelCleared36));
    }

    [Fact]
    public void 集齐一百零八星解锁满天星辰()
    {
        var progress = AchievementProgress.CreateDefault();

        // 107 星：三十六星到了，满天星辰还差一点
        IReadOnlyList<AchievementUnlock> partial = Evaluate(Facts(stats: Stats(totalStars: 107)), progress);
        Assert.True(Unlocked(partial, AchievementIds.Stars36));
        Assert.False(Unlocked(partial, AchievementIds.Stars108));

        IReadOnlyList<AchievementUnlock> full = Evaluate(Facts(stats: Stats(totalStars: 108)), progress);
        Assert.True(Unlocked(full, AchievementIds.Stars108));
        Assert.Equal(108L, progress.ProgressOf(AchievementIds.Stars108));
    }

    [Fact]
    public void 某一关拿到三星解锁三颗星()
    {
        var progress = AchievementProgress.CreateDefault();

        Assert.False(Unlocked(
            Evaluate(Facts(won: true, levelId: "2-1", levelStars: 2), progress),
            AchievementIds.LevelThreeStars));

        Assert.True(Unlocked(
            Evaluate(Facts(won: true, levelId: "2-1", levelStars: 3), progress),
            AchievementIds.LevelThreeStars));
    }

    // ------------------------------------------------------------ 道具

    [Fact]
    public void 首次使用道具解锁工具登场()
    {
        var progress = AchievementProgress.CreateDefault();

        IReadOnlyList<AchievementUnlock> unlocks = Evaluate(Facts(magnifier: true, usedAnyItem: true), progress);

        Assert.True(Unlocked(unlocks, AchievementIds.ItemFirstUse));
        Assert.True(Unlocked(unlocks, AchievementIds.ItemMagnifier));
        Assert.False(Unlocked(unlocks, AchievementIds.ItemSonar));
        Assert.False(Unlocked(unlocks, AchievementIds.ItemFreeze));
    }

    [Fact]
    public void 三种道具都用过解锁三件齐全且跨局累加()
    {
        var progress = AchievementProgress.CreateDefault();

        // 一局只用一种，靠"单件成就的进度"跨局累加出"三种都用过"
        IReadOnlyList<AchievementUnlock> first = Evaluate(Facts(magnifier: true, usedAnyItem: true), progress);
        Assert.True(Unlocked(first, AchievementIds.ItemMagnifier));
        Assert.False(Unlocked(first, AchievementIds.ItemAllThree));
        Assert.Equal(1L, progress.ProgressOf(AchievementIds.ItemAllThree));

        IReadOnlyList<AchievementUnlock> second = Evaluate(Facts(sonar: true, usedAnyItem: true), progress, Now.AddMinutes(1));
        Assert.True(Unlocked(second, AchievementIds.ItemSonar));
        Assert.False(Unlocked(second, AchievementIds.ItemAllThree));
        Assert.Equal(2L, progress.ProgressOf(AchievementIds.ItemAllThree));

        IReadOnlyList<AchievementUnlock> third = Evaluate(Facts(freeze: true, usedAnyItem: true), progress, Now.AddMinutes(2));
        Assert.True(Unlocked(third, AchievementIds.ItemAllThree));
        Assert.Equal(3L, progress.ProgressOf(AchievementIds.ItemAllThree));
    }

    [Fact]
    public void 不用道具通关高级解锁徒手拆雷()
    {
        var progress = AchievementProgress.CreateDefault();

        IReadOnlyList<AchievementUnlock> unlocks = Evaluate(ExpertFacts(), progress);

        Assert.True(Unlocked(unlocks, AchievementIds.ExpertNoItem));
    }

    [Fact]
    public void 用过道具通关高级不解锁徒手拆雷()
    {
        var progress = AchievementProgress.CreateDefault();

        IReadOnlyList<AchievementUnlock> unlocks = Evaluate(ExpertFacts(usedAnyItem: true), progress);

        Assert.False(Unlocked(unlocks, AchievementIds.ExpertNoItem));
    }

    // ------------------------------------------------------------ 挑战

    [Fact]
    public void 一次chord展开五十格解锁连锁反应()
    {
        var progress = AchievementProgress.CreateDefault();

        Assert.False(Unlocked(Evaluate(Facts(maxChord: 49), progress), AchievementIds.Chord50));
        Assert.True(Unlocked(Evaluate(Facts(maxChord: 50), progress), AchievementIds.Chord50));
    }

    [Fact]
    public void 通关一千格以上的盘面解锁大场面()
    {
        var progress = AchievementProgress.CreateDefault();

        // 640 格（32×20）还不够
        Assert.False(Unlocked(
            Evaluate(Facts(won: true, width: 32, height: 20, mines: 190), progress),
            AchievementIds.Board1000));

        // 正好 1000 格就算
        Assert.True(Unlocked(
            Evaluate(Facts(won: true, width: 40, height: 25, mines: 200), progress),
            AchievementIds.Board1000));
    }

    [Fact]
    public void 大场面要求胜利而不是只开过一张大盘()
    {
        var progress = AchievementProgress.CreateDefault();

        Assert.False(Unlocked(
            Evaluate(Facts(won: false, width: 100, height: 100, mines: 999), progress),
            AchievementIds.Board1000));
    }

    [Fact]
    public void 高级通关且零错旗解锁零失误()
    {
        var progress = AchievementProgress.CreateDefault();

        IReadOnlyList<AchievementUnlock> unlocks = Evaluate(ExpertFacts(wrongFlags: 0), progress);

        Assert.True(Unlocked(unlocks, AchievementIds.ExpertFlawless));
    }

    [Fact]
    public void 插错旗时不解锁零失误()
    {
        var progress = AchievementProgress.CreateDefault();

        Assert.False(Unlocked(Evaluate(ExpertFacts(wrongFlags: 1), progress), AchievementIds.ExpertFlawless));
    }

    [Fact]
    public void 自定义的高级盘面不算高级通关()
    {
        var progress = AchievementProgress.CreateDefault();

        // 尺寸和雷数都像高级，但 IsStandardDifficulty = false（自定义盘）
        IReadOnlyList<AchievementUnlock> unlocks = Evaluate(ExpertFacts(standard: false), progress);

        Assert.False(Unlocked(unlocks, AchievementIds.ExpertNoItem));
        Assert.False(Unlocked(unlocks, AchievementIds.ExpertFlawless));
    }

    // ------------------------------------------------------------ 隐藏成就

    [Fact]
    public void 一次chord展开一百格解锁隐藏成就()
    {
        var progress = AchievementProgress.CreateDefault();

        // 99 格不解锁、100 格解锁：边界两侧各测一次
        Assert.False(Unlocked(Evaluate(Facts(maxChord: 99), progress), AchievementIds.HiddenChord100));
        Assert.True(Unlocked(Evaluate(Facts(maxChord: 100), progress), AchievementIds.HiddenChord100));
    }


    [Fact]
    public void 三十秒内通关中级解锁隐藏成就()
    {
        var progress = AchievementProgress.CreateDefault();

        AchievementFacts slow = Facts(won: true, elapsed: 30.5, width: 16, height: 16, mines: 40);
        Assert.False(Unlocked(Evaluate(slow, progress), AchievementIds.HiddenSpeedIntermediate30));

        AchievementFacts fast = Facts(won: true, elapsed: 30.0, width: 16, height: 16, mines: 40);
        Assert.True(Unlocked(Evaluate(fast, progress), AchievementIds.HiddenSpeedIntermediate30));
    }


    // ------------------------------------------------------------ 解锁语义

    [Fact]
    public void 已解锁的成就不重复返回()
    {
        var progress = AchievementProgress.CreateDefault();
        AchievementFacts facts = Facts(won: true, elapsed: 8.0, stats: Stats(wins: 1));

        IReadOnlyList<AchievementUnlock> first = Evaluate(facts, progress);
        Assert.True(Unlocked(first, AchievementIds.Win1));
        Assert.True(Unlocked(first, AchievementIds.SpeedBeginner10));

        // 同一份事实再判一次：已经解锁的不该再弹一次通知
        IReadOnlyList<AchievementUnlock> second = Evaluate(facts, progress, Now.AddHours(1));
        Assert.Empty(second);
    }

    [Fact]
    public void 进度只增不减()
    {
        var progress = AchievementProgress.CreateDefault();

        Evaluate(Facts(stats: Stats(minesSwept: 900, wins: 5, totalStars: 40)), progress);
        Assert.Equal(900L, progress.ProgressOf(AchievementIds.Mines1000));

        // 新的一局更少、更慢、星星更少：进度与解锁状态都不许回退
        Evaluate(Facts(won: false, elapsed: 999, stats: Stats(minesSwept: 3, wins: 0, totalStars: 0)), progress);

        Assert.Equal(900L, progress.ProgressOf(AchievementIds.Mines1000));
        Assert.Equal(5L, progress.ProgressOf(AchievementIds.Wins10));
        Assert.Equal(40L, progress.ProgressOf(AchievementIds.Stars108));
        Assert.True(progress.IsUnlocked(AchievementIds.Win1));
    }

    [Fact]
    public void 解锁时把解锁时间写回记录()
    {
        var progress = AchievementProgress.CreateDefault();

        Evaluate(Facts(won: true, elapsed: 9.0), progress);

        AchievementRecord record = progress.Find(AchievementIds.SpeedBeginner10)!;
        Assert.True(record.Unlocked);
        Assert.Equal(Now, record.UnlockedAtUtc!.Value);

        // 再判一次（已经解锁）不会把时间刷新成"最近一次评估的时间"
        Evaluate(Facts(won: true, elapsed: 9.0), progress, Now.AddDays(1));
        Assert.Equal(Now, record.UnlockedAtUtc!.Value);
    }

    [Fact]
    public void 解锁结果带回定义与目标值()
    {
        var progress = AchievementProgress.CreateDefault();

        IReadOnlyList<AchievementUnlock> unlocks = Evaluate(Facts(stats: Stats(wins: 10)), progress);

        AchievementUnlock win10 = unlocks.Single(u => u.Definition.Id == AchievementIds.Wins10);
        Assert.Equal(10L, win10.Progress);
        Assert.Equal(10L, win10.Target);
        Assert.Equal(AchievementCategory.Progress, win10.Definition.Category);
    }

    [Fact]
    public void 传入null时不抛异常()
    {
        Assert.Empty(AchievementRules.Evaluate(null!, AchievementProgress.CreateDefault(), Now));
        Assert.Empty(AchievementRules.Evaluate(Facts(won: true), null!, Now));
    }

    // ------------------------------------------------------------ 进度模型

    [Fact]
    public void 新建的进度是空的()
    {
        AchievementProgress progress = AchievementProgress.CreateDefault();

        Assert.Equal(AchievementProgress.CurrentSchemaVersion, progress.SchemaVersion);
        Assert.Empty(progress.Records);
        Assert.Equal(0, progress.UnlockedCount);
        Assert.Equal(0, progress.TotalPoints);
        Assert.False(progress.IsUnlocked(AchievementIds.Win1));
        Assert.Equal(0L, progress.ProgressOf(AchievementIds.Win1));
    }

    [Fact]
    public void 取不存在的记录会建一条且空Id不落盘()
    {
        var progress = AchievementProgress.CreateDefault();

        AchievementRecord created = progress.GetOrCreate(AchievementIds.Chord50);
        Assert.Equal(AchievementIds.Chord50, created.Id);
        Assert.Same(created, progress.GetOrCreate(AchievementIds.Chord50));
        Assert.Single(progress.Records);

        // 空 Id 只给一条游离记录：宁可写不进去，也不让存档里出现无名记录
        AchievementRecord orphan = progress.GetOrCreate("   ");
        Assert.Equal("", orphan.Id);
        Assert.Single(progress.Records);
    }

    [Fact]
    public void 查记录时大小写不敏感()
    {
        var progress = AchievementProgress.CreateDefault();
        progress.GetOrCreate(AchievementIds.Win1).Progress = 1;

        Assert.Equal(1L, progress.ProgressOf("WIN1"));
        Assert.False(progress.IsUnlocked("win1"));
        Assert.Null(progress.Find(null));
        Assert.Null(progress.Find("  "));
    }

    [Fact]
    public void 规范化丢弃空Id()
    {
        var progress = AchievementProgress.CreateDefault();
        progress.Records.Add(new AchievementRecord { Id = "" });
        progress.Records.Add(new AchievementRecord { Id = "   " });
        progress.Records.Add(null!);
        progress.Records.Add(new AchievementRecord { Id = AchievementIds.Win1, Progress = 1, Unlocked = true });

        progress.Normalize();

        Assert.Single(progress.Records);
        Assert.Equal(AchievementIds.Win1, progress.Records[0].Id);
    }

    [Fact]
    public void 规范化合并重复Id并取较大进度()
    {
        var progress = AchievementProgress.CreateDefault();
        progress.Records.Add(new AchievementRecord { Id = "win1", Progress = 0, Unlocked = false });
        progress.Records.Add(new AchievementRecord { Id = "Win1", Progress = 1, Unlocked = true, UnlockedAtUtc = Now.AddDays(2) });
        progress.Records.Add(new AchievementRecord { Id = "WIN1", Progress = 5, Unlocked = false, UnlockedAtUtc = Now.AddDays(3) });

        progress.Normalize();

        Assert.Single(progress.Records);
        AchievementRecord kept = progress.Records[0];

        // Id 统一成成就表里的写法，进度取大、解锁状态取或、解锁时间取最早
        Assert.Equal(AchievementIds.Win1, kept.Id);
        Assert.Equal(5L, kept.Progress);
        Assert.True(kept.Unlocked);
        Assert.Equal(Now.AddDays(2), kept.UnlockedAtUtc!.Value);
    }

    [Fact]
    public void 规范化保留未知Id并排在最后()
    {
        var progress = AchievementProgress.CreateDefault();
        progress.Records.Add(new AchievementRecord { Id = "future.achievement", Progress = 3 });
        progress.Records.Add(new AchievementRecord { Id = AchievementIds.Win1, Progress = 1 });
        progress.Records.Add(new AchievementRecord { Id = AchievementIds.Mines100, Progress = 12 });

        progress.Normalize();

        Assert.Equal(3, progress.Records.Count);
        Assert.Equal(AchievementIds.Win1, progress.Records[0].Id);
        Assert.Equal(AchievementIds.Mines100, progress.Records[1].Id);
        Assert.Equal("future.achievement", progress.Records[2].Id);
    }

    [Fact]
    public void 规范化把非法值修正()
    {
        var progress = AchievementProgress.CreateDefault();
        progress.Records.Add(new AchievementRecord { Id = AchievementIds.Stars108, Progress = -20 });
        progress.Records.Add(new AchievementRecord { Id = AchievementIds.Wins50, Progress = 3, Unlocked = false, UnlockedAtUtc = Now });
        progress.Records.Add(new AchievementRecord { Id = "  " + AchievementIds.Win1 + "  ", Progress = 1, Unlocked = true, UnlockedAtUtc = Now });

        progress.Normalize();

        Assert.Equal(0L, progress.ProgressOf(AchievementIds.Stars108));

        // 没解锁就不该留着解锁时间；带空格的 Id 会被修好
        Assert.Null(progress.Find(AchievementIds.Wins50)!.UnlockedAtUtc);
        Assert.Equal(1L, progress.ProgressOf(AchievementIds.Win1));
        Assert.Equal(3, progress.Records.Count);
    }

    [Fact]
    public void 统计只算成就表里存在的Id()
    {
        var progress = AchievementProgress.CreateDefault();
        progress.GetOrCreate(AchievementIds.Win1).Unlocked = true;
        progress.GetOrCreate(AchievementIds.Mines10000).Unlocked = true;

        AchievementRecord unknown = progress.GetOrCreate("future.achievement");
        unknown.Unlocked = true;
        unknown.Progress = 999;

        // Win1 = 10 分，Mines10000 = 50 分；未知 Id 一分不算
        Assert.Equal(2, progress.UnlockedCount);
        Assert.Equal(60, progress.TotalPoints);
    }

    [Fact]
    public void 迁移把schema版本推到当前值()
    {
        var loaded = new AchievementProgress { SchemaVersion = 0 };

        AchievementProgress migrated = AchievementProgress.Migrate(loaded);

        Assert.Same(loaded, migrated);
        Assert.Equal(AchievementProgress.CurrentSchemaVersion, migrated.SchemaVersion);
    }

    // ------------------------------------------------------------ 存档

    [Fact]
    public void 成就存档往返一致且不留临时文件()
    {
        string path = TestPaths.NewFile("achievements.json");
        var store = new AchievementStore(path);

        AchievementLoadResult created = store.Load();
        Assert.Equal(AchievementLoadStatus.CreatedNew, created.Status);
        Assert.True(File.Exists(path));

        AchievementProgress progress = created.Progress;
        Evaluate(Facts(won: true, elapsed: 8.5, stats: Stats(minesSwept: 250, wins: 3)), progress);
        progress.GetOrCreate("future.achievement").Progress = 7;
        store.Save(progress);

        AchievementLoadResult reloaded = new AchievementStore(path).Load();
        Assert.Equal(AchievementLoadStatus.Loaded, reloaded.Status);
        Assert.False(reloaded.HasWarning);
        Assert.True(reloaded.Progress.IsUnlocked(AchievementIds.Win1));
        Assert.True(reloaded.Progress.IsUnlocked(AchievementIds.SpeedBeginner10));
        Assert.Equal(250L, reloaded.Progress.ProgressOf(AchievementIds.Mines1000));
        Assert.Equal(7L, reloaded.Progress.ProgressOf("future.achievement"));
        Assert.Equal(Now, reloaded.Progress.Find(AchievementIds.SpeedBeginner10)!.UnlockedAtUtc!.Value);
        Assert.Equal(progress.UnlockedCount, reloaded.Progress.UnlockedCount);
        Assert.Equal(progress.TotalPoints, reloaded.Progress.TotalPoints);

        string directory = Path.GetDirectoryName(path)!;
        Assert.Empty(Directory.GetFiles(directory, "*.tmp"));

        // 计算属性不进存档
        string json = File.ReadAllText(path);
        Assert.Contains("records", json);
        Assert.DoesNotContain("UnlockedCount", json);
        Assert.DoesNotContain("TotalPoints", json);
    }

    [Fact]
    public void 成就存档损坏时备份重建()
    {
        string path = TestPaths.NewFile("achievements.json");
        File.WriteAllText(path, "{ 这不是合法 JSON");

        AchievementLoadResult result = new AchievementStore(path).Load();

        Assert.Equal(AchievementLoadStatus.RecoveredFromCorrupt, result.Status);
        Assert.True(result.HasWarning);
        Assert.NotNull(result.BackupPath);
        Assert.StartsWith("achievements.corrupt.", Path.GetFileName(result.BackupPath!), StringComparison.Ordinal);
        Assert.Contains("这不是合法 JSON", File.ReadAllText(result.BackupPath!));
        Assert.Equal(0, result.Progress.UnlockedCount);

        // 重建出来的文件是合法 JSON，下次加载就是正常读取
        Assert.Equal(AchievementLoadStatus.Loaded, new AchievementStore(path).Load().Status);
    }

    [Fact]
    public void 成就存档版本过新时拒读且不覆盖原文件()
    {
        string path = TestPaths.NewFile("achievements.json");
        const string original = """{ "schemaVersion": 99, "records": [] }""";
        File.WriteAllText(path, original);

        AchievementLoadResult result = new AchievementStore(path).Load();

        Assert.Equal(AchievementLoadStatus.RejectedVersionTooNew, result.Status);
        Assert.True(result.HasWarning);
        Assert.NotNull(result.BackupPath);
        Assert.StartsWith("achievements.unsupported.", Path.GetFileName(result.BackupPath!), StringComparison.Ordinal);
        Assert.Equal(0, result.Progress.UnlockedCount);

        // 原文件一个字节都不能被动：用户可能只是临时降级运行
        Assert.Equal(original, File.ReadAllText(path));
    }

    [Fact]
    public void 加载低版本存档时迁移并保留数据()
    {
        string path = TestPaths.NewFile("achievements.json");
        File.WriteAllText(path, """
            {
              "schemaVersion": 0,
              "records": [ { "id": "win1", "unlocked": true, "progress": 1 } ]
            }
            """);

        AchievementLoadResult result = new AchievementStore(path).Load();

        Assert.Equal(AchievementLoadStatus.Loaded, result.Status);
        Assert.Equal(AchievementProgress.CurrentSchemaVersion, result.Progress.SchemaVersion);
        Assert.True(result.Progress.IsUnlocked(AchievementIds.Win1));
        Assert.Equal(10, result.Progress.TotalPoints);
    }

    [Fact]
    public void 加载时规范化手改过的存档()
    {
        string path = TestPaths.NewFile("achievements.json");
        File.WriteAllText(path, """
            {
              "schemaVersion": 1,
              "records": [
                { "id": "win1", "unlocked": false, "progress": 0 },
                { "id": "Win1", "unlocked": true, "progress": 1 },
                { "id": "   ", "unlocked": true, "progress": 3 },
                { "id": "future.achievement", "unlocked": true, "progress": 5 }
              ]
            }
            """);

        AchievementLoadResult result = new AchievementStore(path).Load();

        Assert.Equal(AchievementLoadStatus.Loaded, result.Status);

        // 重复 Id 合并、空 Id 丢弃、未知 Id 保留；统计只算成就表里的
        Assert.Equal(2, result.Progress.Records.Count);
        Assert.Equal(1L, result.Progress.ProgressOf(AchievementIds.Win1));
        Assert.True(result.Progress.IsUnlocked(AchievementIds.Win1));
        Assert.Equal(5L, result.Progress.ProgressOf("future.achievement"));
        Assert.Equal(1, result.Progress.UnlockedCount);
        Assert.Equal(10, result.Progress.TotalPoints);
    }

    [Fact]
    public void 空文件按损坏处理并重建()
    {
        string path = TestPaths.NewFile("achievements.json");
        File.WriteAllText(path, "");

        AchievementLoadResult result = new AchievementStore(path).Load();

        Assert.Equal(AchievementLoadStatus.RecoveredFromCorrupt, result.Status);
        Assert.Equal(0, result.Progress.UnlockedCount);
    }
}
