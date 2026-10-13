using System.Globalization;

namespace MineSweeper.Core.Localization;

/// <summary>
/// 界面文案的唯一来源（v1.3）。
///
/// <para>
/// <b>为什么放在 Core、而不是用 WinUI 的 .resw</b>：本工程大部分文案是**代码里生成的**
/// （Toast、关卡名与目标、成就标题与描述、道具名、错误与降级提示），Core 又不允许引用 WinRT。
/// 如果 XAML 走一套 .resw、Core 走另一套，就会出现"两套真相"，迟早对不上。
/// 所以统一走这里：Core 与 App 读同一张表。
/// </para>
/// <para>
/// 取不到当前语言的条目时**逐级回退**：当前语言 → English → 键名本身。
/// 键名兜底是为了"宁可露出一串 key，也不要白屏"；同时守门测试会保证这种情况在已完成的语言里不会发生。
/// </para>
/// </summary>
public static class Strings
{
    private static string _currentCode = LanguageCatalog.DefaultCode;

    /// <summary>当前语言代码（总是 <see cref="LanguageCatalog"/> 里的合法代码）。</summary>
    public static string CurrentCode => _currentCode;

    /// <summary>切换语言。传入无法识别的代码时退回默认语言（简体中文），不会抛异常。</summary>
    public static void SetLanguage(string? code) => _currentCode = LanguageCatalog.Resolve(code).Code;

    /// <summary>取一条文案。<paramref name="args"/> 用于填 <c>{0}</c> 占位符。</summary>
    public static string Get(string key, params object?[] args)
    {
        string text = Resolve(key);

        if (args.Length == 0)
        {
            return text;
        }

        try
        {
            return string.Format(CultureInfo.CurrentCulture, text, args);
        }
        catch (FormatException)
        {
            // 占位符和参数对不上时不要炸游戏：把原文给出去，守门测试会盯住这种键
            return text;
        }
    }

    /// <summary>当前的解析结果（不做格式化），便于单测直接比对。</summary>
    public static string Resolve(string key) => ResolveFor(_currentCode, key);

    /// <summary>
    /// 指定语言下的解析结果（含回退链），守门测试直接比对它。
    /// 回退顺序：该语言 → English → 键名本身。
    /// </summary>
    public static string ResolveFor(string languageCode, string key)
    {
        if (StringTables.For(languageCode).TryGetValue(key, out string? text) && !string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        if (StringTables.For(LanguageCatalog.FallbackCode).TryGetValue(key, out string? fallback) &&
            !string.IsNullOrWhiteSpace(fallback))
        {
            return fallback;
        }

        return key;
    }

    /// <summary>
    /// 按"简体中文原文"反查并翻译。
    ///
    /// <para>
    /// 这是给**没法逐处改调用点**的地方用的：Toast 的标题/正文散落在上百个调用点，
    /// XAML 里的静态标签也有五十来处；与其逐个改成 key，不如在出口处拿原文反查一次。
    /// </para>
    /// <para>
    /// 查不到就原样返回（绝不吞掉内容）。所以"表里漏了一条"的表现是那句还是中文，
    /// 而不是变成空白或键名 —— 漏了能一眼看见，也方便我按日志补齐。
    /// </para>
    /// </summary>
    public static string TranslateLiteral(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return text ?? string.Empty;
        }

        foreach (string key in StringTables.For("zh-Hans").Keys)
        {
            if (string.Equals(StringTables.For("zh-Hans")[key], text, StringComparison.Ordinal))
            {
                return Get(key);
            }
        }

        return text;
    }

    /// <summary>该键在指定语言下是否有真译文（用于守门测试与"待校对清单"）。</summary>
    public static bool HasTranslation(string languageCode, string key)
        => StringTables.For(languageCode).TryGetValue(key, out string? text) && !string.IsNullOrWhiteSpace(text);

    /// <summary>某语言的键集合（守门测试用）。</summary>
    public static IReadOnlyCollection<string> KeysOf(string languageCode) => [.. StringTables.For(languageCode).Keys];
}
