namespace MineSweeper.Core;

/// <summary>
/// 单格位标志（§2.1）。以 byte 存储，与 <see cref="Cell"/> 一起构成 2 字节/格的开销。
/// </summary>
[Flags]
public enum CellFlags : byte
{
    /// <summary>无标记。</summary>
    None = 0,

    /// <summary>该格是雷（布雷前所有格均无此位）。</summary>
    IsMine = 1 << 0,

    /// <summary>已被玩家翻开。</summary>
    IsRevealed = 1 << 1,

    /// <summary>已插旗。</summary>
    IsFlagged = 1 << 2,

    /// <summary>已标记问号。</summary>
    IsQuestion = 1 << 3,

    /// <summary>被道具透视过（放大镜，§5.4）。注意：透视 ≠ 翻开。</summary>
    ItemRevealed = 1 << 4,

    /// <summary>该雷已计入道具获取计数（去重用，§5.2）。</summary>
    FlagCounted = 1 << 5,
}
