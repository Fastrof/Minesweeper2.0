using System.Globalization;

namespace MineSweeper.Core.Updates;

/// <summary>
/// 版本号的解析、比较与规范化。
///
/// 为什么不能直接用 <see cref="Version.Parse(string)"/>：
/// <list type="bullet">
/// <item><c>Version.Parse("1.1")</c> 得到的是 Build = -1 的对象，它<b>小于</b> <c>1.1.0</c>，
/// 于是"程序 1.1 版 + 线上 1.1.0 版"会被判成有新版本，天天弹提示。</item>
/// <item>GitHub 的 tag 习惯带 "v" 前缀，还可能带 <c>-beta.2</c> 之类的预发布后缀，
/// 直接丢给 <c>Version.Parse</c> 会抛异常。</item>
/// </list>
/// 所以统一在这里补齐成固定 4 段再比较：<b>"1.1" 与 "1.1.0" 必须相等</b>。
///
/// 解析失败一律返回 false，宁可漏报一个新版本，也不给玩家弹一个假提示。
/// </summary>
public static class AppVersion
{
    /// <summary>比较用的段数（补齐后的固定长度）。</summary>
    private const int SegmentCount = 4;

    /// <summary>
    /// 解析版本号文本并<b>补齐到 4 段</b>（"1.1" → 1.1.0.0）。
    /// 接受：<c>1.1</c> / <c>1.1.0</c> / <c>1.1.0.0</c> / <c>v1.1.0</c> / <c>"  1.1.0  "</c> / <c>1.1.0-beta.2</c>；
    /// 预发布与构建后缀（<c>-</c> / <c>+</c> 之后的部分）直接忽略 —— 更新提示只关心正式版本号。
    /// 空串、乱码、超过 4 段、含非数字段的一律返回 false。
    /// </summary>
    public static bool TryParse(string? text, out Version version)
    {
        version = new Version(0, 0, 0, 0);

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string trimmed = text.Trim();

        // "v" 只有后面紧跟数字才算前缀，否则 "version2" 会被砍成 "ersion2"
        if (trimmed.Length > 1 && (trimmed[0] == 'v' || trimmed[0] == 'V') && char.IsAsciiDigit(trimmed[1]))
        {
            trimmed = trimmed[1..];
        }

        // 预发布/构建元数据：从第一个 '-' 或 '+' 起全部丢掉
        int suffix = trimmed.IndexOfAny(['-', '+']);
        if (suffix >= 0)
        {
            trimmed = trimmed[..suffix].Trim();
        }

        if (trimmed.Length == 0)
        {
            return false;
        }

        string[] parts = trimmed.Split('.');
        if (parts.Length is < 1 or > SegmentCount)
        {
            return false;
        }

        // 刻意不 Trim 每一段："1. 1.0" 这种坏输入应当直接判失败，
        // NumberStyles.None 顺带挡掉 "+1" / "-1" / 空白。
        var segments = new int[SegmentCount];
        for (int i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out int value) || value < 0)
            {
                return false;
            }

            segments[i] = value;
        }

        version = new Version(segments[0], segments[1], segments[2], segments[3]);
        return true;
    }

    /// <summary>
    /// <paramref name="candidate"/> 是否比 <paramref name="current"/> 新。
    /// 任何一个解析不出来都返回 false：漏报一次更新只是让玩家晚一天看到提示，
    /// 误报则会让"跳过此版本"变得毫无意义。
    /// </summary>
    public static bool IsNewer(string? candidate, string? current)
    {
        if (!TryParse(candidate, out Version parsedCandidate) || !TryParse(current, out Version parsedCurrent))
        {
            return false;
        }

        return parsedCandidate > parsedCurrent;
    }

    /// <summary>
    /// 规范化成可展示、可比较的版本串：固定至少 3 段，末尾多余的 0 段去掉
    /// （<c>1.1</c> → "1.1.0"、<c>1.2.0.0</c> → "1.2.0"、<c>1.2.3.4</c> → "1.2.3.4"）。
    /// 解析失败时返回原串去空白后的结果（可能是空串），让上层原样展示、由策略层拒绝比较。
    /// </summary>
    public static string Normalize(string? text)
    {
        if (!TryParse(text, out Version version))
        {
            return (text ?? string.Empty).Trim();
        }

        int[] segments = [version.Major, version.Minor, version.Build, version.Revision];
        int count = SegmentCount;
        while (count > 3 && segments[count - 1] == 0)
        {
            count--;
        }

        return string.Join('.', segments.Take(count));
    }
}
