using MineSweeper.Core;
using MineSweeper.Core.Items;
using Xunit;

namespace MineSweeper.Core.Tests;

/// <summary>§8 第 7、8、9 条：道具计数、发放、透视。</summary>
public sealed class ItemTests
{
    // ------------------------------------------------------------ §8-7 计数去重

    /// <summary>§8-7：同一颗雷反复插拔只计 1 次（否则可以靠插拔刷道具）。</summary>
    [Fact]
    public void 同一颗雷反复插拔只计一次()
    {
        ItemInventory inventory = TestFactory.Inventory();
        GameSession session = TestFactory.Custom(16, 16, 40, inventory);

        session.Reveal(0);
        int mine = session.Board.GetMineIndexes()[0];

        FlagResult first = session.ToggleFlag(mine);
        Assert.True(first.CountedAsCorrectFlag);
        Assert.Equal(1, inventory.Progress.CorrectMineFlagsTotal);

        // 旗 → 问号
        Assert.Equal(FlagOutcome.QuestionMarked, session.ToggleFlag(mine).Outcome);
        // 问号 → 空
        Assert.Equal(FlagOutcome.Cleared, session.ToggleFlag(mine).Outcome);
        // 空 → 旗（再插一次，不应重复计数）
        FlagResult again = session.ToggleFlag(mine);

        Assert.Equal(FlagOutcome.Flagged, again.Outcome);
        Assert.False(again.CountedAsCorrectFlag);
        Assert.Equal(1, inventory.Progress.CorrectMineFlagsTotal);
    }

    /// <summary>插错位置（插在安全格上）不计数。</summary>
    [Fact]
    public void 插在安全格上不计数()
    {
        ItemInventory inventory = TestFactory.Inventory();
        GameSession session = TestFactory.Custom(16, 16, 40, inventory);
        session.Reveal(0);

        int safeCell = -1;
        for (int index = 0; index < session.Board.CellCount; index++)
        {
            if (!session.Board.IsMine(index) && !session.Board.IsRevealed(index))
            {
                safeCell = index;
                break;
            }
        }

        Assert.True(safeCell >= 0);
        session.ToggleFlag(safeCell);

        Assert.Equal(0, inventory.Progress.CorrectMineFlagsTotal);
    }

    // ------------------------------------------------------------ §8-8 发放

    /// <summary>
    /// §8-8（v0.6 起改机制）：每扫到 <c>threshold</c> 颗雷**攒下一次"三选一"奖励**，
    /// 不再自动进物品栏；玩家领了才进，且某个道具满了可以换一个、奖励不会被白扣。
    /// </summary>
    [Fact]
    public void 跨阈值攒奖励_领了才进物品栏()
    {
        ItemInventory inventory = TestFactory.Inventory(TestFactory.Config(threshold: 20, maxHold: 2, initialGift: 0));
        GameSession session = TestFactory.Custom(30, 16, 99, inventory, choiceRewards: true);

        session.Reveal(0);
        int[] mines = session.Board.GetMineIndexes();

        var rewardPoints = new List<int>();
        for (int i = 0; i < 80; i++)
        {
            FlagResult result = session.ToggleFlag(mines[i]);
            Assert.True(result.CountedAsCorrectFlag, $"第 {i + 1} 颗雷应该计入正确标记。");

            if (result.MagnifierGranted)
            {
                rewardPoints.Add(i + 1);
            }
        }

        // 20 / 40 / 60 / 80 各攒一次，且**一个都还没进物品栏**
        Assert.Equal(new[] { 20, 40, 60, 80 }, rewardPoints);
        Assert.Equal(4, inventory.PendingRewards);
        Assert.Equal(0, inventory.MagnifierCount);
        Assert.Equal(0, inventory.CorrectFlagsTowardNext);

        // 领两个放大镜（上限 2），第二次刚好满
        Assert.True(session.TryClaimReward(ItemIds.Magnifier, out _));
        Assert.True(session.TryClaimReward(ItemIds.Magnifier, out ItemGrantResult full));
        Assert.True(full.InventoryFull);
        Assert.Equal(2, inventory.MagnifierCount);
        Assert.Equal(2, inventory.PendingRewards);

        // 满了就换一个：换声呐能领，奖励不浪费
        Assert.True(session.TryClaimReward(ItemIds.Sonar, out _));
        Assert.Equal(1, inventory.SonarCount);
        Assert.Equal(1, inventory.PendingRewards);

        // 再领已满的放大镜：不发放、也不扣奖励
        Assert.False(session.TryClaimReward(ItemIds.Magnifier, out ItemGrantResult rejected));
        Assert.True(rejected.InventoryFull);
        Assert.Equal(1, inventory.PendingRewards);
    }

