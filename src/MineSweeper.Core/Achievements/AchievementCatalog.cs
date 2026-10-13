namespace MineSweeper.Core.Achievements;

/// <summary>
/// 内置成就表（成就系统的"设计"部分）。
///
/// <b>改文案、调门槛、配平点数只要改这一张表</b>，不用碰存档格式：
/// 定义是代码常量，存档里只存"玩家做到哪一步"。
///
/// 全表 31 条、共 <b>1000</b> 成就点，六个主题：
/// <list type="bullet">
/// <item>Progress 累计（7 条）：胜场/扫雷数/对局数 —— 只要一直在玩就一定拿得到</item>
/// <item>Speed 速度（6 条）：初/中/高级各两档 —— 单局计时门槛，只在胜利局判定</item>
/// <item>Levels 关卡（7 条）：通关数与星数 —— 与关卡制的进度挂钩</item>
/// <item>Items 道具（6 条）：用过什么，以及"不用道具通关高级"这种反向成就</item>
/// <item>Challenge 挑战（3 条）：chord、大盘面、零失误</item>
/// <item>隐藏（2 条）：文案刻意中性，<b>绝不写触发方式</b>（彩蛋不许剧透，见项目红线）</item>
/// </list>
/// 点数的配平是刻意的：容易的 10~20 分、中等的 30 分、难的 50 分，隐藏成就最贵；
/// 这样"成就点/难度"大致单调，1000 分也正好是个整数上限，便于界面显示百分比。
/// </summary>
public static class AchievementCatalog
{
    /// <summary>全部成就（顺序 = <see cref="AchievementIds.All"/>，也是成就面板的显示顺序）。</summary>
    public static readonly IReadOnlyList<AchievementDefinition> All = new AchievementDefinition[]
    {
        // ---------------------------------------------------------- Progress 累计（200 分）
        new(AchievementIds.Win1, AchievementCategory.Progress, "第一滴血", "完成第一局并取得胜利。", 1, 10, false),
        new(AchievementIds.Wins10, AchievementCategory.Progress, "十连胜场", "累计获胜 10 局。", 10, 20, false),
        new(AchievementIds.Wins50, AchievementCategory.Progress, "身经百战", "累计获胜 50 局。", 50, 50, false),
        new(AchievementIds.Mines100, AchievementCategory.Progress, "扫雷入门", "累计扫出 100 颗雷。", 100, 10, false),
        new(AchievementIds.Mines1000, AchievementCategory.Progress, "千雷斩", "累计扫出 1000 颗雷。", 1000, 30, false),
        new(AchievementIds.Mines10000, AchievementCategory.Progress, "万雷归宗", "累计扫出 10000 颗雷。", 10000, 50, false),
        new(AchievementIds.Games100, AchievementCategory.Progress, "常驻雷场", "累计进行 100 局游戏。", 100, 30, false),

        // ---------------------------------------------------------- Speed 速度（210 分）
        new(AchievementIds.SpeedBeginner10, AchievementCategory.Speed, "闪电初心", "10 秒内通关初级。", 1, 50, false),
        new(AchievementIds.SpeedBeginner30, AchievementCategory.Speed, "手速初现", "30 秒内通关初级。", 1, 20, false),
        new(AchievementIds.SpeedIntermediate60, AchievementCategory.Speed, "一眼扫尽", "60 秒内通关中级。", 1, 50, false),
        new(AchievementIds.SpeedIntermediate120, AchievementCategory.Speed, "心中有数", "120 秒内通关中级。", 1, 20, false),
        new(AchievementIds.SpeedExpert180, AchievementCategory.Speed, "雷场疾风", "180 秒内通关高级。", 1, 50, false),
        new(AchievementIds.SpeedExpert300, AchievementCategory.Speed, "稳如老狗", "300 秒内通关高级。", 1, 20, false),

        // ---------------------------------------------------------- Levels 关卡（210 分）
        new(AchievementIds.LevelFirstClear, AchievementCategory.Levels, "踏上征途", "第一次通关任意关卡。", 1, 10, false),
        new(AchievementIds.LevelCleared6, AchievementCategory.Levels, "第一章毕业", "累计通关 6 个关卡。", 6, 20, false),
        new(AchievementIds.LevelCleared18, AchievementCategory.Levels, "半程英雄", "累计通关 18 个关卡。", 18, 30, false),
        new(AchievementIds.LevelCleared36, AchievementCategory.Levels, "全关制霸", "累计通关全部 36 个关卡。", 36, 50, false),
        new(AchievementIds.Stars36, AchievementCategory.Levels, "三十六星", "累计获得 36 颗星。", 36, 30, false),
        new(AchievementIds.Stars108, AchievementCategory.Levels, "满天星辰", "集齐全部 108 颗星。", 108, 50, false),
        new(AchievementIds.LevelThreeStars, AchievementCategory.Levels, "三颗星", "在任意一关拿到三星评价。", 1, 20, false),

        // ---------------------------------------------------------- Items 道具（120 分）
        new(AchievementIds.ItemFirstUse, AchievementCategory.Items, "工具登场", "第一次使用道具。", 1, 10, false),
        new(AchievementIds.ItemMagnifier, AchievementCategory.Items, "透过迷雾", "使用一次放大镜。", 1, 10, false),
        new(AchievementIds.ItemSonar, AchievementCategory.Items, "听声辨雷", "使用一次声呐扫描。", 1, 10, false),
        new(AchievementIds.ItemFreeze, AchievementCategory.Items, "时间暂停", "使用一次时间冻结。", 1, 10, false),
        new(AchievementIds.ItemAllThree, AchievementCategory.Items, "三件齐全", "三种道具都至少用过一次。", 3, 30, false),
        new(AchievementIds.ExpertNoItem, AchievementCategory.Items, "徒手拆雷", "不使用任何道具通关高级。", 1, 50, false),

        // ---------------------------------------------------------- Challenge 挑战（110 分）
        new(AchievementIds.Chord50, AchievementCategory.Challenge, "连锁反应", "一次 chord 展开至少 50 格。", 1, 30, false),
        new(AchievementIds.Board1000, AchievementCategory.Challenge, "大场面", "通关一个不少于 1000 格的盘面。", 1, 30, false),
        new(AchievementIds.ExpertFlawless, AchievementCategory.Challenge, "零失误", "高级通关且一次旗都没插错。", 1, 50, false),

        // ---------------------------------------------------------- 隐藏（150 分，文案不许剧透）
        new(AchievementIds.HiddenChord100, AchievementCategory.Challenge, "一网打尽", "在一次 chord 里展开 100 格以上。", 100, 50, true),
        new(AchievementIds.HiddenSpeedIntermediate30, AchievementCategory.Challenge, "闪电手", "30 秒内通关中级。", 1, 100, true),
    };

    /// <summary>成就总数。</summary>
    public static int Count => All.Count;

    /// <summary>全部成就点之和（设计值正好 1000）。</summary>
    public static int TotalPoints
    {
        get
        {
            int total = 0;
            foreach (AchievementDefinition definition in All)
            {
                total += definition.Points;
            }

            return total;
        }
    }

    /// <summary>按 Id 查找（大小写不敏感、自动去首尾空格）；找不到或 Id 为空返回 null。</summary>
    public static AchievementDefinition? Find(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        string trimmed = id.Trim();
        foreach (AchievementDefinition definition in All)
        {
            if (string.Equals(definition.Id, trimmed, StringComparison.OrdinalIgnoreCase))
            {
                return definition;
            }
        }

        return null;
    }
}
