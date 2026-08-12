using MicroGrid.Domain.Config;

namespace MicroGrid.Domain.Tests;

public class GridSettingsTests
{
    [Fact]
    public void Validate_RejectsAllocationThatDoesNotTotalOne()
    {
        Assert.Throws<ArgumentException>(() =>
            new GridSettings(ActivePct: 0.65m, ReservePct: 0.30m).Validate());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Validate_RejectsSpacingOutsideOpenUnitInterval(decimal spacing)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new GridSettings(Spacing: spacing).Validate());
    }

    [Fact]
    public void Validate_RejectsNegativeSideLevelCountEvenWhenTotalMatches()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new GridSettings(Levels: 25, BuyLevelsBelowMid: -1, SellLevelsAboveMid: 26).Validate());
    }
}
