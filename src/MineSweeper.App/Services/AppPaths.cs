using MineSweeper.Core;
using MineSweeper.Core.Achievements;
using MineSweeper.Core.Items;
using MineSweeper.Core.Levels;
using MineSweeper.Core.Persistence;
using MineSweeper.Core.Settings;
using MineSweeper.Core.Updates;

namespace MineSweeper.App.Services;

/// <summary>
/// 本地数据路径（§7.2）。
///
/// §7.2 规定"MSIX 下本地 JSON 应写到 ApplicationData.Current.LocalFolder"。
/// 本项目当前按 §7.1 以 unpackaged 方式调试，此时没有包身份，
/// 所以退一步写到程序目录下的 data\ —— 既不碰 C 盘，也便于调试时直接看存档；
/// 一旦切成 MSIX 打包版，会自动改用包的 LocalFolder。
/// </summary>
public static class AppPaths
{
    private static string? _dataDirectory;

    /// <summary>数据目录。</summary>
    public static string DataDirectory => _dataDirectory ??= ResolveDataDirectory();

    /// <summary>跨局进度。</summary>
    public static string ProgressFile => Path.Combine(DataDirectory, "progress.json");

    /// <summary>最快记录。</summary>
    public static string RecordsFile => Path.Combine(DataDirectory, "records.json");

    /// <summary>道具配置。</summary>
    public static string ConfigFile => Path.Combine(DataDirectory, "config.json");

    /// <summary>玩家设置（音效 + 主题）。</summary>
    public static string SettingsFile => Path.Combine(DataDirectory, "settings.json");

    /// <summary>关卡制进度（星级 + 最快用时 + 解锁）。</summary>
    public static string LevelProgressFile => Path.Combine(DataDirectory, "levelprogress.json");

    /// <summary>音效目录（首次运行由代码合成写入，可直接替换成自己的 wav）。</summary>
    /// <summary>历史记录（从第一次扫雷起的累计数据）。删掉它就是从头开始。</summary>
    public static string HistoryFile => Path.Combine(DataDirectory, "history.json");

    public static string SoundsDirectory => Path.Combine(DataDirectory, "sounds");

    /// <summary>成就与成就点（v1.1）。</summary>
    public static string AchievementsFile => Path.Combine(DataDirectory, "achievements.json");

    /// <summary>检查更新的状态：上次检查时间 / 已提示版本 / 已跳过版本（v1.1）。</summary>
    public static string UpdateStateFile => Path.Combine(DataDirectory, "updatestate.json");

    /// <summary>下载好的更新包（v1.1）。一个包 90+ MB，所以单独一个目录，且只保留最新一版。</summary>
    public static string UpdatesDirectory => Path.Combine(DataDirectory, "updates");

    /// <summary>资源目录（随 exe 输出）。</summary>
    public static string AssetsDirectory => Path.Combine(AppContext.BaseDirectory, "Assets");

    /// <summary>程序图标（多尺寸 ICO；exe 已嵌入，这里给 AppWindow.SetIcon 显式用）。</summary>
    public static string AppIconFile => Path.Combine(AssetsDirectory, "MineSweeper2.0.ico");

    /// <summary>
    /// Toast 通知的图标（PNG）。
    /// unpackaged 应用不能靠 ms-appx 引用资源，所以通知里只能用 file:// 指向磁盘上的真实文件。
    /// </summary>
    public static string ToastIconFile => Path.Combine(AssetsDirectory, "ToastIcon.png");

    /// <summary>日志文件（崩溃与存档降级提示都写这里）。</summary>
    public static string LogFile => Path.Combine(DataDirectory, "app.log");

