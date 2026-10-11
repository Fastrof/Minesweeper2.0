using MineSweeper.Core;
using Xunit;

namespace MineSweeper.Core.Tests;

/// <summary>§8 第 1、2 条：布雷与参数校验。</summary>
public sealed class BoardGenerationTests
{
    /// <summary>§8-1：雷数恰好等于 M；首点格及其 8 邻格无雷。</summary>
    [Theory]
    [InlineData(9, 9, 10)]
    [InlineData(16, 16, 40)]
    [InlineData(30, 16, 99)]
    public void 布雷后雷数正确且首点安全区无雷(int width, int height, int mines)
    {
        GameSession session = TestFactory.Custom(width, height, mines);
        const int firstClick = 0; // 角落：安全区被裁切，是最容易被写错的位置

        RevealResult result = session.Reveal(firstClick);

        Assert.Equal(RevealOutcome.Revealed, result.Outcome);
        Assert.True(session.Board.MinesPlaced);
        Assert.Equal(mines, session.Board.GetMineIndexes().Length);

        Assert.False(session.Board.IsMine(firstClick), "首点本身不能是雷。");
        foreach (int neighbor in session.Board.Neighbors8(firstClick))
        {
            Assert.False(session.Board.IsMine(neighbor), $"首点的 8 邻格（索引 {neighbor}）不能是雷。");
        }
    }

    /// <summary>首点安全区在棋盘中央时是完整的 3×3。</summary>
    [Fact]
    public void 首点安全区在中央时覆盖完整3x3()
    {
        GameSession session = TestFactory.Custom(16, 16, 40, firstClickOpening: false);
        int center = session.Board.Index(8, 8);

        session.Reveal(center);

        Assert.False(session.Board.IsMine(center));
        foreach (int neighbor in session.Board.Neighbors8(center))
        {
            Assert.False(session.Board.IsMine(neighbor));
        }

        // 关闭"扩展安全区"时用的是 3×3，不能顺手把外圈也挖空
        Assert.Equal(9, BoardGenerator.CountSafeZoneCells(session.Board, center, radius: 1));
    }

    /// <summary>§8-2：M &gt; N − 9 时必须失败，且不得生成盘面、不得布雷。</summary>
    [Fact]
    public void 雷数超过上限时拒绝开局且不布雷()
    {
        // 9×9 = 81 格，上限 = 72
        Assert.False(GameLimits.ValidateParameters(9, 9, 73, out string? directError));
        Assert.NotNull(directError);
        Assert.Contains("首点安全", directError);

        bool created = GameSession.TryCreate(
            9, 9, 73, TestFactory.DefaultSeed, GameSettings.Custom, out GameSession? session, out string? error);

        Assert.False(created);
        Assert.Null(session);
        Assert.NotNull(error);
    }

    /// <summary>恰好等于上限（M = N − 9）应当允许，并且首点仍然安全。</summary>
    [Fact]
    public void 雷数等于上限时允许开局()
    {
        const int width = 9;
        const int height = 9;
        int maxMines = width * height - GameLimits.SafeZoneCellCount;

        Assert.True(GameSession.TryCreate(
            width, height, maxMines, TestFactory.DefaultSeed, GameSettings.ClassicPure, out GameSession? session, out string? error), error);

        RevealResult result = session!.Reveal(0);

        Assert.Equal(RevealOutcome.Revealed, result.Outcome);
        Assert.Equal(maxMines, session.Board.GetMineIndexes().Length);
        Assert.False(session.Board.IsMine(0));
    }

    /// <summary>尺寸越界也必须被拦下（§0.1 的边界）。</summary>
    [Theory]
    [InlineData(0, 9, 0)]
    [InlineData(9, 0, 0)]
    [InlineData(GameLimits.MaxWidth + 1, 9, 1)]
    [InlineData(9, GameLimits.MaxHeight + 1, 1)]
    [InlineData(9, 9, -1)]
    public void 非法尺寸与负雷数被拒绝(int width, int height, int mines)
    {
        Assert.False(GameLimits.ValidateParameters(width, height, mines, out string? error));
        Assert.NotNull(error);
    }

    /// <summary>棋盘太小、放不下首点 3×3 安全区时也必须被拦下。</summary>
    [Theory]
    [InlineData(1, 9, 1)]
    [InlineData(9, 1, 1)]
    [InlineData(3, 3, 1)]
    public void 棋盘放不下安全区时被拒绝(int width, int height, int mines)
    {
        Assert.False(GameLimits.ValidateParameters(width, height, mines, out string? error));
        Assert.NotNull(error);
    }

    /// <summary>同一 seed + 同一首点 → 同一盘面（§2.6 的可复现性）。</summary>
    [Fact]
    public void 相同种子与首点产生相同盘面()
    {
        GameSession a = TestFactory.Custom(16, 16, 40, seed: 777);
        GameSession b = TestFactory.Custom(16, 16, 40, seed: 777);

        const int firstClick = 100;
        a.Reveal(firstClick);
        b.Reveal(firstClick);

        Assert.Equal(a.Board.GetMineIndexes(), b.Board.GetMineIndexes());
    }
}
