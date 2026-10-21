using MineSweeper.Core;
using MineSweeper.Core.Items;
using MineSweeper.Core.Persistence;
using MineSweeper.Core.Updates;
using Xunit;

namespace MineSweeper.Core.Tests;

/// <summary>§8 第 10 条：存档（schemaVersion 迁移、损坏降级）以及 §5.5 ⑥ 的记录分表。</summary>
public sealed class PersistenceTests
{
    [Fact]
    public void 保存后读取内容一致()
    {
        string path = TestPaths.NewFile("progress.json");
        var store = new ProgressStore(path);

        var progress = new PlayerProgress
        {
            CorrectMineFlagsTotal = 137,
            MagnifierCount = 2,
            MagnifierGranted = 5,
            InitialGiftGranted = true,
        };

        store.Save(progress);
        ProgressLoadResult loaded = store.Load();

        Assert.Equal(ProgressLoadStatus.Loaded, loaded.Status);
        Assert.Equal(137, loaded.Progress.CorrectMineFlagsTotal);
        Assert.Equal(2, loaded.Progress.MagnifierCount);
        Assert.Equal(5, loaded.Progress.MagnifierGranted);
        Assert.True(loaded.Progress.InitialGiftGranted);
    }

    [Fact]
    public void 文件不存在时新建默认存档()
    {
        string path = TestPaths.NewFile("progress.json");
        var store = new ProgressStore(path);

        ProgressLoadResult result = store.Load();

        Assert.Equal(ProgressLoadStatus.CreatedNew, result.Status);
        Assert.True(File.Exists(path));
        Assert.Equal(0, result.Progress.CorrectMineFlagsTotal);
    }

    /// <summary>§8-10(a)：损坏的 JSON 必须备份原文件并用默认值重建，而不是静默丢弃。</summary>
    [Fact]
    public void 损坏的JSON降级重建并保留备份()
    {
        string path = TestPaths.NewFile("progress.json");
        File.WriteAllText(path, "{ 这不是合法 JSON ");

        var store = new ProgressStore(path);
        ProgressLoadResult result = store.Load();

        Assert.Equal(ProgressLoadStatus.RecoveredFromCorrupt, result.Status);
        Assert.True(result.HasWarning);
        Assert.NotNull(result.BackupPath);
        Assert.True(File.Exists(result.BackupPath!), "损坏的原文件必须留下备份。");
        Assert.Contains("这不是合法 JSON", File.ReadAllText(result.BackupPath!));

        // 重建出来的存档必须能正常读写
        Assert.Equal(ProgressLoadStatus.Loaded, store.Load().Status);
    }

    /// <summary>§8-10(b)：schemaVersion 低于当前 → 迁移到当前版本，数据保留。</summary>
    [Fact]
    public void 低版本存档被迁移且数据保留()
    {
        string path = TestPaths.NewFile("progress.json");
        File.WriteAllText(path, """
            {
              "schemaVersion": 0,
              "correctMineFlagsTotal": 17,
              "magnifierCount": 1,
              "magnifierGranted": 1,
              "initialGiftGranted": true
            }
            """);

        var store = new ProgressStore(path);
        ProgressLoadResult result = store.Load();

        Assert.Equal(ProgressLoadStatus.Loaded, result.Status);
        Assert.Equal(PlayerProgress.CurrentSchemaVersion, result.Progress.SchemaVersion);
        Assert.Equal(17, result.Progress.CorrectMineFlagsTotal);
        Assert.Equal(1, result.Progress.MagnifierCount);
    }

    /// <summary>§8-10(c)：schemaVersion 高于当前（用户降级安装）→ 拒绝读取、备份、且不覆盖原文件。</summary>
    [Fact]
    public void 高版本存档被拒绝且不被覆盖()
    {
        string path = TestPaths.NewFile("progress.json");
        const string future = """
            {
              "schemaVersion": 99,
              "correctMineFlagsTotal": 500,
              "magnifierCount": 3
            }
            """;
        File.WriteAllText(path, future);

        var store = new ProgressStore(path);
        ProgressLoadResult result = store.Load();

        Assert.Equal(ProgressLoadStatus.RejectedVersionTooNew, result.Status);
        Assert.NotNull(result.BackupPath);
        Assert.True(File.Exists(result.BackupPath!));

        // 关键：原文件不能被降级运行的程序写坏
        Assert.Contains("\"schemaVersion\": 99", File.ReadAllText(path));
    }

