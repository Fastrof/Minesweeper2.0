namespace MineSweeper.Core.Audio;

/// <summary>
/// 游戏内音效。刻意只有 6 个——足够覆盖一盘扫雷的全部动作（含"启动/换难度"的提示音），
/// 也不需要任何素材文件。
/// </summary>
public enum SoundEffect
{
    /// <summary>翻开一格（含洪水填充）。</summary>
    Reveal = 0,

    /// <summary>插旗 / 问号 / 取消。</summary>
    Flag = 1,

    /// <summary>chord 批量展开。</summary>
    Chord = 2,

    /// <summary>踩雷。</summary>
    Explosion = 3,

    /// <summary>胜利。</summary>
    Victory = 4,

    /// <summary>
    /// 启动提示音（v0.8）：程序启动、以及切换难度（初级/中级/高级）时播放。
    /// 刻意做成一小段上行旋律，对应原版扫雷"开局那一声"的听感。
    /// </summary>
    Startup = 5,
}
