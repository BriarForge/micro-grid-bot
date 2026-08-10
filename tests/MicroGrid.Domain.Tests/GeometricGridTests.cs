using MicroGrid.Domain.Config;
using MicroGrid.Domain.Grid;

namespace MicroGrid.Domain.Tests;

public class GeometricGridTests
{
    private static GridSettings Cfg(decimal spacing = 0.0012m, int below = 12, int above = 13)
        => new(Spacing: spacing, BuyLevelsBelowMid: below, SellLevelsAboveMid: above);

    [Fact]
    public void Build_ReturnsCorrectCount_NoLevelEqualsMid_SortedAscending()
    {
        var grid = new GeometricGrid(Cfg());
        var prices = grid.Build(mid: 100_000m);

        Assert.Equal(25, prices.Count);
        Assert.DoesNotContain(prices, p => p == 100_000m);
        for (int i = 1; i < prices.Count; i++)
            Assert.True(prices[i] > prices[i - 1], $"not ascending at index {i}");
    }

    [Fact]
    public void Build_GeometricRatioIsSpacing()
    {
        var grid = new GeometricGrid(Cfg(spacing: 0.0012m));
        var prices = grid.Build(100_000m);

        // Adjacent ratios: levels[k+1] / levels[k] ≈ 1 + spacing for upper cluster
        // We can verify the spread: top / bottom ≈ (1+s)^above / (1-s)^below.
        decimal top = prices[^1];
        decimal bottom = prices[0];
        decimal expected = (decimal)Math.Pow(1.0012, 13) / (decimal)Math.Pow(0.9988, 12);
        decimal actual = top / bottom;

        Assert.InRange(actual / expected, 0.999_999m, 1.000_001m);
    }

    [Fact]
    public void SellForBuy_RoundsUp_BuyForSell_RoundsDown()
    {
        var grid = new GeometricGrid(Cfg(spacing: 0.0012m));
        decimal buy = 100_000m;

        decimal sell = grid.SellForBuy(buy);
        Assert.True(sell > buy);
        // Equivalent to ceil((1+s)*buy) → 1.000000 step
        Assert.Equal(Math.Ceiling(buy * 1.0012m * 1_000_000m) / 1_000_000m, sell);

        decimal nextBuy = grid.BuyForSell(sell);
        // Round-trip downward by spacing; result is the floor of (1-s)*sell
        Assert.Equal(Math.Floor(sell * 0.9988m * 1_000_000m) / 1_000_000m, nextBuy);
    }

    [Fact]
    public void InvalidConfig_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            new GeometricGrid(new GridSettings(BuyLevelsBelowMid: 10, SellLevelsAboveMid: 10)));
    }
}