using Xunit;

namespace MineSweeper.Core.Tests;

/// <summary>
/// 锁定盘面"第一下"的行为（v1.1 修复的玩家 bug）。
///
/// <para>
/// 原来的实现把首次翻开**无条件**钉到开局算好的那格安全点，于是玩家点到安全格也会在别处展开 ——
/// 玩家看到的就是"我点这里，却在那边开了一片"。
/// </para>
/// <para>
/// 锁定盘面的雷位是开局就按 seed 定死的，所以"点到雷"必须挪（否则第一下就炸，违反首点安全）；
/// 但"点到安全格"没有任何理由挪 —— 这一组用例就是把这个区别钉住。
/// </para>
/// </summary>
public sealed class LockedFirstClickTests
{
    private static GameSession CreateLocked(int seed = 20261012)
    {
        bool ok = GameSession.TryCreateLocked(
            9, 9, 10, seed, new GameSettings(), out GameSession? session, out string? error);

        Assert.True(ok, $"锁定盘面应当创建成功：{error}");
        Assert.NotNull(session);
        Assert.True(session!.Board.MinesPlaced, "锁定盘面应当开局就把雷布好");
        return session;
    }

    /// <summary>找一个"既安全、又不是首点"的格子。</summary>
    private static int FindSafeCellOtherThanPinned(GameSession session)
    {
        int pinned = session.Board.FirstClickIndex;
        for (int i = 0; i < session.Board.CellCount; i++)
        {
            if (!session.Board.IsMine(i) && i != pinned)
            {
                return i;
            }
        }

        return -1;
    }

    [Fact]
    public void 点到安全格就在那一格展开而不是被挪到首点()
    {
        GameSession session = CreateLocked();
        int target = FindSafeCellOtherThanPinned(session);

        Assert.True(target >= 0, "9×9/10 雷的盘上必然存在既安全又不是首点的格子");

        RevealResult result = session.Reveal(target);

        // 这一条就是这个 bug 的核心：翻开的必须是玩家点的那一格
        Assert.Equal(target, result.Index);
        Assert.True(session.Board.IsRevealed(target), "玩家点的格子应当被翻开");
        Assert.NotEqual(GameStatus.Lost, session.Status);
    }

    [Fact]
    public void 点到雷时挪到最近的0格且不会炸()
    {
        GameSession session = CreateLocked();
        int mine = session.Board.GetMineIndexes()[0];

        RevealResult result = session.Reveal(mine);

        // 挪到的必须是 0 格：这样"首点必定开出一片"这条原版手感还在
        Assert.NotEqual(GameStatus.Lost, session.Status);
        Assert.False(session.Board.IsRevealed(mine), "点到的雷不应该被翻开");
        Assert.Equal(0, session.Board.GetAdjacentMines(result.Index));
        Assert.True(session.Board.IsRevealed(result.Index));
    }

    [Fact]
    public void 点到雷时挪到的是离它最近的那个0格()
    {
        GameSession session = CreateLocked();
        Board board = session.Board;
        int mine = board.GetMineIndexes()[0];
        int mineCol = board.XOf(mine);
        int mineRow = board.YOf(mine);

        // 独立算一遍"最近 0 格有多远"（切比雪夫距离 = 玩家眼里的隔了几格）
        int nearestPossible = int.MaxValue;
        for (int i = 0; i < board.CellCount; i++)
        {
            // 0 格 = 不是雷、且周围 8 格也没有雷（孤立的雷 GetAdjacentMines 同样是 0，必须排掉）
            if (board.IsMine(i) || board.GetAdjacentMines(i) != 0)
            {
                continue;
            }

            int distance = Math.Max(Math.Abs(board.XOf(i) - mineCol), Math.Abs(board.YOf(i) - mineRow));
            nearestPossible = Math.Min(nearestPossible, distance);
        }

        Assert.True(nearestPossible < int.MaxValue, "9×9/10 雷的盘上必然有 0 格");

        RevealResult result = session.Reveal(mine);
        int actual = Math.Max(
            Math.Abs(board.XOf(result.Index) - mineCol),
            Math.Abs(board.YOf(result.Index) - mineRow));

        Assert.Equal(0, board.GetAdjacentMines(result.Index));
        Assert.Equal(nearestPossible, actual);
    }

    [Fact]
    public void 点不同安全格不会改变雷位()
    {
        GameSession first = CreateLocked();

        // 另一局同样的 seed：点一个**不同**的安全格
        GameSession second = CreateLocked();
        int target = FindSafeCellOtherThanPinned(second);
        second.Reveal(target);

        int[] minesOfFirst = [.. first.Board.GetMineIndexes()];
        int[] minesOfSecond = [.. second.Board.GetMineIndexes()];

        // 确定性不因"玩家点了哪一格"而改变（锁定盘面的全部意义就在这里）
        Assert.Equal(minesOfFirst, minesOfSecond);
        Assert.Equal(first.Board.FirstClickIndex, second.Board.FirstClickIndex);
    }

    [Fact]
    public void 只影响第一下之后按正常规则处理()
    {
        GameSession session = CreateLocked();
        int target = FindSafeCellOtherThanPinned(session);

        session.Reveal(target);
        Assert.NotEqual(GameStatus.NotStarted, session.Status);

        // 第二下点雷就该炸了（"落到安全点"只适用于第一下）
        int mine = session.Board.GetMineIndexes()[0];
        session.Reveal(mine);

        Assert.Equal(GameStatus.Lost, session.Status);
    }
}
