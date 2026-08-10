namespace MicroGrid.Bot.Services;

public sealed record AssetBalance(string Currency, decimal Available, decimal EquityUsd);

public sealed record RuntimeSnapshot(
    bool Connected,
    string Environment,
    string Symbol,
    decimal? LastPrice,
    decimal? BidPrice,
    decimal? AskPrice,
    decimal? TotalEquityUsd,
    decimal? MakerRate,
    decimal? TakerRate,
    decimal? EffectiveSpacing,
    string? FeeTier,
    IReadOnlyList<AssetBalance> Balances,
    DateTimeOffset? UpdatedAt,
    string? LastError)
{
    public static RuntimeSnapshot Starting(string environment) =>
        new(false, environment, "BTC-USDT", null, null, null, null, null, null, null, null, [], null, null);
}

public sealed class RuntimeState
{
    private readonly object _gate = new();
    private RuntimeSnapshot _snapshot = RuntimeSnapshot.Starting("starting");

    public RuntimeSnapshot Get()
    {
        lock (_gate) return _snapshot;
    }

    public void Set(RuntimeSnapshot snapshot)
    {
        lock (_gate) _snapshot = snapshot;
    }
}
