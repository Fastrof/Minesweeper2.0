using MineSweeper.Core;
using MineSweeper.Core.Items;
using MineSweeper.Core.Levels;
using MineSweeper.Core.Persistence;
using Xunit;

namespace MineSweeper.Core.Tests;

/// <summary>
/// 关卡制（关卡表 + 星级 + 解锁 + 进度存档）。
///
/// 其中"关卡表"那几个用例是<b>关卡设计的守门人</b>：
/// 关卡表是手写的常量，写错一个数字（雷数超上限、种子重复、开局就赢）只有这些用例能挡住。
/// </summary>
public sealed class LevelTests
{
    // ------------------------------------------------------------ 关卡表

    [Fact]
    public void 关卡表是六章三十六关且编号连续()
    {
        Assert.Equal(36, LevelTable.Count);
        Assert.Equal(6, LevelTable.ChapterCount);
        Assert.Equal(6, LevelTable.LevelsPerChapter);

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < LevelTable.Count; i++)
        {
            LevelDefinition level = LevelTable.At(i)!;

            // Index 必须等于下标（解锁逻辑直接靠 Index±1）
            Assert.Equal(i, level.Index);

            // Id 与章内序号自洽，且全表唯一
            Assert.Equal($"{level.Chapter}-{level.NumberInChapter}", level.Id);
            Assert.True(ids.Add(level.Id), $"关卡 Id 重复：{level.Id}");

            Assert.False(string.IsNullOrWhiteSpace(level.Name));
            Assert.False(string.IsNullOrWhiteSpace(level.ChapterName));
        }

