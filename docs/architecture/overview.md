# Architecture overview (v1)

Status: locked with scope v1. Code has not been scaffolded yet.

## Portability & multi-exchange constraints (locked)

- **Runtime:** .NET 8 (LTS). Publish targets: `linux-x64`, `linux-arm64`. No Windows-only APIs.
- **Host:** single console worker running as a container. Config from env / mounted `appsettings.json`. No GUI dependency.
- **Secrets:** env vars or mounted file. Never committed. Already covered by `.gitignore`.
- **State:** SQLite file on a volume (portable path) or in-memory for paper mode. Path configurable.
- **Time:** UTC everywhere. `TimeProvider` for testability.
- **Observability:** structured logs to stdout (container-friendly). No local-only log viewers required.

## Multi-exchange boundary

Exchange-specific code is hidden behind a single port in the application layer:

```
MicroGrid.Domain         pure: CapitalAllocator, GeometricGrid, InventoryLedger, risk
MicroGrid.Application    ports: IExchangeGateway, IClock, IStateStore; orchestration
MicroGrid.Exchange.Okx   OKX.Net adapter (v1 only)
MicroGrid.Infrastructure SQLite, config binding, logging
MicroGrid.Bot            generic host worker, DI composition root
```

Rule: no OKX.Net types cross `Application`. v2 Binance/Bybit adapters reuse `MicroGrid.Domain`
and `MicroGrid.Application` unchanged.

## Initial grid knobs (scope defaults; flip later in `appsettings.json`)

- Symbol: BTC-USDT spot, unified account, spot mode
- Spacing: 0.12% geometric, 25 levels, ±~1.5% range
- Order style: equal USDT notional per level
- Active / reserve: 65% / 35% of equity
- Max BTC exposure: 65% (block buys) / 60% (resume) — hysteresis
- Drop trigger: mid < lowest buy − 2.5%
- Drop policy: expand lower first (≤50% reserve, 8–10 levels); full recenter only if still outside or manual
- Rescale trigger: deposit event OR equity Δ ≥ 1× order notional
- Mark: mid from public book
- Demo first; post-only only

## Adapter quantization note

Domain emits ideal notionals and geometric levels. The exchange adapter must quantize to
instrument filters (lot size, tick size, min notional) at the edge so multi-exchange support
is a new adapter, not a domain rewrite.

## Fee tier

Target spacing ≈ 2× round-trip maker fee. VIP 5 (0.025% maker → 0.10–0.12% target) is the
default; stored in config, not hardcoded.