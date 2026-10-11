using MineSweeper.Core;
using MineSweeper.Core.Items;
using MineSweeper.Core.Persistence;
using Xunit;

namespace MineSweeper.Core.Tests;

/// <summary>
/// 针对已修 bug 的回归测试。每个用例都对应一次真实的缺陷，避免同样的坑再踩回去。
/// </summary>
public sealed class RegressionTests
{
    // ------------------------------------------------------------ BUG-1 重绘范围

    /// <summary>
    /// BUG-1：UI 靠 <see cref="GameSession.ConsumeLastAffectedRange"/> 决定重绘范围。
    /// 这个区间<b>必须覆盖所有新翻开的格</b>，否则屏幕上会只开被点击那一格、
    /// 其余逻辑上已翻开的格子仍画成未翻开——玩家再点它们会被拒绝，看起来就是"点了没反应"。
    /// </summary>
    [Fact]
    public void 翻开后回传的区间覆盖所有新翻开的格()
    {
        GameSession session = TestFactory.ReplayCustom(5, 3, TestFactory.WallMines, firstClickIndex: 0);
        bool[] before = Snapshot(session);

        Assert.Equal(RevealOutcome.Revealed, session.Reveal(0).Outcome);

        Assert.True(session.ConsumeLastAffectedRange(out int first, out int last));
        AssertRangeCoversNewReveals(session, before, first, last);

        // 雷墙盘面：左区 6 格（索引 0、1、5、6、10、11）被翻开，索引区间恰好是 0..11
        Assert.Equal(0, first);
        Assert.Equal(11, last);
    }

    /// <summary>只开一格（数字格）时，回传区间就是那一格，不该顺带整屏重绘。</summary>
    [Fact]
    public void 单格翻开只回传那一格()
    {
        GameSession session = TestFactory.ReplayCustom(5, 3, TestFactory.WallMines, firstClickIndex: 0);

        // 索引 1 紧贴雷墙（邻雷数 > 0），翻开它不会触发扩散
        Assert.True(session.Board.GetAdjacentMines(1) > 0);
        Assert.Equal(RevealOutcome.Revealed, session.Reveal(1).Outcome);
        Assert.Equal(1, session.RevealedCount);

        Assert.True(session.ConsumeLastAffectedRange(out int first, out int last));
        Assert.Equal(1, first);
        Assert.Equal(1, last);
    }

    /// <summary>
    /// 踩雷时不回传区间：失败要把全盘雷位摊开，UI 会据此走整屏重绘分支。
    /// 如果这里回传了"某一格"，界面就会只重绘一格，玩家根本看不到雷在哪。
    /// </summary>
    [Fact]
    public void 踩雷时不回传区间()
    {
        GameSession session = TestFactory.ReplayCustom(5, 3, TestFactory.WallMines, firstClickIndex: 0);

        Assert.Equal(RevealOutcome.Exploded, session.Reveal(2).Outcome);
        Assert.Equal(GameStatus.Lost, session.Status);

        Assert.False(session.ConsumeLastAffectedRange(out _, out _));
    }

    /// <summary>区间是"取走一次就清空"的语义，避免陈旧区间被重复消费。</summary>
    [Fact]
    public void 区间取走后即清空()
    {
        GameSession session = TestFactory.ReplayCustom(5, 3, TestFactory.WallMines, firstClickIndex: 0);
        session.Reveal(0);

        Assert.True(session.ConsumeLastAffectedRange(out _, out _));
        Assert.False(session.ConsumeLastAffectedRange(out _, out _));
    }

    /// <summary>chord 也可能一次翻开好几格，回传区间同样要覆盖住。</summary>
    [Fact]
    public void chord回传的区间覆盖所有新翻开的格()
    {
        // 4×4 单雷盘面（沿用 ChordTests 的手推盘面）：先把三个数字格翻开、给雷插旗，再 chord
        GameSession session = TestFactory.ReplayCustom(4, 4, TestFactory.CornerMine, firstClickIndex: 1);
        session.Reveal(1);
        session.Reveal(4);
        session.Reveal(5);
        session.ToggleFlag(0);
        session.ConsumeLastAffectedRange(out _, out _);

        bool[] before = Snapshot(session);
        ChordResult chord = session.Chord(5);

        Assert.Equal(ChordOutcome.Won, chord.Outcome);
        Assert.Equal(12, chord.OpenedCount);

        Assert.True(session.ConsumeLastAffectedRange(out int first, out int last));
        AssertRangeCoversNewReveals(session, before, first, last);

        // 新翻开的 12 格分布在索引 2..15，区间至少要盖住两端
        Assert.True(first <= 2);
        Assert.True(last >= 15);
    }

