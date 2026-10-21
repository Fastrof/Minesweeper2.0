using Xunit;
using System.Diagnostics;
using MineSweeper.Core;
using MineSweeper.Core.Solver;

namespace MineSweeper.Core.Tests;

/// <summary>v1.5 无猜模式与提示：推理器、无猜布雷、分享串、性能。</summary>
public sealed class NoGuessTests
{
    private static GameSettings NoGuessSettings(bool firstClickOpening = true)
        => new() { Mode = GameMode.Custom, FirstClickOpening = firstClickOpening, NoGuess = true };

    private static Board BoardWithMines(int width, int height, int firstClick, params int[] mines)
    {
        var board = new Board(width, height);
        Assert.True(BoardGenerator.TryPlaceMinesAt(board, firstClick, mines, out string? error), error);
        return board;
    }

    private static GameSession NewSession(int width, int height, int mines, int seed, GameSettings settings)
    {
        Assert.True(GameSession.TryCreate(width, height, mines, seed, settings, out GameSession? session, out string? error), error);
        return session!;
    }

    /// <summary>只按提示一路点下去；返回最终状态。提示若指向雷会直接炸，测试就会失败。</summary>
    private static GameStatus AutoPlayWithHints(GameSession session)
    {
        for (int guard = 0; guard < session.Board.CellCount && !session.IsFinished; guard++)
        {
            int hint = session.FindHint();
            if (hint < 0)
            {
                break;
            }

            Assert.False(session.Board.IsMine(hint), $"提示指向了雷：{hint}");
            session.Reveal(hint);
            while (session.HasPendingFlood)
            {
                session.ContinuePendingFlood();
            }
        }

        return session.Status;
    }

    // ---------------------------------------------------------------- 推理器

    [Fact]
    public void 推理器_一眼可解的盘返回可解()
    {
        // 3×4，雷在左上角：点右下角 → 连锁展开把 11 个安全格全部翻完
        Board board = BoardWithMines(3, 4, 11, 0);
        Assert.True(LogicSolver.CanSolveWithoutGuessing(board, 11));
    }

    [Fact]
    public void 推理器_经典二选一返回不可解()
    {
        // 5×2，雷在 (4,0)：首点 (0,0) 展开后最右一列两个格子各 50%，只能猜
        Board board = BoardWithMines(5, 2, 0, 4);
        Assert.False(LogicSolver.CanSolveWithoutGuessing(board, 0));
    }

    [Fact]
    public void 推理器_首点是雷或未布雷时返回不可解()
    {
        Board mined = BoardWithMines(3, 4, 11, 0);
        Assert.False(LogicSolver.CanSolveWithoutGuessing(mined, 0));

        var empty = new Board(4, 4, 2);
        Assert.False(LogicSolver.CanSolveWithoutGuessing(empty, 0));
    }

    [Fact]
    public void 推理器_双格规则能推出单格规则推不出的格子()
    {
        // 经典 1-2 模式：
        //   行 0： ?  ?  ?          （(0,0) 与 (2,0) 是雷，(1,0) 安全）
        //   行 1： 1  2  1   ← 已翻开（底下一行全是 0）
        //   行 2..4： 全是 0（3×5 棋盘，首点在左下角）
        // 单格规则：(0,1)=1 看到 2 个未知，(1,1)=2 看到 3 个未知，(2,1)=1 看到 2 个未知 —— 都推不出。
        // 双格规则：B=(1,1) 独有 {(2,0)}，need(B)−need(A=(0,1)) = 1 = 独有格数 → (2,0) 是雷、A 独有 ∅；
        //          对称地 (0,0) 是雷，于是 (1,0) 安全。
        Board board = BoardWithMines(3, 5, 12, 0, 2);
        Assert.True(LogicSolver.CanSolveWithoutGuessing(board, 12));
    }

