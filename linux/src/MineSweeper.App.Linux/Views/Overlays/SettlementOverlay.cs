using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using MineSweeper.App.Linux.Services;
using MineSweeper.Core.Levels;

namespace MineSweeper.App.Linux.Views.Overlays;

/// <summary>
/// 关卡胜利结算卡片（对应 Windows 端 <c>SettlementOverlay</c>）。
///
/// Windows 端的设计取舍在这里照搬：
///   - 只做**窗口内覆盖层**（不是独立窗口），因为要看"这一局"的成绩；
///   - 失败**不弹**这张卡（Toast 已说明，而失败会反复发生）。
/// </summary>
public sealed class SettlementOverlay : Border, IOverlay
{
    public event EventHandler? CloseRequested;

    public SettlementOverlay(AppHost host, LevelOutcome outcome, Action onClose)
    {
        Background = new SolidColorBrush(UiPalette.Current.Mask);
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Stretch;

        var stack = new StackPanel { Spacing = 8, Margin = new Thickness(24), MaxWidth = 380 };

        stack.Children.Add(new TextBlock
        {
            Text = LocalizationService.T("settle.title", "通关！", "Level cleared!"),
            FontSize = 20,
            FontWeight = FontWeight.Bold,
            Foreground = new SolidColorBrush(UiPalette.Current.Text),
            HorizontalAlignment = HorizontalAlignment.Center,
        });

        stack.Children.Add(new TextBlock
        {
            Text = outcome.StarsText,
            FontSize = 34,
            Foreground = new SolidColorBrush(UiPalette.Current.AccentSoft),
            HorizontalAlignment = HorizontalAlignment.Center,
        });

        stack.Children.Add(new TextBlock
        {
            Text = string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                LocalizationService.T("settle.time", "本局 {0:F1}s · 最好 {1:F1}s", "Time {0:F1}s · Best {1:F1}s"),
                outcome.Seconds,
                outcome.BestSeconds),
            Foreground = new SolidColorBrush(UiPalette.Current.SubText),
            HorizontalAlignment = HorizontalAlignment.Center,
        });

        if (outcome.IsNewBest)
        {
            stack.Children.Add(new TextBlock
            {
                Text = "🏆 " + LocalizationService.T("settle.newBest", "刷新纪录", "New best"),
                Foreground = new SolidColorBrush(UiPalette.Current.AccentSoft),
                HorizontalAlignment = HorizontalAlignment.Center,
            });
        }

        if (outcome.UnlockedNext is not null)
        {
            stack.Children.Add(new TextBlock
            {
                Text = string.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    LocalizationService.T("settle.unlocked", "解锁 {0}", "Unlocked {0}"),
                    outcome.UnlockedNext.DisplayName),
                Foreground = new SolidColorBrush(UiPalette.Current.Success),
                HorizontalAlignment = HorizontalAlignment.Center,
            });
        }

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 10, 0, 0),
        };

        var retry = new Button { Content = LocalizationService.T("settle.retry", "重开这一关", "Retry") };
        retry.Click += (_, _) =>
        {
            onClose();
            host.Game.StartLevel(outcome.Level);
        };
        buttons.Children.Add(retry);

        var next = new Button
        {
            Content = LocalizationService.T("settle.next", "下一关", "Next level"),
            IsEnabled = outcome.UnlockedNext is not null,
        };
        next.Click += (_, _) =>
        {
            if (outcome.UnlockedNext is null)
            {
                return;
            }

            onClose();
            host.Game.StartLevel(outcome.UnlockedNext);
        };
        buttons.Children.Add(next);

        var close = new Button { Content = LocalizationService.Get("common.backToGame") };
        close.Click += (_, _) => onClose();
        buttons.Children.Add(close);

        stack.Children.Add(buttons);

        Child = new Border
        {
            Background = new SolidColorBrush(UiPalette.Current.CardBg),
            BorderBrush = new SolidColorBrush(UiPalette.Current.CardBorder),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Child = stack,
        };
    }
}
