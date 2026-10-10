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
    public const int CurrentSchemaVersion = 1;

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
    /// 远景是否叠加"雷密度热力图"（§1.5 的可选项）。
    ///
    /// <b>默认关闭</b>：它画的是未翻开区域真实的雷密度，等于把信息直接给玩家看，
    /// 开着会破坏 §0"100% 还原原版"的公平性，所以只作为辅助/演示视图存在。
    /// </summary>
    [JsonPropertyName("showMineDensityHeatmap")]
    public bool ShowMineDensityHeatmap { get; set; }

    /// <summary>
    /// 是否使用"锁定盘面"（§2.6）：先按 seed 布雷、再取一个安全首点。
    /// 打开后同一个 seed 永远得到同一张盘（普通模式下换个首点就是另一张盘）。
    /// </summary>
    [JsonPropertyName("lockedBoard")]
    public bool LockedBoard { get; set; }

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
