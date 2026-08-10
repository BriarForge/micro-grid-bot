namespace MicroGrid.Domain.Grid;

/// <summary>
/// Pure geometric grid builder. No tick/lot quantization — the exchange adapter
/// rounds to instrument filters at the boundary. See docs/architecture/overview.md.
/// Levels = BuyLevelsBelowMid + SellLevelsAboveMid; mid is the reference price,
/// not an order level.
/// </summary>
public sealed class GeometricGrid
{
    private readonly Config.GridSettings _cfg;

    public GeometricGrid(Config.GridSettings cfg)
    {
        cfg.Validate();
        _cfg = cfg;
    }

    public IReadOnlyList<decimal> Build(decimal mid)
    {
        if (mid <= 0) throw new ArgumentOutOfRangeException(nameof(mid));

        var prices = new List<decimal>(_cfg.Levels);

        for (int i = _cfg.BuyLevelsBelowMid; i > 0; i--)
            prices.Add(RoundDown(mid * Pow(1m - _cfg.Spacing, i)));

        for (int i = 1; i <= _cfg.SellLevelsAboveMid; i++)
            prices.Add(RoundUp(mid * Pow(1m + _cfg.Spacing, i)));

        return prices;
    }

    /// <summary>Buy fill at <paramref name="buyPrice"/> → next higher sell level.</summary>
    public decimal SellForBuy(decimal buyPrice) => RoundUp(buyPrice * (1m + _cfg.Spacing));

    /// <summary>Sell fill at <paramref name="sellPrice"/> → next lower buy level.</summary>
    public decimal BuyForSell(decimal sellPrice) => RoundDown(sellPrice * (1m - _cfg.Spacing));

    private static decimal Pow(decimal factor, int n)
    {
        // Integer exponent, factor in (0, 2). Decimal multiply is exact enough for ≤ ~30 levels.
        decimal r = 1m;
        for (int i = 0; i < n; i++) r *= factor;
        return r;
    }

    private static decimal RoundDown(decimal v) => Math.Floor(v * 1_000_000m) / 1_000_000m;
    private static decimal RoundUp(decimal v) => Math.Ceiling(v * 1_000_000m) / 1_000_000m;
}