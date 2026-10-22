using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using MineSweeper.App.Linux.Services;
using MineSweeper.Core.Achievements;

namespace MineSweeper.App.Linux.Views.Overlays;

/// <summary>
/// 成就面板（对应 Windows 端主窗口里的成就覆盖层）：31 条、合计 1000 点、五类、2 条隐藏。
///
/// 判定与进度全在 Core（<c>AchievementRules</c> / <c>AchievementProgress</c>），这里只做展示。
/// 隐藏成就未解锁时显示 "???"（与 Windows 端一致）。
/// </summary>
public sealed class AchievementsOverlay : Border, IOverlay
{
    public event EventHandler? CloseRequested;

    public AchievementsOverlay(AppHost host)
    {
        Background = new SolidColorBrush(UiPalette.Current.Mask);
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Stretch;

        AchievementProgress progress = host.Storage.Achievements;
        var stack = new StackPanel { Spacing = 10, Margin = new Thickness(20) };

        int unlocked = AchievementCatalog.All.Count(a => progress.IsUnlocked(a.Id));
        int points = AchievementCatalog.All.Where(a => progress.IsUnlocked(a.Id)).Sum(a => a.Points);
        int totalPoints = AchievementCatalog.All.Sum(a => a.Points);

        stack.Children.Add(new TextBlock
        {
            Text = LocalizationService.Get("ach.summary", unlocked, AchievementCatalog.Count, points, totalPoints),
            FontSize = 20,
            FontWeight = FontWeight.Bold,
            Foreground = new SolidColorBrush(UiPalette.Current.Text),
        });

        foreach (AchievementCategory category in Enum.GetValues<AchievementCategory>())
        {
            List<AchievementDefinition> items = AchievementCatalog.All
                .Where(a => a.Category == category)
                .ToList();
            if (items.Count == 0)
            {
                continue;
            }

            stack.Children.Add(new TextBlock
            {
                Text = CategoryName(category),
                Foreground = new SolidColorBrush(UiPalette.Current.Accent),
                FontWeight = FontWeight.SemiBold,
                Margin = new Thickness(0, 8, 0, 0),
            });

            foreach (AchievementDefinition definition in items)
            {
                stack.Children.Add(BuildRow(progress, definition));
            }
        }

        var close = new Button
        {
            Content = LocalizationService.Get("common.close"),
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0),
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
            MaxWidth = 720,
            MaxHeight = 640,
            Child = new ScrollViewer { Content = stack },
        };
    }

    private static string CategoryName(AchievementCategory category) => category switch
    {
        AchievementCategory.Progress => LocalizationService.Get("ach.cat.progress"),
        AchievementCategory.Speed => LocalizationService.Get("ach.cat.speed"),
        AchievementCategory.Levels => LocalizationService.Get("ach.cat.levels"),
        AchievementCategory.Items => LocalizationService.Get("ach.cat.items"),
        AchievementCategory.Challenge => LocalizationService.Get("ach.cat.challenge"),
        _ => category.ToString(),
    };

    private static Control BuildRow(AchievementProgress progress, AchievementDefinition definition)
    {
        bool unlocked = progress.IsUnlocked(definition.Id);
        bool hidden = definition.Hidden && !unlocked;
        long current = unlocked ? definition.Target : Math.Min(progress.ProgressOf(definition.Id), definition.Target);

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(0, 3, 0, 3),
        };

        var left = new StackPanel { Spacing = 2 };
        left.Children.Add(new TextBlock
        {
            Text = hidden ? "???" : unlocked ? $"🏆 {definition.Title}" : definition.Title,
            Foreground = new SolidColorBrush(unlocked ? UiPalette.Current.AccentSoft : UiPalette.Current.Text),
            FontWeight = FontWeight.SemiBold,
        });
        left.Children.Add(new TextBlock
        {
            Text = hidden ? "???" : definition.Description,
            FontSize = 11,
            Foreground = new SolidColorBrush(UiPalette.Current.SubText),
            TextWrapping = TextWrapping.Wrap,
        });

        if (!hidden && definition.Target > 1)
        {
            left.Children.Add(new ProgressBar
            {
                Minimum = 0,
                Maximum = definition.Target,
                Value = current,
                Height = 6,
                MaxWidth = 420,
                HorizontalAlignment = HorizontalAlignment.Left,
                Foreground = new SolidColorBrush(UiPalette.Current.Accent),
                Background = new SolidColorBrush(UiPalette.Current.CardBorder),
            });
        }

        grid.Children.Add(left);

        var right = new TextBlock
        {
            Text = formattableString(definition, unlocked, current),
            Foreground = new SolidColorBrush(UiPalette.Current.SubText),
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);

        return grid;

        static string formattableString(AchievementDefinition definition, bool unlocked, long current)
            => unlocked
                ? $"{definition.Points} 点"
                : definition.Target > 1
                    ? $"{current}/{definition.Target} · {definition.Points} 点"
                    : $"{definition.Points} 点";
    }
}