    // ------------------------------------------------------------ v0.6 新道具

    /// <summary>声呐扫描：只报数量，**不改变棋盘任何状态**（不翻开、不插旗、不影响胜负、不进"已获知信息"）。</summary>
    [Fact]
    public void 声呐扫描只报数量不改变棋盘()
    {
        ItemInventory inventory = TestFactory.Inventory(TestFactory.Config(threshold: 9999, maxHold: 3, initialGift: 0));
        inventory.Progress.SonarCount = 2;

        GameSession session = TestFactory.Custom(9, 9, 10, inventory);
        session.Reveal(0);

        Board board = session.Board;
        int center = board.Index(4, 4);
        int expected = MagnifierItem.GetAffectedIndexes(board, center, 1).Count(board.IsMine);

        int revealedBefore = session.RevealedCount;
        int flagsBefore = session.FlagCount;

        SonarScanResult result = session.ScanWithSonar(center);

        Assert.True(result.Success);
        Assert.Equal(expected, result.MineCount);
        Assert.Equal(1, inventory.SonarCount);
        Assert.Equal(revealedBefore, session.RevealedCount);
        Assert.Equal(flagsBefore, session.FlagCount);
        Assert.Equal(GameStatus.InProgress, session.Status);
        Assert.False(board.IsItemRevealed(center));
        Assert.Equal(center, session.LastSonarScan!.Value.CenterIndex);

        // 同一个区域可以重复扫（它不占用"已获知信息"），只是多花一个道具
        Assert.True(session.ScanWithSonar(center).Success);
        Assert.Equal(0, inventory.SonarCount);
        Assert.Equal(ItemUseCheck.NotInInventory, new SonarScanItem().Check(session, center));
    }

    /// <summary>时间冻结：计时停住、道具被消耗、期间不能再冻。</summary>
    [Fact]
    public void 时间冻结让计时停住()
    {
        var config = new ItemConfig
        {
            MagnifierThreshold = 9999,
            MagnifierMaxHold = 3,
            MagnifierInitialGift = 0,
            SonarInitialGift = 0,
            FreezeInitialGift = 0,
            FreezeSeconds = 20,
        };

        ItemInventory inventory = TestFactory.Inventory(config);
        inventory.Progress.FreezeCount = 1;

        GameSession session = TestFactory.Custom(30, 16, 99, inventory);
        session.Reveal(0);

        TimeFreezeResult freeze = session.FreezeTimer();
        Assert.True(freeze.Started);
        Assert.Equal(20d, freeze.FrozenSeconds);
        Assert.True(session.IsTimerFrozen);
        Assert.Equal(0, inventory.FreezeCount);

        double before = session.ElapsedSeconds;
        System.Threading.Thread.Sleep(350);
        double after = session.ElapsedSeconds;
        Assert.True(after - before < 0.15, $"冻结期间计时不该前进：{before:F3}s → {after:F3}s");

        // 冻结中再冻一次：拒绝，且不消耗道具
        inventory.Progress.FreezeCount = 1;
        Assert.Equal(ItemUseCheck.AlreadyFrozen, new TimeFreezeItem(config).Check(session, -1));
        Assert.False(session.FreezeTimer().Started);
        Assert.Equal(1, inventory.FreezeCount);
    }

