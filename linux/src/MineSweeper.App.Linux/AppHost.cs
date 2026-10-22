using MineSweeper.App.Linux.Rendering;
using MineSweeper.App.Linux.Services;
using MineSweeper.Core.Localization;
using MineSweeper.Core.Updates;
using MineSweeper.Core.Themes;

namespace MineSweeper.App.Linux;

/// <summary>
/// 组合根：把 8 份存档、各个服务、单局编排装配到一起。
///
/// Linux 端没有 DI 容器（规模不值得），用一个显式的组合根，生命周期与进程一致。
/// 与 Windows 端的对应关系：Windows 的 App 在 MainWindow 构造函数里做同样的事，
/// 但那边散在 5,000 行的窗口里；这里集中一处，方便测试与替换。
/// </summary>
public sealed class AppHost
{
    private AppHost(Storage storage)
    {
        Storage = storage;
        Sound = new SoundService();
        Notifier = new NotificationService();
        L10n = new LocalizationService(() => Strings.CurrentCode, Strings.SetLanguage);
        Theme = new ThemeService(() => UiThemeCatalog.Normalize(Storage.Settings.UiTheme), PersistUiTheme);
        Achievements = new AchievementService(storage);
        Update = new UpdateService(storage);
        Game = new GameCoordinator(storage, Sound, Notifier, Achievements);
    }

    /// <summary>当前进程的唯一实例（窗口/面板从这里取服务）。</summary>
    public static AppHost? Current { get; private set; }

    public Storage Storage { get; }
    public SoundService Sound { get; }
    public NotificationService Notifier { get; }
    public LocalizationService L10n { get; }
    public ThemeService Theme { get; }
    public AchievementService Achievements { get; }
    public UpdateService Update { get; }
    public GameCoordinator Game { get; }

    /// <summary>启动时要弹的「本版新增」版本号（null = 不弹）。</summary>
    public string? PendingWhatsNewVersion { get; private set; }

    /// <summary>初始化：加载存档、恢复语言/音效/主题、合成音效。</summary>
    public static AppHost Create()
    {
        Storage storage = Storage.Create();

        // Linux 端默认浅色：WSLg / 部分桌面的系统主题探测不可靠，会把界面判成全黑。
        // "跟随系统"在 Linux 端按浅色处理；玩家显式选「深色」才切深色。
        if (string.IsNullOrWhiteSpace(storage.Settings.UiTheme) || storage.Settings.UiTheme == "system")
        {
            storage.Settings.UiTheme = "light";
            storage.SaveSettings();
        }

        // 语言必须在任何文案产生之前设定
        Strings.SetLanguage(storage.Settings.Language);

        var host = new AppHost(storage);
        Current = host;

        host.Sound.Enabled = storage.Settings.AudioEnabled;
        host.Sound.Volume = storage.Settings.AudioVolume;
        host.Sound.Initialize();

        // 升级后首次打开：弹「本版新增」（第一次安装不弹，与 Windows 端一致）
        host.PendingWhatsNewVersion = WhatsNewCatalog.ShouldShow(
            storage.Settings.LastRunVersion, UpdateService.CurrentVersion)
            ? UpdateService.CurrentVersion
            : null;

        storage.Settings.LastRunVersion = UpdateService.CurrentVersion;
        storage.SaveSettings();

        foreach (string warning in storage.Warnings)
        {
            AppPaths.Log($"[启动] {warning}");
        }

        AppPaths.Log(
            $"[启动] MineSweeper 2.0 Linux v{UpdateService.DisplayVersion}，数据目录 {AppPaths.DataDirectory}，" +
            $"音效={(host.Sound.HasPlayer ? "可用" : "静默")}，通知={(host.Notifier.HasSystemNotifier ? "可用" : "仅日志")}");

        return host;
    }

    /// <summary>当前棋盘调色板（含覆盖与对比度保障）。</summary>
    public LinuxBoardTheme ResolveBoardTheme(bool darkUi)
    {
        BoardPalette palette = Storage.Settings.ResolvePalette();
        BoardPalette adjusted = Theme.ResolvePalette(palette, darkUi);
        return LinuxBoardTheme.FromPalette(adjusted, darkUi);
    }

    /// <summary>界面模式落盘（ThemeService 的回调）。</summary>
    private void PersistUiTheme(string id)
    {
        Storage.Settings.UiTheme = id;
        Storage.SaveSettings();
    }

    /// <summary>保存设置并生效（音效开关/音量）。</summary>
    public void ApplySettings()
    {
        Sound.Enabled = Storage.Settings.AudioEnabled;
        Sound.Volume = Storage.Settings.AudioVolume;
        Storage.SaveSettings();
    }

    /// <summary>退出时统一落盘。</summary>
    public void Shutdown()
    {
        Game.SaveOnExit();
        AppPaths.Log("[退出] 已落盘");
    }
}
