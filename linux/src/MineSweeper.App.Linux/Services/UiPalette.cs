using Avalonia.Media;

namespace MineSweeper.App.Linux.Services;

/// <summary>
/// 界面配色（浅色 / 深色两套）。
///
/// 为什么要有这一层：Linux 桌面的"系统主题"探测在 WSLg / 部分桌面环境下不可靠
/// （经常被判成深色），导致玩家一打开就是全黑界面。所以：
///   - **默认浅色**（经典扫雷的观感），"跟随系统"在 Linux 端按浅色处理；
///   - 玩家显式选「深色」才切深色；
///   - 面板卡片、文字、金色强调色都从这里取，避免每个面板各写一套颜色。
/// </summary>
public sealed record UiPalette(
    Color WindowBg,
    Color ChromeBg,
    Color ChromeBorder,
    Color Text,
    Color SubText,
    Color CardBg,
    Color CardBorder,
    Color Mask,
    Color Accent,
    Color AccentSoft,
    Color Success,
    Color Danger)
{
    public static UiPalette Light { get; } = new(
        WindowBg: Color.Parse("#F3F3F3"),
        ChromeBg: Color.Parse("#E9E9E9"),
        ChromeBorder: Color.Parse("#CFCFCF"),
        Text: Color.Parse("#1F1F1F"),
        SubText: Color.Parse("#5F5F5F"),
        CardBg: Color.Parse("#FFFFFF"),
        CardBorder: Color.Parse("#CFCFCF"),
        Mask: Color.Parse("#40000000"),
        Accent: Color.Parse("#A67C00"),
        AccentSoft: Color.Parse("#8A6A00"),
        Success: Color.Parse("#2E7D32"),
        Danger: Color.Parse("#C62828"));

    public static UiPalette Dark { get; } = new(
        WindowBg: Color.Parse("#202020"),
        ChromeBg: Color.Parse("#2D2D2D"),
        ChromeBorder: Color.Parse("#4A4A4A"),
        Text: Color.Parse("#F0F0F0"),
        SubText: Color.Parse("#9A9A9A"),
        CardBg: Color.Parse("#2D2D2D"),
        CardBorder: Color.Parse("#4A4A4A"),
        Mask: Color.Parse("#90000000"),
        Accent: Color.Parse("#E0C060"),
        AccentSoft: Color.Parse("#FFD34D"),
        Success: Color.Parse("#7FD08A"),
        Danger: Color.Parse("#FF6B6B"));

    /// <summary>当前生效的配色（由 ThemeService 在启动与切换时设置）。</summary>
    public static UiPalette Current { get; private set; } = Light;

    /// <summary>按深浅切换并返回新配色。</summary>
    public static UiPalette Use(bool dark)
    {
        Current = dark ? Dark : Light;
        return Current;
    }

    public IBrush Brush(Color color) => new SolidColorBrush(color);
}
