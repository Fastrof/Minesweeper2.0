namespace MineSweeper.Core.Achievements;

/// <summary>
/// 成就分类（成就面板按这个分组显示，也是"每类都有东西可拿"的设计保证）。
/// </summary>
public enum AchievementCategory
{
    /// <summary>累计进度：胜场、扫雷数、对局数。</summary>
    Progress = 0,

    /// <summary>速度：单局计时门槛。</summary>
    Speed = 1,

    /// <summary>关卡制：通关数、星数。</summary>
    Levels = 2,

    /// <summary>道具：用过什么、以及"不用道具"。</summary>
    Items = 3,

    /// <summary>挑战：chord、大盘面、零失误。</summary>
    Challenge = 4,
}

/// <summary>
/// 一条成就的定义（纯数据，全部写死在 <see cref="AchievementCatalog"/> 里）。
///
/// 定义与进度刻意分开：定义是代码里的常量（改文案/配平点数只要改表、不需要迁移存档），
/// 存档里只存"玩家做到哪一步"（见 <see cref="AchievementRecord"/>）。
/// 这样即使用户的存档来自旧版本、少了一条成就的记录，补一条空记录就能继续用。
/// </summary>
/// <param name="Id">稳定 Id（持久化用，<b>不要改</b>；改名等于把玩家的成就清零）。</param>
/// <param name="Category">分类。</param>
/// <param name="Title">标题（中文）。</param>
/// <param name="Description">一句话说明。</param>
/// <param name="Target">目标值：进度条的上限；<c>1</c> 表示一次性成就。</param>
/// <param name="Points">成就点。</param>
/// <param name="Hidden">隐藏成就：解锁前界面只显示 "???"（<b>不剧透</b>）。</param>
public sealed record AchievementDefinition(
    string Id,
    AchievementCategory Category,
    string Title,
    string Description,
    long Target,
    int Points,
    bool Hidden);

/// <summary>
/// 成就 Id 常量表。
///
/// 之所以不用裸字符串：Id 会写进 achievements.json，拼错一个字母就是"这条成就永远解不开"
/// 而且只有玩家发现成就面板里少了一条时才会暴露。常量 + <see cref="AchievementCatalog"/> 的守门人测试把这层挡住。
/// <see cref="All"/> 的<b>顺序就是成就面板的顺序</b>，也是 <see cref="AchievementCatalog.All"/> 的顺序。
/// </summary>
public static class AchievementIds
{
    // ---------------------------------------------------------------- 累计进度

    /// <summary>完成第一局。</summary>
    public const string Win1 = "win1";

    /// <summary>累计胜场 10。</summary>
    public const string Wins10 = "wins10";

    /// <summary>累计胜场 50。</summary>
    public const string Wins50 = "wins50";

    /// <summary>累计扫雷 100 颗。</summary>
    public const string Mines100 = "mines100";

    /// <summary>累计扫雷 1000 颗。</summary>
    public const string Mines1000 = "mines1000";

    /// <summary>累计扫雷 10000 颗。</summary>
    public const string Mines10000 = "mines10000";

    /// <summary>累计对局 100 局。</summary>
    public const string Games100 = "games100";

    // ---------------------------------------------------------------- 速度

    /// <summary>初级 ≤10 秒。</summary>
    public const string SpeedBeginner10 = "speedBeginner10";

    /// <summary>初级 ≤30 秒。</summary>
    public const string SpeedBeginner30 = "speedBeginner30";

    /// <summary>中级 ≤60 秒。</summary>
    public const string SpeedIntermediate60 = "speedIntermediate60";

    /// <summary>中级 ≤120 秒。</summary>
    public const string SpeedIntermediate120 = "speedIntermediate120";

    /// <summary>高级 ≤180 秒。</summary>
    public const string SpeedExpert180 = "speedExpert180";

    /// <summary>高级 ≤300 秒。</summary>
    public const string SpeedExpert300 = "speedExpert300";

    // ---------------------------------------------------------------- 关卡

    /// <summary>第一次通关关卡。</summary>
    public const string LevelFirstClear = "levelFirstClear";

    /// <summary>累计通关 6 关。</summary>
    public const string LevelCleared6 = "levelCleared6";

    /// <summary>累计通关 18 关。</summary>
    public const string LevelCleared18 = "levelCleared18";

    /// <summary>累计通关 36 关。</summary>
    public const string LevelCleared36 = "levelCleared36";

    /// <summary>累计 36 星。</summary>
    public const string Stars36 = "stars36";

    /// <summary>累计 108 星（全星）。</summary>
    public const string Stars108 = "stars108";

    /// <summary>某一关拿到三星。</summary>
    public const string LevelThreeStars = "levelThreeStars";

    // ---------------------------------------------------------------- 道具

    /// <summary>第一次使用道具。</summary>
    public const string ItemFirstUse = "itemFirstUse";

    /// <summary>用过一次放大镜。</summary>
    public const string ItemMagnifier = "itemMagnifier";

    /// <summary>用过一次声呐扫描。</summary>
    public const string ItemSonar = "itemSonar";

    /// <summary>用过一次时间冻结。</summary>
    public const string ItemFreeze = "itemFreeze";

    /// <summary>三种道具都用过。</summary>
    public const string ItemAllThree = "itemAllThree";

    /// <summary>不用任何道具通关高级。</summary>
    public const string ExpertNoItem = "expertNoItem";

    // ---------------------------------------------------------------- 挑战

    /// <summary>一次 chord 展开 ≥50 格。</summary>
    public const string Chord50 = "chord50";

    /// <summary>通关盘面 ≥1000 格的局。</summary>
    public const string Board1000 = "board1000";

    /// <summary>高级通关且一次旗都没插错。</summary>
    public const string ExpertFlawless = "expertFlawless";

    // ---------------------------------------------------------------- 隐藏

    /// <summary>触发任意 1 个游戏内彩蛋（隐藏成就，文案不剧透）。</summary>
    public const string HiddenChord100 = "hiddenChord100";

    /// <summary>集齐全部 4 个彩蛋（隐藏成就，文案不剧透）。</summary>
    public const string HiddenSpeedIntermediate30 = "hiddenSpeedIntermediate30";

    /// <summary>全部成就 Id（顺序 = 成就面板顺序 = <see cref="AchievementCatalog.All"/> 的顺序）。</summary>
    public static IReadOnlyList<string> All { get; } = new[]
    {
        Win1,
        Wins10,
        Wins50,
        Mines100,
        Mines1000,
        Mines10000,
        Games100,
        SpeedBeginner10,
        SpeedBeginner30,
        SpeedIntermediate60,
        SpeedIntermediate120,
        SpeedExpert180,
        SpeedExpert300,
        LevelFirstClear,
        LevelCleared6,
        LevelCleared18,
        LevelCleared36,
        Stars36,
        Stars108,
        LevelThreeStars,
        ItemFirstUse,
        ItemMagnifier,
        ItemSonar,
        ItemFreeze,
        ItemAllThree,
        ExpertNoItem,
        Chord50,
        Board1000,
        ExpertFlawless,
        HiddenChord100,
        HiddenSpeedIntermediate30,
    };
}
