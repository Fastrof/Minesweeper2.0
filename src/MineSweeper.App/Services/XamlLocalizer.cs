using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using MineSweeper.Core.Localization;

namespace MineSweeper.App.Services;

/// <summary>
/// 窗口文案本地化（v1.3）：扫描一次可视化树，把"文字正好等于简体中文原文"的静态文案换成当前语言。
///
/// <para>
/// 主窗口有自己的一份（要记住控件以便切换语言时重刷）；这个静态版是给**用完即弃**的窗口用的：
/// 「关于游戏」「关于作者」每次打开都新建，所以扫一次就够，不需要登记表。
/// </para>
/// <para>
/// 覆盖的文案位置：TextBlock.Text、ContentControl.Content、ToggleSwitch 的 Header/OnContent/OffContent、
/// TextBox.PlaceholderText，以及 <b>ToolTip</b>（右键提示这类以前一直漏掉）。
/// 表里没有的原文原样保留 —— 漏了能一眼看见，不会静默变成键名。
/// </para>
/// </summary>
internal static class XamlLocalizer
{
    /// <summary>把一个窗口/面板的静态文案套上当前语言，返回翻译了几处（便于自检写日志）。</summary>
    public static int LocalizeOnce(DependencyObject? root)
    {
        if (root is null)
        {
            return 0;
        }

        var sources = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string key in Strings.KeysOf("zh-Hans"))
        {
            string source = Strings.ResolveFor("zh-Hans", key);
            if (!string.IsNullOrWhiteSpace(source) && !string.Equals(source, key, StringComparison.Ordinal))
            {
                sources.TryAdd(source, key);
            }
        }

        return Walk(root, sources);
    }

    private static int Walk(DependencyObject node, Dictionary<string, string> sources)
    {
        int count = 0;

        if (Translate(node) is { } applied)
        {
            applied();
            count++;
        }

        // ToolTip 挂在元素上，不是子元素，所以单独看一次
        if (node is FrameworkElement element &&
            ToolTipService.GetToolTip(element) is string tip &&
            sources.TryGetValue(tip, out string? tipKey))
        {
            string translated = Strings.Get(tipKey);
            ToolTipService.SetToolTip(element, translated);
            count++;
        }

        int children = VisualTreeHelper.GetChildrenCount(node);
        for (int i = 0; i < children; i++)
        {
            count += Walk(VisualTreeHelper.GetChild(node, i), sources);
        }

        return count;
    }

    /// <summary>命中就返回"写回文案"的委托，否则返回 null。</summary>
    private static Action? Translate(DependencyObject node)
    {
        if (node is TextBlock block && Find(block.Text, out string? t1))
        {
            return () => block.Text = Strings.Get(t1!);
        }

        if (node is ContentControl { Content: string content } control && Find(content, out string? t2))
        {
            return () => control.Content = Strings.Get(t2!);
        }

        if (node is TextBox { PlaceholderText: { Length: > 0 } placeholder } box && Find(placeholder, out string? t3))
        {
            return () => box.PlaceholderText = Strings.Get(t3!);
        }

        if (node is ToggleSwitch toggle)
        {
            if (toggle.Header is string header && Find(header, out string? t4))
            {
                return () => toggle.Header = Strings.Get(t4!);
            }

            if (toggle.OnContent is string on && Find(on, out string? t5))
            {
                return () => toggle.OnContent = Strings.Get(t5!);
            }

            if (toggle.OffContent is string off && Find(off, out string? t6))
            {
                return () => toggle.OffContent = Strings.Get(t6!);
            }
        }

        return null;
    }

    private static bool Find(string? text, out string? key)
    {
        key = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        foreach (string candidate in Strings.KeysOf("zh-Hans"))
        {
            if (string.Equals(Strings.ResolveFor("zh-Hans", candidate), text, StringComparison.Ordinal))
            {
                key = candidate;
                return true;
            }
        }

        return false;
    }
    /// <summary>取节点上第一处字符串文案（范围与 <see cref="Translate"/> 一致），供诊断使用。</summary>
    private static string? SeeText(DependencyObject node)
    {
        if (node is TextBlock block && !string.IsNullOrWhiteSpace(block.Text))
        {
            return block.Text;
        }

        if (node is ContentControl { Content: string content } && !string.IsNullOrWhiteSpace(content))
        {
            return content;
        }

        if (node is TextBox { PlaceholderText: { Length: > 0 } placeholder })
        {
            return placeholder;
        }

        if (node is ToggleSwitch toggle)
        {
            if (toggle.Header is string header && header.Length > 0)
            {
                return header;
            }

            if (toggle.OnContent is string on && on.Length > 0)
            {
                return on;
            }

            if (toggle.OffContent is string off && off.Length > 0)
            {
                return off;
            }
        }

        if (node is FrameworkElement element &&
            ToolTipService.GetToolTip(element) is string tip && tip.Length > 0)
        {
            return tip;
        }

        return null;
    }

    /// <summary>是否含中文（判断"漏翻"）。</summary>
    private static bool HasCjk(string text)
    {
        foreach (char c in text)
        {
            if (c is >= '\u4e00' and <= '\u9fff')
            {
                return true;
            }
        }

        return false;
    }
}
