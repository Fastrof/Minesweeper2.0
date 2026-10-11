using MineSweeper.Core;
using Xunit;

namespace MineSweeper.Core.Tests;

/// <summary>§8 第 3、4 条：洪水填充。</summary>
public sealed class FloodFillTests
{
    /// <summary>
    /// §8-3：展开范围可手算，且不会越过雷列、不会行环绕。
    /// 盘面：5 宽 × 3 高，雷在 x=2 整列（2/7/12），左区 6 格、右区 6 格。
    /// 从左上角点开，只应翻开左区 6 格。
    /// </summary>
    [Fact]
    public void 洪水填充满盘展开不越过雷列且不环绕()
    {
        GameSession session = TestFactory.ReplayCustom(
            5, 3, TestFactory.WallMines, firstClickIndex: 0);
        Board board = session.Board;

        RevealResult result = session.Reveal(0);

        Assert.Equal(RevealOutcome.Revealed, result.Outcome);
        Assert.Equal(6, session.RevealedCount);

        // 左区（x = 0,1）应全部翻开
        foreach (int index in new[] { 0, 1, 5, 6, 10, 11 })
        {
            Assert.True(board.IsRevealed(index), $"左区格 {index} 应已翻开。");
        }

        // 雷列与右区（x ≥ 2）一格都不能翻开
        for (int index = 0; index < board.CellCount; index++)
        {
            if (board.XOf(index) >= 2)
            {
                Assert.False(board.IsRevealed(index), $"{index}（x={board.XOf(index)}）不该被翻开。");
            }
        }

        // 未胜利：12 个安全格只开了 6 个
        Assert.Equal(GameStatus.InProgress, session.Status);
    }

    /// <summary>
    /// 单行棋盘用来验证"行不环绕"：12×1，雷在 0 / 5 / 11。
    /// 从索引 2 点开，只能翻开 1~4 这 4 格，两端都不能越雷、也不能绕到另一头。
    /// </summary>
    [Fact]
    public void 单行棋盘展开不跨过雷格也不环绕()
    {
        GameSession session = TestFactory.ReplayCustom(
            12, 1, new[] { 0, 5, 11 }, firstClickIndex: 2);

        session.Reveal(2);

        Assert.Equal(4, session.RevealedCount);
        Assert.True(session.Board.IsRevealed(1));
        Assert.True(session.Board.IsRevealed(2));
        Assert.True(session.Board.IsRevealed(3));
        Assert.True(session.Board.IsRevealed(4));

        Assert.False(session.Board.IsRevealed(0));
        Assert.False(session.Board.IsRevealed(5));
        Assert.False(session.Board.IsRevealed(11), "不能从索引 0 绕到行尾的索引 11。");
    }

    /// <summary>
    /// §8-4：1000×1000 全空盘必须能展开完且不抛 StackOverflowException。
    /// 这是 §2.3 的回归测试——递归写法在这里会直接杀进程，.NET 里连 try/catch 都救不回来。
    /// 同时验证分帧：一次点击不会同步展开 100 万格。
    /// </summary>
    [Fact]
    public void 百万格空盘展开不栈溢出且分帧推进()
    {
        const int side = 1000;
        GameSession session = TestFactory.Custom(side, side, mines: 0);

        // 这一步如果内部是递归实现，进程会直接退出，测试根本跑不到下一行
        RevealResult first = session.Reveal(0);

        Assert.Equal(RevealOutcome.Revealed, first.Outcome);
        Assert.True(session.HasPendingFlood, "100 万格的展开必须走分帧路径，不能一次同步做完。");

        int guard = 0;
        while (session.HasPendingFlood)
        {
            session.ContinuePendingFlood();
            guard++;

            Assert.True(guard < 100, "分帧推进次数异常，可能是死循环。");
        }

        Assert.Equal(side * side, session.RevealedCount);
        Assert.Equal(GameStatus.Won, session.Status);
        Assert.Equal(0, session.RemainingMines);
    }

    /// <summary>已插旗的格子不会被洪水填充翻开。</summary>
    [Fact]
    public void 插旗的格子不会被展开()
    {
        GameSession session = TestFactory.ReplayCustom(
            5, 3, TestFactory.WallMines, firstClickIndex: 0);

        // 先给左区里的一个安全格插旗，再点开左上角
        Assert.Equal(FlagOutcome.Flagged, session.ToggleFlag(6).Outcome);

        RevealResult result = session.Reveal(0);

        Assert.Equal(RevealOutcome.Revealed, result.Outcome);
        Assert.False(session.Board.IsRevealed(6), "被插旗的安全格不应该被顺带翻开。");

        // 左区 6 格里被旗子挡掉 1 格
        Assert.Equal(5, session.RevealedCount);
    }

    /// <summary>展开操作是分步的：一次只做 maxCells 格，剩余部分留在操作对象里。</summary>
    [Fact]
    public void 展开操作可以按步推进()
    {
        var board = new Board(20, 20, mineCount: 0);
        BoardGenerator.TryPlaceMinesAt(board, 0, Array.Empty<int>(), out _);

        var fill = new FloodFillOperation(board, 0);

        Assert.Equal(5, fill.Step(5));
        Assert.False(fill.IsComplete);
        Assert.Equal(5, fill.RevealedThisOperation);

        fill.StepAll();
        Assert.True(fill.IsComplete);
        Assert.Equal(400, fill.RevealedThisOperation);
    }
}