    [Fact]
    public void 推理器_不修改盘面()
    {
        Board board = BoardWithMines(5, 2, 0, 4);
        LogicSolver.CanSolveWithoutGuessing(board, 0);
        Assert.Equal(0, board.CountRevealed());
        Assert.Equal(0, board.CountFlagged());
    }

    // ---------------------------------------------------------------- 提示

    [Fact]
    public void 提示_开局前与结束后都返回负一()
    {
        GameSession session = NewSession(9, 9, 10, 7, GameSettings.Custom);
        Assert.Equal(-1, session.FindHint());

        session.Reveal(40);
        foreach (int mine in session.Board.EnumerateMines())
        {
            session.Reveal(mine);
            break;
        }

        Assert.True(session.IsFinished);
        Assert.Equal(-1, session.FindHint());
    }

    [Fact]
    public void 提示_二选一局面推不出时返回负一()
    {
        var snapshot = new BoardSnapshot(5, 2, 1, 1, 0, false, new[] { 4 });
        GameSession session = GameSession.CreateReplay(snapshot, GameSettings.Custom);
        session.Reveal(0);
        Assert.Equal(-1, session.FindHint());
    }

    [Fact]
    public void 提示_不受插错的旗影响_也不改动盘面()
    {
        GameSession session = NewSession(16, 16, 40, 99, GameSettings.Custom);
        session.Reveal(8 * 16 + 8);

        // 在所有未翻开的安全格上插旗（全是错旗）
        for (int i = 0; i < session.Board.CellCount; i++)
        {
            if (!session.Board.IsRevealed(i) && !session.Board.IsMine(i))
            {
                session.ToggleFlag(i);
            }
        }

        int revealedBefore = session.Board.CountRevealed();
        int hint = session.FindHint();
        if (hint >= 0)
        {
            Assert.False(session.Board.IsMine(hint));
        }

        Assert.Equal(revealedBefore, session.Board.CountRevealed());
    }

    [Theory]
    [InlineData(9, 9, 10)]
    [InlineData(16, 16, 40)]
    [InlineData(30, 16, 99)]
    public void 提示_随机盘上永远不会指向雷(int width, int height, int mines)
    {
        for (int seed = 1; seed <= 40; seed++)
        {
            GameSession session = NewSession(width, height, mines, seed, GameSettings.Custom);
            session.Reveal(session.Board.Index(width / 2, height / 2));
            AutoPlayWithHints(session);
            Assert.NotEqual(GameStatus.Lost, session.Status);
        }
    }

    // ---------------------------------------------------------------- 无猜布雷

    [Fact]
    public void 无猜_默认关闭()
    {
        Assert.False(new GameSettings().NoGuess);
        Assert.False(GameSettings.ClassicPure.NoGuess);
        Assert.False(GameSettings.Custom.NoGuess);
    }

    [Theory]
    [InlineData(9, 9, 10)]
    [InlineData(16, 16, 40)]
    [InlineData(30, 16, 99)]
    public void 无猜_生成的盘只靠提示就能通关(int width, int height, int mines)
    {
        for (int seed = 1; seed <= 25; seed++)
        {
            GameSession session = NewSession(width, height, mines, seed, NoGuessSettings());
            int first = session.Board.Index(width / 2, height / 2);
            session.Reveal(first);

            Assert.True(session.Board.NoGuessRequested);
            Assert.True(session.Board.NoGuessGuaranteed, $"seed={seed} 没抽到无猜盘");
            Assert.True(LogicSolver.CanSolveWithoutGuessing(session.Board, first));
            Assert.Equal(mines, session.Board.GetMineIndexes().Length);
            Assert.Equal(GameStatus.Won, AutoPlayWithHints(session));
        }
    }

    [Fact]
    public void 无猜_同种子同首点得到同一张盘()
    {
        GameSession a = NewSession(30, 16, 99, 4242, NoGuessSettings());
        GameSession b = NewSession(30, 16, 99, 4242, NoGuessSettings());
        a.Reveal(100);
        b.Reveal(100);
        Assert.Equal(a.Board.GetMineIndexes(), b.Board.GetMineIndexes());
    }

