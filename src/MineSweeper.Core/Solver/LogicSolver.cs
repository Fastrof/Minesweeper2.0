namespace MineSweeper.Core.Solver;

/// <summary>
/// 纯逻辑推理器（v1.5 无猜模式 / 提示）。
///
/// <para>
/// 只用"玩家在盘面上看得到的信息"做推理：已翻开格的数字 + 总雷数。
/// <b>从不偷看未翻开格是不是雷</b>——模拟通关时，只有被推理证明安全的格才会"翻开"并读出它的数字，
/// 和真人玩家能拿到的信息完全一样。
/// </para>
/// <para>
/// 规则（由弱到强，每轮先用便宜的）：
/// <list type="number">
/// <item>单格规则：数字 − 已知雷 = 0 → 周围未知格全安全；= 未知格数 → 全是雷。</item>
/// <item>双格规则：两个相邻数字格 A、B，若 B 独有的未知格数恰好等于 need(B) − need(A)，
///       则 B 独有格全是雷、A 独有格全安全（子集规则是它的特例）。</item>
/// <item>全局规则：剩余雷数 = 0 → 所有未知格安全；= 未知格总数 → 全是雷。</item>
/// </list>
/// 这三条覆盖了绝大多数"人类能不猜就推出来"的局面；推不出的盘一律视为"需要猜"，宁严勿松。
/// </para>
/// </summary>
public static class LogicSolver
{
    /// <summary>无猜生成只对不超过这么多格的棋盘生效（30×16 高级是 480 格，100×100 = 1 万格）。</summary>
    public const int NoGuessMaxCells = 10_000;

    private const byte Unknown = 0;
    private const byte Revealed = 1;
    private const byte KnownMine = 2;
    private const byte KnownSafe = 3;   // 只在"提示"模式里用：推出安全但还没翻开

    /// <summary>
    /// 从 <paramref name="firstClickIndex"/> 开始，只靠逻辑能否把所有安全格翻完。
    /// 盘面必须已布雷；本方法不修改 <paramref name="board"/>。
    /// </summary>
    public static bool CanSolveWithoutGuessing(Board board, int firstClickIndex)
    {
        ArgumentNullException.ThrowIfNull(board);

        if (!board.MinesPlaced || !board.IsValidIndex(firstClickIndex) || board.IsMine(firstClickIndex))
        {
            return false;
        }

        var engine = new Engine(board, simulate: true);
        engine.RevealSafe(firstClickIndex);
        engine.Run();
        return engine.RevealedCount == board.CellCount - board.MineCount;
    }

    /// <summary>
    /// 提示：根据<b>当前已翻开的格子</b>（不看玩家插的旗——旗可能插错）推出一个肯定安全的未翻开格。
    /// 推不出返回 -1（此时局面确实需要猜，或者还没开局）。
    /// </summary>
    public static int FindSafeCell(Board board)
    {
        ArgumentNullException.ThrowIfNull(board);

        if (!board.MinesPlaced)
        {
            return -1;
        }

        var engine = new Engine(board, simulate: false);
        bool any = false;
        for (int i = 0; i < board.CellCount; i++)
        {
            if (board.IsRevealed(i))
            {
                engine.MarkRevealedFromBoard(i);
                any = true;
            }
        }

        if (!any)
        {
            return -1;
        }

        engine.Run();
        return engine.FirstSafeFound;
    }

    /// <summary>推理引擎：一个实例对应一次推理，用完即弃。</summary>
    private sealed class Engine
    {
        private readonly Board _board;
        private readonly bool _simulate;
        private readonly byte[] _state;
        private readonly List<int> _frontier = new();
        private readonly int[] _nbA = new int[8];
        private readonly int[] _nbB = new int[8];
        private readonly Stack<int> _floodStack = new();
        private int _knownMines;
        private int _unknownCount;
        private bool _progress;

        public Engine(Board board, bool simulate)
        {
            _board = board;
            _simulate = simulate;
            _state = new byte[board.CellCount];
            _unknownCount = board.CellCount;
        }

        public int RevealedCount { get; private set; }

        public int FirstSafeFound { get; private set; } = -1;

        private bool Done => !_simulate && FirstSafeFound >= 0;

        public void MarkRevealedFromBoard(int index)
        {
            if (_state[index] == Unknown)
            {
                _state[index] = Revealed;
                _unknownCount--;
                RevealedCount++;
            }
        }

        /// <summary>模拟翻开一格（0 格连锁展开，与游戏里的洪水填充一致）。</summary>
        public void RevealSafe(int start)
        {
            if (_state[start] is Revealed or KnownMine)
            {
                return;
            }

            _floodStack.Push(start);
            Span<int> buffer = stackalloc int[8];

            while (_floodStack.Count > 0)
            {
                int cell = _floodStack.Pop();
                if (_state[cell] is Revealed or KnownMine)
                {
                    continue;
                }

                if (_state[cell] == Unknown)
                {
                    _unknownCount--;
                }

                _state[cell] = Revealed;
                RevealedCount++;

                if (_board.GetAdjacentMines(cell) == 0)
                {
                    int n = _board.GetNeighbors8(cell, buffer);
                    for (int i = 0; i < n; i++)
                    {
                        if (_state[buffer[i]] is Unknown or KnownSafe)
                        {
                            _floodStack.Push(buffer[i]);
                        }
                    }
                }
            }
        }

        public void Run()
        {
            while (!Done)
            {
                _progress = false;
                RebuildFrontier();

                ApplySingleRule();
                if (Done)
                {
                    return;
                }

                if (!_progress)
                {
                    ApplyPairRule();
                    if (Done)
                    {
                        return;
                    }
                }

                if (!_progress)
                {
                    ApplyGlobalRule();
                }

                if (!_progress)
                {
                    return;
                }
            }
        }

