namespace MineSweeper.Core.Localization;

/// <summary>一种受支持的语言（v1.3）。</summary>
/// <param name="Code">BCP-47 代码（zh-Hans / en / pt-BR …），存档与商店清单都用它。</param>
/// <param name="NativeName">母语显示名 —— 语言下拉里只显示这个（玩家不需要先看懂"中文"才能找到自己的语言）。</param>
/// <param name="ChineseName">中文名，只用于日志与文档。</param>
/// <param name="IsRightToLeft">是否从右到左排版（阿拉伯语）。本轮只登记，排版在下一轮做。</param>
/// <param name="IsComplete">译文是否已完整。false = 该语言的界面回退到英文，下拉里标"即将推出"。</param>
public sealed record LanguageInfo(
    string Code,
    string NativeName,
    string ChineseName,
    bool IsRightToLeft,
    bool IsComplete);

/// <summary>
/// 支持的语言清单（v1.3）：目前只有简体中文与 English。
///
/// <para>
/// <b>目前只提供简体中文与 English 两种</b>（v1.3 决定：先把这两种做到 100% 干净，
/// 不做"列了十几项、点进去一半还是中文"的半成品）。
/// 将来要加语言：在 <see cref="All"/> 里补一条、把译文表补齐、把 IsComplete 改成 true 即可，
/// 守门测试会立刻检查它是否每条键都齐。
/// </para>
/// <para>
/// 别名容错很重要：玩家手改 settings.json 时很容易写成 <c>zh</c> / <c>zh-CN</c> / <c>pt</c> / <c>en-US</c>，
/// 这些都必须能认出来，不能因为写法不同就掉回默认语言。
/// </para>
/// </summary>
public static class LanguageCatalog
{
    /// <summary>首次安装的默认语言：简体中文（按项目所有者要求）。</summary>
    public const string DefaultCode = "zh-Hans";

    /// <summary>回退语言：任何缺失的键都从这里取。</summary>
    public const string FallbackCode = "en";

    /// <summary>全部受支持的语言，顺序即下拉框顺序（先中文，再第一梯队，再其余）。</summary>
    public static IReadOnlyList<LanguageInfo> All { get; } =
    [
        new("zh-Hans", "简体中文", "简体中文", false, true),
        new("en", "English", "英语", false, true),
    ];

    /// <summary>是否已完整的语言（= 已备齐译文的语言）。</summary>
    public static IEnumerable<LanguageInfo> Complete => All.Where(l => l.IsComplete);

    /// <summary>商店清单要声明的语言代码（全部都声明，商店才会按语言分发）。</summary>
    public static IReadOnlyList<string> CodesForManifest { get; } = [.. All.Select(l => l.Code)];

    /// <summary>按代码查语言（大小写不敏感，并容错常见别名）；查不到返回 null。</summary>
    public static LanguageInfo? Find(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return null;
        }

        string normalized = code.Trim().Replace('_', '-');

        // ① 完全一致
        foreach (LanguageInfo language in All)
        {
            if (string.Equals(language.Code, normalized, StringComparison.OrdinalIgnoreCase))
            {
                return language;
            }
        }

        string[] parts = normalized.Split('-');
        string primary = parts[0];
        string region = parts.Length > 1 ? parts[^1] : string.Empty;

        // ② 只写了主语言（zh / pt / en / ja…）：中文按"默认简体"，其余取该主语言的第一个
        if (region.Length == 0)
        {
            if (primary.Equals("zh", StringComparison.OrdinalIgnoreCase))
            {
                return FindExact("zh-Hans");
            }

            // 只写了主语言时按主语言匹配：pt → pt-BR、ja → ja
            return All.FirstOrDefault(l => string.Equals(l.Code.Split('-')[0], primary, StringComparison.OrdinalIgnoreCase));
        }

        // ③ 写了繁简（zh-Hans / zh_Hant 这类写法）：直接按它走
        if (primary.Equals("zh", StringComparison.OrdinalIgnoreCase) &&
            (region.Equals("Hans", StringComparison.OrdinalIgnoreCase) ||
             region.Equals("Hant", StringComparison.OrdinalIgnoreCase)))
        {
            return All.FirstOrDefault(l => l.Code.EndsWith(region, StringComparison.OrdinalIgnoreCase));
        }

        // ④ 带地区：优先"地区也一致"的，同时留一个同主语言的兜底（en-US → en、pt-PT → pt-BR）
        LanguageInfo? samePrimary = null;
        foreach (LanguageInfo language in All)
        {
            string[] candidate = language.Code.Split('-');
            if (!string.Equals(candidate[0], primary, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (candidate.Length > 1 && string.Equals(candidate[^1], region, StringComparison.OrdinalIgnoreCase))
            {
                return language;
            }

            samePrimary ??= language;
        }

        // ⑤ 中文的地区别名：CN/SG → 简体，TW/HK/MO → 繁体
        if (primary.Equals("zh", StringComparison.OrdinalIgnoreCase))
        {
            bool traditional = region.Equals("TW", StringComparison.OrdinalIgnoreCase)
                || region.Equals("HK", StringComparison.OrdinalIgnoreCase)
                || region.Equals("MO", StringComparison.OrdinalIgnoreCase);

            return FindExact(traditional ? "zh-Hant" : "zh-Hans");
        }

        return samePrimary;
    }

    private static LanguageInfo? FindExact(string code)
        => All.FirstOrDefault(l => string.Equals(l.Code, code, StringComparison.OrdinalIgnoreCase));

    /// <summary>按代码查语言；查不到或为空时返回默认语言（永不返回 null）。</summary>
    public static LanguageInfo Resolve(string? code) => Find(code) ?? Find(DefaultCode)!;
}
