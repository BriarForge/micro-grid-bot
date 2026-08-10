# Micro Grid Bot

Local-first OKX BTC-USDT micro-grid control panel and monitoring service. One ASP.NET Core process
hosts the browser UI, localhost API, OKX monitor, and durable local settings. Vercel and Supabase
are optional and are not required for this mode.

> Current safety state: OKX market data, balances, and account fee tiers are read live. Order
> placement is deliberately locked off while execution and reconciliation are completed.

## Run locally

Requirements: [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) and an OKX API key
with read permission. Do not grant withdrawal permission.

Copy `.env.example` to `.env` and provide `OKX_API_KEY`, `OKX_API_SECRET`, and `OKX_PASSPHRASE`.
The `.env` file is gitignored. Set `OKX_DEMO_MODE=true` for a demo-trading key or `false` for a
live-trading key. `OKX_REGION` defaults to `GLOBAL`; `AU` and `US` are also supported.

Windows PowerShell:

```powershell
.\scripts\run-local.ps1
```

macOS:

```bash
chmod +x scripts/run-local.sh
./scripts/run-local.sh
```

Open [http://localhost:5080](http://localhost:5080). Stop it with Ctrl+C. The service binds only
to `127.0.0.1` by default, so other computers cannot reach it. Keep the computer awake and the
terminal process running; on macOS, `caffeinate -i ./scripts/run-local.sh` can prevent idle sleep.

Grid settings are written atomically to `data/settings.json`. API secrets stay only in `.env` and
are never returned by the localhost API or displayed in the UI.

## Build and test

```bash
dotnet build MicroGrid.sln -c Release
dotnet test MicroGrid.sln -c Release
```

## Container

```bash
docker build -t micro-grid-bot .
docker run --rm --env-file .env -p 127.0.0.1:5080:8080 -v "$PWD/data:/app/data" micro-grid-bot
```

## Architecture

- `src/MicroGrid.Domain`: allocation, fee-aware grid math, and inventory ledger
- `src/MicroGrid.Application`: exchange-neutral gateway contracts
- `src/MicroGrid.Bot`: localhost UI/API and OKX monitoring host
- `src/MicroGrid.Web`: optional Vercel/Supabase control-plane prototype
- `infra/supabase`: optional cloud control-plane schema

The accepted product scope is in `docs/scope/okx-spot-btc-micro-grid.md`; architecture details are
in `docs/architecture/overview.md`.
