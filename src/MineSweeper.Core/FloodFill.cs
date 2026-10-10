namespace MineSweeper.Core;

/// <summary>
/// 洪水填充（§2.3）—— <b>必须是迭代实现</b>。
///
/// 原稿 §0 第 3 条写的是"递归展开"。递归版本在 1000×1000 的空盘上递归深度可达 100 万层，
/// 会抛 <c>StackOverflowException</c>，而 .NET 里这个异常<b>不可捕获</b>，进程直接退出。
/// 所以这里用显式 Stack&lt;int&gt;：栈里只放 int 索引，100 万格峰值约 4 MB。
///
/// 另外提供分帧能力：<see cref="Step"/> 一次只展开最多 maxCells 格，
/// UI 层可以在 DispatcherQueue 上多帧推进，避免一次点击卡住几秒（§2.3 的"分帧"要求）。
/// </summary>
public sealed class FloodFillOperation
{
    private readonly Board _board;
    private readonly Stack<int> _stack;

    /// <summary>从 startIndex 开始的一次展开操作。</summary>
    public FloodFillOperation(Board board, int startIndex)
    {
        ArgumentNullException.ThrowIfNull(board);

        _board = board;
        _stack = new Stack<int>(Math.Clamp(board.Width * 2, 16, 4096));

        PushIfRevealable(startIndex);
    }

    /// <summary>本次操作已翻开的格数。</summary>
    public int RevealedThisOperation { get; private set; }

    /// <summary>
    /// 本次操作翻开过的格索引下界；一个格子都没翻开时为 <see cref="int.MaxValue"/>。
    /// UI 靠它把"这一整片展开"换算成一个重绘矩形（§4.5）——既不会为了几格刷新整屏，
    /// 也不会只重绘被点击那一格而让整片展开不显示。
    /// </summary>
    public int MinRevealedIndex { get; private set; } = int.MaxValue;

    /// <summary>本次操作翻开过的格索引上界；一个格子都没翻开时为 -1。</summary>
    public int MaxRevealedIndex { get; private set; } = -1;

    /// <summary>本次操作是否真的翻开过格子。</summary>
    public bool HasRevealedAnything => MaxRevealedIndex >= MinRevealedIndex;

    /// <summary>是否已经展开完毕。</summary>
    public bool IsComplete => _stack.Count == 0;

    /// <summary>待处理格数（UI 可用它估算进度）。</summary>
    public int PendingCount => _stack.Count;

    /// <summary>推进最多 <paramref name="maxCells"/> 格，返回本次真正翻开的格数。</summary>
    public int Step(int maxCells)
    {
        if (maxCells <= 0)
        {
            return 0;
        }

        int revealed = 0;
        Span<int> buffer = stackalloc int[8];

        while (_stack.Count > 0 && revealed < maxCells)
        {
            int index = _stack.Pop();

            if (_board.IsRevealed(index) || _board.IsFlagged(index))
            {
                continue;
            }

            // 保险：雷永远不会被压栈（有数字的格不再扩散，而 0 格周围本来就没有雷）。
            // 万一将来改了扩散规则，这里也绝不把雷"翻开"。
            if (_board.IsMine(index))
            {
                continue;
            }

            _board.AddFlag(index, CellFlags.IsRevealed);
            revealed++;
            RevealedThisOperation++;

            // 记录改动范围（索引区间的超集，只做两次比较，零分配）
            if (index < MinRevealedIndex)
            {
                MinRevealedIndex = index;
            }

            if (index > MaxRevealedIndex)
            {
                MaxRevealedIndex = index;
            }

            if (_board.GetAdjacentMines(index) != 0)
            {
                // 有数字的格子不再向外扩散
                continue;
            }

            int neighborCount = _board.GetNeighbors8(index, buffer);
            for (int i = 0; i < neighborCount; i++)
            {
                PushIfRevealable(buffer[i]);
            }
        }

        return revealed;
    }

    /// <summary>一次展开到底（小棋盘或同步场景直接用这个）。</summary>
    public int StepAll() => Step(int.MaxValue);

    private void PushIfRevealable(int index)
    {
        if (!_board.IsRevealed(index) && !_board.IsFlagged(index))
        {
            _stack.Push(index);
        }
    }
}