    /// <summary>记录加载：不存在 / 损坏时都不抛异常。</summary>
    [Fact]
    public void 记录文件损坏时降级为空记录()
    {
        string path = TestPaths.NewFile("records.json");
        File.WriteAllText(path, "}}}");

        var store = new RecordsStore(path);
        GameRecords records = store.Reload();

        Assert.Empty(records.Classic);
        Assert.Empty(records.Custom);
        Assert.Equal(GameRecords.CurrentSchemaVersion, records.SchemaVersion);
    }

    /// <summary>
    /// §5.5 ⑥：用过道具的成绩必须进"带道具"表，不能与纯手工成绩混排。
    /// </summary>
    [Fact]
    public void 带道具成绩与纯手工成绩分表()
    {
        string path = TestPaths.NewFile("records.json");
        var store = new RecordsStore(path);
        GameRecords records = store.Reload();

        // 纯手工局：40 秒
        var pureClock = new ManualClock();
        ItemInventory pureInventory = TestFactory.Inventory();
        GameSession pure = TestFactory.Replay(
            5, 3, TestFactory.WallMines, 0, new GameSettings { Mode = GameMode.ClassicPure },
            pureInventory, clock: pureClock, difficultyId: DifficultyPreset.Beginner.Id);

        pure.Reveal(0);
        pureClock.AdvanceSeconds(40);
        pure.Reveal(4);
        Assert.Equal(GameStatus.Won, pure.Status);

        RecordSubmitResult pureSubmit = store.Submit(pure);
        Assert.True(pureSubmit.IsNewRecord);
        Assert.Equal("classic", pureSubmit.Table);

        // 带道具局：12.5 秒（更快，但必须进另一张表）
        var itemClock = new ManualClock();
        ItemInventory itemInventory = TestFactory.Inventory(TestFactory.Config(threshold: 999, maxHold: 3));
        itemInventory.TryGrantMagnifier();
        GameSession withItem = TestFactory.Replay(
            5, 3, TestFactory.WallMines, 0, new GameSettings { Mode = GameMode.ClassicWithItems },
            itemInventory, clock: itemClock, difficultyId: DifficultyPreset.Beginner.Id);

        withItem.Reveal(0);
        withItem.UseMagnifier(4); // 用掉一个道具，ItemUsed 置位
        itemClock.AdvanceSeconds(12.5);
        withItem.Reveal(4);
        Assert.Equal(GameStatus.Won, withItem.Status);

        RecordSubmitResult itemSubmit = store.Submit(withItem);
        Assert.True(itemSubmit.IsNewRecord);
        Assert.Equal("classicWithItem", itemSubmit.Table);

        store.Save(records);

        // 两张表各自独立
        Assert.Equal(40, records.Classic[DifficultyPreset.Beginner.Id].Seconds, 3);
        Assert.Equal(12.5, records.ClassicWithItem[DifficultyPreset.Beginner.Id].Seconds, 3);
        Assert.False(records.Classic[DifficultyPreset.Beginner.Id].ItemUsed);
        Assert.True(records.ClassicWithItem[DifficultyPreset.Beginner.Id].ItemUsed);

        // 落盘后重新读出来仍然分表
        var reloaded = new RecordsStore(path).Reload();
        Assert.Equal(40, reloaded.Classic[DifficultyPreset.Beginner.Id].Seconds, 3);
        Assert.Equal(12.5, reloaded.ClassicWithItem[DifficultyPreset.Beginner.Id].Seconds, 3);
    }

    /// <summary>更慢的成绩不覆盖既有记录。</summary>
    [Fact]
    public void 较慢的成绩不进记录()
    {
        var store = new RecordsStore(TestPaths.NewFile("records.json"));
        store.Reload();

        GameSession First(double seconds)
        {
            var clock = new ManualClock();
            GameSession session = TestFactory.Replay(
                5, 3, TestFactory.WallMines, 0, new GameSettings { Mode = GameMode.ClassicPure },
                null, clock: clock, difficultyId: DifficultyPreset.Beginner.Id);
            session.Reveal(0);
            clock.AdvanceSeconds(seconds);
            session.Reveal(4);
            return session;
        }

        Assert.True(store.Submit(First(30)).IsNewRecord);

        RecordSubmitResult slower = store.Submit(First(45));

        Assert.False(slower.IsNewRecord);
        Assert.Equal(30, slower.PreviousSeconds);
        Assert.Equal(30, store.Records.Classic[DifficultyPreset.Beginner.Id].Seconds, 3);
    }

