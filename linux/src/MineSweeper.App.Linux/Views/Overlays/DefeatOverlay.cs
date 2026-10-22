using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using MineSweeper.App.Linux.Services;
using MineSweeper.Core;

namespace MineSweeper.App.Linux.Views.Overlays;

/// <summary>
/// 踩雷失败卡片（对应 Windows 端 <c>DefeatOverlay</c>）：
/// 标题「💥 踩到雷了」+ 盘面摘要 + 三个动作：
///   - **重打这一盘**（同一张盘，练习局，不计最快记录）
///   - **换一张新盘**（难度不变，成绩照常计）
///   - **退出游戏**
///
/// Windows 端的取舍照搬：失败会反复发生，所以卡片**只在踩雷时弹一次**，
/// 并且给玩家"立刻重来"的路径，不强迫先点确定。
/// </summary>
public sealed class DefeatOverlay : Border, IOverlay
{
    public event EventHandler? CloseRequested;

    public DefeatOverlay(AppHost host, Action onRetrySameBoard, Action onNewBoard, Action onExit)
    {
        GameSession? session = host.Game.Session;

        Background = new SolidColorBrush(UiPalette.Current.Mask);
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Stretch;

        var stack = new StackPanel { Spacing = 10, MaxWidth = 440, Margin = new Thickness(24) };

        stack.Children.Add(new TextBlock
        {
            Text = LocalizationService.Get("defeat.title"),
            FontSize = 20,
            FontWeight = FontWeight.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = new SolidColorBrush(UiPalette.Current.Text),
        });

        stack.Children.Add(new TextBlock
        {
            Text = session is null
                ? string.Empty
                : $"{session.Board.Width}×{session.Board.Height} · " +
                  LocalizationService.Get("defeat.boardLine", session.Board.MineCount, session.ElapsedSecondsInt) +
                  LocalizationService.Get("defeat.replayHint") +
                  LocalizationService.Get("defeat.newBoardHint"),
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = new SolidColorBrush(UiPalette.Current.SubText),
        });

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 6, 0, 0),
        };

        var retry = new Button { Content = LocalizationService.Get("defeat.sameBoard") };
        retry.Click += (_, _) => onRetrySameBoard();
        buttons.Children.Add(retry);

        var newBoard = new Button { Content = LocalizationService.Get("defeat.newBoard") };
        newBoard.Click += (_, _) => onNewBoard();
        buttons.Children.Add(newBoard);

        var exit = new Button { Content = LocalizationService.Get("defeat.exit") };
        exit.Click += (_, _) => onExit();
        buttons.Children.Add(exit);

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

/// <summary>
/// 声呐扫描结果卡片（对应 Windows 端 <c>SonarOverlay</c>）：
/// **需要玩家手动确认**才关闭 —— 因为瞄准框在落子后就消失了，玩家很容易忘记刚扫的是哪一块。
/// </summary>
public sealed class SonarResultOverlay : Border, IOverlay
{
    public event EventHandler? CloseRequested;

    public SonarResultOverlay(GameSession session, SonarScanResult result)
    {
        Background = new SolidColorBrush(UiPalette.Current.Mask);
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Stretch;

        var stack = new StackPanel { Spacing = 10, MaxWidth = 420, Margin = new Thickness(24) };

        stack.Children.Add(new TextBlock
        {
            Text = result.MineCount == 0
                ? LocalizationService.Get("sonar.dialogEmpty")
                : LocalizationService.Get("sonar.dialogTitle", result.MineCount),
            FontSize = 18,
            FontWeight = FontWeight.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = new SolidColorBrush(UiPalette.Current.Text),
        });

        int centerRow = (result.CenterIndex / session.Board.Width) + 1;
        int centerCol = (result.CenterIndex % session.Board.Width) + 1;
        int radius = result.Radius;

        stack.Children.Add(new TextBlock
        {
            Text = LocalizationService.Get(
                "sonar.dialogArea",
                Math.Max(1, centerRow - radius),
                Math.Min(session.Board.Height, centerRow + radius),
                Math.Max(1, centerCol - radius),
                Math.Min(session.Board.Width, centerCol + radius),
                (radius * 2) + 1),
            FontSize = 13,
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = new SolidColorBrush(UiPalette.Current.SubText),
        });

        stack.Children.Add(new TextBlock
        {
            Text = LocalizationService.Get("sonar.dialogCenter", centerRow, centerCol),
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = new SolidColorBrush(UiPalette.Current.SubText),
        });

        stack.Children.Add(new Border
        {
            Height = 1,
            Background = new SolidColorBrush(UiPalette.Current.CardBorder),
            Margin = new Thickness(0, 4),
        });

        stack.Children.Add(new TextBlock
        {
            Text = LocalizationService.Get("sonar.dialogOnlyCount"),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(UiPalette.Current.SubText),
        });

        stack.Children.Add(new TextBlock
        {
            Text = LocalizationService.Get("sonar.remaining", result.RemainingCount),
            FontSize = 12,
            Foreground = new SolidColorBrush(UiPalette.Current.SubText),
        });

        var close = new Button
        {
            Content = LocalizationService.Get("common.gotIt"),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 8, 0, 0),
        };
        close.Click += (_, _) => CloseRequested?.Invoke(this, EventArgs.Empty);
        stack.Children.Add(close);

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
