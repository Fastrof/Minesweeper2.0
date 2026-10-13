using System.Text.RegularExpressions;
using MineSweeper.Core.Localization;
using Xunit;

namespace MineSweeper.Core.Tests;

/// <summary>
/// v1.3 全球化的**守门测试**。
///
/// <para>
/// 这组测试的唯一使命：让"半成品语言"上不了线。
/// 只要某种语言被标成 IsComplete，它就必须每条键都有真译文、占位符与中文版完全一致、没有空串；
/// 而还没翻译的语言必须干净地回退到英文（而不是露出一串键名）。
/// </para>
/// </summary>
public sealed class LocalizationTests
{
    private static readonly Regex Placeholder = new(@"\{(\d+)\}", RegexOptions.Compiled);

    [Fact]
    public void 语言清单完整且代码唯一()
    {
        Assert.Equal(2, LanguageCatalog.All.Count);
        Assert.Equal(LanguageCatalog.All.Count, LanguageCatalog.All.Select(l => l.Code).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(LanguageCatalog.All.Count, LanguageCatalog.CodesForManifest.Count);

        foreach (LanguageInfo language in LanguageCatalog.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(language.NativeName), language.Code);
            Assert.False(string.IsNullOrWhiteSpace(language.ChineseName), language.Code);
        }
    }

    [Fact]
    public void 目前没有从右到左的语言()
    {
        // 字段留着给以后（阿拉伯语等）；当前只提供简体中文与 English，所以没有 RTL
        Assert.Empty(LanguageCatalog.All.Where(l => l.IsRightToLeft));
    }

    [Fact]
    public void 首次安装的默认语言是简体中文且必须完整()
    {
        Assert.Equal("zh-Hans", LanguageCatalog.DefaultCode);
        Assert.True(LanguageCatalog.Find(LanguageCatalog.DefaultCode)!.IsComplete, "默认语言必须有完整译文");
        Assert.True(LanguageCatalog.Find(LanguageCatalog.FallbackCode)!.IsComplete, "回退语言必须有完整译文");
    }

    [Theory]
    [InlineData("zh", "zh-Hans")]
    [InlineData("zh-CN", "zh-Hans")]
    [InlineData("zh-SG", "zh-Hans")]
    [InlineData("EN", "en")]
    [InlineData("en-US", "en")]
    [InlineData("zh-Hant", "zh-Hans")]
    [InlineData("ja", "zh-Hans")]
    [InlineData("pt-BR", "zh-Hans")]
    public void 语言代码容错别名与大小写(string input, string expected)
        => Assert.Equal(expected, LanguageCatalog.Resolve(input).Code);

    [Fact]
    public void 认不出的语言代码退回默认语言()
    {
        Assert.Equal("zh-Hans", LanguageCatalog.Resolve(null).Code);
        Assert.Equal("zh-Hans", LanguageCatalog.Resolve("").Code);
        Assert.Equal("zh-Hans", LanguageCatalog.Resolve("   ").Code);
        Assert.Equal("zh-Hans", LanguageCatalog.Resolve("klingon").Code);
        Assert.Null(LanguageCatalog.Find("klingon"));
    }

    [Fact]
    public void 已完成的语言必须键集合完全一致()
    {
        IReadOnlyCollection<string> reference = Strings.KeysOf("zh-Hans");

        foreach (LanguageInfo language in LanguageCatalog.Complete)
        {
            IReadOnlyCollection<string> keys = Strings.KeysOf(language.Code);

            Assert.Equal(
                reference.OrderBy(k => k, StringComparer.Ordinal),
                keys.OrderBy(k => k, StringComparer.Ordinal));

            foreach (string key in reference)
            {
                Assert.True(Strings.HasTranslation(language.Code, key), $"{language.Code} 缺键：{key}");
            }
        }
    }

    [Fact]
    public void 已完成的语言不允许空串或纯空白()
    {
        foreach (LanguageInfo language in LanguageCatalog.Complete)
        {
            foreach (string key in Strings.KeysOf(language.Code))
            {
                Assert.False(
                    string.IsNullOrWhiteSpace(Strings.ResolveFor(language.Code, key)),
                    $"{language.Code} 的 {key} 是空串");
            }
        }
    }

    [Fact]
    public void 占位符必须与中文版完全一致()
    {
        foreach (string key in Strings.KeysOf("zh-Hans"))
        {
            string[] expected = Placeholders(Strings.ResolveFor("zh-Hans", key));

            foreach (LanguageInfo language in LanguageCatalog.Complete)
            {
                string[] actual = Placeholders(Strings.ResolveFor(language.Code, key));
                Assert.Equal(expected, actual);
            }
        }
    }

    [Fact]
    public void 未完成的语言回退到英文而不是露出键名()
    {
        foreach (LanguageInfo language in LanguageCatalog.All.Where(l => !l.IsComplete))
        {
            foreach (string key in Strings.KeysOf("zh-Hans"))
            {
                string resolved = Strings.ResolveFor(language.Code, key);

                Assert.NotEqual(key, resolved);
                Assert.Equal(Strings.ResolveFor("en", key), resolved);
            }
        }
    }

    [Fact]
    public void 切换语言后取到对应文案且非法代码退回默认()
    {
        try
        {
            Strings.SetLanguage("en");
            Assert.Equal("en", Strings.CurrentCode);
            Assert.Equal("History", Strings.Get("btn.history"));

            Strings.SetLanguage("ja");
            Assert.Equal("zh-Hans", Strings.CurrentCode);
            // 已被移除的语言代码：退回默认语言（简体中文），不再是「回退英文」
            Assert.Equal("历史记录", Strings.Get("btn.history"));

            Strings.SetLanguage("klingon");
            Assert.Equal("zh-Hans", Strings.CurrentCode);
            Assert.Equal("历史记录", Strings.Get("btn.history"));
        }
        finally
        {
            Strings.SetLanguage(LanguageCatalog.DefaultCode);
        }
    }

    [Fact]
    public void 格式化失败时返回原文而不是抛异常()
    {
        // "update.currentVersion" 只有一个占位符；多给参数不该炸
        Strings.SetLanguage("zh-Hans");
        Assert.Equal("当前版本 1.3", Strings.Get("update.currentVersion", "1.3"));
        Assert.Equal("当前版本 1.3", Strings.Get("update.currentVersion", "1.3", "多余参数"));

        // 完全不存在的键：宁可露出键名，也不要白屏
        Assert.Equal("no.such.key", Strings.Get("no.such.key"));
    }

    private static string[] Placeholders(string text)
        => [.. Placeholder.Matches(text).Select(m => m.Groups[1].Value).OrderBy(v => v, StringComparer.Ordinal)];
}