    /// <summary>插旗只影响自己那一格。</summary>
    [Fact]
    public void 插旗回传自己那一格()
    {
        GameSession session = TestFactory.ReplayCustom(5, 3, TestFactory.WallMines, firstClickIndex: 0);

        Assert.Equal(FlagOutcome.Flagged, session.ToggleFlag(7).Outcome);
        Assert.True(session.ConsumeLastAffectedRange(out int first, out int last));
        Assert.Equal(7, first);
        Assert.Equal(7, last);
    }

    // ------------------------------------------------------------ BUG-3 布雷邻雷数

    /// <summary>
    /// BUG-3：邻雷数计算是全项目唯一的"每格都跑一遍"的热路径，
    /// 换成零分配的 Span 版本之后，结果必须与逐个比较的朴素算法完全一致。
    /// </summary>
    [Fact]
    public void 大盘邻雷数与朴素算法一致()
    {
        const int width = 120;
        const int height = 80;

        GameSession session = TestFactory.Custom(width, height, mines: 300, seed: 20261009);
        session.Reveal(0);

        Board board = session.Board;
        Assert.Equal(300, board.GetMineIndexes().Length);

        for (int index = 0; index < board.CellCount; index++)
        {
            int expected = 0;
            foreach (int neighbor in board.Neighbors8(index))
            {
                if (board.IsMine(neighbor))
                {
                    expected++;
                }
            }

            Assert.Equal(expected, board.GetAdjacentMines(index));
        }
    }

    // ------------------------------------------------------------ BUG-6 首次赠送

    /// <summary>
    /// BUG-6：纯经典模式是"无道具、无物品栏"（§5.5 ⑤），
    /// 所以它既不该拿到赠送，也不该把"已赠送"标记掉——否则玩家切到带道具的模式时反而拿不到。
    /// </summary>
    [Fact]
    public void 纯经典模式不消耗首次赠送()
    {
        var progress = PlayerProgress.CreateDefault();
        ItemInventory inventory = TestFactory.Inventory(TestFactory.Config(initialGift: 1), progress);

        _ = TestFactory.Replay(
            5, 3, TestFactory.WallMines, firstClickIndex: 0,
            new GameSettings { Mode = GameMode.ClassicPure }, inventory);

        Assert.Equal(0, inventory.MagnifierCount);
        Assert.False(progress.InitialGiftGranted);

        // 切到启用道具的模式时，赠送才发生（且只发生一次）
        _ = TestFactory.Custom(9, 9, 10, inventory);

        Assert.Equal(1, inventory.MagnifierCount);
        Assert.True(progress.InitialGiftGranted);

        _ = TestFactory.Custom(9, 9, 10, inventory);
        Assert.Equal(1, inventory.MagnifierCount);
    }

    // ------------------------------------------------------------ BUG-7 原子写盘

    /// <summary>
    /// BUG-7：记录文件改成原子写盘（先写 .tmp 再替换）。
    /// 既要保证内容能读回来，也不能留下临时文件垃圾。
    /// </summary>
    [Fact]
    public void 记录文件原子写盘且不留临时文件()
    {
        string path = TestPaths.NewFile("records.json");
        var store = new RecordsStore(path);
        store.Reload();

        store.Records.Classic["beginner"] = new BestTimeEntry(
            12.5, 9, 9, 10, ItemUsed: false, AchievedAtUtc: DateTimeOffset.UtcNow.ToString("O"));
        store.Save(store.Records);

        Assert.True(File.Exists(path));
        Assert.False(File.Exists(path + ".tmp"), "原子写盘不应留下 .tmp 文件。");

        // 覆盖写（走 File.Replace 的路径）同样不能留临时文件，内容也要对
        store.Records.Classic["beginner"] = new BestTimeEntry(
            9.5, 9, 9, 10, ItemUsed: false, AchievedAtUtc: DateTimeOffset.UtcNow.ToString("O"));
        store.Save(store.Records);

        Assert.False(File.Exists(path + ".tmp"), "覆盖写之后不应留下 .tmp 文件。");

        GameRecords reloaded = new RecordsStore(path).Reload();
        Assert.Equal(9.5, reloaded.Classic["beginner"].Seconds, 3);
    }

