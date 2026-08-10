namespace MicroGrid.Domain.Config;

/// <summary>
/// Scope-locked defaults for v1. Override via appsettings; values here are the
/// "scope says this" baseline (docs/scope/okx-spot-btc-micro-grid.md §3.1-3.4).
/// Levels = BuyLevelsBelowMid + SellLevelsAboveMid; no order is placed AT mid.
/// </summary>
public sealed record GridSettings(
    decimal ActivePct = 0.65m,
    decimal ReservePct = 0.35m,
    decimal MaxBtcExposurePct = 0.65m,
    decimal ResumeBtcExposurePct = 0.60m,
    int Levels = 25,
    decimal Spacing = 0.0012m,
    int BuyLevelsBelowMid = 12,
    int SellLevelsAboveMid = 13
)
{
    public void Validate()
    {
        if (ActivePct <= 0 || ActivePct > 1) throw new ArgumentOutOfRangeException(nameof(ActivePct));
        if (Levels <= 0) throw new ArgumentOutOfRangeException(nameof(Levels));
        if (Spacing <= 0) throw new ArgumentOutOfRangeException(nameof(Spacing));
        if (BuyLevelsBelowMid + SellLevelsAboveMid != Levels)
            throw new ArgumentException(
                $"BuyLevelsBelowMid ({BuyLevelsBelowMid}) + SellLevelsAboveMid ({SellLevelsAboveMid}) " +
                $"must equal Levels ({Levels})");
        if (MaxBtcExposurePct <= 0 || MaxBtcExposurePct > 1)
            throw new ArgumentOutOfRangeException(nameof(MaxBtcExposurePct));
        if (ResumeBtcExposurePct <= 0 || ResumeBtcExposurePct >= MaxBtcExposurePct)
            throw new ArgumentOutOfRangeException(nameof(ResumeBtcExposurePct));
    }
}