        private void RebuildFrontier()
        {
            _frontier.Clear();
            Span<int> buffer = stackalloc int[8];

            for (int i = 0; i < _state.Length; i++)
            {
                if (_state[i] != Revealed || _board.GetAdjacentMines(i) == 0)
                {
                    continue;
                }

                int n = _board.GetNeighbors8(i, buffer);
                for (int k = 0; k < n; k++)
                {
                    if (_state[buffer[k]] == Unknown)
                    {
                        _frontier.Add(i);
                        break;
                    }
                }
            }
        }

        /// <summary>收集 cell 周围的未知格，返回 (未知格数, 还差几颗雷)。</summary>
        private (int Count, int Need) Collect(int cell, int[] unknowns)
        {
            Span<int> buffer = stackalloc int[8];
            int n = _board.GetNeighbors8(cell, buffer);
            int count = 0;
            int mines = 0;

            for (int k = 0; k < n; k++)
            {
                byte s = _state[buffer[k]];
                if (s == Unknown)
                {
                    unknowns[count++] = buffer[k];
                }
                else if (s == KnownMine)
                {
                    mines++;
                }
            }

            return (count, _board.GetAdjacentMines(cell) - mines);
        }

        private void ApplySingleRule()
        {
            foreach (int cell in _frontier)
            {
                var (count, need) = Collect(cell, _nbA);
                if (count == 0)
                {
                    continue;
                }

                if (need == 0)
                {
                    for (int k = 0; k < count; k++)
                    {
                        MarkSafe(_nbA[k]);
                        if (Done)
                        {
                            return;
                        }
                    }
                }
                else if (need == count)
                {
                    for (int k = 0; k < count; k++)
                    {
                        MarkMine(_nbA[k]);
                    }
                }
            }
        }

        private void ApplyPairRule()
        {
            foreach (int a in _frontier)
            {
                int ax = _board.XOf(a);
                int ay = _board.YOf(a);

                for (int dy = -2; dy <= 2; dy++)
                {
                    for (int dx = -2; dx <= 2; dx++)
                    {
                        if (dx == 0 && dy == 0)
                        {
                            continue;
                        }

                        int bx = ax + dx;
                        int by = ay + dy;
                        if (!_board.InBounds(bx, by))
                        {
                            continue;
                        }

                        int b = _board.Index(bx, by);
                        if (_state[b] != Revealed || _board.GetAdjacentMines(b) == 0)
                        {
                            continue;
                        }

                        TryPair(a, b);
                        if (Done)
                        {
                            return;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 若 need(B) − need(A) 恰好等于 B 独有未知格数：B 独有格全是雷，A 独有格全安全。
        /// （A ⊆ B 且两者 need 相等时，B 独有格数 = 0 = 差值，同样推出 B 独有格安全——由对称的 (B, A) 调用覆盖。）
        /// </summary>
        private void TryPair(int a, int b)
        {
            var (countA, needA) = Collect(a, _nbA);
            var (countB, needB) = Collect(b, _nbB);
            if (countA == 0 || countB == 0)
            {
                return;
            }

            int onlyB = 0;
            for (int i = 0; i < countB; i++)
            {
                if (Array.IndexOf(_nbA, _nbB[i], 0, countA) < 0)
                {
                    onlyB++;
                }
            }

            int onlyA = 0;
            for (int i = 0; i < countA; i++)
            {
                if (Array.IndexOf(_nbB, _nbA[i], 0, countB) < 0)
                {
                    onlyA++;
                }
            }

            if (onlyA == 0 && onlyB == 0)
            {
                return;
            }

            if (needB - needA != onlyB)
            {
                return;
            }

            // 先拷出来：MarkSafe 在模拟模式下会翻格，不影响这两个数组，但保持可读
            for (int i = 0; i < countB; i++)
            {
                if (Array.IndexOf(_nbA, _nbB[i], 0, countA) < 0)
                {
                    MarkMine(_nbB[i]);
                }
            }

            for (int i = 0; i < countA; i++)
            {
                if (Array.IndexOf(_nbB, _nbA[i], 0, countB) < 0)
                {
                    MarkSafe(_nbA[i]);
                    if (Done)
                    {
                        return;
                    }
                }
            }
        }

        private void ApplyGlobalRule()
        {
            int remaining = _board.MineCount - _knownMines;
            if (_unknownCount == 0)
            {
                return;
            }

            if (remaining == 0)
            {
                for (int i = 0; i < _state.Length; i++)
                {
                    if (_state[i] == Unknown)
                    {
                        MarkSafe(i);
                        if (Done)
                        {
                            return;
                        }
                    }
                }
            }
            else if (remaining == _unknownCount)
            {
                for (int i = 0; i < _state.Length; i++)
                {
                    if (_state[i] == Unknown)
                    {
                        MarkMine(i);
                    }
                }
            }
        }

        private void MarkMine(int index)
        {
            if (_state[index] != Unknown)
            {
                return;
            }

            _state[index] = KnownMine;
            _knownMines++;
            _unknownCount--;
            _progress = true;
        }

        private void MarkSafe(int index)
        {
            if (_state[index] != Unknown)
            {
                return;
            }

            _progress = true;

            if (_simulate)
            {
                RevealSafe(index);
                return;
            }

            _state[index] = KnownSafe;
            _unknownCount--;
            if (FirstSafeFound < 0)
            {
                FirstSafeFound = index;
            }
        }
    }
}
