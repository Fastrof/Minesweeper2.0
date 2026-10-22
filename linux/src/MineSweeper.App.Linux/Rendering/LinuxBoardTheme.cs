using Avalonia.Media;
using MineSweeper.Core.Themes;

namespace MineSweeper.App.Linux.Rendering;

/// <summary>
/// Linux 端棋盘配色。
///
/// 与 Windows 端的对应关系：
///   Core <c>BoardPalette</c>（21 个颜色键 + 4 套内置）→ 这里做一次 "hex → Avalonia Color" 映射，
///   所以 Linux 端自动获得 Windows 端全部主题能力；数字八色按经典规则固定（Windows 端同样是固定色）。
/// </summary>
public sealed class LinuxBoardTheme
{
    /// <summary>远景阈值：格宽小于它时只画色块、不画数字/旗子（与 Windows 端 BoardTheme.FarZoomCellSize 一致）。</summary>
    public const double FarZoomCellSize = 6;

    public static LinuxBoardTheme Classic { get; } = FromPalette(BoardPalette.Classic, false);

    /// <summary>从 Core 调色板构建主题。<paramref name="darkUi"/> 决定窗口底色（棋盘底色由调色板给）。</summary>
    public static LinuxBoardTheme FromPalette(BoardPalette palette, bool darkUi) => new()
    {
        Background = Parse(darkUi ? "#202020" : "#F3F3F3", darkUi ? Colors.Black : Colors.White),
        CoveredFill = Parse(palette.CoveredFill, Color.Parse("#C0C0C0")),
        CoveredHighlight = Parse(palette.CoveredHighlight, Color.Parse("#E8E8E8")),
        CoveredShadow = Parse(palette.CoveredShadow, Color.Parse("#8A8A8A")),
        RevealedFill = Parse(palette.RevealedFill, Color.Parse("#E4E4E4")),
        GridLine = Parse(palette.GridLine, Color.Parse("#B8B8B8")),
        ItemRevealedFill = Parse(palette.ItemRevealedFill, Color.Parse("#D6E4FF")),
        ExplodedFill = Parse(palette.ExplodedFill, Color.Parse("#FF5050")),
        MineFill = Parse(palette.MineBody, Color.Parse("#202020")),
        ItemRevealedMineOutline = Parse(palette.ItemRevealedMineOutline, Color.Parse("#1E63D8")),
        FlagCloth = Parse(palette.FlagCloth, Color.Parse("#E03030")),
        FlagPole = Parse(palette.FlagPole, Color.Parse("#404040")),
        QuestionFill = Parse(palette.QuestionMark, Color.Parse("#E0A800")),
        AimingRing = Parse(palette.AimingRing, Color.Parse("#1E63D8")),

        // 声呐扫过的区域：半透明蓝叠在未翻开的格子上（在浅色/深色下都能看出"这块扫过"）
        SonarMarkFill = darkUi
            ? Color.FromArgb(0x50, 0x6F, 0xA8, 0xFF)
            : Color.FromArgb(0x3A, 0x2E, 0x7C, 0xD8),
        SonarMarkBorder = darkUi ? Color.Parse("#8FB8FF") : Color.Parse("#1E63D8"),
        HighlightRing = Parse(palette.HighlightRing, Color.Parse("#1E885A")),
        Foreground = Parse(palette.MineBody, Color.Parse("#202020")),

        // 经典数字配色（1 蓝 / 2 绿 / 3 红 / 4 深蓝 / 5 棕 / 6 青 / 7 黑 / 8 灰）。
        // Windows 端 BoardTheme 同样是硬编码这一组，不随主题变化。
        Number1 = Color.Parse("#1E63D8"),
        Number2 = Color.Parse("#1B7A3A"),
        Number3 = Color.Parse("#D03030"),
        Number4 = Color.Parse("#2A2A8A"),
        Number5 = Color.Parse("#8A3A1A"),
        Number6 = Color.Parse("#1A7A8A"),
        Number7 = Color.Parse("#202020"),
        Number8 = Color.Parse("#707070"),
    };

    private static Color Parse(string? hex, Color fallback)
    {
        if (string.IsNullOrWhiteSpace(hex))
        {
            return fallback;
        }

        try
        {
            return Color.Parse(hex);
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            return fallback;
        }
    }

    public Color Background { get; init; }
    public Color CoveredFill { get; init; }
    public Color CoveredHighlight { get; init; }
    public Color CoveredShadow { get; init; }
    public Color RevealedFill { get; init; }
    public Color GridLine { get; init; }
    public Color FlagCloth { get; init; }
    public Color FlagPole { get; init; }
    public Color QuestionFill { get; init; }
    public Color MineFill { get; init; }
    public Color ItemRevealedMineOutline { get; init; }
    public Color ExplodedFill { get; init; }
    public Color ItemRevealedFill { get; init; }
    public Color AimingRing { get; init; }
    public Color SonarMarkFill { get; init; }
    public Color SonarMarkBorder { get; init; }
    public Color HighlightRing { get; init; }
    public Color Foreground { get; init; }
    public Color Number1 { get; init; }
    public Color Number2 { get; init; }
    public Color Number3 { get; init; }
    public Color Number4 { get; init; }
    public Color Number5 { get; init; }
    public Color Number6 { get; init; }
    public Color Number7 { get; init; }
    public Color Number8 { get; init; }

    public Color NumberColor(int adjacentMines) => adjacentMines switch
    {
        1 => Number1,
        2 => Number2,
        3 => Number3,
        4 => Number4,
        5 => Number5,
        6 => Number6,
        7 => Number7,
        8 => Number8,
        _ => Foreground,
    };
}
