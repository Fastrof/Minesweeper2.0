using System.Text.Json.Serialization;

namespace MineSweeper.Core.Themes;

/// <summary>
/// 棋盘调色板（一套主题）。
///
/// 之所以"主题"可以完全不需要图片素材：棋盘、数字、旗子、雷全都是 Win2D 矢量自绘
/// （§1.2.1 ④），所以一套主题本质上就是一组颜色。这里用纯数据 + 十六进制字符串表达，
/// 玩家可以直接手改 theme.json，和 config.json 的用法一致。
///
/// 颜色取 <see cref="PaletteColor"/> 的文本形式，键名与 C# 属性一一对应。
/// </summary>
public sealed class BoardPalette
{
    /// <summary>经典配色的 Id（默认主题）。</summary>
    public const string ClassicId = "classic";

    [JsonPropertyName("id")]
    public string Id { get; set; } = ClassicId;

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = "经典";

    // ---------- 未翻开格（凸起立体感，§4.1）----------

    [JsonPropertyName("coveredFill")]
    public string CoveredFill { get; set; } = "#C0C0C0";

    [JsonPropertyName("coveredHighlight")]
    public string CoveredHighlight { get; set; } = "#E6E6E6";

    [JsonPropertyName("coveredShadow")]
    public string CoveredShadow { get; set; } = "#8A8A8A";

    // ---------- 已翻开格与网格线 ----------

    [JsonPropertyName("revealedFill")]
    public string RevealedFill { get; set; } = "#DCDCDC";

    [JsonPropertyName("gridLine")]
    public string GridLine { get; set; } = "#A8A8A8";

    // ---------- 透视（§5.4）----------

    [JsonPropertyName("itemRevealedFill")]
    public string ItemRevealedFill { get; set; } = "#D6E4FF";

    [JsonPropertyName("itemRevealedMineOutline")]
    public string ItemRevealedMineOutline { get; set; } = "#1E63D8";

    // ---------- 雷与旗 ----------

    [JsonPropertyName("explodedFill")]
    public string ExplodedFill { get; set; } = "#E81111";

    [JsonPropertyName("mineBody")]
    public string MineBody { get; set; } = "#202020";

    [JsonPropertyName("flagPole")]
    public string FlagPole { get; set; } = "#303030";

    [JsonPropertyName("flagCloth")]
    public string FlagCloth { get; set; } = "#D11A1A";

    [JsonPropertyName("wrongFlagCross")]
    public string WrongFlagCross { get; set; } = "#B00000";

    [JsonPropertyName("questionMark")]
    public string QuestionMark { get; set; } = "#E0A800";

    // ---------- 焦点环与高亮 ----------



    [JsonPropertyName("aimingRing")]
    public string AimingRing { get; set; } = "#1E63D8";

    [JsonPropertyName("aimingFill")]
    public string AimingFill { get; set; } = "#401E63D8";

    [JsonPropertyName("highlightRing")]
    public string HighlightRing { get; set; } = "#1E885A";

    [JsonPropertyName("highlightFill")]
    public string HighlightFill { get; set; } = "#401E885A";

    // ---------- 远景雷密度热力图（§1.5 可选叠加，默认关闭）----------

    /// <summary>密度最低处的着色。</summary>
    [JsonPropertyName("heatLowFill")]
    public string HeatLowFill { get; set; } = "#2E7DFF";

    /// <summary>密度最高处的着色。</summary>
    [JsonPropertyName("heatHighFill")]
    public string HeatHighFill { get; set; } = "#FF3B30";

    // ---------- 经典数字八色（§0 第 8 条）----------

    [JsonPropertyName("numberColors")]
    public string[] NumberColors { get; set; } = DefaultNumberColors();

