using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using MineSweeper.App.Linux.Services;
using MineSweeper.Core.Levels;
using MineSweeper.Core.Localization;

namespace MineSweeper.App.Linux.Views.Overlays;

/// <summary>
/// 关卡面板（**按 Windows 端 <c>LevelPanel</c> 重做**）：6 章 × 6 关。
///
/// 每关一张卡片：编号 + 名称、盘面与星数、星级门槛、最好用时；未解锁显示 🔒 并置灰。
/// 顶部汇总「已通关 x/36 · 星数 y/108」，右下角「返回游戏」。
/// 触发彩蛋后，最下面会多出隐藏关「EX-1 卡布奇诺」（每次打开重摇盘面）。
/// </summary>
public sealed class LevelOverlay : Border, IOverlay
{
    public event EventHandler? CloseRequested;

    public LevelOverlay(AppHost host, Action<LevelDefinition> onStartLevel)
    {
        LevelProgress progress = host.Storage.LevelProgress;

        Background = new SolidColorBrush(UiPalette.Current.Mask);
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Stretch;

        var stack = new StackPanel { Spacing = 12, Margin = new Thickness(20) };

        // ---- 顶部：标题 + 汇总 + 返回 ----
        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        header.Children.Add(new TextBlock
        {
            Text = LocalizationService.Get("btn.levels"),
            FontSize = 18,
            FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(UiPalette.Current.Text),
            VerticalAlignment = VerticalAlignment.Center,
        });
        header.Children.Add(new TextBlock
        {
            Text = string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                LocalizationService.T("levels.summary", "已通关 {0}/{1} 关 · 星数 {2}/{3}",
                    "Cleared {0}/{1} · Stars {2}/{3}"),
                progress.ClearedCount, LevelTable.Count, progress.TotalStars, LevelTable.Count * 3),
            Foreground = new SolidColorBrush(UiPalette.Current.SubText),
            VerticalAlignment = VerticalAlignment.Center,
        });
        var back = new Button
        {
            Content = LocalizationService.Get("common.backToGame"),
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        back.Click += (_, _) => CloseRequested?.Invoke(this, EventArgs.Empty);
        header.Children.Add(back);
        stack.Children.Add(header);

        // ---- 按章分组 ----
        for (int chapter = 1; chapter <= LevelTable.ChapterCount; chapter++)
        {
            IReadOnlyList<LevelDefinition> levels = LevelTable.Chapter(chapter);
            if (levels.Count == 0)
            {
                continue;
            }

            stack.Children.Add(new TextBlock
            {
                Text = string.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    LocalizationService.T("levels.chapter", "第 {0} 章 · {1}", "Chapter {0} · {1}"),
                    chapter,
                    Strings.TranslateLiteral(levels[0].ChapterName)),
                Foreground = new SolidColorBrush(UiPalette.Current.Accent),
                FontWeight = FontWeight.SemiBold,
                Margin = new Thickness(0, 10, 0, 0),
            });

            var wrap = new WrapPanel { Orientation = Orientation.Horizontal };
            foreach (LevelDefinition level in levels)
            {
                wrap.Children.Add(BuildCard(progress, level, onStartLevel));
            }

            stack.Children.Add(wrap);
        }

        // ---- 隐藏关（只有彩蛋解锁后才出现） ----
        if (progress.SecretUnlocked)
        {
            stack.Children.Add(new TextBlock
            {
                Text = LocalizationService.T("levels.secret", "隐藏章节", "Secret chapter"),
                Foreground = new SolidColorBrush(UiPalette.Current.Accent),
                FontWeight = FontWeight.SemiBold,
                Margin = new Thickness(0, 14, 0, 0),
            });

            LevelDefinition secret = LevelTable.CreateSecret();
            var wrap = new WrapPanel { Orientation = Orientation.Horizontal };
            wrap.Children.Add(BuildCard(progress, secret, onStartLevel));
            stack.Children.Add(wrap);
        }

        var close = new Button
        {
            Content = LocalizationService.Get("common.close"),
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0),
        };
        close.Click += (_, _) => CloseRequested?.Invoke(this, EventArgs.Empty);
        stack.Children.Add(close);

        Child = new Border
        {
            Background = new SolidColorBrush(UiPalette.Current.CardBg),
            BorderBrush = new SolidColorBrush(UiPalette.Current.CardBorder),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(24),
            MaxWidth = 900,
            MaxHeight = 700,
            Child = new ScrollViewer { Content = stack },
        };
    }

    /// <summary>单关卡片（编号/名称、盘面与星数、目标、最好用时）。</summary>
    private static Control BuildCard(
        LevelProgress progress, LevelDefinition level, Action<LevelDefinition> onStart)
    {
        bool unlocked = LevelRules.IsUnlocked(progress, level);
        int stars = progress.StarsOf(level.Id);
        double? best = progress.BestSecondsOf(level.Id);

        var content = new StackPanel { Spacing = 2 };

        content.Children.Add(new TextBlock
        {
            Text = unlocked
                ? $"{level.DisplayId} {Strings.TranslateLiteral(level.Name)}"
                : $"🔒 {level.DisplayId}",
            FontSize = 14,
            FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(UiPalette.Current.Text),
        });

        content.Children.Add(new TextBlock
        {
            Text = unlocked
                ? $"{level.BoardText} · {LevelRules.StarsText(stars)}"
                : LocalizationService.Get("levelCard.locked"),
            FontSize = 11,
            Foreground = new SolidColorBrush(UiPalette.Current.SubText),
            TextWrapping = TextWrapping.Wrap,
        });

        if (unlocked)
        {
            content.Children.Add(new TextBlock
            {
                Text = level.GoalText,
                FontSize = 11,
                Foreground = new SolidColorBrush(UiPalette.Current.SubText),
                TextWrapping = TextWrapping.Wrap,
            });

            if (best is { } seconds)
            {
                content.Children.Add(new TextBlock
                {
                    Text = string.Format(
                        System.Globalization.CultureInfo.InvariantCulture,
                        LocalizationService.Get("levelCard.best", seconds.ToString("F1")),
                        seconds),
                    FontSize = 11,
                    Foreground = new SolidColorBrush(UiPalette.Current.Accent),
                });
            }
        }

        var button = new Button
        {
            Content = content,
            Width = 240,
            MinHeight = 92,
            Margin = new Thickness(0, 0, 10, 10),
            IsEnabled = unlocked,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            VerticalContentAlignment = VerticalAlignment.Top,
            Padding = new Thickness(12, 10),
        };

        if (unlocked)
        {
            button.Click += (_, _) => onStart(level);
        }

        return button;
    }
}
