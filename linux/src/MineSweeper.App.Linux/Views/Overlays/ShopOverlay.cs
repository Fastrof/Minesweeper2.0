using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using MineSweeper.App.Linux.Services;
using MineSweeper.Core;
using MineSweeper.Core.Items;

namespace MineSweeper.App.Linux.Views.Overlays;

/// <summary>
/// 金币商店（对应 Windows 端棋盘右上角的 🛒 商店）。
///
/// 规则全在 Core：<c>GameSession.TryBuyItem</c> 做五道校验（道具系统可用 / 未结束 /
/// 价格有效 / 金币够 / 库存未满），任一不过**一分不扣**。这里只负责展示与转发。
/// </summary>
public sealed class ShopOverlay : Border, IOverlay
{
    private readonly AppHost _host;
    private readonly GameCoordinator _game;
    private readonly TextBlock _coinsText = new();
    private readonly TextBlock _statusText = new() { FontSize = 12, Foreground = new SolidColorBrush(UiPalette.Current.SubText) };
    private readonly List<(string Id, Button Button, TextBlock Owned)> _rows = new();

    public event EventHandler? CloseRequested;

    public ShopOverlay(AppHost host)
    {
        _host = host;
        _game = host.Game;

        Background = new SolidColorBrush(UiPalette.Current.Mask);
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Stretch;

        var stack = new StackPanel { Spacing = 10, Margin = new Thickness(20), MaxWidth = 460 };

        stack.Children.Add(new TextBlock
        {
            Text = LocalizationService.Get("shop.title"),
            FontSize = 20,
            FontWeight = FontWeight.Bold,
            Foreground = new SolidColorBrush(UiPalette.Current.Text),
        });

        _coinsText.Foreground = new SolidColorBrush(UiPalette.Current.AccentSoft);
        stack.Children.Add(_coinsText);

        stack.Children.Add(new TextBlock
        {
            Text = LocalizationService.Get("shop.subtitle"),
            FontSize = 12,
            Foreground = new SolidColorBrush(UiPalette.Current.SubText),
            TextWrapping = TextWrapping.Wrap,
        });

        AddRow(stack, ItemIds.Magnifier, "item.magnifier");
        AddRow(stack, ItemIds.Sonar, "item.sonar");
        AddRow(stack, ItemIds.Freeze, "item.freeze");

        stack.Children.Add(_statusText);

        var close = new Button
        {
            Content = LocalizationService.Get("shop.close"),
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 8, 0, 0),
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

        Refresh();
    }

    private void AddRow(StackPanel parent, string itemId, string nameKey)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto") };

        grid.Children.Add(new TextBlock
        {
            Text = LocalizationService.Get(nameKey),
            Foreground = new SolidColorBrush(UiPalette.Current.Text),
            VerticalAlignment = VerticalAlignment.Center,
        });

        var price = new TextBlock
        {
            Text = $"🪙 {_game.ShopPriceOf(itemId)}",
            Foreground = new SolidColorBrush(UiPalette.Current.AccentSoft),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0),
        };
        Grid.SetColumn(price, 1);
        grid.Children.Add(price);

        var owned = new TextBlock
        {
            Foreground = new SolidColorBrush(UiPalette.Current.SubText),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0),
        };
        Grid.SetColumn(owned, 2);
        grid.Children.Add(owned);

        var buy = new Button { Content = LocalizationService.Get("shop.buy"), Width = 80 };
        Grid.SetColumn(buy, 3);
        grid.Children.Add(buy);

        buy.Click += (_, _) =>
        {
            ShopPurchaseResult result = _game.Buy(itemId);
            _statusText.Text = DescribeResult(result);
            Refresh();
        };

        _rows.Add((itemId, buy, owned));
        parent.Children.Add(grid);
    }

    private void Refresh()
    {
        GameSession? session = _game.Session;
        if (session is null)
        {
            return;
        }

        _coinsText.Text = $"🪙 {_game.AccountCoins}";

        foreach ((string id, Button button, TextBlock owned) in _rows)
        {
            int count = session.Inventory?.CountOf(id) ?? 0;
            int max = session.Inventory?.MaxHold ?? 0;
            owned.Text = $"{count}/{max}";
            button.IsEnabled = _game.AccountCoins >= _game.ShopPriceOf(id)
                               && !(session.Inventory?.IsFull(id) ?? true);
        }
    }

    private static string DescribeResult(ShopPurchaseResult result) => result.Outcome switch
    {
        ShopPurchaseOutcome.Bought => LocalizationService.Get("shop.bought"),
        ShopPurchaseOutcome.NotEnoughCoins => LocalizationService.Get("shop.notEnough"),
        ShopPurchaseOutcome.InventoryFull => LocalizationService.Get("shop.full"),
        ShopPurchaseOutcome.ItemsNotAvailable => LocalizationService.Get("shop.unavailable"),
        _ => string.Empty,
    };
}
