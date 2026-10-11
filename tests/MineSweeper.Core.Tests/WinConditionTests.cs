using MineSweeper.Core;
using Xunit;

namespace MineSweeper.Core.Tests;

/// <summary>
/// §8 第 6 条：胜负判定。
/// 盘面：5 宽 × 3 高，雷在 x=2 整列，左区 6 格 + 右区 6 格 = 12 个安全格。
/// 先开左区不会取胜（不早判），再开右区正好凑满 12 格立刻取胜（不晚判）。
/// </summary>
public sealed class WinConditionTests
{
    private static GameSession WallSession()
        => TestFactory.Replay(5, 3, TestFactory.WallMines, firstClickIndex: 0, new GameSettings { Mode = GameMode.ClassicPure });

    [Fact]
    public void 开完最后一个安全格立即取胜且不早判()
    {
        GameSession session = WallSession();

        RevealResult left = session.Reveal(0);

        Assert.Equal(RevealOutcome.Revealed, left.Outcome);
        Assert.Equal(6, session.RevealedCount);
        Assert.Equal(GameStatus.InProgress, session.Status);

        // 剩余雷数 = 总雷数 − 已插旗数：旗子一个没插，所以还是 3
        Assert.Equal(3, session.RemainingMines);

        RevealResult right = session.Reveal(4);

        Assert.Equal(RevealOutcome.Won, right.Outcome);
        Assert.Equal(12, session.RevealedCount);
        Assert.Equal(GameStatus.Won, session.Status);
        Assert.Equal(5 * 3 - 3, session.RevealedCount);
    }

    [Fact]
    public void 踩雷即失败并记录踩雷位置()
    {
        GameSession session = WallSession();

        session.Reveal(0);
        RevealResult boom = session.Reveal(7); // 雷列中间那一颗

        Assert.Equal(RevealOutcome.Exploded, boom.Outcome);
        Assert.Equal(GameStatus.Lost, session.Status);
        Assert.Equal(7, session.ExplodedIndex);
        Assert.False(session.Board.IsRevealed(7));
    }

    [Fact]
    public void 结束后不能再操作()
    {
        GameSession session = WallSession();
        session.Reveal(0);
        session.Reveal(4);
        Assert.Equal(GameStatus.Won, session.Status);

        Assert.Equal(RevealOutcome.Blocked, session.Reveal(1).Outcome);
        Assert.Equal(FlagOutcome.Blocked, session.ToggleFlag(2).Outcome);
        Assert.Equal(ChordOutcome.Blocked, session.Chord(0).Outcome);
    }

    /// <summary>失败后棋盘仍保留全部雷位信息，供 UI 展示（§0 第 10 条）。</summary>
    [Fact]
    public void 失败后仍可枚举全部雷位()
    {
        GameSession session = WallSession();
        session.Reveal(0);
        session.Reveal(2);

        Assert.Equal(GameStatus.Lost, session.Status);
        Assert.Equal(TestFactory.WallMines, session.Board.GetMineIndexes());
    }

    [Fact]
    public void 剩余雷数等于总雷数减已插旗数()
    {
        GameSession session = WallSession();

        Assert.Equal(3, session.RemainingMines);

        session.ToggleFlag(2);
        Assert.Equal(2, session.RemainingMines);

        // 旗 → 问号：问号不计入插旗数
        session.ToggleFlag(2);
        Assert.Equal(3, session.RemainingMines);

        // 问号 → 空
        Assert.Equal(FlagOutcome.Cleared, session.ToggleFlag(2).Outcome);
        Assert.Equal(3, session.RemainingMines);

        // 空 → 旗：三态循环回到起点
        Assert.Equal(FlagOutcome.Flagged, session.ToggleFlag(2).Outcome);
        Assert.Equal(2, session.RemainingMines);
    }
    /// <summary>
    /// 胜利时要把剩下的雷自动插旗。
    ///
    /// 胜利条件是"翻开所有非雷格"，**并不要求玩家把雷标出来**，所以原本赢了之后画面上
    /// 还会留着一堆没翻开的格子。玩家看到的是"还有雷没扫完"却已经判胜、下一关还解锁了——
    /// 很自然会认为关卡逻辑坏了。原版扫雷在赢的那一刻也会把剩下的雷标出来。
    /// </summary>
    [Fact]
    public void 胜利时剩下的雷会自动插旗()
    {
        // 4×4，雷在右下角；点左上角会连锁翻开其余 15 格 → 直接胜利，此时雷还没插旗
        GameSession session = TestFactory.ReplayCustom(4, 4, new[] { 15 }, firstClickIndex: 0);

        session.Reveal(0);

        Assert.Equal(GameStatus.Won, session.Status);
        Assert.True(session.Board.IsFlagged(15), "胜利时剩下的雷应当被自动插上旗。");
        Assert.Equal(0, session.RemainingMines);
        Assert.Equal(1, session.FlagCount);
    }

    /// <summary>雷上原本是问号的话，胜利时也要换成旗（而不是留着问号）。</summary>
    [Fact]
    public void 胜利时问号会换成旗()
    {
        GameSession session = TestFactory.ReplayCustom(4, 4, new[] { 15 }, firstClickIndex: 0);

        session.ToggleFlag(15);   // 空 → 旗
        session.ToggleFlag(15);   // 旗 → 问号
        Assert.True(session.Board.IsQuestionMarked(15));

        session.Reveal(0);

        Assert.Equal(GameStatus.Won, session.Status);
        Assert.True(session.Board.IsFlagged(15));
        Assert.False(session.Board.IsQuestionMarked(15));
        Assert.Equal(0, session.RemainingMines);
    }
}