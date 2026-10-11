namespace MineSweeper.Core.Levels;

/// <summary>
/// 一个关卡的定义（关卡制的数据模型）。
///
/// 设计要点：扫雷本身没有关卡，所以"关卡"= <b>一张固定的盘 + 一个目标</b>：
/// <list type="bullet">
/// <item><b>固定盘</b>：每关写死一个 <see cref="Seed"/>，配合锁定盘面模式
/// （<c>GameSession.TryCreateLocked</c>）保证"同一关永远是同一张盘"——
/// 这是关卡能重试、能刷星、能分享的前提。</item>
/// <item><b>目标</b>：星级按通关用时评（★ 通关 / ★★ ≤ <see cref="TargetSeconds"/> /
/// ★★★ ≤ <see cref="ChallengeSeconds"/>），不做强制限时——踩雷本来就是失败条件。</item>
/// <item><b>变化性</b>：棋盘尺寸、雷数、可用放大镜数、是否启用首点扩展安全区，都逐关不同。</item>
/// </list>
/// 本类型是纯数据，不依赖任何 UI（Core 层可全量单测）。
/// </summary>
public sealed record LevelDefinition(
    string Id,
    int Chapter,
    string ChapterName,
    int Index,
    string Name,
    int Width,
    int Height,
    int MineCount,
    int Seed,
    int TargetSeconds,
    int ChallengeSeconds,
      // 关卡表里记录的"这关原本设计给几个放大镜"。v0.6 起不再参与分发（道具改为全局共用），
      // 只作为关卡设计数据保留 —— 道具是否可用由 ItemsAvailable 决定，与这个数无关。
    int MagnifierCount,
    bool FirstClickOpening,
    bool IsSecret = false)
{
    /// <summary>章节内序号（1 起），用于显示"2-3"。</summary>
    public int NumberInChapter => (Index % LevelTable.LevelsPerChapter) + 1;

    /// <summary>
    /// 展示用编号，例如 <c>1-3</c>。
    /// 隐藏关不属于任何一章，直接用它的 Id（<c>EX-1</c>）。
    /// </summary>
    public string DisplayId => IsSecret ? Id : $"{Chapter}-{NumberInChapter}";

    /// <summary>展示用全名，例如 <c>1-3 三面楚歌</c>。</summary>
    public string DisplayName => $"{DisplayId} {Name}";

    /// <summary>盘面摘要，例如 <c>16×16 / 40 雷</c>。</summary>
    public string BoardText => $"{Width}×{Height} / {MineCount} 雷";

    /// <summary>星级门槛文案。</summary>
      /// <summary>星级门槛文案（道具全局共用，所以这里只讲时间目标）。</summary>
      public string GoalText
          => $"★★ ≤ {TargetSeconds}s · ★★★ ≤ {ChallengeSeconds}s";

      /// <summary>
      /// 该关是否允许使用道具。
      /// v0.6 起**所有关卡都允许** —— 道具改成全局共用（初次各送 2 个、每扫 20 颗雷补给一次），
      /// 关卡不再单独发放，所以主线关卡恒为 true。**隐藏关是例外**：它刻意不给道具。
      /// </summary>
      public bool ItemsAvailable => !IsSecret;

      /// <summary>本关对应的游戏模式：道具全局可用，所以所有关卡都走"经典 + 道具"。</summary>
      public GameMode Mode => IsSecret ? GameMode.ClassicPure : GameMode.ClassicWithItems;
}
