using MicroGrid.Domain.Config;
using MicroGrid.Domain.Grid;

namespace MicroGrid.Domain.Tests;

public class FeeAwareSpacingTests
{
    [Fact]
    public void Resolve_RaisesSpacingFromLiveMakerFee()
    {
        var result = FeeAwareSpacing.Resolve(
            new GridSettings(Spacing: 0.0012m),
            makerRate: 0.0008m,
            takerRate: 0.001m);

        Assert.Equal(0.0016m, result.MakerRoundTripRate);
        Assert.Equal(0.0032m, result.MinimumTargetSpacing);
        Assert.Equal(0.0032m, result.EffectiveSpacing);
        Assert.True(result.WasAdjusted);
    }

    [Fact]
    public void Resolve_PreservesConfiguredSpacingWhenAlreadySafer()
    {
        var result = FeeAwareSpacing.Resolve(
            new GridSettings(Spacing: 0.004m),
            makerRate: 0.0008m,
            takerRate: 0.001m);

        Assert.Equal(0.004m, result.EffectiveSpacing);
        Assert.False(result.WasAdjusted);
    }

    [Fact]
    public void Resolve_RejectsInvalidFeeRates()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            FeeAwareSpacing.Resolve(new GridSettings(), -0.0008m, 0.001m));
    }
}
