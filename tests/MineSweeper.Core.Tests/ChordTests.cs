using MineSweeper.Core;
using Xunit;

namespace MineSweeper.Core.Tests;

/// <summary>
/// §8 第 5 条：chord。
/// 盘面用 4×4、只在索引 0（左上角）放一颗雷 —— 该盘面满足 M ≤ N − 9（1 ≤ 7），
/// 且邻雷数可以手推：
///   邻雷数 = 1 的格：1、4、5
///   邻雷数 = 0 的格：2、3、6、7、8、9、10、11、12、13、14、15
/// 于是可以"一次只翻开一格"（数字格不扩散）来搭出确定的中间状态。
/// </summary>
public sealed class ChordTests
{
    private static GameSession Session()
        => TestFactory.Replay(4, 4, new[] { 0 }, firstClickIndex: 1, new GameSettings { Mode = GameMode.ClassicPure });

    /// <summary>搭出"1、4、5 已翻开"的确定状态（这三个格子邻雷数都是 1，不会引发扩散）。</summary>
    private static GameSession WithThreeNumbersRevealed()
    {
        GameSession session = Session();
        Assert.Equal(RevealOutcome.Revealed, session.Reveal(1).Outcome);
        Assert.Equal(RevealOutcome.Revealed, session.Reveal(4).Outcome);
        Assert.Equal(RevealOutcome.Revealed, session.Reveal(5).Outcome);
        Assert.Equal(3, session.RevealedCount);
        return session;
    }

    /// <summary>§8-5(a)：旗数等于数字时批量展开（本盘面会连带开完整盘安全格并取胜）。</summary>
    [Fact]
    public void 旗数等于数字时展开并可能直接取胜()
    {
        GameSession session = WithThreeNumbersRevealed();

        FlagResult flag = session.ToggleFlag(0);
        Assert.Equal(FlagOutcome.Flagged, flag.Outcome);
        Assert.Equal(0, session.RemainingMines);

        ChordResult chord = session.Chord(5);

        // 15 个安全格里，此前已翻开 3 个，chord 一次开完剩下 12 个
        Assert.Equal(ChordOutcome.Won, chord.Outcome);
        Assert.Equal(12, chord.OpenedCount);
        Assert.Equal(15, session.RevealedCount);
        Assert.Equal(GameStatus.Won, session.Status);
    }

    /// <summary>§8-5(b)：旗数不等于数字时不展开（也不报错）。</summary>
    [Fact]
    public void 旗数不等于数字时不展开()
    {
        GameSession session = WithThreeNumbersRevealed();

        ChordResult chord = session.Chord(5);

        Assert.Equal(ChordOutcome.Blocked, chord.Outcome);
        Assert.Equal(0, chord.OpenedCount);
        Assert.Contains("旗数", chord.Reason);
        Assert.Equal(3, session.RevealedCount);
    }

    /// <summary>
    /// §8-5(c)：旗插错位置时 chord 踩雷失败 —— 这是原版行为，必须保留，
    /// 不能"贴心地"替玩家过滤掉。
    /// </summary>
    [Fact]
    public void 旗插错位置时chord踩雷失败()
    {
        GameSession session = WithThreeNumbersRevealed();

        // 在安全格 2（数字格 5 的邻居）上插旗：数量凑上了，位置是错的
        session.ToggleFlag(2);

        ChordResult chord = session.Chord(5);

        Assert.Equal(ChordOutcome.Exploded, chord.Outcome);
        Assert.Equal(0, chord.ExplodedIndex);
        Assert.Equal(GameStatus.Lost, session.Status);
        Assert.Equal(0, session.ExplodedIndex);
    }

    /// <summary>未翻开的格子、以及邻雷数为 0 的格子都不能 chord。</summary>
    [Fact]
    public void 未翻开或零邻雷的格子不能chord()
    {
        GameSession session = Session();

        // 未翻开：被拒绝，且理由说明得清楚
        ChordResult notRevealed = session.Chord(2);
        Assert.Equal(ChordOutcome.Blocked, notRevealed.Outcome);
        Assert.Contains("已翻开", notRevealed.Reason);

        // 零邻雷的格子：用纯函数验证该分支。
        // 在 4×4 单雷盘面上，翻开任何一个 0 格都会连锁开完整盘并直接取胜，
        // 而终局后 Chord 会被"本局已结束"先拦下，所以这里不走 GameSession.Chord。
        session.Reveal(2);

        Assert.Equal(0, session.Board.GetAdjacentMines(2));
        Assert.False(ChordSolver.CanChord(session.Board, 2));
        Assert.Equal(GameStatus.Won, session.Status);
    }

    /// <summary>chord 判定是纯函数，可以单独测：旗数与数字相等才为真。</summary>
    [Fact]
    public void ChordSolver判定与数字一致()
    {
        GameSession session = WithThreeNumbersRevealed();

        Assert.False(ChordSolver.CanChord(session.Board, 5));

        session.ToggleFlag(0);
        Assert.True(ChordSolver.CanChord(session.Board, 5));
    }
}
