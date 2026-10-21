using MineSweeper.Core;
using MineSweeper.Core.Items;
using Xunit;

namespace MineSweeper.Core.Tests;

/// <summary>
/// 文档里那些"可选/备选"功能的测试：锁定盘面（§2.6）、透视迷雾（§5.4 备选）、
/// 信息面板文本（§5.6）。
/// </summary>
public sealed class AdvancedFeatureTests
{
    // ------------------------------------------------------------ §2.6 锁定盘面

    /// <summary>锁定盘面的核心承诺：同一个 seed 必定是同一张盘（与玩家点哪里无关）。</summary>
    [Fact]
    public void 锁定盘面同种子必定同盘()
    {
        GameSession first = TestFactory.Locked(30, 16, 99, seed: 777);
        GameSession second = TestFactory.Locked(30, 16, 99, seed: 777);

        Assert.Equal(first.Board.GetMineIndexes(), second.Board.GetMineIndexes());
        Assert.Equal(first.Board.FirstClickIndex, second.Board.FirstClickIndex);

        // 雷位在创建时就已经按 seed 定好，但**开局不替玩家翻开**：整盘未翻开、计时为 0。
        Assert.True(first.Board.MinesPlaced);
        Assert.True(first.Locked);
        Assert.Equal(GameStatus.NotStarted, first.Status);
        Assert.Equal(0, first.ElapsedSeconds);
        for (int i = 0; i < first.Board.CellCount; i++)
        {
            Assert.False(first.Board.IsRevealed(i), $"第 {i} 格不该在开局就被翻开。");
        }
    }

    /// <summary>
    /// 玩家报了"关卡一开始就自己走了一步"，这一条守两件事：
    /// ① 开局整盘未翻开、状态是"未开始"；② 玩家随便点哪一格，第一次翻开都被钉到
    /// 开局算好的那格安全点上——锁定盘面的"首点安全"承诺照样成立，而且盘面不变。
    /// </summary>
    [Fact]
    public void 锁定盘面开局不自动翻开且首次点击必定安全()
    {
        const int width = 30;
        const int height = 16;
        const int mines = 99;
        const int seed = 424242;

        var clock = new ManualClock();
        GameSession session = TestFactory.Locked(width, height, mines, seed, clock: clock);
        Board board = session.Board;
        int safe = board.FirstClickIndex;

        // 开局：已布雷、但一格都没翻开，也没开始计时
        Assert.True(board.MinesPlaced);
        Assert.Equal(GameStatus.NotStarted, session.Status);
        Assert.Equal(0, session.ElapsedSeconds);
        Assert.Equal(0, session.RevealedCount);

        // 安全点本身必须是安全的，且一定不是角落外那种越界值
        Assert.True(board.IsValidIndex(safe));
        Assert.False(board.IsMine(safe));
        Assert.Equal(0, board.GetAdjacentMines(safe));

        // 玩家第一下点了**一颗雷**：首点必须保证安全，所以这一下会被挪到开局算好的安全点。
        // （v1.1 起这里刻意改用雷格：点到**安全格**会照玩家点的位置开，不再挪动 ——
        //   那一条由 LockedFirstClickTests.点到安全格就在那一格展开 覆盖。）
        int clicked = board.GetMineIndexes()[0];
        Assert.NotEqual(clicked, safe);

        RevealResult reveal = session.Reveal(clicked);

        // ① 返回的是"真正被翻开的那一格"（离点击位置最近的 0 格），所以调用方能看出这次首点被挪动了
        Assert.Equal(RevealOutcome.Revealed, reveal.Outcome);
        Assert.NotEqual(clicked, reveal.Index);
        Assert.Equal(0, board.GetAdjacentMines(reveal.Index));

        // ② 真正展开的是那格 0 格，玩家点到的那颗雷并没有被翻开
        Assert.True(board.IsRevealed(reveal.Index));
        Assert.False(board.IsMine(reveal.Index));
        Assert.False(board.IsRevealed(clicked), "首点被挪到安全区后，玩家点的那颗雷不该被翻开。");

        // ③ 是"首点展开了一片"的正常开局，而不是踩雷
        Assert.Equal(GameStatus.InProgress, session.Status);
        Assert.True(session.RevealedCount > 1);
        Assert.True(session.ElapsedSeconds >= 0);

        // ④ 雷数一个不多一个不少
        Assert.Equal(mines, board.GetMineIndexes().Length);
    }