    /// <summary>§6.3：同盘重开 —— 快照还原后盘面与雷位完全一致。</summary>
    [Fact]
    public void 快照还原出完全相同的盘面()
    {
        GameSession original = TestFactory.Custom(16, 16, 40, seed: 4242);
        original.Reveal(77);

        BoardSnapshot snapshot = original.CaptureSnapshot();
        GameSession replay = GameSession.CreateReplay(snapshot, new GameSettings { Mode = GameMode.Custom });

        Assert.Equal(original.Board.GetMineIndexes(), replay.Board.GetMineIndexes());
        Assert.Equal(original.Seed, replay.Seed);
        Assert.Equal(original.Board.FirstClickIndex, replay.Board.FirstClickIndex);

        // 同盘重开：第一次点击仍然落在同一个首点上且安全
        RevealResult first = replay.Reveal(snapshot.FirstClickIndex);
        Assert.Equal(RevealOutcome.Revealed, first.Outcome);
    }

    /// <summary>种子分享串可往返解析（§2.6 / §6.3）。</summary>
    [Fact]
    public void 种子分享串可以往返解析()
    {
        // 默认开启"首点扩展安全区"，且这个盘面放得下 5×5 安全区（480 − 25 ≥ 99）
        GameSession session = TestFactory.Custom(30, 16, 99, seed: 123456);
        session.Reveal(100);

        string code = session.CaptureSnapshot().ToShareCode();

        Assert.True(BoardSnapshot.TryParseShareCode(code, out int width, out int height, out int mines, out int seed, out int firstClick, out bool extended));
        Assert.Equal(30, width);
        Assert.Equal(16, height);
        Assert.Equal(99, mines);
        Assert.Equal(123456, seed);
        Assert.Equal(100, firstClick);
        Assert.True(extended, "开了扩展安全区时分享串里应当是 1。");

        // 关掉扩展安全区时应当是 0
        GameSession plain = TestFactory.Custom(30, 16, 99, seed: 123456, firstClickOpening: false);
        plain.Reveal(100);
        Assert.True(BoardSnapshot.TryParseShareCode(plain.CaptureSnapshot().ToShareCode(), out _, out _, out _, out _, out _, out bool plainExtended));
        Assert.False(plainExtended);

        Assert.False(BoardSnapshot.TryParseShareCode("乱写的字符串", out _, out _, out _, out _, out _, out _));
    }

    /// <summary>道具配置的 JSON 键名就是文档里那些点号键，方便手改调参（§5.7）。</summary>
    [Fact]
    public void 道具配置键名与文档一致()
    {
        string json = System.Text.Json.JsonSerializer.Serialize(TestFactory.Config());
        Assert.Contains("item.magnifier.threshold", json);
        Assert.Contains("item.magnifier.maxHold", json);
        Assert.Contains("item.magnifier.initialGift", json);

        // v1.4：半径换成宽高（放大镜削弱成 2×2），并新增商店价格键
        Assert.Contains("item.magnifier.width", json);
        Assert.Contains("item.magnifier.height", json);
        Assert.Contains("shop.magnifier.price", json);
        Assert.Contains("shop.sonar.price", json);
        Assert.Contains("shop.freeze.price", json);
    }

    // ------------------------------------------------------------ 版本过新：拒读 + 禁写
    //
    // 这批用例针对的是"只拒读、不禁写"留下的坑：Load 因为 schemaVersion 过新而拒读并
    // 返回默认对象后，App 会拿着默认对象继续跑，关窗时照常 Save，于是更新版本写的存档
    // 被这份默认值覆盖。所以每个 Store 都要验一遍：拒读之后再 Save，磁盘文件一个字节都不变。

    [Fact]
    public void 进度版本过新后保存被禁止()
    {
        string path = TestPaths.NewFile("progress.json");
        const string future = """{ "schemaVersion": 99, "correctMineFlagsTotal": 500 }""";
        File.WriteAllText(path, future);

        var store = new ProgressStore(path);
        ProgressLoadResult result = store.Load();

        Assert.Equal(ProgressLoadStatus.RejectedVersionTooNew, result.Status);
        Assert.True(store.SaveBlocked, "拒读时应当同时标记为禁止保存。");
        Assert.NotNull(result.BackupPath);
        Assert.True(File.Exists(result.BackupPath!));

        // 模拟 App 用默认对象继续玩、关窗时照常保存
        store.Save(result.Progress);

        Assert.Equal(future, File.ReadAllText(path));
    }

