using MineSweeper.Core;
using MineSweeper.Core.Items;
using Xunit;

namespace MineSweeper.Core.Tests;

/// <summary>
/// v1.4 新增的两块基础件：<see cref="AreaShape"/>（区域形状）与 <see cref="CoinWallet"/>（金币钱包）。
///
/// 这两块被放大镜、声呐、商店同时依赖，坏了会影响一大片，所以单独锁一遍。
/// </summary>
public sealed class AreaShapeAndCoinTests
{
    // ------------------------------------------------------------ AreaShape

    /// <summary>2×2、锚点左上角：点哪格就从那格开始向右下扩。</summary>
    [Fact]
    public void 左上角锚点的二乘二正好覆盖四格()
    {
        Board board = new Board(9, 9, 10, seed: 1);
        AreaShape shape = AreaShape.TopLeft(2, 2);

        int center = board.Index(4, 4);
        IReadOnlyList<int> indexes = shape.IndexesAt(board, center);

        Assert.Equal(4, indexes.Count);
        Assert.Equal(
            new[] { board.Index(4, 4), board.Index(5, 4), board.Index(4, 5), board.Index(5, 5) },
            indexes);
    }

    /// <summary>居中锚点（声呐的 3×3）：等价于旧版的"半径 1"。</summary>
    [Fact]
    public void 居中锚点的三乘三等价于半径一()
    {
        Board board = new Board(9, 9, 10, seed: 1);
        AreaShape shape = AreaShape.Square(1);

        Assert.Equal(3, shape.Width);
        Assert.Equal(3, shape.Height);
        Assert.Equal(1, shape.AnchorX);
        Assert.Equal(1, shape.AnchorY);

        Assert.Equal(9, shape.IndexesAt(board, board.Index(4, 4)).Count);
    }

    /// <summary>贴边按棋盘边界裁切，不环绕、不补偿。</summary>
    [Fact]
    public void 贴边时按边界裁切()
    {
        Board board = new Board(9, 9, 10, seed: 1);
        AreaShape shape = AreaShape.TopLeft(2, 2);

        // 右下角 (8,8)：只有它自己 1 格
        Assert.Single(shape.IndexesAt(board, board.Index(8, 8)));

        // 最右一列 (8,0)：向右扩不出去，只剩竖向 2 格
        Assert.Equal(2, shape.IndexesAt(board, board.Index(8, 0)).Count);

        // 左上角 (0,0)：完整 4 格
        Assert.Equal(4, shape.IndexesAt(board, board.Index(0, 0)).Count);
    }

    /// <summary><see cref="AreaShape.FitsAt"/> 判断"完整放得下"。</summary>
    [Fact]
    public void 完整放得下的判断()
    {
        Board board = new Board(9, 9, 10, seed: 1);
        AreaShape shape = AreaShape.TopLeft(2, 2);

        Assert.True(shape.FitsAt(board, board.Index(7, 7)));
        Assert.False(shape.FitsAt(board, board.Index(8, 8)));
        Assert.False(shape.FitsAt(board, board.Index(8, 0)));
    }

    // ------------------------------------------------------------ CoinWallet

    /// <summary>每扫出一颗雷 1 金币，累计与总额都涨。</summary>
    [Fact]
    public void 扫雷累加金币()
    {
        var wallet = new CoinWallet();

        Assert.Equal(0, wallet.Coins);

        for (int i = 0; i < 5; i++)
        {
            wallet.EarnForMine();
        }

        Assert.Equal(5, wallet.Coins);
        Assert.Equal(5, wallet.EarnedTotal);
        Assert.Equal(0, wallet.SpentTotal);
    }

    /// <summary>三种道具的售价按需求是 30 / 20 / 20。</summary>
    [Fact]
    public void 三种道具售价符合需求()
    {
        Assert.Equal(30, CoinWallet.PriceOf(ItemIds.Magnifier));
        Assert.Equal(20, CoinWallet.PriceOf(ItemIds.Sonar));
        Assert.Equal(20, CoinWallet.PriceOf(ItemIds.Freeze));
        Assert.Equal(0, CoinWallet.PriceOf("不存在"));
    }

    /// <summary>买得起才扣钱，买不起一分不扣。</summary>
    [Fact]
    public void 金币不足时不扣钱()
    {
        var wallet = new CoinWallet();
        for (int i = 0; i < 19; i++)
        {
            wallet.EarnForMine();
        }

        // 19 金币，声呐要 20 → 买不起
        Assert.False(wallet.CanAfford(CoinWallet.PriceOf(ItemIds.Sonar)));
        Assert.False(wallet.TrySpend(CoinWallet.PriceOf(ItemIds.Sonar)));
        Assert.Equal(19, wallet.Coins);
        Assert.Equal(0, wallet.SpentTotal);

        // 再来 1 金币刚好够
        wallet.EarnForMine();
        Assert.True(wallet.CanAfford(CoinWallet.PriceOf(ItemIds.Sonar)));
        Assert.True(wallet.TrySpend(CoinWallet.PriceOf(ItemIds.Sonar)));
        Assert.Equal(0, wallet.Coins);
        Assert.Equal(20, wallet.SpentTotal);
    }

    /// <summary>清零会把金币、累计赚、累计花全部归零。</summary>
    [Fact]
    public void 清零()
    {
        var wallet = new CoinWallet();
        for (int i = 0; i < 30; i++)
        {
            wallet.EarnForMine();
        }

        wallet.TrySpend(CoinWallet.PriceOf(ItemIds.Freeze));
        wallet.Reset();

        Assert.Equal(0, wallet.Coins);
        Assert.Equal(0, wallet.EarnedTotal);
        Assert.Equal(0, wallet.SpentTotal);
    }
}