    /// <summary>冻结到期后计时恢复前进（用 1 秒的短冻结来测）。</summary>
    [Fact]
    public void 冻结到期后计时恢复()
    {
        var config = new ItemConfig
        {
            MagnifierThreshold = 9999,
            MagnifierMaxHold = 3,
            MagnifierInitialGift = 0,
            SonarInitialGift = 0,
            FreezeInitialGift = 0,
            FreezeSeconds = 1,
        };

        ItemInventory inventory = TestFactory.Inventory(config);
        inventory.Progress.FreezeCount = 1;

        GameSession session = TestFactory.Custom(30, 16, 99, inventory);
        session.Reveal(0);

        Assert.True(session.FreezeTimer().Started);
        System.Threading.Thread.Sleep(1200);

        Assert.False(session.IsTimerFrozen);
        double before = session.ElapsedSeconds;
        System.Threading.Thread.Sleep(300);
        double after = session.ElapsedSeconds;
        Assert.True(after - before > 0.15, $"冻结到期后计时应当恢复：{before:F3}s → {after:F3}s");
    }

    /// <summary>进度计数与阈值取模（UI 的 13/20 进度条）。</summary>
    [Fact]
    public void 进度按阈值取模()
    {
        ItemInventory inventory = TestFactory.Inventory(TestFactory.Config(threshold: 20, maxHold: 3));
        GameSession session = TestFactory.Custom(30, 16, 99, inventory);
        session.Reveal(0);

        int[] mines = session.Board.GetMineIndexes();
        for (int i = 0; i < 13; i++)
        {
            session.ToggleFlag(mines[i]);
        }

        Assert.Equal(13, inventory.CorrectFlagsTowardNext);
        Assert.Equal(13, inventory.Progress.CorrectMineFlagsTotal);
        Assert.Equal(0, inventory.MagnifierCount);
    }


    /// <summary>纯经典模式（§5.5 ⑤）：只累计计数，不发放。</summary>
    [Fact]
    public void 纯经典模式不发放道具()
    {
        ItemInventory inventory = TestFactory.Inventory(TestFactory.Config(threshold: 1, maxHold: 3));
        var settings = new GameSettings { Mode = GameMode.ClassicPure };

        GameSession session = TestFactory.Replay(
            5, 3, TestFactory.WallMines, firstClickIndex: 0, settings, inventory);

        session.Reveal(0);
        session.ToggleFlag(2);
        session.ToggleFlag(7);

        Assert.Equal(2, inventory.Progress.CorrectMineFlagsTotal);
        Assert.Equal(0, inventory.MagnifierCount);
    }

    // ------------------------------------------------------------ §8-9 透视

    /// <summary>§8-9(a)：角落只覆盖 4 格、中央 9 格，越界不抛异常。</summary>
    [Fact]
    public void 透视范围按边界裁切()
    {
        ItemInventory inventory = TestFactory.Inventory(TestFactory.Config(threshold: 999, maxHold: 3, initialGift: 0));
        for (int i = 0; i < 3; i++)
        {
            inventory.TryGrantMagnifier();
        }

        GameSession session = TestFactory.Custom(9, 9, 10, inventory);
        session.Reveal(40);

        // 左上角
        MagnifierUseResult corner = session.UseMagnifier(0);
        Assert.True(corner.Success);
        Assert.Equal(4, corner.Reveal!.AffectedCount);

        // 中央
        MagnifierUseResult center = session.UseMagnifier(40);
        Assert.True(center.Success);
        Assert.Equal(9, center.Reveal!.AffectedCount);

        // 右下角
        MagnifierUseResult bottomRight = session.UseMagnifier(80);
        Assert.True(bottomRight.Success);
        Assert.Equal(4, bottomRight.Reveal!.AffectedCount);

        // 越界：不抛异常，只是被拒绝（且不消耗道具）
        MagnifierUseResult outside = session.UseMagnifier(9999);
        Assert.False(outside.Success);
        Assert.Equal(ItemUseCheck.OutOfBounds, outside.Check);
        Assert.Equal(0, inventory.MagnifierCount);
    }