    [Fact]
    public void 设置版本过新后保存被禁止()
    {
        string path = TestPaths.NewFile("settings.json");
        const string future = """{ "schemaVersion": 99, "audioEnabled": false }""";
        File.WriteAllText(path, future);

        var store = new AppSettingsStore(path);
        SettingsLoadResult result = store.Load();

        Assert.Equal(SettingsLoadStatus.RejectedVersionTooNew, result.Status);
        Assert.True(store.SaveBlocked, "拒读时应当同时标记为禁止保存。");
        Assert.NotNull(result.BackupPath);
        Assert.True(File.Exists(result.BackupPath!));

        store.Save(result.Settings);

        Assert.Equal(future, File.ReadAllText(path));
    }

    [Fact]
    public void 关卡进度版本过新后保存被禁止()
    {
        string path = TestPaths.NewFile("levelprogress.json");
        const string future = """{ "schemaVersion": 99, "records": [] }""";
        File.WriteAllText(path, future);

        var store = new LevelProgressStore(path);
        LevelProgressLoadResult result = store.Load();

        Assert.Equal(LevelProgressLoadStatus.RejectedVersionTooNew, result.Status);
        Assert.True(store.SaveBlocked, "拒读时应当同时标记为禁止保存。");
        Assert.NotNull(result.BackupPath);
        Assert.True(File.Exists(result.BackupPath!));

        store.Save(result.Progress);

        Assert.Equal(future, File.ReadAllText(path));
    }

    [Fact]
    public void 成就版本过新后保存被禁止()
    {
        string path = TestPaths.NewFile("achievements.json");
        const string future = """{ "schemaVersion": 99, "records": [] }""";
        File.WriteAllText(path, future);

        var store = new AchievementStore(path);
        AchievementLoadResult result = store.Load();

        Assert.Equal(AchievementLoadStatus.RejectedVersionTooNew, result.Status);
        Assert.True(store.SaveBlocked, "拒读时应当同时标记为禁止保存。");
        Assert.NotNull(result.BackupPath);
        Assert.True(File.Exists(result.BackupPath!));

        store.Save(result.Progress);

        Assert.Equal(future, File.ReadAllText(path));
    }

    [Fact]
    public void 更新状态版本过新后保存被禁止()
    {
        string path = TestPaths.NewFile("updatestate.json");
        const string future = """{ "schemaVersion": 99, "skippedVersion": "9.9" }""";
        File.WriteAllText(path, future);

        var store = new UpdateStateStore(path);
        UpdateStateLoadResult result = store.Load();

        Assert.Equal(UpdateStateLoadStatus.RejectedVersionTooNew, result.Status);
        Assert.True(store.SaveBlocked, "拒读时应当同时标记为禁止保存。");
        Assert.NotNull(result.BackupPath);
        Assert.True(File.Exists(result.BackupPath!));

        store.Save(result.State);

        Assert.Equal(future, File.ReadAllText(path));
    }

    [Fact]
    public void 历史版本过新后保存被禁止()
    {
        string path = TestPaths.NewFile("history.json");
        const string future = """{ "schemaVersion": 99, "totalMinesSwept": 999 }""";
        File.WriteAllText(path, future);

        var store = new HistoryStore(path);
        HistoryLoadResult result = store.Load();

        Assert.Equal(HistoryLoadStatus.RejectedVersionTooNew, result.Status);
        Assert.True(store.SaveBlocked, "拒读时应当同时标记为禁止保存。");
        Assert.NotNull(result.BackupPath);
        Assert.True(File.Exists(result.BackupPath!));

        store.Save(result.History);

        Assert.Equal(future, File.ReadAllText(path));
    }

    /// <summary>
    /// records.json 没有 LoadStatus 返回值，"过新"以前被当成损坏处理；这条用例确认
    /// 它也会记下"拒读过"，因此保存被禁、新版文件不被空记录覆盖。
    /// </summary>
    [Fact]
    public void 记录版本过新后保存被禁止()
    {
        string path = TestPaths.NewFile("records.json");
        const string future = """{ "schemaVersion": 99, "classic": { "beginner": { "seconds": 1 } } }""";
        File.WriteAllText(path, future);

        var store = new RecordsStore(path);
        GameRecords records = store.Reload();

        Assert.True(store.SaveBlocked, "版本过新被当作损坏降级时，也必须禁止保存。");
        Assert.Empty(records.Classic);
        Assert.NotEmpty(Directory.GetFiles(Path.GetDirectoryName(path)!, "records.corrupt.*.json"));

        store.Save(records);

        Assert.Equal(future, File.ReadAllText(path));
    }

    // ------------------------------------------------------------ config.json 的健壮性