    /// <summary>第二次点击起就完全按玩家点的那一格算，不再有首点钉定。</summary>
    [Fact]
    public void 锁定盘面第二次点击不再被钉到安全点()
    {
        GameSession session = TestFactory.Locked(9, 9, 10, seed: 555);
        Board board = session.Board;

        session.Reveal(0);

        int target = -1;
        for (int i = 0; i < board.CellCount; i++)
        {
            if (!board.IsRevealed(i) && !board.IsMine(i))
            {
                target = i;
                break;
            }
        }

        Assert.True(target >= 0, "应当还剩至少一个未翻开的安全格。");

        RevealResult reveal = session.Reveal(target);
        Assert.Equal(target, reveal.Index);
        Assert.True(board.IsRevealed(target));
    }

    /// <summary>不同 seed 应当得到不同的盘（概率上几乎必然）。</summary>
    [Fact]
    public void 锁定盘面不同种子得到不同盘()
    {
        GameSession a = Locked(30, 16, 99, seed: 1);
        GameSession b = Locked(30, 16, 99, seed: 2);

        Assert.NotEqual(a.Board.GetMineIndexes(), b.Board.GetMineIndexes());
    }

    /// <summary>锁定盘面的首点必须安全：它自己与 8 邻格都不能是雷（§0 第 2 条）。</summary>
    [Theory]
    [InlineData(9, 9, 10)]
    [InlineData(30, 16, 99)]
    [InlineData(16, 16, 40)]
    public void 锁定盘面首点安全(int width, int height, int mines)
    {
        GameSession session = Locked(width, height, mines, seed: 20261009);
        Board board = session.Board;
        int first = board.FirstClickIndex;

        Assert.True(board.IsValidIndex(first));
        Assert.False(board.IsMine(first));
        Assert.Equal(0, board.GetAdjacentMines(first));

        foreach (int neighbor in board.Neighbors8(first))
        {
            Assert.False(board.IsMine(neighbor), $"首点邻格 {neighbor} 不能是雷。");
        }

        Assert.Equal(mines, board.GetMineIndexes().Length);
    }

    /// <summary>
    /// 雷极密时全盘可能找不到安全首点，这时要能把安全区内的雷搬出去——
    /// 搬完雷数不变、首点仍然安全。5×5 / 16 雷就是这种盘（M 已经是上限 N−9）。
    /// </summary>
    [Fact]
    public void 雷很密时也能搬出安全首点()
    {
        const int width = 5;
        const int height = 5;
        const int mines = width * height - GameLimits.SafeZoneCellCount;

        GameSession session = Locked(width, height, mines, seed: 12345);
        Board board = session.Board;
        int first = board.FirstClickIndex;

        Assert.Equal(mines, board.GetMineIndexes().Length);
        Assert.Equal(0, board.GetAdjacentMines(first));
        foreach (int neighbor in board.Neighbors8(first))
        {
            Assert.False(board.IsMine(neighbor));
        }
    }

    /// <summary>锁定盘面的分享串要能精确复现（第 6 节 locked 标志必须起作用）。</summary>
    [Fact]
    public void 锁定盘面分享串可精确复现()
    {
        GameSession original = Locked(30, 16, 99, seed: 4242);
        string code = original.CaptureSnapshot().ToShareCode();

        // 分享串带上了锁定标志
        Assert.True(BoardSnapshot.TryParseShareCode(code, out _, out _, out _, out _, out _, out _, out bool locked));
        Assert.True(locked);

        Assert.True(GameSession.TryReplayShareCode(
            code, GameMode.Custom, out GameSession? replay, out string? error, TestFactory.Inventory()), error);

        Assert.Equal(original.Board.GetMineIndexes(), replay!.Board.GetMineIndexes());
        Assert.Equal(original.Board.FirstClickIndex, replay.Board.FirstClickIndex);
        Assert.True(replay.Locked);

        // 复现出来就该是"分享方那张开局图"：首点已经翻开、对局已开始。
        // （曾经漏过这一步：锁定分支不翻首点，复现出来是一张全未翻开的盘，和普通分享串不一致。）
        // 注意别拿 `original` 直接比：v0.5 起锁定盘面**开局不再自动翻开**，
        // 所以 `original` 是"未开局"的盘（0 格已翻开），要比的是"它翻开首点之后"的样子。
        var expected = Locked(30, 16, 99, seed: 4242);
        expected.Reveal(expected.Board.FirstClickIndex);

        Assert.Equal(GameStatus.InProgress, replay.Status);
        Assert.True(replay.Board.IsRevealed(replay.Board.FirstClickIndex));
        Assert.Equal(expected.RevealedCount, replay.RevealedCount);
    }

    /// <summary>旧格式（5 节）分享串仍然能解析，locked 视为 false——不能破坏既有分享。</summary>
    [Fact]
    public void 旧格式分享串仍可解析且视为非锁定()
    {
        const string legacy = "9x9:10:123456:40:1";

        Assert.True(BoardSnapshot.TryParseShareCode(legacy, out int width, out int height, out int mines, out int seed, out int first, out bool extended, out bool locked));
        Assert.Equal(9, width);
        Assert.Equal(9, height);
        Assert.Equal(10, mines);
        Assert.Equal(123456, seed);
        Assert.Equal(40, first);
        Assert.True(extended);
        Assert.False(locked);
    }

