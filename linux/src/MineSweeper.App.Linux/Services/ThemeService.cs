using MineSweeper.Core.Themes;

namespace MineSweeper.App.Linux.Services;

/// <summary>
/// 主题服务：把"棋盘调色板 + 界面深浅色"落成 Avalonia 资源，并广播变化。
///
/// 职责边界与 Windows 端一致：
///   - Core <c>BoardPalette</c> 是**唯一颜色真相源**（4 套内置 + 逐项覆盖）；
///   - <c>BoardContrast</c> 负责"棋盘与窗口背景必须分得开"（WCAG 亮度差 ≥ 0.12）；
///   - 这里只做映射与事件。
/// </summary>
public sealed class ThemeService
{
    private readonly Func<UiThemeMode> _currentMode;
    private readonly Action<string> _persistUiTheme;

    public ThemeService(Func<UiThemeMode> currentMode, Action<string> persistUiTheme)
    {
        _currentMode = currentMode;
        _persistUiTheme = persistUiTheme;
    }

    /// <summary>主题变化（棋盘与界面都要重绘）。</summary>
    public event EventHandler? Changed;

    /// <summary>可选的界面深浅色。</summary>
    public IReadOnlyList<string> UiThemeIds => UiThemeCatalog.Ids;

    /// <summary>当前界面模式。</summary>
    public UiThemeMode Mode => _currentMode();

    /// <summary>当前是否深色（跟随系统时读 Avalonia 的实际主题）。</summary>
    public bool IsDark { get; private set; }

    /// <summary>可选的棋盘调色板（来自 Core）。</summary>
    public IReadOnlyList<BoardPalette> Palettes => BoardPalette.BuiltIn;

    /// <summary>解析当前调色板（含逐项覆盖），并把棋盘底色推离窗口底色。</summary>
    public BoardPalette ResolvePalette(BoardPalette? selected, bool darkUi)
    {
        BoardPalette palette = selected ?? BoardPalette.Classic;
        string background = darkUi ? "#202020" : "#F3F3F3";

        if (!BoardContrast.IsVisible(palette.CoveredFill, background))
        {
            // BoardPalette 是可变 class（不是 record），直接改这一项即可。
            palette.CoveredFill = BoardContrast.EnsureVisible(palette.CoveredFill, background);
        }

        return palette;
    }

    /// <summary>设置界面模式（system / light / dark）并落盘。</summary>
    public void SetMode(string id)
    {
        _persistUiTheme(id);
        IsDark = UiThemeCatalog.Normalize(id) == UiThemeMode.Dark;
        AppPaths.Log($"[主题] 界面模式 -> {id}");
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>外部改完调色板后广播一次（设置面板用）。</summary>
    public void NotifyChanged() => Changed?.Invoke(this, EventArgs.Empty);

    /// <summary>标记当前实际深浅色（跟随系统时由窗口的 ActualThemeVariantChanged 调用）。</summary>
    public void SetActualDark(bool dark)
    {
        if (IsDark == dark)
        {
            return;
        }

        IsDark = dark;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