    /// <summary>进度文件与记录文件共用同一套原子写盘实现，行为要一致。</summary>
    [Fact]
    public void 进度文件原子写盘且不留临时文件()
    {
        string path = TestPaths.NewFile("progress.json");
        var store = new ProgressStore(path);

        store.Save(new PlayerProgress { CorrectMineFlagsTotal = 7 });
        store.Save(new PlayerProgress { CorrectMineFlagsTotal = 9 });

        Assert.False(File.Exists(path + ".tmp"), "原子写盘不应留下 .tmp 文件。");
        Assert.Equal(9, store.Load().Progress.CorrectMineFlagsTotal);
    }

    // ------------------------------------------------------------ BUG-8 分享串

    /// <summary>
    /// BUG-8：分享串必须能"粘贴回来复现同一盘面"。
    /// 关键在于复现时要用分享串里的"是否扩展安全区"，而不是本机当前设置。
    /// </summary>
    [Fact]
    public void 分享串可精确复现同一盘面()
    {
        GameSession original = TestFactory.Custom(30, 16, 99, seed: 4242);
        original.Reveal(120);
        string code = original.CaptureSnapshot().ToShareCode();

        // 本机设置故意与分享串相反：复现仍然必须得到同一张盘
        Assert.True(GameSession.TryReplayShareCode(
            code, GameMode.ClassicWithItems, out GameSession? replay, out string? error, TestFactory.Inventory()), error);

        Assert.NotNull(replay);
        Assert.Equal(original.Board.GetMineIndexes(), replay!.Board.GetMineIndexes());
        Assert.Equal(original.Board.FirstClickIndex, replay.Board.FirstClickIndex);
        Assert.Equal(original.Board.UsedExtendedSafeZone, replay.Board.UsedExtendedSafeZone);
        Assert.Equal(original.Seed, replay.Seed);

        // 首点已经替玩家翻开（复现的盘面立刻可玩）
        Assert.True(replay.Board.IsRevealed(120));
    }

    /// <summary>非法分享串要给出可展示的报错，而不是抛异常。</summary>
    [Fact]
    public void 非法分享串被拒绝()
    {
        Assert.False(GameSession.TryReplayShareCode("乱写的字符串", GameMode.Custom, out GameSession? session, out string? error));
        Assert.Null(session);
        Assert.NotNull(error);
    }

    // ------------------------------------------------------------ BUG-4 分帧推进

    /// <summary>
    /// BUG-4：分帧推进改用"每帧块大小 + 时间片"。这里验证按每帧块大小推进也能完整展开，
    /// 不会卡在中途（时间片只是把多次推进压进一帧，不改变可达性）。
    /// </summary>
    [Fact]
    public void 按每帧块大小分帧推进也能完整展开()
    {
        GameSession session = TestFactory.Custom(400, 400, mines: 0);

        Assert.Equal(RevealOutcome.Revealed, session.Reveal(0).Outcome);
        Assert.True(session.HasPendingFlood, "16 万格应当走分帧路径。");

        int guard = 0;
        while (session.HasPendingFlood)
        {
            session.ContinuePendingFlood(GameSession.FloodChunkPerFrame);
            Assert.True(++guard < 1000, "分帧推进次数异常，可能是死循环。");
        }

        Assert.Equal(400 * 400, session.RevealedCount);
        Assert.Equal(GameStatus.Won, session.Status);
    }

    // ------------------------------------------------------------ 辅助

    private static bool[] Snapshot(GameSession session)
    {
        Board board = session.Board;
        var revealed = new bool[board.CellCount];
        for (int i = 0; i < board.CellCount; i++)
        {
            revealed[i] = board.IsRevealed(i);
        }

        return revealed;
    }

    /// <summary>断言回传区间覆盖了所有"从 before 到现在新翻开"的格。</summary>
    private static void AssertRangeCoversNewReveals(GameSession session, bool[] before, int first, int last)
    {
        Assert.True(last >= first, "有格子被翻开时区间不能为空。");

        Board board = session.Board;
        for (int i = 0; i < board.CellCount; i++)
        {
            if (board.IsRevealed(i) && !before[i])
            {
                Assert.InRange(i, first, last);
            }
        }
    }
}