    /// <summary>文件不存在时创建默认配置并落盘，第一份文件就该带着当前版本号。</summary>
    [Fact]
    public void 道具配置不存在时创建默认并落盘()
    {
        string path = TestPaths.NewFile("config.json");

        ItemConfig config = ItemConfigStore.Load(path);

        Assert.Equal(ItemConfig.CurrentSchemaVersion, config.SchemaVersion);
        Assert.True(File.Exists(path));

        string json = File.ReadAllText(path);
        Assert.Contains($"\"schemaVersion\": {ItemConfig.CurrentSchemaVersion}", json);
        Assert.Contains("item.magnifier.maxHold", json);
    }

    /// <summary>v1 老配置：迁移后立即写回（否则每次启动都要重迁，且看不到新增的键）。</summary>
    [Fact]
    public void 旧版道具配置迁移后写回()
    {
        string path = TestPaths.NewFile("config.json");
        File.WriteAllText(path, """
            {
              "schemaVersion": 1,
              "item.magnifier.threshold": 20,
              "item.magnifier.maxHold": 3,
              "item.magnifier.initialGift": 1,
              "item.magnifier.radius": 1
            }
            """);

        ItemConfig config = ItemConfigStore.Load(path);

        Assert.Equal(ItemConfig.CurrentSchemaVersion, config.SchemaVersion);
        Assert.Equal(ItemConfig.DefaultMaxHold, config.MagnifierMaxHold);

        // 迁移结果必须落盘：磁盘上是新版本号与提上去的持有上限
        string json = File.ReadAllText(path);
        Assert.Contains($"\"schemaVersion\": {ItemConfig.CurrentSchemaVersion}", json);
        Assert.Contains($"\"item.magnifier.maxHold\": {ItemConfig.DefaultMaxHold}", json);
    }

    /// <summary>损坏的配置先备份成 config.corrupt.*.json，再重建一份默认配置。</summary>
    [Fact]
    public void 损坏的道具配置备份后降级默认()
    {
        string path = TestPaths.NewFile("config.json");
        File.WriteAllText(path, "{ 这不是合法 JSON ");

        ItemConfig config = ItemConfigStore.Load(path);

        Assert.Equal(ItemConfig.CurrentSchemaVersion, config.SchemaVersion);

        string[] backups = Directory.GetFiles(Path.GetDirectoryName(path)!, "config.corrupt.*.json");
        Assert.Single(backups);
        Assert.Contains("这不是合法 JSON", File.ReadAllText(backups[0]));

        // 备份之后要重建默认配置：否则原文件一直坏着，每次启动都多一份 corrupt 备份
        Assert.True(File.Exists(path));
        Assert.Contains($"\"schemaVersion\": {ItemConfig.CurrentSchemaVersion}", File.ReadAllText(path));

        // 重建后的文件应当是正常的：再读一次不再产生第二份备份
        ItemConfig reloaded = ItemConfigStore.Load(path);
        Assert.Equal(ItemConfig.CurrentSchemaVersion, reloaded.SchemaVersion);
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(path)!, "config.corrupt.*.json"));
    }

    /// <summary>Save 走原子写：落盘成功后不能留下 .tmp 残留文件。</summary>
    [Fact]
    public void 道具配置保存后不留临时文件()
    {
        string path = TestPaths.NewFile("config.json");
        ItemConfig config = ItemConfig.CreateDefault();
        config.MagnifierMaxHold = 5;

        ItemConfigStore.Save(config, path);

        Assert.True(File.Exists(path));
        Assert.False(File.Exists(path + ".tmp"), "原子写完成后不应残留 .tmp 文件。");
        Assert.Contains("\"item.magnifier.maxHold\": 5", File.ReadAllText(path));
    }

    /// <summary>
    /// 迁移后的写回失败（目录被占/磁盘不可写）只吞掉写文件异常，
    /// 不能整体回默认：玩家手改的配置还在内存里，本次运行就该按它来。
    /// 用"占住 .tmp 路径"来稳定制造写失败。
    /// </summary>
    [Fact]
    public void 道具配置迁移写回失败时仍返回迁移结果()
    {
        string path = TestPaths.NewFile("config.json");
        File.WriteAllText(path, """{ "schemaVersion": 1, "item.magnifier.maxHold": 3 }""");
        Directory.CreateDirectory(path + ".tmp");

        ItemConfig config = ItemConfigStore.Load(path);

        Assert.Equal(ItemConfig.CurrentSchemaVersion, config.SchemaVersion);
        Assert.Equal(ItemConfig.DefaultMaxHold, config.MagnifierMaxHold);
    }
}
