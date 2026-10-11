namespace MineSweeper.Core;

/// <summary>游戏模式（§5.5 ⑤：道具默认不在纯经典模式启用）。</summary>
public enum GameMode
{
    /// <summary>纯经典模式：无道具、无物品栏，手感与原版一致，可进入最快记录。</summary>
    ClassicPure = 0,

    /// <summary>经典盘面 + 道具系统。</summary>
    ClassicWithItems = 1,

    /// <summary>自定义棋盘（宽/高/雷数自由设定），道具默认开启。</summary>
    Custom = 2,
}

/// <summary>单局设置。字段与文档附录 A 的待拍板项一一对应。</summary>
public sealed record GameSettings
{
    /// <summary>本局模式（§5.5 ⑤）。</summary>
    public GameMode Mode { get; init; } = GameMode.ClassicPure;

    /// <summary>
    /// 首点是否使用扩展安全区（附录 A-2，默认开启）。
    /// 说明：§0 第 2 条已强制"点击格 + 8 邻格"无雷，这本身就保证了首点格的邻雷数为 0，
    /// 也就是"首点必开一片空区"已经成立；本开关额外把安全区扩大到 5×5，
    /// 让首点必定翻开 ≥3×3 的一片区域。棋盘太小或雷太密时自动退回 3×3。
    /// </summary>
    public bool FirstClickOpening { get; init; } = true;

    /// <summary>
    /// 奖励机制：true = 每扫够阈值攒一次"三选一"（关卡模式，v0.6 新增）；
    /// false = 老行为，跨阈值直接发一个放大镜（普通模式，保持原样）。
    /// </summary>
    public bool ChoiceRewards { get; set; }

    /// <summary>窗口失焦时是否暂停计时（附录 A-4，默认不暂停，保持原版行为）。</summary>
    public bool PauseTimerOnFocusLoss { get; init; }

    /// <summary>道具系统在本局是否可用（纯经典模式恒为 false）。</summary>
    public bool ItemsAvailable => Mode != GameMode.ClassicPure;

    /// <summary>纯经典模式（无道具）的默认设置。</summary>
    public static GameSettings ClassicPure => new() { Mode = GameMode.ClassicPure };

    /// <summary>自定义模式的默认设置（道具开启）。</summary>
    public static GameSettings Custom => new() { Mode = GameMode.Custom };
}
