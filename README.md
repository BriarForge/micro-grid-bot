# Micro Grid Bot

Automated spot micro-grid trading bot for **OKX BTC-USDT** (v1). Designed to run in a container on
any host with Docker / Podman / OrbStack. The trading engine is exchange-agnostic behind
`IExchangeGateway`; the v1 adapter wraps [OKX.Net](https://github.com/JKorf/CryptoExchange.Net),
which sits on [CryptoExchange.Net](https://github.com/JKorf/CryptoExchange.Net).

> **Status (v1):** pure domain + paper-gateway scaffold only. **No live exchange wiring yet.**
> 13/13 domain tests green; no exchange I/O in this build.

See [`docs/scope/okx-spot-btc-micro-grid.md`](./docs/scope/okx-spot-btc-micro-grid.md) for the
accepted v1 product scope and [`docs/architecture/overview.md`](./docs/architecture/overview.md)
for the locked architecture (multi-exchange boundary, hostability rules, scope defaults).

## Solution layout

```
MicroGrid.sln
Directory.Build.props        shared TFM / nullable / warnings-as-errors
src/
  MicroGrid.Domain/          pure: CapitalAllocator, GeometricGrid, InventoryLedger
  MicroGrid.Application/     ports: IExchangeGateway (multi-exchange boundary)
  MicroGrid.Bot/             generic-host worker (entry point)
tests/
  MicroGrid.Domain.Tests/    xUnit, no network
Dockerfile                   multi-stage, non-root, dotnet/runtime:10.0
.dockerignore
```

| Project | Purpose | Public types |
|---|---|---|
| `MicroGrid.Domain` | Allocation, grid math, ledger. No I/O. | `GridSettings`, `CapitalAllocator.Allocation`, `GeometricGrid`, `InventoryLedger` |
| `MicroGrid.Application` | Exchange-agnostic ports. | `IExchangeGateway`, `InstrumentSpec`, `Balances`, `OrderUpdate`, … |
| `MicroGrid.Bot` | Worker entry point. | `Program` |
| `MicroGrid.Domain.Tests` | xUnit tests for the pure domain. | — |

## Build & test

```bash
dotnet test MicroGrid.sln -c Release
dotnet build MicroGrid.sln -c Release
```

Expected: `Passed: 13, Failed: 0`.

## Run in a container

```bash
docker build -t micro-grid-bot .
docker run --rm \
  -e MICROGRID_MODE=demo \
  -e OKX_API_KEY=...        -e OKX_API_SECRET=...    -e OKX_PASSPHRASE=... \
  -v "$PWD/data:/data" \
  micro-grid-bot
```

> Exchange env vars above are placeholders — the OKX.Net adapter is not wired in this build.
> The worker is demo-only at this stage. It validates OKX demo credentials and streams BTC-USDT;
> it does not place orders.

## Run OKX demo locally (Windows and macOS)

The local runner loads the ignored repository-root `.env`, authenticates against OKX demo trading,
fetches BTC-USDT, and maintains a demo WebSocket ticker subscription. It does not place orders.
Startup refuses to continue unless `OKX_DEMO_MODE=true`.
`OKX_REGION` defaults to `AU`; use `US` for US accounts or `GLOBAL` for accounts registered on
the global OKX site.

Create demo-only API credentials in OKX under **Trade → Demo Trading → Personal Center → Demo
Trading API**, then populate `.env` from `.env.example`.

Windows PowerShell:

```powershell
.\scripts\run-demo.ps1
```

macOS:

```bash
chmod +x scripts/run-demo.sh
./scripts/run-demo.sh
```

Set `MICROGRID_RUN_ONCE=true` to authenticate, print one sanitized ticker snapshot, and exit. Leave
it `false` to keep the demo WebSocket running until Ctrl+C. The same `.env` keys and .NET 10 SDK
are used on both operating systems; no Windows-only APIs are present.

If OKX returns `50101 APIKey does not match current environment`, the values came from live trading.
Create a separate key while the OKX interface is in **Demo Trading** and replace the three values
in `.env`. Live API keys are intentionally rejected by this runner.

### Live keys, when we get there

OKX live keys must be:

- **trade-only** (no withdraw)
- **IP-whitelisted** to the host running the container
- stored in env or a mounted secrets file — **never** committed to the image

## Configuration

`appsettings.json` is the baseline; env vars override (`MicroGrid__ActivePct`, `MicroGrid__Levels`,
`MicroGrid__Spacing`, …). SQLite state path defaults to `/data/state.db` inside the container.

## Development

This repo lives under `/Users/mike/Projects/BriarForge/`. All git activity must go through a
per-person wrapper (`git-aoife`, `git-declan`, `git-milena`, `git-sofia`, …) — see `AGENTS.md`.
Bare `git push` is forbidden in this folder.

## Web control plane

The first Vercel/Supabase dashboard slice lives in `src/MicroGrid.Web`; its source-controlled
database contract is under `infra/supabase`. The web and engine deliberately use separate
environment templates. Keep `OKX_API_KEY`, `OKX_API_SECRET`, and `OKX_PASSPHRASE` only on the
machine running `MicroGrid.Bot`—they are intentionally not consumed by the web app.

```powershell
Set-Location src/MicroGrid.Web
Copy-Item .env.example .env.local
npm.cmd install
npm.cmd run dev
```

For the engine host, copy the repository-root `.env.example` into your secret manager or service
environment and provide the three OKX values there. The file is a key-name template only; the
worker reads actual values from process environment variables.

The dashboard displays a configuration screen until valid Supabase public values are present.
Apply `infra/supabase/migrations/202608100001_initial_control_plane.sql`, create an Auth user, then
follow the commented seed statements in `infra/supabase/seed.sql` to authorize the first operator.
