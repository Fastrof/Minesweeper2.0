using MineSweeper.Core.Themes;
using Xunit;

namespace MineSweeper.Core.Tests;

/// <summary>v1.3：界面颜色模式 + 棋盘/背景对比度保障。</summary>
public class UiThemeTests
{
    [Theory]
    [InlineData("system", UiThemeMode.System)]
    [InlineData("light", UiThemeMode.Light)]
    [InlineData("LIGHT", UiThemeMode.Light)]
    [InlineData("  dark  ", UiThemeMode.Dark)]
    [InlineData("深色", UiThemeMode.Dark)]
    [InlineData("浅色", UiThemeMode.Light)]
    [InlineData("klingon", UiThemeMode.System)]
    [InlineData("", UiThemeMode.System)]
    [InlineData(null, UiThemeMode.System)]
    public void 界面颜色模式_容错解析(string? id, UiThemeMode expected)
    {
        Assert.Equal(expected, UiThemeCatalog.Normalize(id));
    }

    [Fact]
    public void 界面颜色模式_三种且键与id一一对应()
    {
        Assert.Equal(3, UiThemeCatalog.Ids.Count);
        Assert.Equal("system", UiThemeCatalog.IdOf(UiThemeMode.System));
        Assert.Equal("light", UiThemeCatalog.IdOf(UiThemeMode.Light));
        Assert.Equal("dark", UiThemeCatalog.IdOf(UiThemeMode.Dark));
        Assert.Equal("uiTheme.system", UiThemeCatalog.KeyOf(UiThemeMode.System));
        Assert.Equal("uiTheme.light", UiThemeCatalog.KeyOf(UiThemeMode.Light));
        Assert.Equal("uiTheme.dark", UiThemeCatalog.KeyOf(UiThemeMode.Dark));
    }

    [Theory]
    [InlineData("#FFFFFF", 1.0)]
    [InlineData("#000000", 0.0)]
    public void 相对亮度_黑白基准(string hex, double expected)
    {
        Assert.Equal(expected, BoardContrast.RelativeLuminance(hex), 3);
    }

    /// <summary>核心保障：不管选哪套棋盘主题、哪种界面模式，棋盘都必须与背景分得开。</summary>
    [Theory]
    [InlineData("#C0C0C0", "#F3F3F3")]   // 经典棋盘 + 浅色界面
    [InlineData("#C0C0C0", "#202020")]   // 经典棋盘 + 深色界面（本来就够）
    [InlineData("#3A3A3E", "#202020")]   // 深色棋盘 + 深色界面 ← 就是玩家报的那类问题
    [InlineData("#3A3A3E", "#F3F3F3")]   // 深色棋盘 + 浅色界面
    [InlineData("#000000", "#000000")]   // 极端：同色
    [InlineData("#FFFFFF", "#FFFFFF")]   // 极端：同色
    [InlineData("#8A8A8A", "#808080")]   // 近似灰
    public void 棋盘与背景_永远分得开(string boardFill, string background)
    {
        string adjusted = BoardContrast.EnsureVisible(boardFill, background);

        Assert.True(
            BoardContrast.IsVisible(adjusted, background),
            $"棋盘 {boardFill} 在背景 {background} 下调整成 {adjusted} 后仍然分不开");

        // 幂等：再调一次不应该继续变形
        Assert.Equal(adjusted, BoardContrast.EnsureVisible(adjusted, background));
    }

    [Fact]
    public void 已经够远时_原样返回()
    {
        Assert.Equal("#C0C0C0", BoardContrast.EnsureVisible("#C0C0C0", "#202020"));
    }

    [Fact]
    public void 带井号与大小写_都能解析()
    {
        Assert.Equal(
            BoardContrast.RelativeLuminance("#C0C0C0"),
            BoardContrast.RelativeLuminance("c0c0c0"),
            6);
    }
}
