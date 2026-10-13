using System;

namespace MineSweeper.Core.Themes;

/// <summary>界面颜色模式（v1.3）：跟随系统 / 浅色 / 深色。</summary>
public enum UiThemeMode
{
    /// <summary>跟随 Windows 的系统颜色设置（默认）。</summary>
    System,

    /// <summary>强制浅色。</summary>
    Light,

    /// <summary>强制深色。</summary>
    Dark,
}

/// <summary>
/// 界面颜色模式的清单与容错（v1.3）。
/// 与语言一样：认不出来的写法退回默认值，绝不白屏、绝不抛异常。
/// </summary>
public static class UiThemeCatalog
{
    public const string SystemId = "system";
    public const string LightId = "light";
    public const string DarkId = "dark";

    public static string DefaultId => SystemId;

    public static IReadOnlyList<string> Ids { get; } = [SystemId, LightId, DarkId];

    /// <summary>认不出来的（含手改坏、大小写、中文写法）统一退回 System。</summary>
    public static UiThemeMode Normalize(string? id) => id?.Trim().ToLowerInvariant() switch
    {
        "light" or "浅色" or "明亮" => UiThemeMode.Light,
        "dark" or "深色" or "暗色" => UiThemeMode.Dark,
        _ => UiThemeMode.System,
    };

    public static string IdOf(UiThemeMode mode) => mode switch
    {
        UiThemeMode.Light => LightId,
        UiThemeMode.Dark => DarkId,
        _ => SystemId,
    };

    /// <summary>对应语言表里的键（界面显示用，中英各一份）。</summary>
    public static string KeyOf(UiThemeMode mode) => mode switch
    {
        UiThemeMode.Light => "uiTheme.light",
        UiThemeMode.Dark => "uiTheme.dark",
        _ => "uiTheme.system",
    };
}

/// <summary>
/// 棋盘与界面背景的对比度保障（v1.3）。
///
/// <para>
/// 需求背景：界面切到深色后，如果棋盘主题本身也是深色，棋盘会和窗口背景糊在一起——
/// 玩家看不出棋盘从哪儿开始。这里的规则是"把棋盘底色推到与背景有足够亮度差"，
/// 保持色相不变（尊重玩家选的主题），必要时再靠一圈描边兜底。
/// </para>
/// <para>
/// 全部是纯计算，可单元测试；<see cref="EnsureVisible"/> 是幂等的（已经够远就原样返回）。
/// </para>
/// </summary>
public static class BoardContrast
{
    /// <summary>默认要求的最小相对亮度差（0.12 在深色界面下足以看清边界）。</summary>
    public const double DefaultMinDelta = 0.12;

    /// <summary>相对亮度（WCAG：0 = 黑，1 = 白）。</summary>
    public static double RelativeLuminance(string hex)
    {
        (int r, int g, int b) = Parse(hex);
        return (0.2126 * Channel(r)) + (0.7152 * Channel(g)) + (0.0722 * Channel(b));
    }

    /// <summary>两色的相对亮度差。</summary>
    public static double DeltaL(string a, string b) =>
        Math.Abs(RelativeLuminance(a) - RelativeLuminance(b));

    /// <summary>反差够不够。</summary>
    public static bool IsVisible(string boardFillHex, string backgroundHex, double minDelta = DefaultMinDelta) =>
        DeltaL(boardFillHex, backgroundHex) >= minDelta;

    /// <summary>
    /// 让棋盘底色与界面背景"分得开"：不够就朝远离背景的方向推，直到差值达标。
    /// 已经达标时原样返回（幂等），所以可以放心反复调用。
    /// </summary>
    public static string EnsureVisible(
        string boardFillHex,
        string backgroundHex,
        double minDelta = DefaultMinDelta)
    {
        if (IsVisible(boardFillHex, backgroundHex, minDelta))
        {
            return Normalize(boardFillHex);
        }

        (int r, int g, int b) = Parse(boardFillHex);
        bool goLighter = RelativeLuminance(backgroundHex) < RelativeLuminance(boardFillHex);

        for (int step = 1; step <= 20; step++)
        {
            double t = step / 20.0;
            (int nr, int ng, int nb) = goLighter
                ? (Lerp(r, 255, t), Lerp(g, 255, t), Lerp(b, 255, t))
                : (Lerp(r, 0, t), Lerp(g, 0, t), Lerp(b, 0, t));
            string candidate = ToHex(nr, ng, nb);
            if (IsVisible(candidate, backgroundHex, minDelta))
            {
                return candidate;
            }
        }

        // 极端情况（例如背景正好是中灰）：纯白/纯黑里挑对比更大的那个。
        return DeltaL("#FFFFFF", backgroundHex) >= DeltaL("#000000", backgroundHex) ? "#FFFFFF" : "#000000";
    }

    private static double Channel(int value)
    {
        double c = value / 255.0;
        return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }

    private static int Lerp(int from, int to, double t) =>
        Math.Clamp((int)Math.Round(from + ((to - from) * t)), 0, 255);

    private static (int R, int G, int B) Parse(string hex)
    {
        string text = (hex ?? string.Empty).Trim().TrimStart('#');
        if (text.Length == 8)
        {
            text = text[2..];   // 带 alpha 的 AARRGGBB：忽略 alpha
        }

        if (text.Length != 6 ||
            !int.TryParse(text[..2], System.Globalization.NumberStyles.HexNumber, null, out int r) ||
            !int.TryParse(text[2..4], System.Globalization.NumberStyles.HexNumber, null, out int g) ||
            !int.TryParse(text[4..6], System.Globalization.NumberStyles.HexNumber, null, out int b))
        {
            return (0, 0, 0);   // 认不出来按黑色处理（配合描边仍能看见棋盘）
        }

        return (r, g, b);
    }

    private static string Normalize(string hex)
    {
        (int r, int g, int b) = Parse(hex);
        return ToHex(r, g, b);
    }

    private static string ToHex(int r, int g, int b) => $"#{r:X2}{g:X2}{b:X2}";
}