    /// <summary>按 JSON 键名取色；键名不认识或颜色非法时返回 <paramref name="fallback"/>。</summary>
    public PaletteColor Color(string key, PaletteColor fallback = default)
    {
        string? raw = key switch
        {
            "coveredFill" => CoveredFill,
            "coveredHighlight" => CoveredHighlight,
            "coveredShadow" => CoveredShadow,
            "revealedFill" => RevealedFill,
            "gridLine" => GridLine,
            "itemRevealedFill" => ItemRevealedFill,
            "itemRevealedMineOutline" => ItemRevealedMineOutline,
            "explodedFill" => ExplodedFill,
            "mineBody" => MineBody,
            "flagPole" => FlagPole,
            "flagCloth" => FlagCloth,
            "wrongFlagCross" => WrongFlagCross,
            "questionMark" => QuestionMark,
            "aimingRing" => AimingRing,
            "aimingFill" => AimingFill,
            "highlightRing" => HighlightRing,
            "highlightFill" => HighlightFill,
            "heatLowFill" => HeatLowFill,
            "heatHighFill" => HeatHighFill,
            _ => null,
        };

        return PaletteColor.ParseOr(raw, fallback);
    }

    /// <summary>数字 1..8 的颜色；越界返回兜底灰。</summary>
    public PaletteColor NumberColor(int number)
    {
        int index = number - 1;
        if (index < 0 || index >= NumberColors.Length)
        {
            return PaletteColor.ParseOr("#404040", new PaletteColor(0x40, 0x40, 0x40));
        }

        return PaletteColor.ParseOr(NumberColors[index], new PaletteColor(0x40, 0x40, 0x40));
    }

    /// <summary>
    /// 把所有颜色字段规范化：非法或缺失的颜色一律回落到内置经典配色，
    /// 并把数字数组补齐到 8 项。绝不因为玩家手改坏一个字段就让整盘变成黑色。
    /// </summary>
    public void Normalize()
    {
        BoardPalette fallback = Classic;

        Id = string.IsNullOrWhiteSpace(Id) ? ClassicId : Id.Trim();
        DisplayName = string.IsNullOrWhiteSpace(DisplayName) ? Id : DisplayName.Trim();

        CoveredFill = Fix(CoveredFill, fallback.CoveredFill);
        CoveredHighlight = Fix(CoveredHighlight, fallback.CoveredHighlight);
        CoveredShadow = Fix(CoveredShadow, fallback.CoveredShadow);
        RevealedFill = Fix(RevealedFill, fallback.RevealedFill);
        GridLine = Fix(GridLine, fallback.GridLine);
        ItemRevealedFill = Fix(ItemRevealedFill, fallback.ItemRevealedFill);
        ItemRevealedMineOutline = Fix(ItemRevealedMineOutline, fallback.ItemRevealedMineOutline);
        ExplodedFill = Fix(ExplodedFill, fallback.ExplodedFill);
        MineBody = Fix(MineBody, fallback.MineBody);
        FlagPole = Fix(FlagPole, fallback.FlagPole);
        FlagCloth = Fix(FlagCloth, fallback.FlagCloth);
        WrongFlagCross = Fix(WrongFlagCross, fallback.WrongFlagCross);
        QuestionMark = Fix(QuestionMark, fallback.QuestionMark);
;
        AimingRing = Fix(AimingRing, fallback.AimingRing);
        AimingFill = Fix(AimingFill, fallback.AimingFill);
        HighlightRing = Fix(HighlightRing, fallback.HighlightRing);
        HighlightFill = Fix(HighlightFill, fallback.HighlightFill);
        HeatLowFill = Fix(HeatLowFill, fallback.HeatLowFill);
        HeatHighFill = Fix(HeatHighFill, fallback.HeatHighFill);

        string[] defaults = DefaultNumberColors();
        var numbers = new string[8];
        for (int i = 0; i < numbers.Length; i++)
        {
            string? raw = NumberColors is not null && i < NumberColors.Length ? NumberColors[i] : null;
            numbers[i] = Fix(raw, defaults[i]);
        }

        NumberColors = numbers;
    }

