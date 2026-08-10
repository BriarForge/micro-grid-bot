namespace MicroGrid.Domain.Capital;

/// <summary>
/// Pure allocation logic. No I/O, no clock, no exchange types.
/// Money is decimal throughout to match the underlying asset.
/// </summary>
public static class CapitalAllocator
{
    public sealed record Allocation(
        decimal TotalEquity,
        decimal ActiveCapital,
        decimal ReserveCapital,
        decimal OrderNotional,
        decimal BtcExposurePct,
        bool AllowBuy
    );

    public static Allocation Compute(
        decimal usdt,
        decimal btc,
        decimal mid,
        Config.GridSettings cfg,
        bool previouslyAllowed = true)
    {
        if (cfg.Levels <= 0) throw new ArgumentOutOfRangeException(nameof(cfg.Levels));
        if (mid <= 0) throw new ArgumentOutOfRangeException(nameof(mid));

        decimal equity = usdt + btc * mid;
        decimal active = Math.Round(equity * cfg.ActivePct, 8, MidpointRounding.ToEven);
        decimal reserve = Math.Round(equity - active, 8, MidpointRounding.ToEven);
        decimal notional = Math.Round(active / cfg.Levels, 8, MidpointRounding.ToEven);

        decimal btcValue = btc * mid;
        decimal btcPct = equity <= 0 ? 0m : Math.Round(btcValue / equity, 8, MidpointRounding.ToEven);

        // Hysteresis: once blocked, stay blocked until exposure ≤ ResumePct.
        bool allowBuy = previouslyAllowed
            ? btcPct < cfg.MaxBtcExposurePct
            : btcPct <= cfg.ResumeBtcExposurePct;

        return new Allocation(equity, active, reserve, notional, btcPct, allowBuy);
    }
}