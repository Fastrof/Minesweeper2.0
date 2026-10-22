using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using MineSweeper.App.Linux.Services;
using MineSweeper.Core.Updates;

namespace MineSweeper.App.Linux.Views.Overlays;

/// <summary>
/// 「本版新增」面板（对应 Windows 端的 WhatsNew）。
///
/// 内容来自 Core 的 <c>WhatsNewCatalog</c>（内置、不联网），并且按**当前语言**实时翻译，
/// 所以切到英文再打开就是英文。
/// </summary>
public sealed class WhatsNewOverlay : Border, IOverlay
{
    public event EventHandler? CloseRequested;

    public WhatsNewOverlay(AppHost host, string? version = null)
    {
        Background = new SolidColorBrush(UiPalette.Current.Mask);
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Stretch;

        string target = version ?? UpdateService.DisplayVersion;
        WhatsNewEntry? entry = WhatsNewCatalog.Find(target);

        var stack = new StackPanel { Spacing = 8, Margin = new Thickness(20), MaxWidth = 560 };

        stack.Children.Add(new TextBlock
        {
            Text = entry?.Title ?? LocalizationService.Get("btn.whatsNew"),
            FontSize = 20,
            FontWeight = FontWeight.Bold,
            Foreground = new SolidColorBrush(UiPalette.Current.Text),
            TextWrapping = TextWrapping.Wrap,
        });

        if (entry is null)
        {
            stack.Children.Add(new TextBlock
            {
                Text = LocalizationService.T("whatsnew.empty", "这一版没有单独的更新说明。", "No release notes for this version."),
                Foreground = new SolidColorBrush(UiPalette.Current.SubText),
            });
        }
        else
        {
            foreach (string highlight in entry.Highlights)
            {
                stack.Children.Add(new TextBlock
                {
                    Text = "· " + highlight,
                    Foreground = new SolidColorBrush(UiPalette.Current.Text),
                    FontSize = 13,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 2, 0, 2),
                });
            }
        }

        var close = new Button
        {
            Content = LocalizationService.Get("common.gotIt"),
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0),
        };
        close.Click += (_, _) => CloseRequested?.Invoke(this, EventArgs.Empty);
        stack.Children.Add(close);

        Child = new Border
        {
            Background = new SolidColorBrush(UiPalette.Current.CardBg),
            BorderBrush = new SolidColorBrush(UiPalette.Current.CardBorder),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MaxHeight = 620,
            Child = new ScrollViewer { Content = stack },
        };
    }
}
