namespace MicroGrid.Application.Ports;

/// <summary>
/// Exchange-agnostic surface the grid engine speaks to. v1 = OKX.Net adapter.
/// Paper implementation will live in MicroGrid.Application.Paper for CI / demo runs.
/// Multi-exchange = new adapter implementing this; domain never changes.
/// </summary>
public interface IExchangeGateway
{
    /// <summary>Fetch canonical instrument filters (tick size, lot size, min notional).</summary>
    Task<InstrumentSpec> GetInstrumentAsync(string symbol, CancellationToken ct);

    /// <summary>Current free balance per asset on the trading account.</summary>
    Task<Balances> GetBalancesAsync(CancellationToken ct);

    /// <summary>
    /// Fetch the current account-specific fee rates for an instrument. Implementations must
    /// refresh this from the exchange; callers must not substitute configured fee assumptions.
    /// </summary>
    Task<TradingFeeRates> GetFeeRatesAsync(string symbol, CancellationToken ct);

    /// <summary>Subscribe to order updates (filled, partial, canceled) for the trading account.</summary>
    IAsyncEnumerable<OrderUpdate> StreamOrdersAsync(CancellationToken ct);

    /// <summary>Place a post-only limit order. Idempotency via <paramref name="clientOrderId"/>.</summary>
    Task<PlaceOrderResult> PlacePostOnlyAsync(
        string symbol, Side side, decimal price, decimal qty, string clientOrderId, CancellationToken ct);

    /// <summary>Cancel an open order by exchange ID or our clientOrderId.</summary>
    Task<bool> CancelAsync(string exchangeOrderId, CancellationToken ct);

    /// <summary>List open orders for the symbol (used at startup to reconcile state).</summary>
    Task<IReadOnlyList<OpenOrder>> ListOpenOrdersAsync(string symbol, CancellationToken ct);
}

public enum Side { Buy, Sell }

public sealed record InstrumentSpec(
    string Symbol,
    decimal TickSize,
    decimal LotSize,
    decimal MinQty,
    decimal MinNotional);

public sealed record Balances(decimal UsdtFree, decimal BtcFree, DateTimeOffset AsOf);

public sealed record TradingFeeRates(
    string Symbol,
    decimal MakerRate,
    decimal TakerRate,
    string Tier,
    DateTimeOffset AsOf);

public sealed record OpenOrder(
    string ExchangeOrderId,
    string ClientOrderId,
    Side Side,
    decimal Price,
    decimal Qty,
    decimal QtyFilled);

public sealed record PlaceOrderResult(
    bool Accepted,
    string? ExchangeOrderId,
    string? Reason);

public sealed record OrderUpdate(
    string ExchangeOrderId,
    string ClientOrderId,
    Side Side,
    FillKind Kind,
    decimal FilledQty,
    decimal FillPrice,
    decimal Fee,
    DateTimeOffset At);

public enum FillKind { New, PartialFill, Filled, Canceled, Rejected }