    /// <summary>把一行日志追加到 app.log。</summary>
    public static void Log(string message)
    {
        try
        {
            Directory.CreateDirectory(DataDirectory);
            File.AppendAllText(
                LogFile,
                $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// 旧版（v0.6 以前）放存档的地方：exe 旁边的 data\。
    /// 只在"第一次启动搬迁"时用一下，之后不再使用。
    /// </summary>
    public static string LegacyDataDirectory => Path.Combine(AppContext.BaseDirectory, "data");

    /// <summary>
    /// 第一次启动时的搬迁：把旧版 exe 旁边 data\ 里的存档搬到 <see cref="DataDirectory"/>，搬完删掉旧的 data\。
    /// 新位置已有同名文件时**不覆盖**（新数据优先），旧的留着不动。任何失败都不影响启动，下次启动会再试。
    /// </summary>
    public static void MigrateLegacyDataIfNeeded()
    {
        try
        {
            string legacy = LegacyDataDirectory;
            if (!Directory.Exists(legacy))
            {
                return;
            }

            string current = DataDirectory;
            if (string.Equals(
                    Path.GetFullPath(legacy).TrimEnd(Path.DirectorySeparatorChar),
                    Path.GetFullPath(current).TrimEnd(Path.DirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase))
            {
                return;   // 万一两者是同一个目录，别自己搬自己
            }

            Directory.CreateDirectory(current);

            int moved = 0;
            foreach (string file in Directory.GetFiles(legacy))
            {
                string target = Path.Combine(current, Path.GetFileName(file));
                if (File.Exists(target))
                {
                    continue;
                }

                try
                {
                    File.Move(file, target);
                }
                catch (IOException)
                {
                    // 数据目录和 exe 不在同一个盘时 Move 可能不行：退化成"拷贝 + 删源"
                    File.Copy(file, target, overwrite: false);
                    File.Delete(file);
                }

                moved++;
            }

            foreach (string dir in Directory.GetDirectories(legacy))
            {
                string target = Path.Combine(current, Path.GetFileName(dir));

                // Directory.Move 不能跨盘，而且目标可能已经有同名目录（比如全新的 sounds\），
                // 所以这里逐文件合并：已有的不覆盖（新数据优先），缺的补上，最后删掉旧目录。
                MergeDirectory(dir, target);
                TryDeleteDirectory(dir);
                moved++;
            }

            try
            {
                if (Directory.GetFileSystemEntries(legacy).Length == 0)
                {
                    Directory.Delete(legacy);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }

            if (moved > 0)
            {
                Log($"[存档] 第一次启动：已把旧 data\\ 里的 {moved} 项搬到 {current}（以后只用这个目录）。");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            Log($"[存档] 搬迁旧 data\\ 失败（下次启动会再试）：{ex.Message}");
        }
    }

    /// <summary>递归合并拷贝：只是把 <paramref name="source"/> 里"目标还没有的"文件补过去，不覆盖已有文件。</summary>
    private static void MergeDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);

        foreach (string file in Directory.GetFiles(source))
        {
            string targetFile = Path.Combine(target, Path.GetFileName(file));
            if (!File.Exists(targetFile))
            {
                File.Copy(file, targetFile, overwrite: false);
            }
        }

        foreach (string dir in Directory.GetDirectories(source))
        {
            MergeDirectory(dir, Path.Combine(target, Path.GetFileName(dir)));
        }
    }

    /// <summary>删目录，删不掉也不影响启动（旧的空目录留几天也无所谓）。</summary>
    private static void TryDeleteDirectory(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static bool? _isPackaged;

    /// <summary>
    /// 当前是不是"打包版（MSIX）"。
    /// v1.1 有两处依赖它做产品决策：
    /// ① 检查更新只在**未打包**（GitHub 侧载）版启用 —— 商店版由商店自己更新，
    /// 而且不该把商店用户引导到 GitHub 去侧载；
    /// ② 下载完的安装方式不同 —— 打包版能就地升级（要重启），未打包版只能唤起系统安装程序。
    /// </summary>
    public static bool IsPackaged => _isPackaged ??= DetectPackaged();

    private static bool DetectPackaged()
    {
        try
        {
            return Windows.ApplicationModel.Package.Current is not null;
        }
        catch (InvalidOperationException)
        {
            // 没有包身份 = unpackaged
            return false;
        }
        catch (TypeInitializationException)
        {
            return false;
        }
    }

    private static string ResolveDataDirectory()
    {
        // 打包版：用包的 LocalFolder（§7.2）
        try
        {
            string? packaged = Windows.Storage.ApplicationData.Current?.LocalFolder?.Path;
            if (!string.IsNullOrEmpty(packaged) && Windows.ApplicationModel.Package.Current is not null)
            {
                return Path.Combine(packaged, "MineSweeper2");
            }
        }
        catch (InvalidOperationException)
        {
            // 没有包身份（unpackaged），按下面的分支处理
        }
        catch (TypeInitializationException)
        {
        }

        // 未打包：统一放 %LOCALAPPDATA%\MineSweeper2。
        // v0.6 起改到这里 —— 老版本放在 exe 旁边的 data\，第一次启动会自动搬过来（见 MigrateLegacyDataIfNeeded）。
        // 好处：装到 Program Files 这类只读目录也能正常存档，玩家数据不会混在游戏目录里。
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MineSweeper2");
    }

    /// <summary>一次性装好存档、配置、记录与设置（§6.1）。</summary>
    public static AppStorage CreateStorage()
    {
        // 第一次启动：先把旧 data\ 里的存档搬过来（只做一次；以后不再使用 data 目录）
        MigrateLegacyDataIfNeeded();

        Directory.CreateDirectory(DataDirectory);

        var progressStore = new ProgressStore(ProgressFile);
        ProgressLoadResult progress = progressStore.Load();
        if (progress.HasWarning && progress.Message is not null)
        {
            Log($"进度加载警告：{progress.Message}");
        }

        ItemConfig config = ItemConfigStore.Load(ConfigFile);

        var recordsStore = new RecordsStore(RecordsFile);
        recordsStore.Reload();

        var settingsStore = new AppSettingsStore(SettingsFile);
        SettingsLoadResult settings = settingsStore.Load();
        if (settings.HasWarning && settings.Message is not null)
        {
            Log($"设置加载警告：{settings.Message}");
        }

        var levelStore = new LevelProgressStore(LevelProgressFile);
        LevelProgressLoadResult levels = levelStore.Load();
        if (levels.HasWarning && levels.Message is not null)
        {
            Log($"关卡进度加载警告：{levels.Message}");
        }

        var achievementStore = new AchievementStore(AchievementsFile);
        AchievementLoadResult achievements = achievementStore.Load();
        if (achievements.HasWarning && achievements.Message is not null)
        {
            Log($"成就加载警告：{achievements.Message}");
        }

        var updateStore = new UpdateStateStore(UpdateStateFile);
        UpdateStateLoadResult updateState = updateStore.Load();
        if (updateState.HasWarning && updateState.Message is not null)
        {
            Log($"检查更新状态加载警告：{updateState.Message}");
        }

        return new AppStorage(
            progressStore,
            progress.Progress,
            recordsStore,
            config,
            progress,
            settingsStore,
            settings.Settings,
            settings,
            levelStore,
            levels.Progress,
            levels,
            achievementStore,
            achievements.Progress,
            achievements,
            updateStore,
            updateState.State,
            updateState);
    }
}

/// <summary>App 用到的一组存储对象。</summary>
public sealed record AppStorage(
    ProgressStore ProgressStore,
    PlayerProgress Progress,
    RecordsStore RecordsStore,
    ItemConfig Config,
    ProgressLoadResult ProgressLoadResult,
    AppSettingsStore SettingsStore,
    AppSettings Settings,
    SettingsLoadResult SettingsLoadResult,
    LevelProgressStore LevelStore,
    LevelProgress LevelProgress,
    LevelProgressLoadResult LevelProgressLoadResult,
    AchievementStore AchievementStore,
    AchievementProgress AchievementProgress,
    AchievementLoadResult AchievementLoadResult,
    UpdateStateStore UpdateStateStore,
    UpdateState UpdateState,
    UpdateStateLoadResult UpdateStateLoadResult);
