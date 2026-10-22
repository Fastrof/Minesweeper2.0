using MineSweeper.Core;
using MineSweeper.Core.Achievements;
using MineSweeper.Core.History;
using MineSweeper.Core.Items;
using MineSweeper.Core.Levels;
using MineSweeper.Core.Persistence;
using MineSweeper.Core.Settings;
using MineSweeper.Core.Updates;

namespace MineSweeper.App.Linux.Services;

/// <summary>
/// 8 份存档的装配与读写（对应 Windows 端 <c>AppPaths.CreateStorage</c> + 各 Store）。
///
/// 分工：**读写逻辑全在 Core 的 Store 里**（原子写盘、损坏备份、schemaVersion 拒写），
/// 这里只负责：按 Linux 路径创建 Store、汇总加载警告、把异常挡在游戏外面。
/// </summary>
public sealed class Storage
{
    private readonly ProgressStore _progressStore;
    private readonly RecordsStore _recordsStore;
    private readonly AppSettingsStore _settingsStore;
    private readonly LevelProgressStore _levelProgressStore;
    private readonly AchievementStore _achievementStore;
    private readonly HistoryStore _historyStore;
    private readonly UpdateStateStore _updateStateStore;

    private Storage(
        ProgressStore progressStore,
        RecordsStore recordsStore,
        AppSettingsStore settingsStore,
        LevelProgressStore levelProgressStore,
        AchievementStore achievementStore,
        HistoryStore historyStore,
        UpdateStateStore updateStateStore)
    {
        _progressStore = progressStore;
        _recordsStore = recordsStore;
        _settingsStore = settingsStore;
        _levelProgressStore = levelProgressStore;
        _achievementStore = achievementStore;
        _historyStore = historyStore;
        _updateStateStore = updateStateStore;
    }

    public PlayerProgress Progress { get; private set; } = PlayerProgress.CreateDefault();
    public GameRecords Records { get; private set; } = GameRecords.CreateDefault();
    public AppSettings Settings { get; private set; } = AppSettings.CreateDefault();
    public LevelProgress LevelProgress { get; private set; } = LevelProgress.CreateDefault();
    public AchievementProgress Achievements { get; private set; } = AchievementProgress.CreateDefault();
    public PlayHistory History { get; private set; } = PlayHistory.CreateDefault();
    public UpdateState UpdateState { get; private set; } = UpdateState.CreateDefault();
    public ItemConfig ItemConfig { get; private set; } = ItemConfig.CreateDefault();

    /// <summary>加载期间产生的可展示警告（损坏、版本过新等）。</summary>
    public List<string> Warnings { get; } = new();

    /// <summary>某个存档因"版本过新"被拒写时为 true（此时不能再 Save）。</summary>
    public bool SaveBlocked =>
        _progressStore.SaveBlocked || _recordsStore.SaveBlocked || _settingsStore.SaveBlocked
        || _levelProgressStore.SaveBlocked || _achievementStore.SaveBlocked || _updateStateStore.SaveBlocked;

    /// <summary>按 Linux 路径创建并加载全部存档。</summary>
    public static Storage Create()
    {
        var storage = new Storage(
            new ProgressStore(AppPaths.ProgressFile),
            new RecordsStore(AppPaths.RecordsFile),
            new AppSettingsStore(AppPaths.SettingsFile),
            new LevelProgressStore(AppPaths.LevelProgressFile),
            new AchievementStore(AppPaths.AchievementsFile),
            new HistoryStore(AppPaths.HistoryFile),
            new UpdateStateStore(AppPaths.UpdateStateFile));

        storage.LoadAll();
        return storage;
    }

    private void LoadAll()
    {
        ProgressLoadResult progress = _progressStore.Load();
        Progress = progress.Progress;
        AddWarning("progress", progress.HasWarning, progress.Message);

        Records = _recordsStore.Load();
        if (_recordsStore.SaveBlocked)
        {
            Warnings.Add("records.json 版本过新，已备份并禁止写回。");
        }

        SettingsLoadResult settings = _settingsStore.Load();
        Settings = settings.Settings;
        AddWarning("settings", settings.HasWarning, settings.Message);
        // 语言必须是合法值；非法值回退默认（Core 的 Strings.SetLanguage 也会兜底）
        Settings.Normalize();

        LevelProgressLoadResult levels = _levelProgressStore.Load();
        LevelProgress = levels.Progress;
        AddWarning("levelprogress", levels.HasWarning, levels.Message);

        AchievementLoadResult achievements = _achievementStore.Load();
        Achievements = achievements.Progress;
        AddWarning("achievements", achievements.HasWarning, achievements.Message);

        HistoryLoadResult history = _historyStore.Load();
        History = history.History;
        AddWarning("history", history.HasWarning, history.Message);

        UpdateStateLoadResult update = _updateStateStore.Load();
        UpdateState = update.State;
        AddWarning("updatestate", update.HasWarning, update.Message);

        try
        {
            ItemConfig = ItemConfigStore.Load(AppPaths.ConfigFile);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ItemConfig = ItemConfig.CreateDefault();
            Warnings.Add($"config.json 读取失败，用默认配置：{ex.Message}");
        }

        foreach (string warning in Warnings)
        {
            AppPaths.Log($"[存档] {warning}");
        }
    }

    private void AddWarning(string name, bool hasWarning, string? message)
    {
        if (hasWarning)
        {
            Warnings.Add($"{name}.json：{message ?? "已降级为默认值"}");
        }
    }

    // ---------------------------------------------------------------- 保存

    public void SaveProgress() => SafeSave(() => _progressStore.Save(Progress), "progress");
    public void SaveRecords() => SafeSave(() => _recordsStore.Save(Records), "records");
    public void SaveSettings() => SafeSave(() => _settingsStore.Save(Settings), "settings");
    public void SaveLevelProgress() => SafeSave(() => _levelProgressStore.Save(LevelProgress), "levelprogress");
    public void SaveAchievements() => SafeSave(() => _achievementStore.Save(Achievements), "achievements");
    public void SaveHistory() => SafeSave(() => _historyStore.Save(History), "history");
    public void SaveUpdateState() => SafeSave(() => _updateStateStore.Save(UpdateState), "updatestate");

    public void SaveItemConfig() => SafeSave(
        () => ItemConfigStore.Save(ItemConfig, AppPaths.ConfigFile), "config");

    /// <summary>退出时统一落盘（按"改动过的才写"由调用方决定，这里只负责兜住异常）。</summary>
    public void SaveAll()
    {
        SaveSettings();
        SaveProgress();
        SaveRecords();
        SaveLevelProgress();
        SaveAchievements();
        SaveHistory();
        SaveUpdateState();
        SaveItemConfig();
    }

    /// <summary>任何存档写失败都只写日志——绝不影响游戏本身。</summary>
    private static void SafeSave(Action save, string what)
    {
        try
        {
            save();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppPaths.Log($"[存档] {what}.json 写入失败：{ex.GetType().Name}：{ex.Message}");
        }
    }

    /// <summary>提交最快记录（Core 内部按会话参数选表；练习/提示/无猜局由调用方先过滤）。</summary>
    public RecordSubmitResult SubmitRecord(GameSession session, bool eligible)
    {
        if (!eligible)
        {
            return RecordSubmitResult.NotSubmitted("本局不计入最快记录");
        }

        RecordSubmitResult result = _recordsStore.Submit(session);
        SaveRecords();
        return result;
    }
}
