namespace MineSweeper.Core.Levels;

/// <summary>
/// 内置关卡表（关卡制的"关卡设计"部分）。
///
/// <b>调难度只要改这一张表</b> —— 每关的盘面、雷数、固定种子、星级时间门槛、
/// 可用放大镜数、是否启用首点扩展安全区，全部集中在这里，改完重新编译即可。
///
/// 六章共 36 关（v0.7 从 18 关扩到 36 关）：
/// <list type="bullet">
/// <item>第一章 入门（9×9，10→20 雷）：教基础操作</item>
/// <item>第二章 进阶（16×16，30→55 雷）：中盘推理</item>
/// <item>第三章 高手（30×16，70→120 雷）：标准高级起步</item>
/// <item>第四章 专家（30×16，130→180 雷）：密度上台阶，全部关掉首点扩展</item>
/// <item>第五章 大师（30×18，150→225 雷）：更大的盘面，靠时间目标拉开差距</item>
/// <item>第六章 宗师（32×20，190→290 雷）：本作最长的一批盘面</item>
/// </list>
/// 种子是写死的常量：同一个关卡在任何机器上都是同一张盘，重试、刷星、分享都靠它。
///
/// 注意：关卡的 MagnifierCount 只是关卡设计数据，**不再决定道具是否可用**
/// （v0.6 起道具全局共用，所有关卡都能用；见 LevelDefinition.ItemsAvailable）。
/// </summary>
public static class LevelTable
{
    /// <summary>每章的关卡数。</summary>
    public const int LevelsPerChapter = 6;

    /// <summary>章节数。</summary>
    public const int ChapterCount = 6;