    /// <summary>复制一份（改副本不会影响内置主题）。</summary>
    public BoardPalette Clone()
    {
        var copy = (BoardPalette)MemberwiseClone();
        copy.NumberColors = (string[])NumberColors.Clone();
        return copy;
    }

    // ---------------------------------------------------------------- 内置主题

    /// <summary>经典配色（与文档 §4.1 / §0 第 8 条一致）。</summary>
    public static BoardPalette Classic => new();

    /// <summary>深色配色：暗底 + 提亮后的数字色。</summary>
    public static BoardPalette Dark => new()
    {
        Id = "dark",
        DisplayName = "深色",
        CoveredFill = "#3A3A3E",
        CoveredHighlight = "#4E4E54",
        CoveredShadow = "#26262A",
        RevealedFill = "#2A2A2E",
        GridLine = "#454549",
        ItemRevealedFill = "#1E2A44",
        ItemRevealedMineOutline = "#6FA8FF",
        ExplodedFill = "#C81E1E",
        MineBody = "#F0F0F0",
        FlagPole = "#E0E0E0",
        FlagCloth = "#E04A4A",
        WrongFlagCross = "#FF8A8A",
        QuestionMark = "#FFD24A",
        AimingRing = "#4EA8FF",
        AimingFill = "#404EA8FF",
        HighlightRing = "#3ED08A",
        HighlightFill = "#403ED08A",
        HeatLowFill = "#3A6FFF",
        HeatHighFill = "#FF5A5A",
        NumberColors = new[]
        {
            "#6FA8FF", "#6FE08A", "#FF7A7A", "#9FB8FF",
            "#E0A870", "#6FE0E0", "#FFFFFF", "#B8B8B8",
        },
    };

    /// <summary>高对比配色：纯黑底 + 高亮数字，给视力受限的玩家用。</summary>
    public static BoardPalette HighContrast => new()
    {
        Id = "highContrast",
        DisplayName = "高对比",
        CoveredFill = "#1A1A1A",
        CoveredHighlight = "#FFFFFF",
        CoveredShadow = "#000000",
        RevealedFill = "#000000",
        GridLine = "#FFFFFF",
        ItemRevealedFill = "#001A33",
        ItemRevealedMineOutline = "#00E5FF",
        ExplodedFill = "#FF0000",
        MineBody = "#FFFFFF",
        FlagPole = "#FFFFFF",
        FlagCloth = "#FF0000",
        WrongFlagCross = "#FFD400",
        QuestionMark = "#FFD400",
        AimingRing = "#00E5FF",
        AimingFill = "#4000E5FF",
        HighlightRing = "#00FF66",
        HighlightFill = "#4000FF66",
        HeatLowFill = "#00E5FF",
        HeatHighFill = "#FF0000",
        NumberColors = new[]
        {
            "#4FC3F7", "#69F0AE", "#FF8A80", "#B388FF",
            "#FFD180", "#84FFFF", "#FFFFFF", "#E0E0E0",
        },
    };

    /// <summary>纸质配色：暖色米黄底，长时间看不刺眼。</summary>
    public static BoardPalette Paper => new()
    {
        Id = "paper",
        DisplayName = "纸质",
        CoveredFill = "#E8DFC8",
        CoveredHighlight = "#F7F1E1",
        CoveredShadow = "#B9AE93",
        RevealedFill = "#F3EDDC",
        GridLine = "#CBBF9F",
        ItemRevealedFill = "#DCE8F5",
        ItemRevealedMineOutline = "#2B5FA8",
        ExplodedFill = "#D0342C",
        MineBody = "#3A3226",
        FlagPole = "#4A4030",
        FlagCloth = "#C0392B",
        WrongFlagCross = "#8E2B22",
        QuestionMark = "#2B5FA8",
        AimingRing = "#2B5FA8",
        AimingFill = "#402B5FA8",
        HighlightRing = "#2E7D4F",
        HighlightFill = "#402E7D4F",
        HeatLowFill = "#2B5FA8",
        HeatHighFill = "#C0392B",
        NumberColors = new[]
        {
            "#1F4FC4", "#1F7A33", "#C62828", "#1A237E",
            "#7A4A16", "#00695C", "#263238", "#6D6D6D",
        },
    };

