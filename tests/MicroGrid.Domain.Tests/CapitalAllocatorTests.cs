using MicroGrid.Domain.Capital;
using MicroGrid.Domain.Config;

namespace MicroGrid.Domain.Tests;

public class CapitalAllocatorTests
{
    private static GridSettings Cfg() => new(); // scope defaults

    [Fact]
    public void ColdStart_1000U_ZeroBtc_Allocates65Percent_Notional26_AllowBuy_ZeroExposure()
    {
        var a = CapitalAllocator.Compute(usdt: 1000m, btc: 0m, mid: 100_000m, Cfg());

        Assert.Equal(1000m, a.TotalEquity);
        Assert.Equal(650m, a.ActiveCapital);
        Assert.Equal(350m, a.ReserveCapital);
        Assert.Equal(26m, a.OrderNotional); // 650 / 25
        Assert.Equal(0m, a.BtcExposurePct);
        Assert.True(a.AllowBuy);
    }

    [Fact]
    public void Hysteresis_BlocksAtMaxExposure_StaysBlockedAboveResume_AllowsAtOrBelowResume()
    {
        var cfg = Cfg();
        // equity = usdt + btc*mid. To hit 65%, pick btc such that btc*mid / equity = 0.65.
        decimal usdt = 350m; decimal btc = 0.0065m; decimal mid = 100_000m;
        // equity = 350 + 650 = 1000; btc value = 650; exposure = 0.65 exactly
        var a = CapitalAllocator.Compute(usdt, btc, mid, cfg, previouslyAllowed: true);
        Assert.Equal(0.65m, a.BtcExposurePct);
        Assert.False(a.AllowBuy); // ≥ Max blocks

        // 62% with previouslyBlocked: stays blocked
        var stuck = CapitalAllocator.Compute(usdt: 380m, btc: 0.0062m, mid, cfg, previouslyAllowed: false);
        Assert.Equal(0.62m, stuck.BtcExposurePct);
        Assert.False(stuck.AllowBuy);

        // 60% with previouslyBlocked: resumes (≤ ResumeBtcExposurePct)
        var resume = CapitalAllocator.Compute(usdt: 400m, btc: 0.0060m, mid, cfg, previouslyAllowed: false);
        Assert.Equal(0.60m, resume.BtcExposurePct);
        Assert.True(resume.AllowBuy);
    }

    [Fact]
    public void Deposit_1500_ScalesActiveAndNotional_PreservesRatios()
    {
        var a = CapitalAllocator.Compute(usdt: 1500m, btc: 0m, mid: 100_000m, Cfg());

        Assert.Equal(1500m, a.TotalEquity);
        Assert.Equal(975m, a.ActiveCapital);   // 1500 * 0.65
        Assert.Equal(525m, a.ReserveCapital);  // 1500 * 0.35
        Assert.Equal(39m, a.OrderNotional);    // 975 / 25
        Assert.Equal(0m, a.BtcExposurePct);
        Assert.True(a.AllowBuy);
    }

    [Fact]
    public void ZeroMid_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => CapitalAllocator.Compute(1000m, 0m, 0m, Cfg()));
    }
}