        Assert.Equal(LevelTable.All[0].Id, LevelTable.First.Id);
        Assert.Equal(LevelTable.All[^1].Id, LevelTable.Last.Id);
        Assert.Null(LevelTable.Next(LevelTable.Last));
        Assert.Equal(LevelTable.All[1].Id, LevelTable.Next(LevelTable.First)!.Id);
        Assert.Equal(6, LevelTable.Chapter(2).Count);
        Assert.All(LevelTable.Chapter(3), l => Assert.Equal(3, l.Chapter));
        Assert.Null(LevelTable.At(-1));
        Assert.Null(LevelTable.At(LevelTable.Count));
        Assert.Equal("1-1", LevelTable.FindById("1-1")!.Id);
        Assert.Null(LevelTable.FindById("9-9"));
        Assert.Null(LevelTable.FindById(null));
    }

    /// <summary>每关的盘面参数都必须能通过 §0.1 的合法性校验，否则那一关根本开不了局。</summary>
    [Fact]
    public void 每一关的参数都合法()
    {
        foreach (LevelDefinition level in LevelTable.All)
        {
            Assert.True(
                GameLimits.ValidateParameters(level.Width, level.Height, level.MineCount, out string? error),
                $"{level.DisplayName} 参数非法：{error}");
        }
    }

    /// <summary>星级门槛要"挑战严于目标"且都为正，否则三星毫无意义。</summary>
    [Fact]
    public void 星级门槛单调且为正()
    {
        foreach (LevelDefinition level in LevelTable.All)
        {
            Assert.True(level.ChallengeSeconds > 0, $"{level.DisplayName} 挑战时间必须为正。");
            Assert.True(
                level.ChallengeSeconds < level.TargetSeconds,
                $"{level.DisplayName} 的挑战时间({level.ChallengeSeconds}) 必须严于目标时间({level.TargetSeconds})。");
        }
    }

    /// <summary>种子必须互不相同，否则两关是同一张盘。</summary>
    [Fact]
    public void 每一关的种子互不相同()
    {
        var seeds = new HashSet<int>();
        foreach (LevelDefinition level in LevelTable.All)
        {
            Assert.True(seeds.Add(level.Seed), $"{level.DisplayName} 的种子与前面某关重复：{level.Seed}");
            Assert.True(level.Seed > 0);
        }
    }

    /// <summary>
    /// v0.6 起道具全局共用：**每一关都允许使用道具**，关卡表里的 MagnifierCount 只是设计数据，
    /// 不再决定道具能不能用（这正是"道具默认就可用"这条规则的落点）。
    /// </summary>
    [Fact]
    public void 每一关都允许使用道具且模式统一()
    {
        foreach (LevelDefinition level in LevelTable.All)
        {
            Assert.True(level.MagnifierCount >= 0);
            Assert.True(level.ItemsAvailable, $"{level.DisplayName} 应当允许使用道具。");
            Assert.Equal(GameMode.ClassicWithItems, level.Mode);
        }
    }


    /// <summary>
    /// 关卡制的核心承诺：<b>同一关永远是同一张盘</b>（重试、刷星、分享都靠这个）。
    /// 同时确认每关确实能开起来、开局已经翻开首点、且没有"一开局就赢"这种白送关卡。
    /// </summary>
    [Fact]
    public void 每一关都能创建且同一关永远同一张盘()
    {
        foreach (LevelDefinition level in LevelTable.All)
        {
            GameSession first = CreateLevel(level);
            GameSession second = CreateLevel(level);

            Assert.Equal(first.Board.GetMineIndexes(), second.Board.GetMineIndexes());
            Assert.Equal(first.Board.FirstClickIndex, second.Board.FirstClickIndex);
            Assert.Equal(level.MineCount, first.Board.GetMineIndexes().Length);
            Assert.True(first.Locked);
            Assert.True(first.Board.MinesPlaced);

            // 雷位开局就定好了，但**不替玩家走第一步**：
            // 整盘未翻开、状态是"未开始"、计时为 0，等玩家自己点。
            Assert.Equal(GameStatus.NotStarted, first.Status);
            Assert.Equal(0, first.ElapsedSeconds);
            Assert.Equal(0, first.RevealedCount);
            for (int i = 0; i < first.Board.CellCount; i++)
            {
                Assert.False(first.Board.IsRevealed(i), $"第 {i} 格不该在开局就被翻开。");
            }

            // 首点安全（§0 第 2 条）：安全点是开局就算好的，玩家点哪一格第一次都会落到它上面
            Assert.True(first.Board.IsValidIndex(first.Board.FirstClickIndex));
            Assert.Equal(0, first.Board.GetAdjacentMines(first.Board.FirstClickIndex));
            foreach (int neighbor in first.Board.Neighbors8(first.Board.FirstClickIndex))
            {
                Assert.False(first.Board.IsMine(neighbor));
            }
        }
    }
    /// <summary>
    /// v0.6：关卡不再每关另发道具，**与普通模式共用同一套物品栏**（账号进度）。
    /// 所以在关卡里用掉一个道具，账号库存会直接减少 —— 这条锁住的就是这个共用关系。
    /// </summary>
    [Fact]
    public void 关卡与普通模式共用同一套道具()
    {
        var account = new PlayerProgress { InitialGiftGranted = true, MagnifierCount = 3, SonarCount = 2, FreezeCount = 1 };
        var inventory = new ItemInventory(account, TestFactory.Config(threshold: 20, maxHold: 3, initialGift: 0));

        LevelDefinition level = LevelTable.First;
        GameSession session = CreateLevel(level, inventory);

        Assert.Same(inventory, session.Inventory);
        Assert.Equal(3, inventory.MagnifierCount);

        // 在关卡里用掉一个放大镜：账号库存直接减少（不再有"关卡专用库存"这回事）
        session.Reveal(session.Board.Index(session.Board.Width / 2, session.Board.Height / 2));
        int target = session.Board.Index(1, 1);
        Assert.True(session.UseMagnifier(target).Success);
        Assert.Equal(2, account.MagnifierCount);

        // 关卡定义里的 MagnifierCount 只是关卡表数据，不再参与分发
        Assert.True(level.MagnifierCount > 0);
        Assert.Equal(2, inventory.MagnifierCount);
    }

    // ------------------------------------------------------------ 星级

    [Theory]
    [InlineData(10, 3)]   // 快于挑战
    [InlineData(50, 3)]   // 正好等于挑战
    [InlineData(51, 2)]
    [InlineData(90, 2)]   // 正好等于目标
    [InlineData(91, 1)]
    [InlineData(9999, 1)]
    public void 星级按通关用时评定(double seconds, int expected)
    {
        LevelDefinition level = LevelTable.FindById("1-1")!;   // 挑战 50 / 目标 90
        Assert.Equal(50, level.ChallengeSeconds);
        Assert.Equal(90, level.TargetSeconds);

        Assert.Equal(expected, LevelRules.EvaluateStars(level, seconds));
    }

    [Fact]
    public void 星数文案三段()
    {
        Assert.Equal("★★★", LevelRules.StarsText(3));
        Assert.Equal("★★☆", LevelRules.StarsText(2));
        Assert.Equal("★☆☆", LevelRules.StarsText(1));
        Assert.Equal("☆☆☆", LevelRules.StarsText(0));

        // 越界输入被夹住，不会画出 ★★★★
        Assert.Equal("★★★", LevelRules.StarsText(9));
        Assert.Equal("☆☆☆", LevelRules.StarsText(-3));
    }

    // ------------------------------------------------------------ 解锁

    [Fact]
    public void 第一关默认解锁其余逐关解锁()
    {
        var progress = LevelProgress.CreateDefault();

        Assert.True(LevelRules.IsUnlocked(progress, LevelTable.First));
        Assert.False(LevelRules.IsUnlocked(progress, LevelTable.At(1)!));
        Assert.False(LevelRules.IsUnlocked(progress, LevelTable.Last));
        Assert.Equal(1, LevelRules.UnlockedCount(progress));
        Assert.Equal(LevelTable.First.Id, LevelRules.NextUnclearedLevel(progress)!.Id);

        // 通关第一关 → 解锁第二关
        LevelOutcome outcome = LevelRules.Settle(progress, LevelTable.First, won: true, elapsedSeconds: 30);
        Assert.Equal(2, LevelRules.UnlockedCount(progress));
        Assert.True(LevelRules.IsUnlocked(progress, LevelTable.At(1)!));
        Assert.False(LevelRules.IsUnlocked(progress, LevelTable.At(2)!));
        Assert.Equal(LevelTable.At(1)!.Id, LevelRules.NextUnclearedLevel(progress)!.Id);

        // 结算结果里报告了"刚解锁的那一关"
        Assert.NotNull(outcome.UnlockedNext);
        Assert.Equal(LevelTable.At(1)!.Id, outcome.UnlockedNext!.Id);
    }

    [Fact]
    public void 失败不解锁只累加尝试次数()
    {
        var progress = LevelProgress.CreateDefault();

        LevelOutcome failure = LevelRules.Settle(progress, LevelTable.First, won: false, elapsedSeconds: 12);

        Assert.False(failure.Won);
        Assert.Equal(0, failure.Stars);
        Assert.Null(failure.UnlockedNext);
        Assert.False(progress.IsCleared(LevelTable.First.Id));
        Assert.Equal(1, LevelRules.UnlockedCount(progress));

        // 失败也会被记下来（战绩展示用），但不留成绩
        Assert.Equal(1, progress.Find(LevelTable.First.Id)!.Attempts);
        Assert.Equal(0, progress.BestSecondsOf(LevelTable.First.Id) ?? 0);
    }

    [Fact]
    public void 星数只升不降最好成绩只取更快()
    {
        var progress = LevelProgress.CreateDefault();
        LevelDefinition level = LevelTable.First;

        LevelOutcome slow = LevelRules.Settle(progress, level, won: true, elapsedSeconds: 200);
        Assert.Equal(1, slow.Stars);
        Assert.True(slow.IsNewBest);
        Assert.True(slow.FirstClear);
        Assert.Equal(200, slow.BestSeconds, 3);

        LevelOutcome fast = LevelRules.Settle(progress, level, won: true, elapsedSeconds: 40);
        Assert.Equal(3, fast.Stars);
        Assert.True(fast.IsNewBest);
        Assert.False(fast.FirstClear);
        Assert.Equal(40, fast.BestSeconds, 3);

        // 再来一局更慢的：星数不回退、最好成绩不变
        LevelOutcome slower = LevelRules.Settle(progress, level, won: true, elapsedSeconds: 300);
        Assert.Equal(1, slower.Stars);              // 本局星数
        Assert.False(slower.IsNewBest);
        Assert.Equal(40, slower.BestSeconds, 3);    // 历史最好仍然是 40
        Assert.Equal(3, progress.StarsOf(level.Id)); // 存档里的星数保持 3
        Assert.Equal(3, progress.Find(level.Id)!.Attempts);
    }

    [Fact]
    public void 星数合计与通关计数()
    {
        var progress = LevelProgress.CreateDefault();

        Assert.Equal(0, progress.ClearedCount);
        Assert.Equal(0, progress.TotalStars);
        Assert.Equal(LevelTable.Count * 3, LevelTable.Count * LevelRules.MaxStars);

        LevelRules.Settle(progress, LevelTable.At(0)!, won: true, elapsedSeconds: 30);   // 3 星
        LevelRules.Settle(progress, LevelTable.At(1)!, won: true, elapsedSeconds: 200);  // 1 星

        Assert.Equal(2, progress.ClearedCount);
        Assert.Equal(4, progress.TotalStars);
    }

    // ------------------------------------------------------------ 进度规范化

    [Fact]
    public void 规范化合并重复项并夹住非法值()
    {
        var progress = new LevelProgress
        {
            Records = new List<LevelRecord>
            {
                new() { LevelId = "1-1", Cleared = true, Stars = 9, BestSeconds = 40, Attempts = 2 },
                new() { LevelId = "1-1", Cleared = true, Stars = 2, BestSeconds = 30, Attempts = 3 },
                new() { LevelId = "1-2", Cleared = false, Stars = 3, BestSeconds = 10, Attempts = -5 },
                new() { LevelId = "  ", Cleared = true, Stars = 3 },
                new() { LevelId = "9-9", Cleared = true, Stars = 3, BestSeconds = 5 },
                new() { LevelId = "1-3", Cleared = true, Stars = 2, BestSeconds = double.NaN },
            },
        };

        progress.Normalize();

        // 重复项合并：星数取大、成绩取快、尝试次数相加
        LevelRecord merged = progress.Find("1-1")!;
        Assert.Equal(3, merged.Stars);
        Assert.Equal(30, merged.BestSeconds, 3);
        Assert.Equal(5, merged.Attempts);

        // 未通关的条目不该有星数和成绩
        LevelRecord notCleared = progress.Find("1-2")!;
        Assert.Equal(0, notCleared.Stars);
        Assert.Equal(0, notCleared.BestSeconds);
        Assert.Equal(0, notCleared.Attempts);

        // 空 Id 被丢掉；未知关卡 Id 保留；NaN 变成 0
        Assert.Null(progress.Find("  "));
        Assert.NotNull(progress.Find("9-9"));
        Assert.Equal(0, progress.Find("1-3")!.BestSeconds);

        // 已知关卡按关卡表顺序排在前面
        var knownOrder = progress.Records
            .Where(r => LevelTable.FindById(r.LevelId) is not null)
            .Select(r => r.LevelId)
            .ToArray();
        Assert.Equal(new[] { "1-1", "1-2", "1-3" }, knownOrder);
    }

    // ------------------------------------------------------------ 存档

    [Fact]
    public void 关卡进度存取往返一致且不留临时文件()
    {
        string path = TestPaths.NewFile("levelprogress.json");
        var store = new LevelProgressStore(path);

        LevelProgressLoadResult created = store.Load();
        Assert.Equal(LevelProgressLoadStatus.CreatedNew, created.Status);
        Assert.True(File.Exists(path));

        LevelRules.Settle(created.Progress, LevelTable.At(0)!, won: true, elapsedSeconds: 33.5);
        LevelRules.Settle(created.Progress, LevelTable.At(1)!, won: true, elapsedSeconds: 88);
        store.Save(created.Progress);

        LevelProgressLoadResult reloaded = new LevelProgressStore(path).Load();
        Assert.Equal(LevelProgressLoadStatus.Loaded, reloaded.Status);
        Assert.Equal(2, reloaded.Progress.ClearedCount);
        Assert.Equal(3, reloaded.Progress.StarsOf("1-1"));
        Assert.Equal(33.5, reloaded.Progress.BestSecondsOf("1-1")!.Value, 3);
        Assert.Equal(1, reloaded.Progress.Find("1-2")!.Attempts);

        string directory = Path.GetDirectoryName(path)!;
        Assert.Empty(Directory.GetFiles(directory, "*.tmp"));

        // 存档里只该有"真正要持久化的数据"：计算属性（ClearedCount / TotalStars）不能写进去
        string json = File.ReadAllText(path);
        Assert.Contains("records", json);
        Assert.DoesNotContain("ClearedCount", json);
        Assert.DoesNotContain("TotalStars", json);
    }

    [Fact]
    public void 关卡进度损坏时备份重建()
    {
        string path = TestPaths.NewFile("levelprogress.json");
        File.WriteAllText(path, "{ 这不是合法 JSON");

        LevelProgressLoadResult result = new LevelProgressStore(path).Load();

        Assert.Equal(LevelProgressLoadStatus.RecoveredFromCorrupt, result.Status);
        Assert.True(result.HasWarning);
        Assert.NotNull(result.BackupPath);
        Assert.True(File.Exists(result.BackupPath!));
        Assert.Equal(0, result.Progress.ClearedCount);

        // 备份保留了原始内容，且新文件是合法 JSON
        Assert.Contains("这不是合法 JSON", File.ReadAllText(result.BackupPath!));
        Assert.Equal(LevelProgressLoadStatus.Loaded, new LevelProgressStore(path).Load().Status);
    }

    [Fact]
    public void 关卡进度版本过新时拒读且不覆盖()
    {
        string path = TestPaths.NewFile("levelprogress.json");
        string original = """{ "schemaVersion": 99, "records": [] }""";
        File.WriteAllText(path, original);

        LevelProgressLoadResult result = new LevelProgressStore(path).Load();

        Assert.Equal(LevelProgressLoadStatus.RejectedVersionTooNew, result.Status);
        Assert.True(result.HasWarning);
        Assert.NotNull(result.BackupPath);

        // 原文件一个字节都不能被动
        Assert.Equal(original, File.ReadAllText(path));
    }

    // ------------------------------------------------------------ 隐藏关
    //
    // 隐藏关与主线关卡有三点不同，这三条都是需求：
    //   ① 不在主线关卡的链条里（不参与解锁链与 x/N 统计）
    //   ② 雷数 100+ 且每次触发随机、盘面也随之不同
    //   ③ 解锁状态与成绩都**不落盘**（关掉游戏就消失）
    //   ④ 不给任何道具

    [Fact]
    public void 隐藏关不在主线三十六关里()
    {
        LevelDefinition secret = LevelTable.CreateSecret(seed: 1);

        Assert.True(secret.IsSecret);
        Assert.DoesNotContain(LevelTable.All, l => l.Id == secret.Id);
        Assert.Equal(36, LevelTable.Count);

        // 主线全通关也不会顺带解锁隐藏关，统计仍然只算主线关数
        var progress = LevelProgress.CreateDefault();
        foreach (LevelDefinition level in LevelTable.All)
        {
            LevelRules.Settle(progress, level, won: true, elapsedSeconds: 10);
        }

        Assert.Equal(36, progress.ClearedCount);
        Assert.Equal(36 * 3, progress.TotalStars);
        Assert.Equal(LevelTable.Count, LevelRules.UnlockedCount(progress));
        Assert.False(LevelRules.IsUnlocked(progress, secret));
    }

    [Fact]
    public void 隐藏关雷数随机且都大于一百()
    {
        var seen = new HashSet<int>();

        for (int i = 0; i < 200; i++)
        {
            LevelDefinition secret = LevelTable.CreateSecret();

            Assert.True(secret.MineCount > 100, $"雷数必须大于 100，实际 {secret.MineCount}");
            Assert.InRange(secret.MineCount, LevelTable.SecretMinMines, LevelTable.SecretMaxMines);
            Assert.Equal(LevelTable.SecretWidth, secret.Width);
            Assert.Equal(LevelTable.SecretHeight, secret.Height);
            Assert.True(secret.IsSecret);

            // 不给任何道具 → 纯经典模式
            Assert.Equal(0, secret.MagnifierCount);
            Assert.False(secret.ItemsAvailable);
            Assert.Equal(GameMode.ClassicPure, secret.Mode);

            Assert.True(
                GameLimits.ValidateParameters(secret.Width, secret.Height, secret.MineCount, out string? error),
                error);

            Assert.True(secret.ChallengeSeconds < secret.TargetSeconds);
            seen.Add(secret.MineCount);
        }

        // 200 次抽样不可能只摇出寥寥几种雷数
        Assert.True(seen.Count > 5, $"雷数应当随机分布，实际只出现 {seen.Count} 种取值。");
    }

    [Fact]
    public void 每次触发的隐藏关盘面都不同()
    {
        LevelDefinition a = LevelTable.CreateSecret();
        LevelDefinition b = LevelTable.CreateSecret();

        Assert.True(
            a.MineCount != b.MineCount || a.Seed != b.Seed,
            "两次触发得到了完全相同的隐藏关（雷数与种子都一样）。");
    }

    [Fact]
    public void 指定雷数与种子时隐藏关可复现()
    {
        LevelDefinition a = LevelTable.CreateSecret(mineCount: 128, seed: 4242);
        LevelDefinition b = LevelTable.CreateSecret(mineCount: 128, seed: 4242);

        Assert.Equal(128, a.MineCount);
        Assert.Equal(4242, a.Seed);
        Assert.Equal(a.BoardText, b.BoardText);
        Assert.Equal("20×20 / 128 雷", a.BoardText);

        // 雷数被夹在合法范围内
        Assert.Equal(LevelTable.SecretMinMines, LevelTable.CreateSecret(mineCount: 3).MineCount);
        Assert.Equal(LevelTable.SecretMaxMines, LevelTable.CreateSecret(mineCount: 99999).MineCount);
    }

    /// <summary>
    /// 隐藏关用"首点后才布雷"（原版行为）：**开局是全新未翻开的盘面**。
    ///
    /// 这条是玩家报的 bug 的回归测试：原来隐藏关跟主线关卡一样用锁定盘面，
    /// 而锁定盘面会在开局时替玩家翻开首点区域，于是"一打开隐藏关就有一块被点开了"。
    /// </summary>
    [Fact]
    public void 隐藏关开局是全新未翻开的盘面()
    {
        LevelDefinition secret = LevelTable.CreateSecret(mineCount: 120, seed: 31337);
        GameSession session = CreateLevel(secret);

        Assert.False(session.Locked);
        Assert.Equal(GameStatus.NotStarted, session.Status);
        Assert.Empty(session.Board.GetMineIndexes());   // 还没布雷
        Assert.Equal(120, session.RemainingMines);      // 计数器直接显示关卡雷数

        for (int i = 0; i < session.Board.CellCount; i++)
        {
            Assert.False(session.Board.IsRevealed(i), $"第 {i} 格不该在开局就被翻开。");
        }
    }

    /// <summary>首点才布雷、才计时；且首点与 8 邻格无雷（原版保证）。</summary>
    [Fact]
    public void 隐藏关首点安全且从首点开始计时()
    {
        LevelDefinition secret = LevelTable.CreateSecret(mineCount: 120, seed: 31337);
        GameSession session = CreateLevel(secret);

        Assert.Equal(0, session.ElapsedSeconds);

        int first = session.Board.Index(10, 10);
        session.Reveal(first);

        Assert.Equal(GameStatus.InProgress, session.Status);
        Assert.True(session.Board.IsRevealed(first));
        Assert.False(session.Board.IsMine(first));

        foreach (int neighbor in session.Board.Neighbors8(first))
        {
            Assert.False(session.Board.IsMine(neighbor));
        }

        Assert.Equal(120, session.Board.GetMineIndexes().Length);
    }

    /// <summary>同一颗种子 + 同一个首点 → 同一张盘（原版的确定性来源）。</summary>
    [Fact]
    public void 隐藏关同种子同首点得到同一张盘()
    {
        LevelDefinition secret = LevelTable.CreateSecret(mineCount: 120, seed: 31337);

        GameSession a = CreateLevel(secret);
        GameSession b = CreateLevel(secret);
        a.Reveal(a.Board.Index(10, 10));
        b.Reveal(b.Board.Index(10, 10));

        Assert.Equal(a.Board.GetMineIndexes(), b.Board.GetMineIndexes());
        Assert.Equal(a.Board.FirstClickIndex, b.Board.FirstClickIndex);
        Assert.True(a.Locked == b.Locked);
    }

    /// <summary>
    /// 关键行为：隐藏关的**解锁状态与成绩都不落盘**。
    /// 于是关掉游戏再开，隐藏关就消失了；存档文件里也留不下任何痕迹（不会被翻存档剧透）。
    /// </summary>
    [Fact]
    public void 隐藏关解锁与成绩都不落盘()
    {
        string path = TestPaths.NewFile("levelprogress.json");
        var store = new LevelProgressStore(path);

        LevelProgress progress = store.Load().Progress;
        LevelDefinition secret = LevelTable.CreateSecret(mineCount: 120, seed: 777);

        // 解锁 + 通关隐藏关；顺带把主线第 1 关也通了，用来确认主线照常落盘
        progress.SecretUnlocked = true;
        LevelRules.Settle(progress, LevelTable.First, won: true, elapsedSeconds: 30);
        LevelRules.Settle(progress, secret, won: true, elapsedSeconds: 300);
        store.Save(progress);

        string json = File.ReadAllText(path);

        Assert.DoesNotContain(LevelTable.SecretId, json);
        Assert.DoesNotContain("secretUnlocked", json);
        Assert.DoesNotContain("secret", json, StringComparison.OrdinalIgnoreCase);

        // 重新加载：隐藏关消失、解锁状态归零，但主线成绩还在
        LevelProgress reloaded = new LevelProgressStore(path).Load().Progress;
        Assert.False(reloaded.SecretUnlocked);
        Assert.False(LevelRules.IsUnlocked(reloaded, secret));
        Assert.Null(reloaded.Find(secret.Id));
        Assert.True(reloaded.IsCleared(LevelTable.First.Id));
    }

    [Fact]
    public void 隐藏关结算不发下一关但记成绩()
    {
        var progress = LevelProgress.CreateDefault();
        LevelDefinition secret = LevelTable.CreateSecret(mineCount: 101, seed: 5);

        LevelOutcome outcome = LevelRules.Settle(progress, secret, won: true, elapsedSeconds: 100);

        Assert.Null(outcome.UnlockedNext);   // 没有"下一关"
        Assert.Equal(3, outcome.Stars);      // 101 雷 → 挑战 545s
        Assert.Equal(3, progress.StarsOf(secret.Id));
        Assert.Equal(1, progress.Find(secret.Id)!.Attempts);

        // 不进主线统计
        Assert.Equal(0, progress.ClearedCount);
        Assert.Equal(0, progress.TotalStars);
    }

    [Fact]
    public void 隐藏关解锁开关可置位()
    {
        var progress = LevelProgress.CreateDefault();
        LevelDefinition secret = LevelTable.CreateSecret(seed: 3);

        Assert.False(progress.SecretUnlocked);
        Assert.False(LevelRules.IsUnlocked(progress, secret));

        progress.SecretUnlocked = true;
        Assert.True(LevelRules.IsUnlocked(progress, secret));

        // "继续挑战"不会跳到隐藏关
        Assert.Equal(LevelTable.First.Id, LevelRules.NextUnclearedLevel(progress)!.Id);
    }
    // ------------------------------------------------------------ 辅助

    private static ItemInventory CreateLevelInventory(LevelDefinition level)
    {
        // 关卡用独立的临时物品栏：固定数量、不吃账号库存，也不额外发首次赠送
        var progress = PlayerProgress.CreateDefault();
        progress.InitialGiftGranted = true;
        progress.MagnifierCount = level.MagnifierCount;
        return new ItemInventory(progress, TestFactory.Config(threshold: 999, maxHold: 99, initialGift: 0));
    }

    /// <summary>
    /// 把关卡盘的每一个安全格都翻开，应当判胜。
    /// 这条走的是真人路径：先点一格（锁定盘面会被钉到安全点）、再把剩下的安全格点完。
    /// </summary>
    [Fact]
    public void 关卡盘翻开全部安全格后判胜()
    {
        LevelDefinition level = LevelTable.First;   // 1-1，9×9 / 10 雷
        GameSession session = CreateLevel(level);
        Board board = session.Board;
        int safeTotal = board.CellCount - board.MineCount;

        // 首点：随便点一格都行，锁定盘面会把它钉到安全点
        session.Reveal(0);
        PumpFlood(session);

        for (int i = 0; i < board.CellCount; i++)
        {
            if (!board.IsMine(i))
            {
                session.Reveal(i);
                PumpFlood(session);
            }
        }

        Assert.Equal(safeTotal, session.RevealedCount);

        var unrevealed = new List<int>();
        for (int i = 0; i < board.CellCount; i++)
        {
            if (!board.IsMine(i) && !board.IsRevealed(i))
            {
                unrevealed.Add(i);
            }
        }

        Assert.True(unrevealed.Count == 0, "未翻开的安全格：" + string.Join(",", unrevealed));
        Assert.Equal(GameStatus.Won, session.Status);
    }

    /// <summary>把挂起的展开一次推完（UI 定时器做的就是这件事）。</summary>
    private static void PumpFlood(GameSession session)
    {
        while (session.HasPendingFlood)
        {
            session.ContinuePendingFlood(GameSession.FloodChunkPerFrame);
        }
    }

    /// <summary>
    /// 关卡面板"上一关 / 下一关"按钮依据的核心规则：
    /// 上一关只受"是不是第一关"限制；下一关还要**已解锁**才算能进。
    /// </summary>
    [Fact]
    public void 前后跳转按顺序与解锁状态决定()
    {
        LevelProgress progress = LevelProgress.CreateDefault();

        // 进度为空：只有 1-1 开放
        LevelDefinition first = LevelTable.First;               // 1-1
        Assert.Null(LevelTable.At(first.Index - 1));            // 第一关没有上一关
        LevelDefinition? lockedNext = LevelTable.Next(first);   // 1-2
        Assert.NotNull(lockedNext);
        Assert.False(LevelRules.IsUnlocked(progress, lockedNext!));   // 还没通关 → "下一关"该置灰

        // 通关 1-1 之后：1-2 解锁，"下一关"可点；1-1 仍然没有上一关
        progress.GetOrCreate(first.Id).Cleared = true;
        Assert.True(LevelRules.IsUnlocked(progress, lockedNext!));

        // 走到 1-2 之后：上一关回到 1-1，且它必然已解锁（能站在 1-2 就说明 1-1 通了）
        LevelDefinition? back = LevelTable.At(lockedNext!.Index - 1);
        Assert.Equal(first.Id, back!.Id);
        Assert.True(LevelRules.IsUnlocked(progress, back));

        // 第 1 章最后一关（1-6）的下一关跨到第 2 章 2-1，1-6 没通关时它必须是锁着的
        LevelDefinition endOfChapter1 = LevelTable.FindById("1-6")!;
        LevelDefinition? chapter2First = LevelTable.Next(endOfChapter1);
        Assert.Equal("2-1", chapter2First!.Id);
        Assert.False(LevelRules.IsUnlocked(progress, chapter2First));

        // 最后一关没有下一关
        Assert.Null(LevelTable.Next(LevelTable.Last));
        // 第一关也没有上一关（防止 Index 1 那关被误判成"有上一关"到 0 号越界）
        Assert.Equal(0, LevelTable.First.Index);
    }

    /// <summary>隐藏关不在主线关卡链条里：既不参与前后跳转，也不会被解锁链影响。</summary>
    [Fact]
    public void 隐藏关不参与前后跳转()
    {
        LevelDefinition secret = LevelTable.CreateSecret(mineCount: 120, seed: 31337);

        Assert.True(secret.IsSecret);
        Assert.Equal(-1, secret.Index);                        // Index 为 -1，所以"上一关"必然取不到
        Assert.Null(LevelTable.At(secret.Index - 1));

        // 界面上隐藏关的"下一关"是直接置灰的（UI 走 level.IsSecret 分支），
        // 这里守的是同一条：它不在主线关卡链条里，不能被 LevelTable.Next 当成 1-1 的邻居。
        // 注意隐藏关的 Index 是 -1，若 Next 不显式挡掉，At(-1 + 1) 会返回 1-1。
        Assert.Null(LevelTable.Next(secret));

        // 解锁与否只由 SecretUnlocked 决定，与主线进度无关
        LevelProgress progress = LevelProgress.CreateDefault();
        Assert.False(LevelRules.IsUnlocked(progress, secret));

        progress.SecretUnlocked = true;
        Assert.True(LevelRules.IsUnlocked(progress, secret));
        Assert.Equal(0, progress.ClearedCount);                 // 主线进度一点没被带动
    }

    private static GameSession CreateLevel(LevelDefinition level, ItemInventory? inventory = null)
    {
        var settings = new GameSettings
        {
            Mode = level.Mode,
            FirstClickOpening = level.FirstClickOpening,
        };

        // 与 App 的 GameViewModel.StartLevel 保持一致：
        // 隐藏关走"首点后才布雷"，主线关卡走锁定盘面（开局替玩家翻开首点）。
        bool created = level.IsSecret
            ? GameSession.TryCreate(
                level.Width,
                level.Height,
                level.MineCount,
                level.Seed,
                settings,
                out GameSession? session,
                out string? error,
                inventory ?? CreateLevelInventory(level),
                clock: null,
                difficultyId: level.Id)
            : GameSession.TryCreateLocked(
                level.Width,
                level.Height,
                level.MineCount,
                level.Seed,
                settings,
                out session,
                out error,
                inventory ?? CreateLevelInventory(level),
                clock: null,
                difficultyId: level.Id);

        Assert.True(created, $"{level.DisplayName} 创建失败：{error}");
        return session!;
    }
}
