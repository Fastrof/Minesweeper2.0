using System.Text.Json.Serialization;
using MineSweeper.Core.Themes;

namespace MineSweeper.Core.Settings;

/// <summary>
/// 玩家设置（音效 + 主题），落在 <c>data\settings.json</c>。
///
/// 为什么不塞进 progress.json：文档 §6.1 的两张表是"跨局进度"和"记录"，都已经带
/// schemaVersion 且经过验证；再加一个独立文件，既不动那两个存档的版本，也让
/// "玩家可手改的设置"集中在一处（和 config.json / theme 覆盖同一种交互方式）。
/// </summary>
public sealed class AppSettings
{
    /// <summary>当前 schema 版本。改结构时 +1 并补迁移。</summary>
    public const int CurrentSchemaVersion = 2;

    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    /// <summary>是否播放音效。</summary>
    [JsonPropertyName("audioEnabled")]
    public bool AudioEnabled { get; set; } = true;

    /// <summary>音效音量 0..1。</summary>
    [JsonPropertyName("audioVolume")]
    public double AudioVolume { get; set; } = 0.7;

    /// <summary>当前主题 Id（见 <see cref="BoardPalette.BuiltIn"/>）。</summary>
    [JsonPropertyName("themeId")]
    public string ThemeId { get; set; } = BoardPalette.ClassicId;

    /// <summary>
    /// 逐项覆盖当前主题里的单个颜色（键名 = 主题里的颜色键，值 = #RGB/#RRGGBB/#AARRGGBB）。
    /// 只改玩家想改的那几个颜色，不用把整套调色板抄一遍。
    /// </summary>
    [JsonPropertyName("themeOverrides")]
    public Dictionary<string, string> ThemeOverrides { get; set; } = new(StringComparer.Ordinal);

    /// <summary>
    /// 启动时自动检查更新（v1.1）。
    ///
    /// 默认开：只请求 GitHub 的公开发布接口，不带任何标识、不发任何玩家数据，
    /// 而且限流 24 小时一次 —— 这点流量换"玩家不会一直用旧版本"是划算的。
    /// 商店版（打包版）永远不查：那里由 Microsoft Store 负责更新。
    /// </summary>
    [JsonPropertyName("autoCheckUpdate")]
    public bool AutoCheckUpdate { get; set; } = true;

    /// <summary>
    /// 发现新版本后自动下载安装包（v1.1）。
    ///
    /// <b>默认关</b>：安装包 90+ MB，在手机热点/流量网络上自动下会很难受；
    /// 玩家想省事可以在设置里打开，打开后就是"后台下好、只等你点安装"。
    /// </summary>
    [JsonPropertyName("autoDownloadUpdate")]
    public bool AutoDownloadUpdate { get; set; }

    /// <summary>
    /// 自动检查更新的间隔小时数（v1.2）：0 = 每次启动，24 = 每天，168 = 每周……
    /// 档位定义与收敛规则见 <c>MineSweeper.Core.Updates.UpdateFrequency</c>；
    /// 手改 settings.json 写了个怪数字也不会失控（读取时会收敛到最近的合法档位）。
    /// </summary>
    [JsonPropertyName("updateCheckIntervalHours")]
    public int UpdateCheckIntervalHours { get; set; } = 24;

    /// <summary>
    /// 暂停更新到什么时候为止（v1.2，UTC）。null = 没暂停；已经过期 = 等同于没暂停（自动恢复）。
    /// 暂停期间**自动检查一次请求都不发**；玩家手动点「检查更新」则会立即结束暂停（见 MainWindow）。
    /// </summary>
    [JsonPropertyName("updatePausedUntilUtc")]
    public DateTimeOffset? UpdatePausedUntilUtc { get; set; }

    /// <summary>
    /// 上一次运行过的版本号（v1.2）。用来判断"升级后要不要弹本版新增"：
    /// 空 = 第一次安装（不弹），比当前版本低 = 弹一次。
    /// </summary>
    [JsonPropertyName("lastRunVersion")]
    public string LastRunVersion { get; set; } = string.Empty;

    /// <summary>
    /// 界面语言（v1.3）。存 BCP-47 代码，<b>首次安装默认简体中文</b>。
    /// 认不出来的写法（玩家手改坏了）由 LanguageCatalog 退回默认语言，绝不白屏；
    /// 还没翻译的语言回退到英文（"半成品"不会上线，有守门测试盯着）。
    /// </summary>
    [JsonPropertyName("language")]
    public string Language { get; set; } = "zh-Hans";

    /// <summary>
    /// 界面颜色模式（v1.3）："system"（跟随系统，默认）/ "light" / "dark"。
    /// 认不出来的写法由 <see cref="Themes.UiThemeCatalog"/> 退回 system，绝不白屏。
    /// </summary>
    [JsonPropertyName("uiTheme")]
    public string UiTheme { get; set; } = Themes.UiThemeCatalog.DefaultId;

    /// <summary>
    /// 无猜模式（v1.5，默认关闭）：开启后普通开局只发"从首点出发全程可纯逻辑推完"的盘面。
    /// 关卡模式使用固定锁定盘，不受这个开关影响。纯新增字段（默认 false），
    /// 所以旧 settings.json 读进来不需要额外迁移逻辑（schemaVersion 仍升到 2 以标记结构变化）。
    /// </summary>
    [JsonPropertyName("noGuess")]
    public bool NoGuess { get; set; }

    /// <summary>默认设置。</summary>
    public static AppSettings CreateDefault() => new();

    /// <summary>
    /// 规范化：音量夹到 0..1、主题 Id 必须存在、覆盖项必须是合法颜色。
    /// 玩家手改坏了任何一项都只丢那一项，不影响其余设置。
    /// </summary>
    public void Normalize()
    {
        AudioVolume = Math.Clamp(AudioVolume, 0d, 1d);

        if (BoardPalette.FindById(ThemeId) is null)
        {
            ThemeId = BoardPalette.ClassicId;
        }

        ThemeOverrides ??= new Dictionary<string, string>(StringComparer.Ordinal);

        var cleaned = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, string> pair in ThemeOverrides)
        {
            if (string.IsNullOrWhiteSpace(pair.Key) || !PaletteColor.TryParse(pair.Value, out PaletteColor parsed))
            {
                continue;
            }

            cleaned[pair.Key.Trim()] = parsed.ToHex();
        }

        ThemeOverrides = cleaned;
    }

    /// <summary>解析出当前生效的调色板（内置主题 + 覆盖项）。</summary>
    public BoardPalette ResolvePalette()
    {
        Normalize();
        return BoardPalette.Resolve(ThemeId, ThemeOverrides);
    }

    /// <summary>把结构从旧版本升到当前版本。</summary>
    internal static AppSettings Migrate(AppSettings loaded)
    {
        if (loaded.SchemaVersion < CurrentSchemaVersion)
        {
            // 目前只有 v1；将来加字段时在这里补默认值
            loaded.SchemaVersion = CurrentSchemaVersion;
        }

        return loaded;
    }
}