    /// <summary>全部关卡（按顺序，<see cref="LevelDefinition.Index"/> 就是这里的下标）。</summary>
    public static readonly IReadOnlyList<LevelDefinition> All = new LevelDefinition[]
    {
        // ---------------------------------------------------------- 第一章 入门（9×9）
        new("1-1", 1, "入门", 0, "第一次点击", 9, 9, 10, 11099, 90, 50, 1, true),
        new("1-2", 1, "入门", 1, "别急着插旗", 9, 9, 12, 11171, 100, 55, 1, true),
        new("1-3", 1, "入门", 2, "三面楚歌", 9, 9, 14, 11243, 110, 60, 1, true),
        new("1-4", 1, "入门", 3, "靠推理", 9, 9, 16, 11317, 120, 70, 0, true),
        new("1-5", 1, "入门", 4, "雷区渐密", 9, 9, 18, 11393, 140, 80, 0, true),
        new("1-6", 1, "入门", 5, "入门毕业", 9, 9, 20, 11471, 160, 90, 0, true),

        // ---------------------------------------------------------- 第二章 进阶（16×16）
        new("2-1", 2, "进阶", 6, "视野打开", 16, 16, 30, 22079, 240, 140, 2, true),
        new("2-2", 2, "进阶", 7, "一叶知秋", 16, 16, 35, 22153, 260, 150, 2, true),
        new("2-3", 2, "进阶", 8, "步步为营", 16, 16, 40, 22229, 280, 170, 1, true),
        new("2-4", 2, "进阶", 9, "迷雾重重", 16, 16, 45, 22307, 320, 190, 1, true),
        new("2-5", 2, "进阶", 10, "五十个雷", 16, 16, 50, 22391, 360, 210, 1, true),
        new("2-6", 2, "进阶", 11, "进阶毕业", 16, 16, 55, 22469, 420, 240, 0, true),

        // ---------------------------------------------------------- 第三章 高手（30×16）
        // 本章关掉首点扩展安全区（只保留 §0 强制的 3×3），更接近原版手感
        new("3-1", 3, "高手", 12, "老手入场", 30, 16, 70, 33091, 480, 280, 1, false),
        new("3-2", 3, "高手", 13, "八十雷阵", 30, 16, 80, 33179, 540, 320, 1, false),
        new("3-3", 3, "高手", 14, "九十个雷", 30, 16, 90, 33251, 600, 360, 1, false),
        new("3-4", 3, "高手", 15, "标准高级", 30, 16, 99, 33331, 660, 400, 1, false),
        new("3-5", 3, "高手", 16, "超纲密度", 30, 16, 110, 33413, 780, 480, 0, false),
        new("3-6", 3, "高手", 17, "雷海毕业", 30, 16, 120, 33493, 900, 540, 0, false),

        // ---------------------------------------------------------- 第四章 专家（30×16）
        // 也是关掉首点扩展安全区；密度从 27% 一路加到 37.5%
        new("4-1", 4, "专家", 18, "一百三十雷", 30, 16, 130, 44017, 900, 600, 0, false),
        new("4-2", 4, "专家", 19, "一百四十雷", 30, 16, 140, 44111, 960, 640, 0, false),
        new("4-3", 4, "专家", 20, "一百五十雷", 30, 16, 150, 44189, 1020, 680, 0, false),
        new("4-4", 4, "专家", 21, "一百六十雷", 30, 16, 160, 44273, 1080, 720, 0, false),
        new("4-5", 4, "专家", 22, "一百七十雷", 30, 16, 170, 44357, 1140, 760, 0, false),
        new("4-6", 4, "专家", 23, "专家毕业", 30, 16, 180, 44449, 1200, 800, 0, false),

        // ---------------------------------------------------------- 第五章 大师（30×18）
        // 换个更大的盘面：雷数不再一路加，靠盘面变大 + 时间目标收紧来拉难度
        new("5-1", 5, "大师", 24, "换张大图", 30, 18, 150, 55021, 1120, 750, 0, false),
        new("5-2", 5, "大师", 25, "一百六十五雷", 30, 18, 165, 55109, 1200, 800, 0, false),
        new("5-3", 5, "大师", 26, "一百八十雷", 30, 18, 180, 55193, 1280, 850, 0, false),
        new("5-4", 5, "大师", 27, "一百九十五雷", 30, 18, 195, 55277, 1360, 900, 0, false),
        new("5-5", 5, "大师", 28, "两百一十雷", 30, 18, 210, 55361, 1440, 960, 0, false),
        new("5-6", 5, "大师", 29, "大师毕业", 30, 18, 225, 55453, 1520, 1010, 0, false),

        // ---------------------------------------------------------- 第六章 宗师（32×20）
        // 本作最长的一批盘面：640 格，密度 30%→45%
        new("6-1", 6, "宗师", 30, "大场面", 32, 20, 190, 66029, 1560, 1040, 0, false),
        new("6-2", 6, "宗师", 31, "两百一十雷", 32, 20, 210, 66113, 1640, 1090, 0, false),
        new("6-3", 6, "宗师", 32, "两百三十雷", 32, 20, 230, 66197, 1720, 1150, 0, false),
        new("6-4", 6, "宗师", 33, "两百五十雷", 32, 20, 250, 66281, 1800, 1200, 0, false),
        new("6-5", 6, "宗师", 34, "两百七十雷", 32, 20, 270, 66367, 1880, 1250, 0, false),
        new("6-6", 6, "宗师", 35, "宗师毕业", 32, 20, 290, 66457, 1960, 1310, 0, false),
    };

    /// <summary>关卡总数。</summary>
    public static int Count => All.Count;

    // ---------------------------------------------------------------- 隐藏关

    /// <summary>隐藏关的 Id。</summary>
    public const string SecretId = "EX-1";

    /// <summary>隐藏关的盘面尺寸。</summary>
    public const int SecretWidth = 20;

    /// <summary>隐藏关的盘面高度。</summary>
    public const int SecretHeight = 20;

    /// <summary>隐藏关雷数下限（不含）——"至少大于 100"。</summary>
    public const int SecretMinMines = 101;

