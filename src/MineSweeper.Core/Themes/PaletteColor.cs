using System.Globalization;

namespace MineSweeper.Core.Themes;

/// <summary>
/// 主题里的一个颜色（纯数据，不依赖任何 UI 类型）。
///
/// 文本表示沿用 XAML 的惯例，支持三种写法，方便玩家手改 theme.json：
/// <list type="bullet">
/// <item><c>#RGB</c>——简写，每位重复一次（<c>#0AF</c> = <c>#00AAFF</c>）</item>
/// <item><c>#RRGGBB</c>——不透明</item>
/// <item><c>#AARRGGBB</c>——带透明度（瞄准/高亮的半透明填充需要）</item>
/// </list>
/// </summary>
public readonly record struct PaletteColor(byte A, byte R, byte G, byte B)
{
    /// <summary>不透明颜色。</summary>
    public PaletteColor(byte r, byte g, byte b)
        : this(0xFF, r, g, b)
    {
    }

    /// <summary>是否完全不透明。</summary>
    public bool IsOpaque => A == 0xFF;

    /// <summary>解析 <c>#RGB</c> / <c>#RRGGBB</c> / <c>#AARRGGBB</c>；<c>#</c> 可省略。</summary>
    public static bool TryParse(string? text, out PaletteColor color)
    {
        color = default;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string body = text.Trim().TrimStart('#');

        // #RGB 简写
        if (body.Length == 3)
        {
            body = string.Concat(body[0], body[0], body[1], body[1], body[2], body[2]);
        }

        if (body.Length is not (6 or 8))
        {
            return false;
        }

        if (!TryParseByte(body, 0, out byte first)
            || !TryParseByte(body, 2, out byte second)
            || !TryParseByte(body, 4, out byte third))
        {
            return false;
        }

        if (body.Length == 6)
        {
            color = new PaletteColor(0xFF, first, second, third);
            return true;
        }

        // 8 位：AARRGGBB
        if (!TryParseByte(body, 6, out byte fourth))
        {
            return false;
        }

        color = new PaletteColor(first, second, third, fourth);
        return true;
    }

    /// <summary>解析失败时返回 <paramref name="fallback"/>，绝不抛异常（存档/主题文件是玩家可手改的）。</summary>
    public static PaletteColor ParseOr(string? text, PaletteColor fallback)
        => TryParse(text, out PaletteColor parsed) ? parsed : fallback;

    /// <summary>输出文本形式（不透明时省略 alpha）。</summary>
    public string ToHex()
        => IsOpaque
            ? $"#{R:X2}{G:X2}{B:X2}"
            : $"#{A:X2}{R:X2}{G:X2}{B:X2}";

    private static bool TryParseByte(string text, int start, out byte value)
        => byte.TryParse(
            text.AsSpan(start, 2),
            NumberStyles.HexNumber,
            CultureInfo.InvariantCulture,
            out value);
}