    /// <summary>§8-9(b)：透视不计入"已翻开"，不改变剩余雷数，也不触发胜利。</summary>
    [Fact]
    public void 透视不算翻开且不触发胜利()
    {
        ItemInventory inventory = TestFactory.Inventory(TestFactory.Config(threshold: 999, maxHold: 3, initialGift: 0));
        inventory.TryGrantMagnifier();
        GameSession session = TestFactory.Custom(9, 9, 10, inventory);

        session.Reveal(40);
        int revealedBefore = session.RevealedCount;
        int remainingBefore = session.RemainingMines;

        // 先记录目标区域"使用前"的翻开状态，用完之后必须一一对应
        const int target = 41;
        IReadOnlyList<int> affected = MagnifierItem.GetAffectedIndexes(session.Board, target, 1);
        bool[] revealedBeforeArray = affected.Select(index => session.Board.IsRevealed(index)).ToArray();

        MagnifierUseResult used = session.UseMagnifier(target);

        Assert.True(used.Success);
        Assert.Equal(revealedBefore, session.RevealedCount);
        Assert.Equal(remainingBefore, session.RemainingMines);
        Assert.Equal(GameStatus.InProgress, session.Status);
        Assert.True(session.ItemUsed);

        // 每格都被打上透视标记，但都没被自动插旗
        foreach (MagnifierCellInfo cell in used.Reveal!.Cells)
        {
            Assert.True(session.Board.IsItemRevealed(cell.Index));
            Assert.False(session.Board.IsFlagged(cell.Index), "透视不自动插旗（否则可循环刷计数）。");
        }

        // "已翻开"的集合在使用前后完全一致 —— 透视绝不等于翻开
        for (int i = 0; i < affected.Count; i++)
        {
            Assert.Equal(revealedBeforeArray[i], session.Board.IsRevealed(affected[i]));
        }
    }

    /// <summary>§8-9(c)：首点之前禁止使用（此时棋盘上还没有雷）。</summary>
    [Fact]
    public void 首点之前不能使用放大镜()
    {
        ItemInventory inventory = TestFactory.Inventory(TestFactory.Config(threshold: 999, maxHold: 3, initialGift: 0));
        inventory.TryGrantMagnifier();
        GameSession session = TestFactory.Custom(9, 9, 10, inventory);

        Assert.False(session.Board.MinesPlaced);

        MagnifierUseResult result = session.UseMagnifier(0);

        Assert.False(result.Success);
        Assert.Equal(ItemUseCheck.BeforeFirstClick, result.Check);
        Assert.Equal(1, inventory.MagnifierCount);
    }

    /// <summary>已全部透视过的区域：拒绝使用且不消耗道具（§5.3）。</summary>
    [Fact]
    public void 区域已全部透视时不消耗道具()
    {
        ItemInventory inventory = TestFactory.Inventory(TestFactory.Config(threshold: 999, maxHold: 3, initialGift: 0));
        inventory.TryGrantMagnifier();
        inventory.TryGrantMagnifier();
        GameSession session = TestFactory.Custom(9, 9, 10, inventory);
        session.Reveal(40);

        MagnifierUseResult first = session.UseMagnifier(0);
        Assert.True(first.Success);
        Assert.Equal(1, inventory.MagnifierCount);

        MagnifierUseResult second = session.UseMagnifier(0);

        Assert.False(second.Success);
        Assert.Equal(ItemUseCheck.AllAlreadyRevealed, second.Check);
        Assert.Equal(1, inventory.MagnifierCount);
    }