    /// <summary>隐藏关雷数上限。</summary>
    public const int SecretMaxMines = 150;

    /// <summary>隐藏关所在的分组名（关卡面板用）。</summary>
    public const string SecretChapterName = "隐藏关卡";

    /// <summary>
    /// 生成一个隐藏关。
    ///
    /// 与主线关卡不同，隐藏关**不是固定盘**：
    /// <list type="bullet">
    /// <item><b>雷数随机</b>：每次触发彩蛋都在 <see cref="SecretMinMines"/>..<see cref="SecretMaxMines"/> 之间摇一个
    /// （20×20 = 400 格，所以密度在 25%~37.5%，很难但还有的解）。</item>
    /// <item><b>种子也随机</b>：于是每次触发的盘面完全不同。摇定之后在本次会话里保持不变，
    /// 所以这一局仍然可以原样重试。</item>
    /// <item><b>不给任何道具</b>（<c>MagnifierCount = 0</c> → 纯经典模式）。</item>
    /// </list>
    /// 隐藏关只在本次运行内有效（解锁状态与成绩都不落盘），关掉游戏就消失。
    /// </summary>
    public static LevelDefinition CreateSecret(int? mineCount = null, int? seed = null)
    {
        int mines = Math.Clamp(
            mineCount ?? Random.Shared.Next(SecretMinMines, SecretMaxMines + 1),
            SecretMinMines,
            SecretMaxMines);

        return new LevelDefinition(
            Id: SecretId,
            Chapter: 0,
            ChapterName: SecretChapterName,
            Index: -1,
            Name: "卡布奇诺",
            Width: SecretWidth,
            Height: SecretHeight,
            MineCount: mines,
            Seed: seed ?? GameSession.GenerateSeed(),

            // 时间门槛跟着雷数走：雷越多给得越宽
            TargetSeconds: 60 + (mines * 8),
            ChallengeSeconds: 40 + (mines * 5),
            MagnifierCount: 0,
            FirstClickOpening: false,
            IsSecret: true);
    }

    /// <summary>按全表序号取关卡；越界返回 null。</summary>
    public static LevelDefinition? At(int index)
        => index >= 0 && index < All.Count ? All[index] : null;

    /// <summary>取第一关（关卡模式的入口）。</summary>
    public static LevelDefinition First => All[0];

    /// <summary>取最后一关。</summary>
    public static LevelDefinition Last => All[^1];

    /// <summary>按 Id 查找（大小写不敏感）；找不到返回 null。</summary>
    public static LevelDefinition? FindById(string? id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return null;
        }

        foreach (LevelDefinition level in All)
        {
            if (string.Equals(level.Id, id, StringComparison.OrdinalIgnoreCase))
            {
                return level;
            }
        }

        return null;
    }

    /// <summary>
    /// 取下一关；已是最后一关、或者这根本不是一个主线关卡时返回 null。
    ///
    /// <b>必须显式挡掉隐藏关</b>：它的 <see cref="LevelDefinition.Index"/> 是 -1，
    /// 直接 <c>At(Index + 1)</c> 会算出 <c>At(0)</c> 并返回 <b>1-1</b> ——
    /// 于是"隐藏关的下一关"会莫名其妙变成第一关。
    /// </summary>
    public static LevelDefinition? Next(LevelDefinition level)
    {
        ArgumentNullException.ThrowIfNull(level);

        if (level.IsSecret)
        {
            return null;
        }

        return At(level.Index + 1);
    }

    /// <summary>取某一章的全部关卡（<paramref name="chapter"/> 从 1 起）。</summary>
    public static IReadOnlyList<LevelDefinition> Chapter(int chapter)
    {
        var list = new List<LevelDefinition>(LevelsPerChapter);
        foreach (LevelDefinition level in All)
        {
            if (level.Chapter == chapter)
            {
                list.Add(level);
            }
        }

        return list;
    }
}
