using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using MineSweeper.App.Linux.Services;
using MineSweeper.Core;
using MineSweeper.Core.History;
using MineSweeper.Core.Persistence;

namespace MineSweeper.App.Linux.Views.Overlays;

/// <summary>
/// 记录 + 历史面板（对应 Windows 端的「历史记录」弹窗与最快记录表）。
///
/// 数据全部来自 Core：<c>GameRecords</c>（三张表）与 <c>PlayHistory</c>（从第一次扫雷起累计）。
/// </summary>
public sealed class RecordsOverlay : Border, IOverlay
{
    public event EventHandler? CloseRequested;

    public RecordsOverlay(AppHost host)
    {
        Background = new SolidColorBrush(UiPalette.Current.Mask);
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Stretch;

        GameRecords records = host.Storage.Records;
        PlayHistory history = host.Storage.History;

        var stack = new StackPanel { Spacing = 8, Margin = new Thickness(20), MaxWidth = 520 };

        stack.Children.Add(new TextBlock
        {
            Text = LocalizationService.T("records.title", "记录与统计", "Records & statistics"),
            FontSize = 20,
            FontWeight = FontWeight.Bold,
            Foreground = new SolidColorBrush(UiPalette.Current.Text),
        });

        stack.Children.Add(Section(LocalizationService.T("records.best", "最快记录", "Best times")));

        AddBest(stack, records, DifficultyPreset.Beginner);
        AddBest(stack, records, DifficultyPreset.Intermediate);
        AddBest(stack, records, DifficultyPreset.Expert);

        stack.Children.Add(Section(LocalizationService.Get("btn.history")));

        stack.Children.Add(Line(LocalizationService.T("records.mines", "累计扫雷", "Mines swept"),
            history.TotalMinesSwept.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)));
        stack.Children.Add(Line(LocalizationService.T("records.games", "总局数", "Games"),
            history.TotalGames.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)));
        stack.Children.Add(Line(LocalizationService.T("records.wins", "胜场", "Wins"),
            history.TotalWins.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)));
        stack.Children.Add(Line(LocalizationService.T("records.time", "总用时", "Total time"),
            PlayHistory.FormatDuration(history.TotalPlayedSeconds)));

        if (history.FirstSweptAtUtc is not null)
        {
            stack.Children.Add(Line(LocalizationService.T("records.since", "开始于", "Since"),
                history.FirstSweptAtUtc.Value.ToLocalTime().ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)));
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
            Child = stack,
        };
    }

    private static void AddBest(StackPanel stack, GameRecords records, DifficultyPreset preset)
    {
        // 两张表一起看（v0.6 起界面恒为"经典 + 道具"，纯手工表基本是空的）
        BestTimeEntry? best = null;
        foreach (Dictionary<string, BestTimeEntry> table in new[] { records.Classic, records.ClassicWithItem })
        {
            if (table.TryGetValue(preset.Id, out BestTimeEntry? entry) && entry is not null
                && (best is null || entry.Seconds < best.Seconds))
            {
                best = entry;
            }
        }

        string value = best is null
            ? "—"
            : $"{best.Seconds:F1}s · {best.Width}×{best.Height}/{best.MineCount}";

        stack.Children.Add(Line(preset.DisplayName, value));
    }

    private static Control Section(string text) => new TextBlock
    {
        Text = text,
        Foreground = new SolidColorBrush(UiPalette.Current.Accent),
        FontWeight = FontWeight.SemiBold,
        Margin = new Thickness(0, 8, 0, 2),
    };

    private static Control Line(string label, string value)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        grid.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = new SolidColorBrush(UiPalette.Current.Text),
        });
        var right = new TextBlock
        {
            Text = value,
            Foreground = new SolidColorBrush(UiPalette.Current.Text),
        };
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);
        return grid;
    }
}
