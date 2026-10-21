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

    // ------------------------------------------------------------ 金币经济（v1.4）

    /// <summary>
    /// v1.4 起"每 20 颗雷送一次三选一"的机制已被金币商店取代：
    /// 扫雷只累计正确标记数与金币，**不再自动发任何道具**。
    /// </summary>
    [Fact]
    public void 扫雷不再自动发道具只加金币()
    {
        ItemInventory inventory = TestFactory.Inventory(TestFactory.Config(threshold: 20, maxHold: 2, initialGift: 0));
        GameSession session = TestFactory.Custom(30, 16, 99, inventory);

        session.Reveal(0);
        int[] mines = session.Board.GetMineIndexes();

        for (int i = 0; i < 80; i++)
        {
            FlagResult result = session.ToggleFlag(mines[i]);
            Assert.True(result.CountedAsCorrectFlag, $"第 {i + 1} 颗雷应该计入正确标记。");
            Assert.False(result.MagnifierGranted, "v1.4 起扫雷不再直接发放大镜。");
        }

        // 80 颗雷 = 80 金币，但一个道具都没进物品栏
        Assert.Equal(80, session.Coins.Coins);
        Assert.Equal(80, inventory.Progress.CorrectMineFlagsTotal);
        Assert.Equal(0, inventory.MagnifierCount);
        Assert.Equal(0, inventory.SonarCount);
        Assert.Equal(0, inventory.FreezeCount);
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

    /// <summary>
    /// v1.4：放大镜是 2×2、**点在左上角**。所以
    /// 左上角 (0,0) 正好铺满 4 格；中央 (4,4) 也是 4 格；
    /// 而右下角（最后一列/最后一行）会被裁切到只剩 1 格 —— 越界不抛异常。
    /// </summary>
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

        // 左上角：2×2 正好铺满
        MagnifierUseResult corner = session.UseMagnifier(0);
        Assert.True(corner.Success);
        Assert.Equal(4, corner.Reveal!.AffectedCount);

        // 中央：同样是 4 格（2×2）
        MagnifierUseResult center = session.UseMagnifier(40);
        Assert.True(center.Success);
        Assert.Equal(4, center.Reveal!.AffectedCount);

        // 右下角（8,8）：向右下扩会被裁掉，只剩它自己 1 格
        MagnifierUseResult bottomRight = session.UseMagnifier(80);
        Assert.True(bottomRight.Success);
        Assert.Equal(1, bottomRight.Reveal!.AffectedCount);

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

    /// <summary>透视范围来自配置（v1.4：默认 2×2，可配）。</summary>
    [Fact]
    public void 透视范围跟随配置()
    {
        ItemInventory inventory = TestFactory.Inventory(TestFactory.Config(threshold: 999, maxHold: 3, initialGift: 0, magnifierWidth: 3, magnifierHeight: 3));
        inventory.TryGrantMagnifier();
        GameSession session = TestFactory.Custom(9, 9, 10, inventory);
        session.Reveal(40);

        MagnifierUseResult result = session.UseMagnifier(40);

        Assert.True(result.Success);
        Assert.Equal(3, result.Reveal!.Width);
        Assert.Equal(3, result.Reveal.Height);
        Assert.Equal(9, result.Reveal.AffectedCount);
    }

    /// <summary>
    /// v1.4：默认放大镜是 **2×2**，且**被点中的格子是左上角** ——
    /// 点哪格就翻"它 + 右 + 下 + 右下"四格。
    /// </summary>
    [Fact]
    public void 默认放大镜是二乘二且点在左上角()
    {
        ItemInventory inventory = TestFactory.Inventory(TestFactory.Config(threshold: 999, maxHold: 3, initialGift: 0));
        inventory.TryGrantMagnifier();
        GameSession session = TestFactory.Custom(9, 9, 10, inventory);
        session.Reveal(40);

        int center = session.Board.Index(4, 4);
        MagnifierUseResult result = session.UseMagnifier(center);

        Assert.True(result.Success);
        Assert.Equal(2, result.Reveal!.Width);
        Assert.Equal(2, result.Reveal.Height);
        Assert.Equal(4, result.Reveal.AffectedCount);

        // 覆盖的正好是 (4,4) (5,4) (4,5) (5,5)
        int[] expected =
        [
            session.Board.Index(4, 4),
            session.Board.Index(5, 4),
            session.Board.Index(4, 5),
            session.Board.Index(5, 5),
        ];
        Assert.Equal(expected, result.Reveal.Cells.Select(c => c.Index));
    }

    /// <summary>
    /// v1.4：每扫出一颗雷 +1 金币（与"正确标记"同一事件），
    /// 误插旗 / 取消旗 / 问号都不给金币。
    /// </summary>
    [Fact]
    public void 扫出一颗雷加一个金币()
    {
        ItemInventory inventory = TestFactory.Inventory(TestFactory.Config(threshold: 20, maxHold: 3, initialGift: 0));
        GameSession session = TestFactory.Custom(30, 16, 99, inventory);

        session.Reveal(0);
        Assert.Equal(0, session.Coins.Coins);

        int[] mines = session.Board.GetMineIndexes();

        // 连插 5 颗正确的雷 → 5 金币
        for (int i = 0; i < 5; i++)
        {
            session.ToggleFlag(mines[i]);
        }

        Assert.Equal(5, session.Coins.Coins);
        Assert.Equal(5, session.Coins.EarnedTotal);

        // 同一颗雷再插一次（旗 → 问号 → 空）不会再给金币
        session.ToggleFlag(mines[0]);
        session.ToggleFlag(mines[0]);
        session.ToggleFlag(mines[0]);
        Assert.Equal(5, session.Coins.Coins);
    }

    /// <summary>v1.4：金币只在正确插旗时发放，插在空格上没有金币。</summary>
    [Fact]
    public void 插错旗不给金币()
    {
        ItemInventory inventory = TestFactory.Inventory(TestFactory.Config(threshold: 20, maxHold: 3, initialGift: 0));
        GameSession session = TestFactory.Custom(9, 9, 10, inventory);
        session.Reveal(40);

        var mineSet = session.Board.GetMineIndexes().ToHashSet();
        int emptyCell = Enumerable.Range(0, 81).First(i => !mineSet.Contains(i) && !session.Board.IsRevealed(i));

        FlagResult result = session.ToggleFlag(emptyCell);

        Assert.False(result.CountedAsCorrectFlag);
        Assert.Equal(0, session.Coins.Coins);
    }

    /// <summary>
    /// v1.4 商店：金币够就能买，买完扣钱、道具进"本局购买"；
    /// 金币不够一分不扣。
    /// </summary>
    [Fact]
    public void 商店购买扣金币并把道具记进本局()
    {
        ItemInventory inventory = TestFactory.Inventory(TestFactory.Config(threshold: 20, maxHold: 3, initialGift: 0));
        GameSession session = TestFactory.Custom(30, 16, 99, inventory);

        session.Reveal(0);
        int[] mines = session.Board.GetMineIndexes();

        // 攒够 30 金币（放大镜售价）
        for (int i = 0; i < 30; i++)
        {
            session.ToggleFlag(mines[i]);
        }

        Assert.Equal(30, session.Coins.Coins);
        Assert.Equal(0, inventory.MagnifierCount);

        ShopPurchaseResult bought = session.TryBuyItem(ItemIds.Magnifier);

        Assert.True(bought.Bought);
        Assert.Equal(30, bought.Price);
        Assert.Equal(0, bought.CoinsAfter);
        Assert.Equal(0, session.Coins.Coins);
        Assert.Equal(1, inventory.MagnifierCount);
        Assert.Equal(1, inventory.PurchasedCountOf(ItemIds.Magnifier));

        // 账号库存不受影响（买的这份只活在本局）
        Assert.Equal(1, inventory.SessionPurchases.MagnifierCount);
    }

    /// <summary>v1.4 商店：金币不够时报"还差多少"，并且不扣钱、不给道具。</summary>
    [Fact]
    public void 金币不足时买不了且不扣钱()
    {
        ItemInventory inventory = TestFactory.Inventory(TestFactory.Config(threshold: 20, maxHold: 3, initialGift: 0));
        GameSession session = TestFactory.Custom(30, 16, 99, inventory);

        session.Reveal(0);
        int[] mines = session.Board.GetMineIndexes();
        for (int i = 0; i < 10; i++)
        {
            session.ToggleFlag(mines[i]);
        }

        Assert.Equal(10, session.Coins.Coins);

        ShopPurchaseResult result = session.TryBuyItem(ItemIds.Sonar);

        Assert.False(result.Bought);
        Assert.Equal(ShopPurchaseOutcome.NotEnoughCoins, result.Outcome);
        Assert.Equal(10, result.Shortfall);      // 声呐 20，差 10
        Assert.Equal(10, session.Coins.Coins);   // 一分没扣
        Assert.Equal(0, inventory.SonarCount);
    }

    /// <summary>v1.4 商店：每种道具的价格按需求分别是 30 / 20 / 20。</summary>
    [Fact]
    public void 商店三种道具价格符合需求()
    {
        ItemInventory inventory = TestFactory.Inventory();
        GameSession session = TestFactory.Custom(9, 9, 10, inventory);

        Assert.Equal(30, session.ShopPriceOf(ItemIds.Magnifier));
        Assert.Equal(20, session.ShopPriceOf(ItemIds.Sonar));
        Assert.Equal(20, session.ShopPriceOf(ItemIds.Freeze));
    }

    /// <summary>v1.4：金币与"本局购买"都在新局 / 新关卡开始时清零。</summary>
    [Fact]
    public void 新开局时金币与本局购买清零()
    {
        ItemInventory inventory = TestFactory.Inventory(TestFactory.Config(threshold: 20, maxHold: 3, initialGift: 0));

        GameSession first = TestFactory.Custom(30, 16, 99, inventory);
        first.Reveal(0);
        int[] mines = first.Board.GetMineIndexes();
        for (int i = 0; i < 30; i++)
        {
            first.ToggleFlag(mines[i]);
        }

        first.TryBuyItem(ItemIds.Magnifier);
        Assert.Equal(1, inventory.PurchasedCountOf(ItemIds.Magnifier));

        // 新的一局 = 新会话 = 新钱包；"本局购买"要显式 ResetSession
        GameSession second = TestFactory.Custom(30, 16, 99, inventory);
        inventory.ResetSession();

        Assert.Equal(0, second.Coins.Coins);
        Assert.Equal(0, second.Coins.EarnedTotal);
        Assert.Equal(0, inventory.PurchasedCountOf(ItemIds.Magnifier));
        Assert.Equal(0, inventory.MagnifierCount);
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

    // ------------------------------------------------------------ v1.4 修复回归

    /// <summary>
    /// 修复"价格双源"：判断能不能买、以及实际扣款，都必须用 config.json 里的价格。
    /// 这里把放大镜配成 15（默认常量是 30），如果钱包还在按常量扣款，断言会抓住它。
    /// </summary>
    [Fact]
    public void 商店按配置价扣款而不是默认常量价()
    {
        ItemConfig config = TestFactory.Config(threshold: 20, maxHold: 9, initialGift: 0);
        config.ShopMagnifierPrice = 15;

        ItemInventory inventory = TestFactory.Inventory(config);
        GameSession session = TestFactory.Custom(30, 16, 99, inventory);

        session.Reveal(0);
        int[] mines = session.Board.GetMineIndexes();
        for (int i = 0; i < 15; i++)
        {
            session.ToggleFlag(mines[i]);
        }

        Assert.Equal(15, session.Coins.Coins);

        ShopPurchaseResult result = session.TryBuyItem(ItemIds.Magnifier);

        Assert.True(result.Bought);
        Assert.Equal(15, result.Price);
        Assert.Equal(0, result.CoinsAfter);
        Assert.Equal(0, session.Coins.Coins);
        Assert.Equal(15, session.Coins.SpentTotal);   // 不是 30
    }

    /// <summary>商店的四类失败分支：满仓 / 未启用道具 / 本局已结束 / 不认识的道具。</summary>
    [Fact]
    public void 商店失败分支都不扣钱()
    {
        // 满仓：账号里已经有上限数量，买进来会溢出
        ItemConfig config = TestFactory.Config(threshold: 20, maxHold: 1, initialGift: 0);
        var progress = new PlayerProgress { MagnifierCount = 1 };
        ItemInventory fullInventory = new(progress, config);
        GameSession fullSession = TestFactory.Custom(30, 16, 99, fullInventory);
        fullSession.Reveal(0);
        int[] mines = fullSession.Board.GetMineIndexes();
        for (int i = 0; i < 30; i++)
        {
            fullSession.ToggleFlag(mines[i]);
        }

        ShopPurchaseResult full = fullSession.TryBuyItem(ItemIds.Magnifier);
        Assert.Equal(ShopPurchaseOutcome.InventoryFull, full.Outcome);
        Assert.Equal(30, fullSession.Coins.Coins);

        // 纯经典模式没有物品栏：商店不卖
        GameSession classic = TestFactory.ClassicPure(9, 9, 10);
        Assert.Equal(ShopPurchaseOutcome.ItemsNotAvailable, classic.TryBuyItem(ItemIds.Sonar).Outcome);

        // 本局已结束：先安全翻开首点，再故意踩一颗雷
        ItemInventory inventory = TestFactory.Inventory(TestFactory.Config(initialGift: 0));
        GameSession finished = TestFactory.Custom(9, 9, 10, inventory);
        finished.Reveal(0);
        finished.Reveal(finished.Board.GetMineIndexes()[0]);
        Assert.Equal(GameStatus.Lost, finished.Status);
        Assert.Equal(ShopPurchaseOutcome.GameFinished, finished.TryBuyItem(ItemIds.Freeze).Outcome);

        // 不认识的道具 Id
        ItemInventory unknownInventory = TestFactory.Inventory(TestFactory.Config(initialGift: 0));
        GameSession unknown = TestFactory.Custom(9, 9, 10, unknownInventory);
        Assert.Equal(ShopPurchaseOutcome.UnknownItem, unknown.TryBuyItem("不存在").Outcome);
    }

    /// <summary>
    /// 三种道具都算"用过道具"：声呐/冻结以前不会置 ItemUsed，
    /// 导致记录里的 itemUsed 只反映放大镜；同时它们各自要有独立的标记（成就判定用）。
    /// </summary>
    [Fact]
    public void 声呐与冻结也算用过道具()
    {
        ItemInventory inventory = TestFactory.Inventory(TestFactory.Config(initialGift: 2));
        GameSession session = TestFactory.Custom(9, 9, 10, inventory);

        session.Reveal(0);

        Assert.False(session.ItemUsed);
        Assert.False(session.UsedSonar);
        Assert.False(session.UsedFreeze);
        Assert.False(session.UsedMagnifier);

        session.ScanWithSonar(40);
        Assert.True(session.ItemUsed);
        Assert.True(session.UsedSonar);
        Assert.False(session.UsedFreeze);

        session.FreezeTimer();
        Assert.True(session.UsedFreeze);

        // 放大镜走独立标记
        int target = Enumerable.Range(0, session.Board.CellCount).First(i => !session.Board.IsRevealed(i));
        session.UseMagnifier(target);
        Assert.True(session.UsedMagnifier);
        Assert.True(session.ItemUsed);
    }

    /// <summary>
    /// 分帧展开期间不接受会改动棋盘的输入：泛洪会跳过已插旗格、放大镜会重置改动区间，
    /// 两者都会让"翻开全部安全格才胜利"的判定卡死或让 UI 局部重绘丢格。
    /// </summary>
    [Fact]
    public void 分帧展开期间不接受插旗与放大镜()
    {
        // 300×300 全空盘一次 Reveal 只能推进 50000 格，必然进入 pending
        ItemInventory inventory = TestFactory.Inventory(TestFactory.Config(initialGift: 1));
        GameSession session = TestFactory.Custom(300, 300, 0, inventory);

        session.Reveal(0);
        Assert.True(session.HasPendingFlood);

        FlagResult flagged = session.ToggleFlag(1);
        Assert.True(flagged.IsBlocked);
        Assert.Contains("展开", flagged.Reason);

        MagnifierUseResult magnifier = session.UseMagnifier(1);
        Assert.False(magnifier.Success);
        Assert.Equal(ItemUseCheck.FloodPending, magnifier.Check);
    }
}