    [Fact]
    public void 无猜_首点安全区规则不变()
    {
        GameSession session = NewSession(30, 16, 99, 5, NoGuessSettings());
        int first = session.Board.Index(3, 3);
        session.Reveal(first);

        foreach (int mine in session.Board.EnumerateMines())
        {
            Assert.False(BoardGenerator.IsInSafeZone(session.Board, first, mine, 1));
        }
    }

    [Fact]
    public void 无猜_超大棋盘退回普通盘但照常可玩()
    {
        GameSession session = NewSession(200, 200, 4000, 1, NoGuessSettings());
        RevealResult result = session.Reveal(100 * 200 + 100);

        Assert.False(result.IsBlocked);
        Assert.True(session.Board.NoGuessRequested);
        Assert.False(session.Board.NoGuessGuaranteed);
        Assert.Equal(4000, session.Board.GetMineIndexes().Length);
    }

    [Fact]
    public void 无猜_关闭时标志位都是假()
    {
        GameSession session = NewSession(9, 9, 10, 3, GameSettings.Custom);
        session.Reveal(40);
        Assert.False(session.Board.NoGuessRequested);
        Assert.False(session.Board.NoGuessGuaranteed);
    }

    [Fact]
    public void 无猜_高级难度生成足够快()
    {
        var watch = Stopwatch.StartNew();
        for (int seed = 1; seed <= 20; seed++)
        {
            GameSession session = NewSession(30, 16, 99, seed, NoGuessSettings());
            session.Reveal(8 * 30 + 15);
        }

        watch.Stop();

        // 20 张高级无猜盘，正常一两百毫秒；给慢机器留足余量
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), $"耗时 {watch.Elapsed.TotalMilliseconds:F0} ms");
    }

    // ---------------------------------------------------------------- 分享串

    [Fact]
    public void 分享串_无猜盘写成七节并能精确复现()
    {
        GameSession original = NewSession(30, 16, 99, 777, NoGuessSettings());
        original.Reveal(8 * 30 + 15);

        string code = original.CaptureSnapshot().ToShareCode();
        Assert.Equal(7, code.Split(':').Length);
        Assert.EndsWith(":0:1", code);

        Assert.True(BoardSnapshot.TryParseShareCode(code, out _, out _, out _, out _, out _, out _, out bool locked, out bool noGuess));
        Assert.False(locked);
        Assert.True(noGuess);

        Assert.True(GameSession.TryReplayShareCode(code, GameMode.Custom, out GameSession? replay, out string? error), error);
        Assert.Equal(original.Board.GetMineIndexes(), replay!.Board.GetMineIndexes());
        Assert.True(replay.Board.NoGuessGuaranteed);
    }

    [Fact]
    public void 分享串_不开无猜时格式与旧版一致()
    {
        GameSession session = NewSession(16, 16, 40, 12, GameSettings.Custom);
        session.Reveal(0);
        string code = session.CaptureSnapshot().ToShareCode();
        Assert.Equal(5, code.Split(':').Length);

        Assert.True(BoardSnapshot.TryParseShareCode(code, out _, out _, out _, out _, out _, out _, out _, out bool noGuess));
        Assert.False(noGuess);
    }

    [Fact]
    public void 快照重开_保留无猜标志()
    {
        GameSession original = NewSession(16, 16, 40, 31, NoGuessSettings());
        original.Reveal(8 * 16 + 8);

        BoardSnapshot snapshot = original.CaptureSnapshot();
        Assert.True(snapshot.NoGuess);

        GameSession replay = GameSession.CreateReplay(snapshot, NoGuessSettings());
        Assert.True(replay.Board.NoGuessRequested);
        Assert.True(replay.Board.NoGuessGuaranteed);
        Assert.True(replay.CaptureSnapshot().NoGuess);
    }
}
