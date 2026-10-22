using System.Text;

namespace MineSweeper.App.Linux.Services;

/// <summary>
/// Linux 端的数据路径（XDG 规范）。
///
/// 与 Windows 端 <c>AppPaths</c> 的对应关系：
///   %LOCALAPPDATA%\MineSweeper2\  →  $XDG_DATA_HOME/minesweeper2/（默认 ~/.local/share/minesweeper2）
///
/// 约定与 Windows 端保持一致：**删掉这个目录 = 全部重置**。
/// 所有个人数据都只写在这里，不碰注册表（Linux 没有）、不碰用户主目录其它位置。
/// </summary>
public static class AppPaths
{
    private static string? _dataDirectory;

    /// <summary>数据目录。</summary>
    public static string DataDirectory => _dataDirectory ??= ResolveDataDirectory();

    public static string ProgressFile => Path.Combine(DataDirectory, "progress.json");
    public static string RecordsFile => Path.Combine(DataDirectory, "records.json");
    public static string ConfigFile => Path.Combine(DataDirectory, "config.json");
    public static string SettingsFile => Path.Combine(DataDirectory, "settings.json");
    public static string LevelProgressFile => Path.Combine(DataDirectory, "levelprogress.json");
    public static string HistoryFile => Path.Combine(DataDirectory, "history.json");
    public static string AchievementsFile => Path.Combine(DataDirectory, "achievements.json");
    public static string UpdateStateFile => Path.Combine(DataDirectory, "updatestate.json");
    public static string SoundsDirectory => Path.Combine(DataDirectory, "sounds");
    public static string LogFile => Path.Combine(DataDirectory, "app.log");

    /// <summary>资源目录（图标等，随程序输出）。</summary>
    public static string AssetsDirectory => Path.Combine(AppContext.BaseDirectory, "Assets");

    /// <summary>
    /// 把一行日志追加到 app.log。
    ///
    /// 刻意保持与 Windows 端同样的语义：**日志失败绝不影响游戏**（吞掉所有异常）。
    /// </summary>
    public static void Log(string message)
    {
        try
        {
            Directory.CreateDirectory(DataDirectory);
            File.AppendAllText(
                LogFile,
                $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}",
                Encoding.UTF8);
        }
        catch
        {
            // 日志不可写时静默放弃——这是本工程反复强调的一条铁律。
        }
    }

    /// <summary>XDG 数据目录（不存在 XDG_DATA_HOME 时退回 ~/.local/share）。</summary>
    private static string ResolveDataDirectory()
    {
        string? xdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        string root = string.IsNullOrWhiteSpace(xdg)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".local", "share")
            : xdg;

        return Path.Combine(root, "minesweeper2");
    }
}
