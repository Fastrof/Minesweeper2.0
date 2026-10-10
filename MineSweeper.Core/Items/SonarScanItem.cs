namespace MineSweeper.Core.Items;

/// <summary>
/// 声呐扫描（v0.6 新增道具）：选一个 3×3 区域，**只报出里面有几颗雷**，不告诉你位置在哪。
///
/// 与放大镜的区别很关键：
/// <list type="bullet">
/// <item>放大镜是"透视"——把区域标成已获知（信息进棋盘状态，UI 会一直显示）。</item>
/// <item>声呐只**返回一个数字**，不改变棋盘任何状态：不翻开、不插旗、不影响胜负判定，
/// 也不消耗"已知信息"的边界（同一个区域可以重复扫，只是浪费道具）。</item>
/// </list>
/// 复用 <see cref="MagnifierItem.GetAffectedIndexes"/> 裁切 3×3，保证与放大镜的取格规则完全一致。
/// </summary>
public sealed class SonarScanItem : IItem
{
    public const string ItemId = ItemIds.Sonar;

    /// <summary>默认半径：1 = 3×3。</summary>
    public const int DefaultRadius = 1;

    public SonarScanItem(int radius = DefaultRadius)
        => Radius = Math.Clamp(radius, 0, 4);

    public SonarScanItem(ItemConfig? config)
        : this(config?.SonarRadius ?? DefaultRadius)
    {
    }

    public string Id => ItemId;

    public string DisplayName => "声呐扫描";

    /// <summary>扫描半径：1 = 3×3。</summary>
    public int Radius { get; }

    public ItemUseCheck Check(GameSession session, int targetIndex)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (!session.Settings.ItemsAvailable || session.Inventory is null)
        {
            return ItemUseCheck.ItemsNotAvailable;
        }

        if (session.Status is GameStatus.Won or GameStatus.Lost)
        {
            return ItemUseCheck.GameFinished;
        }

        if (!session.Board.MinesPlaced)
        {
            return ItemUseCheck.BeforeFirstClick;
        }

        if (session.Inventory.CountOf(Id) <= 0)
        {
            return ItemUseCheck.NotInInventory;
        }

        if (!IsAreaInBounds(session.Board, targetIndex, Radius))
        {
            return ItemUseCheck.OutOfBounds;
        }

        return ItemUseCheck.Ok;
    }

    /// <summary>统计以 <paramref name="centerIndex"/> 为中心的区域里有几颗雷。</summary>
    public int CountMines(GameSession session, int centerIndex)
    {
        ArgumentNullException.ThrowIfNull(session);

        Board board = session.Board;
        int mines = 0;

        foreach (int index in MagnifierItem.GetAffectedIndexes(board, centerIndex, Radius))
        {
            if (board.IsMine(index))
            {
                mines++;
            }
        }

        return mines;
    }

    /// <summary>区域是否完整落在棋盘内（与放大镜同样要求"不裁切"，避免玩家算错范围）。</summary>
    internal static bool IsAreaInBounds(Board board, int centerIndex, int radius)
    {
        if (!board.IsValidIndex(centerIndex))
        {
            return false;
        }

        int x = board.XOf(centerIndex);
        int y = board.YOf(centerIndex);

        return x - radius >= 0 && y - radius >= 0 &&
               x + radius < board.Width && y + radius < board.Height;
    }
}