using MineSweeper.Core.Localization;

namespace MineSweeper.App.Linux.Services;

/// <summary>
/// 语言服务（对应 Windows 端 Core 的 <c>Strings</c> + App 的 <c>XamlLocalizer</c>）。
///
/// Linux 端比 Windows 端简单：界面全部是自绘或少量 XAML，且 XAML 里直接绑定
/// <see cref="Get"/> 的结果（切语言时重建/刷新文案），不需要"扫描可视化树反查"那一套。
/// </summary>
public sealed class LocalizationService
{
    private readonly Func<string> _currentCode;
    private readonly Action<string> _setLanguage;

    public LocalizationService(Func<string> currentCode, Action<string> setLanguage)
    {
        _currentCode = currentCode;
        _setLanguage = setLanguage;
    }

    /// <summary>语言变化（界面订阅它刷新全部文案）。</summary>
    public event EventHandler? LanguageChanged;

    /// <summary>当前语言代码（zh-Hans / en）。</summary>
    public string CurrentCode => _currentCode();

    /// <summary>可选语言（只列出文案完整的两套）。</summary>
    public IReadOnlyList<LanguageInfo> Languages { get; } = [.. LanguageCatalog.Complete];

    /// <summary>切换语言并通知界面。</summary>
    public void SetLanguage(string? code)
    {
        _setLanguage(code);
        AppPaths.Log($"[语言] 切换为 {CurrentCode}");
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>取一条文案（带参数）。</summary>
    public static string Get(string key, params object?[] args) => Strings.Get(key, args);

    /// <summary>
    /// 取文案；Core 里没有这个 key 时，用调用方给的中英兜底文案。
    ///
    /// Linux 端有些面板（设置、结算）在 Core 的语言表里还没有对应 key，
    /// 用这个重载可以避免把 key 本身显示到界面上。
    /// </summary>
    public static string T(string key, string zh, string en, params object?[] args)
    {
        string value = Strings.Get(key);
        if (string.Equals(value, key, StringComparison.Ordinal) || string.IsNullOrEmpty(value))
        {
            value = (Strings.CurrentCode ?? "zh-Hans").StartsWith("zh", StringComparison.Ordinal) ? zh : en;
        }

        return args.Length == 0
            ? value
            : string.Format(System.Globalization.CultureInfo.InvariantCulture, value, args);
    }

    /// <summary>把 Core 里已有的整句中文翻译成当前语言。</summary>
    public static string Translate(string? text) => Strings.TranslateLiteral(text);
}
