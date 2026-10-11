using MineSweeper.Core;
using MineSweeper.Core.Items;

namespace MineSweeper.Core.Tests;

/// <summary>
/// 测试用的临时目录。
/// 刻意落在测试程序集输出目录下（E 盘，随 bin 一起清理），
/// 而不是系统临时目录——这台机器 C 盘空间紧张，测试也不该往 C 盘写东西。
/// </summary>
internal static class TestPaths
{
    public static string NewDirectory()
    {
        string root = Path.Combine(AppContext.BaseDirectory, "artifacts", "tests");
        string dir = Path.Combine(root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static string NewFile(string fileName)
    {
        return Path.Combine(NewDirectory(), fileName);
    }
}

/// <summary>构造对局的便捷工厂，避免每个用例重复写一堆参数。</summary>
internal static class TestFactory
{
    public const int DefaultSeed = 20261008;

    public static ItemConfig Config(int threshold = 20, int maxHold = 3, int initialGift = 0, int radius = 1)
        => new()
        {
            MagnifierThreshold = threshold,
            MagnifierMaxHold = maxHold,
            MagnifierInitialGift = initialGift,
            SonarInitialGift = initialGift,
            FreezeInitialGift = initialGift,
            MagnifierRadius = radius,
        };

    public static ItemInventory Inventory(ItemConfig? config = null, PlayerProgress? progress = null)
        => new(progress ?? PlayerProgress.CreateDefault(), config ?? Config());

    /// <summary>自定义模式（道具可用）的对局。</summary>
    public static GameSession Custom(
        int width,
        int height,
        int mines,
        ItemInventory? inventory = null,
        int seed = DefaultSeed,
        bool choiceRewards = false,
        bool firstClickOpening = true,
        IClock? clock = null)
    {
        var settings = new GameSettings
        {
            Mode = GameMode.Custom,
            FirstClickOpening = firstClickOpening,
            ChoiceRewards = choiceRewards,
        };

        return Create(width, height, mines, settings, inventory, seed, clock, difficultyId: null);
    }

    /// <summary>纯经典模式（无道具）的对局。</summary>
    public static GameSession ClassicPure(int width, int height, int mines, int seed = DefaultSeed, IClock? clock = null)
    {
        var settings = new GameSettings
        {
            Mode = GameMode.ClassicPure,
            FirstClickOpening = false,
        };

        return Create(width, height, mines, settings, inventory: null, seed, clock, difficultyId: null);
    }

    /// <summary>按给定雷位直接还原一局（盘面完全确定，便于手算断言）。</summary>
    public static GameSession Replay(
        int width,
        int height,
        int[] mineIndexes,
        int firstClickIndex,
        GameSettings settings,
        ItemInventory? inventory = null,
        int seed = DefaultSeed,
        IClock? clock = null,
        string? difficultyId = null)
    {
        var snapshot = new BoardSnapshot(width, height, mineIndexes.Length, seed, firstClickIndex, UsedExtendedSafeZone: false, mineIndexes);
        return GameSession.CreateReplay(snapshot, settings, inventory, clock, difficultyId);
    }

    /// <summary>自定义模式 + 指定雷位的手算盘面。</summary>
    public static GameSession ReplayCustom(
        int width,
        int height,
        int[] mineIndexes,
        int firstClickIndex,
        ItemInventory? inventory = null,
        IClock? clock = null)
        => Replay(width, height, mineIndexes, firstClickIndex, new GameSettings { Mode = GameMode.Custom }, inventory, DefaultSeed, clock);

    /// <summary>
    /// §8 第 3 条用的"雷墙"盘面：5 宽 × 3 高，雷在 x = 2 整列（索引 2 / 7 / 12）。
    /// 左区（x=0,1）与右区（x=3,4）被雷列隔开，各 6 格 —— 展开范围可以手算。
    /// </summary>
    public static readonly int[] WallMines = { 2, 7, 12 };

    /// <summary>§8 第 5、6 条用的角雷盘面：3×3，雷在左上角（索引 0）。</summary>
    public static readonly int[] CornerMine = { 0 };

    /// <summary>
    /// 锁定盘面模式（§2.6）：雷位与安全首点都由 seed 决定，开局不替玩家翻开。
    /// </summary>
    public static GameSession Locked(
        int width,
        int height,
        int mines,
        int seed = DefaultSeed,
        bool choiceRewards = false,
        bool firstClickOpening = true,
        ItemInventory? inventory = null,
        IClock? clock = null)
    {
        var settings = new GameSettings
        {
            Mode = GameMode.Custom,
            FirstClickOpening = firstClickOpening,
            ChoiceRewards = choiceRewards,
        };

        if (!GameSession.TryCreateLocked(width, height, mines, seed, settings, out GameSession? session, out string? error, inventory, clock))
        {
            throw new InvalidOperationException($"测试锁定盘面创建失败：{error}");
        }

        return session!;
    }

    private static GameSession Create(
        int width,
        int height,
        int mines,
        GameSettings settings,
        ItemInventory? inventory,
        int seed,
        IClock? clock,
        string? difficultyId)
    {
        if (!GameSession.TryCreate(width, height, mines, seed, settings, out GameSession? session, out string? error, inventory, clock, difficultyId))
        {
            throw new InvalidOperationException($"测试对局创建失败：{error}");
        }

        return session!;
    }
}