    /// <summary>没有道具时不能再使用。</summary>
    [Fact]
    public void 物品栏为空时不能使用()
    {
        ItemInventory inventory = TestFactory.Inventory(TestFactory.Config(threshold: 999, maxHold: 3, initialGift: 0));
        GameSession session = TestFactory.Custom(9, 9, 10, inventory);
        session.Reveal(40);

        MagnifierUseResult result = session.UseMagnifier(0);

        Assert.False(result.Success);
        Assert.Equal(ItemUseCheck.NotInInventory, result.Check);
    }

    /// <summary>纯经典模式下道具恒不可用（§5.5 ⑤）。</summary>
    [Fact]
    public void 纯经典模式下道具不可用()
    {
        ItemInventory inventory = TestFactory.Inventory(TestFactory.Config(threshold: 1, maxHold: 3));
        var settings = new GameSettings { Mode = GameMode.ClassicPure };

        GameSession session = TestFactory.Replay(
            5, 3, TestFactory.WallMines, firstClickIndex: 0, settings, inventory);

        session.Reveal(0);

        Assert.Equal(ItemUseCheck.ItemsNotAvailable, session.CheckMagnifier(0));
        Assert.False(session.UseMagnifier(0).Success);
    }

    /// <summary>透视半径来自配置，抽出来是为了以后做"大放大镜"（§5.7）。</summary>
    [Fact]
    public void 透视半径跟随配置()
    {
        ItemInventory inventory = TestFactory.Inventory(TestFactory.Config(threshold: 999, maxHold: 3, initialGift: 0, radius: 2));
        inventory.TryGrantMagnifier();
        GameSession session = TestFactory.Custom(9, 9, 10, inventory);
        session.Reveal(40);

        MagnifierUseResult result = session.UseMagnifier(40);

        Assert.True(result.Success);
        Assert.Equal(2, session.MagnifierRadius);
        Assert.Equal(25, result.Reveal!.AffectedCount);
    }
    /// <summary>
    /// Core 的另一种奖励方式：跨阈值**直接发放大镜**（不弹三选一），且满仓之后不再发。
    /// 正式玩法（普通 + 关卡）v0.6 起都用"三选一"，这条只是锁住 Core 的这条分支。
    /// </summary>
    [Fact]
    public void 奖励设为自动发放时跨阈值直接发大镜()
    {
        ItemInventory inventory = TestFactory.Inventory(TestFactory.Config(threshold: 20, maxHold: 3, initialGift: 0));
        GameSession session = TestFactory.Custom(30, 16, 99, inventory, choiceRewards: false);

        session.Reveal(0);
        int[] mines = session.Board.GetMineIndexes();

        var grantPoints = new List<int>();
        for (int i = 0; i < 80; i++)
        {
            FlagResult result = session.ToggleFlag(mines[i]);
            if (result.MagnifierGranted)
            {
                grantPoints.Add(i + 1);
            }
        }

        Assert.Equal(new[] { 20, 40, 60 }, grantPoints);   // 80 时满仓不再发
        Assert.Equal(3, inventory.MagnifierCount);
        Assert.Equal(0, inventory.PendingRewards);          // 普通模式不攒"三选一"
    }

    /// <summary>v0.6：初次赠送改成**三种道具各 2 个**，且只送一次。</summary>
    [Fact]
    public void 初次赠送三种道具各两个且只送一次()
    {
        var progress = new PlayerProgress();
        var inventory = new ItemInventory(progress, TestFactory.Config(threshold: 20, maxHold: 3, initialGift: 2));

        Assert.True(inventory.EnsureInitialGift());
        Assert.Equal(2, inventory.MagnifierCount);
        Assert.Equal(2, inventory.SonarCount);
        Assert.Equal(2, inventory.FreezeCount);

        Assert.False(inventory.EnsureInitialGift());   // 再调不会重复赠送
        Assert.Equal(2, inventory.MagnifierCount);
    }
}
