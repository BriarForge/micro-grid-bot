namespace MicroGrid.Domain.Inventory;

/// <summary>
/// In-memory ledger. Apply buy/sell fills → recompute btc, usdt.
/// Realized PnL is tracked via an average-cost basis on the BTC we still hold.
/// Persistence (SQLite) is an Application-layer concern.
/// </summary>
public sealed class InventoryLedger
{
    public decimal Btc { get; private set; }
    public decimal Usdt { get; private set; }
    public decimal RealizedPnL { get; private set; }
    public decimal AverageCostBasis { get; private set; }

    public void ApplyBuy(decimal price, decimal qty, decimal feeRate)
    {
        if (price <= 0) throw new ArgumentOutOfRangeException(nameof(price));
        if (qty <= 0) throw new ArgumentOutOfRangeException(nameof(qty));
        if (feeRate < 0) throw new ArgumentOutOfRangeException(nameof(feeRate));

        decimal notional = price * qty;
        decimal fee = notional * feeRate;
        decimal totalCost = notional + fee;

        if (totalCost > Usdt)
            throw new InvalidOperationException("Buy cost exceeds held USDT.");

        if (Btc + qty > 0)
            AverageCostBasis = (AverageCostBasis * Btc + totalCost) / (Btc + qty);

        Usdt -= totalCost;
        Btc += qty;
    }

    public void ApplySell(decimal price, decimal qty, decimal feeRate)
    {
        if (price <= 0) throw new ArgumentOutOfRangeException(nameof(price));
        if (qty <= 0) throw new ArgumentOutOfRangeException(nameof(qty));
        if (feeRate < 0) throw new ArgumentOutOfRangeException(nameof(feeRate));
        if (qty > Btc) throw new InvalidOperationException("Sell qty exceeds held BTC.");

        decimal notional = price * qty;
        decimal fee = notional * feeRate;
        decimal netProceeds = notional - fee;

        decimal costOfSold = AverageCostBasis * qty;
        RealizedPnL += netProceeds - costOfSold;

        Usdt += netProceeds;
        Btc -= qty;
        if (Btc == 0m) AverageCostBasis = 0m;
    }

    public void Deposit(decimal usdtAmount)
    {
        if (usdtAmount <= 0) throw new ArgumentOutOfRangeException(nameof(usdtAmount));
        Usdt += usdtAmount;
    }

    public decimal Equity(decimal mid) =>
        mid <= 0 ? throw new ArgumentOutOfRangeException(nameof(mid)) : Usdt + Btc * mid;

    public decimal BtcExposurePct(decimal mid)
    {
        decimal e = Equity(mid);
        return e <= 0 ? 0m : (Btc * mid) / e;
    }

    /// <summary>
    /// Seed balances and (optionally) an explicit average-cost basis.
    /// USDT balance is NOT cost basis — callers must supply basis if known.
    /// </summary>
    public void Seed(decimal usdt, decimal btc, decimal averageCostBasis = 0m, decimal realizedPnL = 0m)
    {
        if (usdt < 0) throw new ArgumentOutOfRangeException(nameof(usdt));
        if (btc < 0) throw new ArgumentOutOfRangeException(nameof(btc));
        if (averageCostBasis < 0) throw new ArgumentOutOfRangeException(nameof(averageCostBasis));

        Usdt = usdt;
        Btc = btc;
        AverageCostBasis = btc > 0 ? averageCostBasis : 0m;
        RealizedPnL = realizedPnL;
    }
}
