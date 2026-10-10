using MineSweeper.Core.Themes;
using Windows.UI;

namespace MineSweeper.App.Rendering;

/// <summary>
/// 渲染用的调色板（§4.1）：把 Core 侧的 <see cref="BoardPalette"/>（纯数据、可 JSON 手改）
/// 映射成 Win2D 需要的 <see cref="Color"/>。
///
/// 尺寸与缩放常量与主题无关，仍然是静态常量；<b>颜色</b>全部改成实例成员，
/// 这样切换主题只需要换一个 <see cref="BoardTheme"/> 实例传给渲染器，不用改任何全局状态。
///
/// 经典数字配色严格按 §0 第 8 条：1 蓝、2 绿、3 红、4 深蓝、5 棕、6 青、7 黑、8 灰。
/// </summary>
internal sealed class BoardTheme
{
    /// <summary>默认格宽（DIP）。</summary>
    public const float DefaultCellSize = 26f;

    /// <summary>
    /// "适应窗口"时允许把格子放大到的上限（DIP）。
    ///
    /// 以前适应窗口只会"缩小"、不会"放大"（上限就是 DefaultCellSize），于是 9×9 这种小盘
    /// 在一大片空白里只有中间一小块能点——玩家点到棋盘外面自然"没反应"。
    /// 现在小盘会放大到接近铺满，格子更大也更好点。
    /// </summary>
    public const float MaxFitCellSize = 44f;

    /// <summary>可读格宽下限（§1.5：数字与旗子可辨认的下限）。</summary>
    public const float MinReadableCellSize = 20f;

    /// <summary>远景降级阈值（§1.5：低于这个格宽就只画色块）。</summary>
    public const float FarZoomCellSize = 6f;

    /// <summary>缩放上下限（§1.5）。</summary>
    public const float MinZoom = 0.02f;
    public const float MaxZoom = 4f;

    private readonly Color[] _numberColors;

    private BoardTheme(BoardPalette palette)
    {
        Palette = palette;

        CoveredFill = ToColor(palette.Color("coveredFill"));
        CoveredHighlight = ToColor(palette.Color("coveredHighlight"));
        CoveredShadow = ToColor(palette.Color("coveredShadow"));
        RevealedFill = ToColor(palette.Color("revealedFill"));
        GridLine = ToColor(palette.Color("gridLine"));
        ItemRevealedFill = ToColor(palette.Color("itemRevealedFill"));
        ItemRevealedMineOutline = ToColor(palette.Color("itemRevealedMineOutline"));
        ExplodedFill = ToColor(palette.Color("explodedFill"));
        MineBody = ToColor(palette.Color("mineBody"));
        FlagPole = ToColor(palette.Color("flagPole"));
        FlagCloth = ToColor(palette.Color("flagCloth"));
        WrongFlagCross = ToColor(palette.Color("wrongFlagCross"));
        QuestionMark = ToColor(palette.Color("questionMark"));
        AimingRing = ToColor(palette.Color("aimingRing"));
        AimingFill = ToColor(palette.Color("aimingFill"));
        HighlightRing = ToColor(palette.Color("highlightRing"));
        HighlightFill = ToColor(palette.Color("highlightFill"));
        HeatLowFill = ToColor(palette.Color("heatLowFill"));
        HeatHighFill = ToColor(palette.Color("heatHighFill"));

        _numberColors = new Color[8];
        for (int i = 0; i < _numberColors.Length; i++)
        {
            _numberColors[i] = ToColor(palette.NumberColor(i + 1));
        }
    }

    /// <summary>经典主题（默认）。</summary>
    public static BoardTheme Classic { get; } = FromPalette(BoardPalette.Classic);

    /// <summary>底层调色板（设置界面显示当前主题名用）。</summary>
    public BoardPalette Palette { get; }

    // ---------- 未翻开格（凸起感）----------

    public Color CoveredFill { get; }

    public Color CoveredHighlight { get; }

    public Color CoveredShadow { get; }

    // ---------- 已翻开格与网格线 ----------

    public Color RevealedFill { get; }

    public Color GridLine { get; }

    // ---------- 透视 ----------

    public Color ItemRevealedFill { get; }

    public Color ItemRevealedMineOutline { get; }

    // ---------- 雷与旗 ----------

    public Color ExplodedFill { get; }

    public Color MineBody { get; }

    public Color FlagPole { get; }

    public Color FlagCloth { get; }

    public Color WrongFlagCross { get; }

    public Color QuestionMark { get; }

    // ---------- 焦点环与高亮 ----------


    public Color AimingRing { get; }

    public Color AimingFill { get; }

    public Color HighlightRing { get; }

    public Color HighlightFill { get; }

    // ---------- 远景雷密度热力图（§1.5 可选，默认关闭）----------

    public Color HeatLowFill { get; }

    public Color HeatHighFill { get; }

    /// <summary>由调色板构造主题（会先把调色板规范化，幂等）。</summary>
    public static BoardTheme FromPalette(BoardPalette palette)
    {
        ArgumentNullException.ThrowIfNull(palette);

        palette.Normalize();
        return new BoardTheme(palette);
    }

    /// <summary>数字 1..8 的颜色；越界返回深灰兜底。</summary>
    public Color NumberColor(int number)
    {
        int index = number - 1;
        return index >= 0 && index < _numberColors.Length
            ? _numberColors[index]
            : Color.FromArgb(0xFF, 0x40, 0x40, 0x40);
    }

    private static Color ToColor(PaletteColor color) => Color.FromArgb(color.A, color.R, color.G, color.B);
}