    // ------------------------------------------------------------ §5.4 透视迷雾（备选）

    /// <summary>默认（fadeSeconds = 0）信息永久保留：这是 §5.4 硬性约束 3 的取值。</summary>
    [Fact]
    public void 透视信息默认永久保留()
    {
        var clock = new ManualClock();
        ItemInventory inventory = InventoryWithFade(0);
        GameSession session = TestFactory.Custom(9, 9, 10, inventory, clock: clock);
        session.Reveal(40);

        Assert.True(session.UseMagnifier(0).Success);
        Assert.True(session.Board.IsItemRevealed(0));

        clock.AdvanceSeconds(600);
        Assert.False(session.ExpireItemReveals());
        Assert.True(session.Board.IsItemRevealed(0));
    }

    /// <summary>配置成"几秒后恢复迷雾"时，到期必须真的把透视标记清掉并报出改动范围。</summary>
    [Fact]
    public void 透视迷雾到期后恢复()
    {
        var clock = new ManualClock();
        ItemInventory inventory = InventoryWithFade(2);
        GameSession session = TestFactory.Custom(9, 9, 10, inventory, clock: clock);
        session.Reveal(40);

        Assert.True(session.UseMagnifier(0).Success);
        Assert.True(session.Board.IsItemRevealed(0));
        session.ConsumeLastAffectedRange(out _, out _);

        // 还没到期
        clock.AdvanceSeconds(1);
        Assert.False(session.ExpireItemReveals());
        Assert.True(session.Board.IsItemRevealed(0));

        // 到期
        clock.AdvanceSeconds(1.5);
        Assert.True(session.ExpireItemReveals());
        Assert.False(session.Board.IsItemRevealed(0));

        // 报出的改动范围覆盖了被清掉的那一格（UI 据此重绘）
        Assert.True(session.ConsumeLastAffectedRange(out int first, out int last));
        Assert.InRange(0, first, last);
        Assert.False(session.HasExpiringItemReveals);
    }

    /// <summary>迷雾清掉之后，"该区域已透视"的拒绝判定也要跟着失效（否则用不了第二次）。</summary>
    [Fact]
    public void 迷雾清掉后可以再次透视同一区域()
    {
        var clock = new ManualClock();
        ItemInventory inventory = InventoryWithFade(2);
        inventory.TryGrantMagnifier();
        GameSession session = TestFactory.Custom(9, 9, 10, inventory, clock: clock);
        session.Reveal(40);

        Assert.True(session.UseMagnifier(0).Success);
        Assert.Equal(ItemUseCheck.AllAlreadyRevealed, session.CheckMagnifier(0));

        clock.AdvanceSeconds(3);
        Assert.True(session.ExpireItemReveals());

        Assert.Equal(ItemUseCheck.Ok, session.CheckMagnifier(0));
    }

    // ------------------------------------------------------------ §5.6 信息面板

    [Fact]
    public void 信息面板文本列出坐标与内容()
    {
        ItemInventory inventory = InventoryWithFade(0);
        inventory.TryGrantMagnifier();
        GameSession session = TestFactory.Custom(9, 9, 10, inventory);
        session.Reveal(40);

        // v1.4：放大镜是 2×2、点在左上角。9×9 上索引 40 是 (4,4) = 第 5 行第 5 列
        string center = session.PreviewMagnifier(40).ToPanelText(session.Board);
        Assert.Contains("第5行 第5列", center);
        Assert.Contains("覆盖 4 格", center);

        // 左上角同样覆盖 4 格（2×2 正好铺满）
        string corner = session.PreviewMagnifier(0).ToPanelText(session.Board);
        Assert.Contains("覆盖 4 格", corner);

        // 每一格都要有"雷"或"空/数字"的描述
        foreach (string line in corner.Split(Environment.NewLine).Skip(1))
        {
            Assert.True(line.Contains("雷") || line.Contains("空") || line.Contains("数字"), line);
        }
    }

    // ------------------------------------------------------------ 辅助

    private static GameSession Locked(int width, int height, int mines, int seed)
        => TestFactory.Locked(width, height, mines, seed);

    private static ItemInventory InventoryWithFade(int fadeSeconds)
    {
        ItemConfig config = TestFactory.Config(threshold: 999, maxHold: 3, initialGift: 1);
        config.MagnifierRevealFadeSeconds = fadeSeconds;
        return new ItemInventory(PlayerProgress.CreateDefault(), config);
    }
}