    /// <summary>全部内置主题（设置界面的下拉项）。</summary>
    public static IReadOnlyList<BoardPalette> BuiltIn { get; } = new[]
    {
        Classic, Dark, HighContrast, Paper,
    };

    /// <summary>按 Id 查找内置主题（大小写不敏感），找不到返回 null。</summary>
    public static BoardPalette? FindById(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        string wanted = id.Trim();
        foreach (BoardPalette palette in BuiltIn)
        {
            if (string.Equals(palette.Id, wanted, StringComparison.OrdinalIgnoreCase))
            {
                return palette;
            }
        }

        return null;
    }

    /// <summary>
    /// 解析出最终生效的调色板：先取内置主题，再按 <paramref name="overrides"/> 逐项覆盖。
    /// 覆盖是"按颜色键名改单个颜色"，玩家不用把整套调色板抄一遍。
    /// </summary>
    public static BoardPalette Resolve(string? themeId, IReadOnlyDictionary<string, string>? overrides)
    {
        BoardPalette palette = (FindById(themeId) ?? Classic).Clone();

        if (overrides is not null)
        {
            foreach (KeyValuePair<string, string> pair in overrides)
            {
                ApplyOverride(palette, pair.Key, pair.Value);
            }
        }

        palette.Normalize();
        return palette;
    }

    private static bool ApplyOverride(BoardPalette palette, string key, string value)
    {
        if (!PaletteColor.TryParse(value, out _))
        {
            // 玩家把颜色写错了：忽略这一项，其余照旧生效
            return false;
        }

        switch (key)
        {
            case "coveredFill": palette.CoveredFill = value; return true;
            case "coveredHighlight": palette.CoveredHighlight = value; return true;
            case "coveredShadow": palette.CoveredShadow = value; return true;
            case "revealedFill": palette.RevealedFill = value; return true;
            case "gridLine": palette.GridLine = value; return true;
            case "itemRevealedFill": palette.ItemRevealedFill = value; return true;
            case "itemRevealedMineOutline": palette.ItemRevealedMineOutline = value; return true;
            case "explodedFill": palette.ExplodedFill = value; return true;
            case "mineBody": palette.MineBody = value; return true;
            case "flagPole": palette.FlagPole = value; return true;
            case "flagCloth": palette.FlagCloth = value; return true;
            case "wrongFlagCross": palette.WrongFlagCross = value; return true;
            case "questionMark": palette.QuestionMark = value; return true;
;
            case "aimingRing": palette.AimingRing = value; return true;
            case "aimingFill": palette.AimingFill = value; return true;
            case "highlightRing": palette.HighlightRing = value; return true;
            case "highlightFill": palette.HighlightFill = value; return true;
            case "heatLowFill": palette.HeatLowFill = value; return true;
            case "heatHighFill": palette.HeatHighFill = value; return true;
            case "numberColors":
                // 数组形式的整表覆盖不在这里处理；想改数字色请用 number1..number8 逐项覆盖
                return false;
            default:
                // number1..number8：逐个数字的颜色
                if (key.Length == 7 && key.StartsWith("number", StringComparison.Ordinal)
                    && int.TryParse(key.AsSpan(6), out int number) && number is >= 1 and <= 8)
                {
                    palette.NumberColors[number - 1] = value;
                    return true;
                }

                return false;
        }
    }

    private static string Fix(string? value, string fallback)
        => PaletteColor.TryParse(value, out PaletteColor parsed) ? parsed.ToHex() : fallback;

    private static string[] DefaultNumberColors() => new[]
    {
        "#0000FF", // 1 蓝
        "#008000", // 2 绿
        "#FF0000", // 3 红
        "#000080", // 4 深蓝
        "#804000", // 5 棕
        "#008080", // 6 青
        "#000000", // 7 黑
        "#808080", // 8 灰
    };
}
