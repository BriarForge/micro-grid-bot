using MicroGrid.Domain.Inventory;

namespace MicroGrid.Domain.Tests;

public class InventoryLedgerTests
{
    [Fact]
    public void BuyThenSellAtOneSpacing_MatchesAvgCostPnL_WithFees()
    {
        var l = new InventoryLedger();
        decimal fee = 0.00025m;
        l.Seed(usdt: 2_000m, btc: 0m);

        l.ApplyBuy(price: 100_000m, qty: 0.01m, feeRate: fee);
        // usdt -= 1000 + 0.25 = 1000.25 → 999.75 ; btc = 0.01 ; basis ≈ 100025/0.01 = 10,002,500 (per BTC)
        // Then sell at +0.12%: 100120
        l.ApplySell(price: 100_120m, qty: 0.01m, feeRate: fee);

        // Realized PnL = (notional - fee) - (basis * qty)
        decimal notional = 100_120m * 0.01m;
        decimal feeAmt = notional * fee;
        decimal netProceeds = notional - feeAmt;
        decimal basis = 100_000m * 0.01m + 100_000m * 0.01m * fee; // avg cost of 0.01 BTC
        decimal expected = netProceeds - basis;

        Assert.Equal(expected, l.RealizedPnL);
        Assert.Equal(0m, l.Btc); // sold everything → basis resets
        Assert.Equal(0m, l.AverageCostBasis);
    }

    [Fact]
    public void Deposit_IncreasesUsdt_DoesNotAffectPnL()
    {
        var l = new InventoryLedger();
        l.Seed(usdt: 1000m, btc: 0m);
        decimal pnlBefore = l.RealizedPnL;

        l.Deposit(500m);

        Assert.Equal(1500m, l.Usdt);
        Assert.Equal(0m, l.Btc);
        Assert.Equal(pnlBefore, l.RealizedPnL);
    }

    [Fact]
    public void SellQtyExceedsBtc_Throws()
    {
        var l = new InventoryLedger();
        l.Seed(usdt: 0m, btc: 0.005m, averageCostBasis: 100_000m);
        Assert.Throws<InvalidOperationException>(() =>
            l.ApplySell(price: 100_000m, qty: 0.01m, feeRate: 0.00025m));
    }

    [Fact]
    public void BuyCostExceedsUsdt_ThrowsWithoutMutatingBalances()
    {
        var l = new InventoryLedger();
        l.Seed(usdt: 100m, btc: 0m);

        Assert.Throws<InvalidOperationException>(() => l.ApplyBuy(100_000m, 0.002m, 0.0008m));
        Assert.Equal(100m, l.Usdt);
        Assert.Equal(0m, l.Btc);
    }

    [Fact]
    public void Exposure_AfterBuys_MatchesLedgerMath()
    {
        var l = new InventoryLedger();
        l.Seed(usdt: 1000m, btc: 0m);
        // Buy 0.001 BTC @ 100_000, fee 0 → cost 100. After: usdt=900, btc=0.001.
        l.ApplyBuy(100_000m, 0.001m, 0m);

        decimal mid = 100_000m;
        decimal equity = l.Equity(mid);                          // 900 + 0.001*100_000 = 1000
        Assert.Equal(1000m, equity);
        Assert.Equal(0.1m, l.BtcExposurePct(mid));               // (0.001*100_000)/1000
    }

    [Fact]
    public void Seed_ExplicitBasis_Preserved()
    {
        var l = new InventoryLedger();
        l.Seed(usdt: 0m, btc: 1m, averageCostBasis: 90_000m);
        Assert.Equal(90_000m, l.AverageCostBasis);
        Assert.Equal(1m, l.Btc);
    }
}
