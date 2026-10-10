namespace MineSweeper.Core;

/// <summary>单局状态机取值（§2.5）。</summary>
public enum GameStatus
{
    /// <summary>已开局但尚未进行任何操作（棋盘未布雷）。</summary>
    NotStarted = 0,

    /// <summary>进行中。</summary>
    InProgress = 1,

    /// <summary>胜利：已翻开格数 == 总线 − 雷数。</summary>
    Won = 2,

    /// <summary>失败：翻到了雷。</summary>
    Lost = 3,
}

/// <summary>经典三级难度（§0 第 7 条）。高级为 30 宽 × 16 高。</summary>
public sealed record DifficultyPreset(string Id, string DisplayName, int Width, int Height, int MineCount)
{
    /// <summary>初级 9×9 / 10 雷。</summary>
    public static readonly DifficultyPreset Beginner = new("beginner", "初级", 9, 9, 10);

    /// <summary>中级 16×16 / 40 雷。</summary>
    public static readonly DifficultyPreset Intermediate = new("intermediate", "中级", 16, 16, 40);

    /// <summary>高级 30×16 / 99 雷。</summary>
    public static readonly DifficultyPreset Expert = new("expert", "高级", 30, 16, 99);

    /// <summary>全部预设。</summary>
    public static readonly IReadOnlyList<DifficultyPreset> All = new[] { Beginner, Intermediate, Expert };

    /// <summary>按 Id 查找预设，找不到返回 null。</summary>
    public static DifficultyPreset? FindById(string? id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return null;
        }

        foreach (DifficultyPreset preset in All)
        {
            if (string.Equals(preset.Id, id, StringComparison.OrdinalIgnoreCase))
            {
                return preset;
            }
        }

        return null;
    }
}
