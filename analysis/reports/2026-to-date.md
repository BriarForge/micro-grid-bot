# BTC-USDT 2026 pre-trading validation

## Overall assessment: not ready for real-money trading

The repository currently implements a read-only OKX monitor and four domain math helpers. It
does not implement the execution/reconciliation system required to operate a bot. In particular,
`TradingEnabled=true` is rejected, and there is no `IExchangeGateway` implementation, order
placement loop, private order stream consumer, partial-fill handling, open-order reconciliation,
durable inventory/order state, recenter algorithm, reserve deployment algorithm, or automated
kill switch.

The literal result of starting the current program with 100 USDT on 2026-01-01 is therefore
**100 USDT**, because it cannot place an order. The strategy proxy result below answers the
separate question: what might the documented grid have produced if the missing engine existed?

## Strategy proxy result

- Period: 2026-01-01 00:00 UTC through 2026-08-12 22:28 UTC (the latest available minute at run time)
- OKX BTC-USDT: 87,638.8 to 63,507.0 USDT, a 27.5355% decline
- Starting equity: 100 USDT
- Account-specific fees read through the existing monitor: Lv1 maker 0.08%, taker 0.10%
- Effective spacing under `FeeAwareSpacing`: 0.32% (the 0.12% configured floor is not economical at Lv1)
- Corrected, compounding 25-level full-grid proxy: **83.788558–83.790480 USDT** depending on minute OHLC ordering
- Return: **-16.2114% to -16.2095%**
- Ending assets: about 36.26–36.27 USDT + 0.00074827–0.00074836 BTC
- Realized PnL: 0.7016–0.7076 USDT; modeled fees: about 1.3232 USDT
- Maximum modeled BTC exposure: 63.6103%–63.6180%
- Cash benchmark: 100 USDT; fee-adjusted BTC buy-and-hold benchmark: 72.392087 USDT

An all-USDT cold-start proxy (only the 12 buy levels can be placed) ended at about 92.01285 USDT,
but that is not the requested 25-level grid. A full grid requires bootstrapping roughly 0.00037712
BTC (about 33.05 USDT at the starting price) to fund its 13 initial sell orders.

## Data and quality checks

The public, unauthenticated OKX `history-candles` API supplied both extracts. The requested daily
CSV contains 224 UTC candles. The backtest uses 322,469 one-minute candles because daily OHLC is
too coarse for a 0.32% grid. Both files have zero duplicate timestamps, zero interval gaps, and
zero invalid OHLC rows. Their final candle is partial/unconfirmed and is explicitly retained so
the answer reaches the run time rather than stopping at the prior UTC day.

- `data/okx_btc_usdt_1d_2026_to_date.csv`: requested daily extract
- `data/okx_btc_usdt_1m_2026_to_date.csv`: local 32 MB backtest input (gitignored)
- `../grid_backtest.py`: downloader, validation checks, and deterministic simulator
- `../results/2026-to-date.json`: complete machine-readable results and assumptions

Official references:

- OKX API: https://www.okx.com/docs-v5/en/#order-book-trading-market-data-get-candlesticks-history
- OKX historical download page: https://www.okx.com/en-gb/historical-data
- OKX candlestick download notes: https://www.okx.com/en-gb/help/candlestick-faqs-and-settings

## Model limitations

This is an optimistic strategy simulation, not execution validation. A limit price touch is assumed
to produce a complete maker fill. Queue position, spread, latency, slippage, partial fills,
post-only rejection, disconnects, stale market data, and taxes are not modeled. Both possible
one-minute OHLC paths are run; their close agreement reduces candle-order uncertainty here but
does not remove fill uncertainty. The current fee tier is applied to the full historical window
because a historical account fee-tier series is unavailable.

The documented drop/recenter/reserve policy is not simulated because it is prose rather than an
executable algorithm. This matters: BTC fell far outside the initial grid during the period.

Compounding resizes the next buy after every completed sell to `current equity × 65% ÷ 25`, then
uses the acquired BTC quantity for its paired sell. The earlier fixed-size result was about
83.97443 USDT; compounding accumulated slightly more BTC and reduced final marked-to-market equity
by about 0.184–0.186 USDT during this falling window.

## Remediation performed

- Corrected the lower geometric levels to divide by `1 + spacing`; the old multiplication by
  `1 - spacing` caused every completed cycle to drift downward.
- Added domain validation for reserve allocation, allocation totals, spacing bounds, negative side
  counts, and negative account balances.
- Prevented the inventory ledger from silently accepting buys that exceed held USDT.
- Expanded the domain suite from 16 to 23 passing tests.
- Verified a clean Release build and zero known NuGet/npm dependency vulnerabilities.

## Required before real money

1. Implement the exchange adapter and a paper engine with deterministic order reservation,
   quantization, post-only retry, partial fills, idempotency, and startup reconciliation.
2. Specify and test initial BTC bootstrap, upward and downward recentering, reserve deployment,
   exposure enforcement against open orders, and kill-switch behavior.
3. Persist orders/fills/inventory atomically and reconcile them against OKX after every restart or
   stream gap.
4. Run demo trading continuously, inject disconnect/reject/duplicate-event failures, and reconcile
   every fill and balance before considering a tiny live canary.
