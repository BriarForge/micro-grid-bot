namespace MicroGrid.Domain.Grid;

/// <summary>
/// Resolves safe grid spacing from exchange-supplied fee rates. Fee rates are runtime data,
/// never strategy configuration. Rates are positive magnitudes (0.0008 = 0.08%).
/// </summary>
public static class FeeAwareSpacing
{
    public sealed record Result(
        decimal MakerRate,
        decimal TakerRate,
        decimal MakerRoundTripRate,
        decimal MinimumTargetSpacing,
        decimal ConfiguredSpacing,
        decimal EffectiveSpacing,
        bool WasAdjusted);

    public static Result Resolve(
        Config.GridSettings settings,
        decimal makerRate,
        decimal takerRate,
        decimal grossProfitMultiple = 2m)
    {
        settings.Validate();
        if (makerRate < 0) throw new ArgumentOutOfRangeException(nameof(makerRate));
        if (takerRate < 0) throw new ArgumentOutOfRangeException(nameof(takerRate));
        if (grossProfitMultiple < 1) throw new ArgumentOutOfRangeException(nameof(grossProfitMultiple));

        var roundTrip = makerRate * 2m;
        var target = roundTrip * grossProfitMultiple;
        var effective = Math.Max(settings.Spacing, target);

        return new Result(
            makerRate,
            takerRate,
            roundTrip,
            target,
            settings.Spacing,
            effective,
            effective != settings.Spacing);
    }
}
