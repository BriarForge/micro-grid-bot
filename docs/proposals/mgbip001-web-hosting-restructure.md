---
status: draft
id: mgbip001
title: Web hosting and deployment restructure
authors: [Vladislava]
date: 2026-08-10
parent_thread: bf-github-repos / Micro Grid Bot
supersedes: (none)
related: [docs/scope/okx-spot-btc-micro-grid.md, docs/architecture/overview.md]
---

# MGBIP-001: Web hosting and deployment restructure

## Context

v1 is locked as a headless .NET worker running in a single container. The architecture doc (overview.md) is explicit: "single console worker running as a container, no GUI dependency." The current layout is `Domain → Application → Bot`, with `IExchangeGateway` and `IStateStore` as application-layer ports, and a SQLite-backed `IStateStore`.

User now wants the bot exposed as a web app so it can be monitored and controlled remotely.

## Hard constraints

- .NET 10 codebase stays. `CapitalAllocator`, `GeometricGrid`, `InventoryLedger` and the Application layer are coded and tested. Rewriting in Node/TS is out of scope.
- `IExchangeGateway` boundary stays clean. v2 Binance/Bybit adapters must not be blocked.
- Real money on the line. Uptime, secrets handling, and observability are non-negotiable.
- Single owner (Mike). Ops burden must stay low.
- Cost-sensitive: house deposit goal is $2M AUD by 2029. Free tiers preferred, paid tiers only when they pay for themselves.

## Options

### A. Vercel + Supabase + bot on a small container (recommended)

- Vercel hosts the Next.js dashboard.
- Supabase provides Postgres (replaces SQLite), Auth (magic link + OAuth), Realtime (live grid/fills stream).
- Bot runs as a container on Hetzner CX22 (€4.5/mo), Fly.io, or Railway. Talks to OKX normally. Writes state to Supabase Postgres. Publishes events to Supabase Realtime.
- Dashboard reads from Postgres, subscribes to Realtime, calls a small control API on the bot (start/pause/recenter/rescale), auth gated by Supabase.
- Migration: add `MicroGrid.Infrastructure.Postgres` adapter for `IStateStore`, add a Supabase Realtime publisher, migrate SQLite data, add `MicroGrid.Web` for the dashboard, move bot to a container host.

Pros: clean separation, dashboard can iterate fast, managed Postgres, free auth, real-time updates out of the box, easy failover.
Cons: 3 services to operate, state is now remote (adds ~30–80ms latency per write), bots two homes to secure.

### B. Self-contained single container (bot serves its own UI)

- Bot exposes a small ASP.NET Core Kestrel endpoint with a static HTML/JS dashboard and JSON control routes. One Docker image. SQLite stays. No Vercel, no Supabase.
- Add a minimal auth layer (cookie + bcrypt) or front with Authelia/Caddy.

Pros: simplest possible stack, single deploy, $0/mo on Mike's own hardware.
Cons: ASP.NET Core HTTP layer added to a "headless" bot, letsencrypt or Caddy burden, UI polish limited, no managed auth, no realtime channel (adds polling or SignalR).

### C. Vercel + Supabase + bot on Mike's own hardware (Hetzner box already owned)

- Same as A but the bot runs on a box Mike already operates (or a Hetzner box he adds). $0/mo on the host if he already has one; €4.5/mo otherwise.

Pros: cheapest, no managed Docker host.
Cons: depends on Mike's hardware staying up, SSL/reverse proxy work, manual backups.

### D. Vercel + rewrite bot in TypeScript (rejected)

- Throw away the .NET code. Rewrite the domain in TS. Host both dashboard and bot on Vercel.

Pros: single deploy target.
Cons: throws away ~all the work. No mature TS equivalent of CryptoExchange.Net. Multi-exchange boundary becomes harder. **Not worth the rewrite.**

## Recommendation

**Option A: Vercel + Supabase + bot on a small container.**

Rationale:
- Supabase is the load-bearing piece. It replaces SQLite with Postgres (better for dashboard queries + concurrent writes), gives free magic-link + OAuth auth, and gives free Realtime for live updates. All three are exactly what a trading dashboard needs.
- Vercel is the best place to host a Next.js dashboard. Free tier is generous; Pro is $20/mo if needed.
- The .NET bot can't run on Vercel or Supabase. It needs a container host. Hetzner CX22 (€4.5/mo) is the cheapest viable. Fly.io and Railway have free tiers for small workloads.
- The `IStateStore` port swap (SQLite → Postgres) is a small adapter change. The Application layer doesn't change. The `IExchangeGateway` boundary stays.

### Cheapest viable split (free tier to start)

- Vercel hobby (Next.js dashboard)
- Supabase free (Postgres ≤500MB, Auth, Realtime)
- Fly.io free allowance or Hetzner CX22 (€4.5/mo) for the bot

Total: **$0–5/mo**.

### Recommended production split (when real money is live)

- Vercel Pro ($20/mo) — password protection, team features if needed
- Supabase Pro ($25/mo) — 8GB DB, daily backups, PITR, larger egress
- Hetzner CX22 or Fly.io dedicated $5–10/mo for the bot

Total: **~$50–55/mo**.

## Proposed target architecture

```
┌────────────────────────────────────────────┐
│ Vercel (Next.js 14, App Router)            │
│  - Dashboard UI (live grid, fills, PnL)    │
│  - Control actions (start/pause/rescale)   │
│  - Auth via Supabase JS SDK                │
└────────────────────────────────────────────┘
                 │ HTTPS + WSS
                 ▼
┌────────────────────────────────────────────┐
│ Supabase                                   │
│  - Postgres (state, fills, orders, pnl)    │
│  - Auth (magic link + OAuth, MFA)          │
│  - Realtime (grid diffs, fills, errors)    │
└────────────────────────────────────────────┘
                 ▲ Postgres TCP + Realtime WS
                 │
┌────────────────────────────────────────────┐
│ Bot (Hetzner / Fly.io / Railway, .NET 10)  │
│  - MicroGrid.Bot (existing worker)         │
│  - New Postgres adapter for IStateStore    │
│  - New Realtime publisher                  │
│  - New minimal control API (gated)          │
└────────────────────────────────────────────┘
                 │ OKX REST/WS
                 ▼
             OKX Exchange
```

## Proposed repo layout

New projects:
- `src/MicroGrid.Web/` — Next.js app (TypeScript, deployed to Vercel)
- `src/MicroGrid.Infrastructure.Postgres/` — Postgres adapter for `IStateStore`
- `src/MicroGrid.Infrastructure.Realtime/` — Supabase Realtime publisher

Modified projects:
- `src/MicroGrid.Bot/` — add PostgreSQL + Realtime wiring, add minimal control API, secrets via env
- `src/MicroGrid.Application/` — no port changes; same `IStateStore` interface, new adapter behind it
- `docs/architecture/overview.md` — bump to v2 with the new layers
- `docs/scope/okx-spot-btc-micro-grid.md` — supersede-or-amend to v2

New infra:
- `infra/supabase/` — Supabase project setup, migrations, RLS policies
- `infra/vercel/` — Vercel project config, env matrix
- `infra/bot-host/` — Dockerfile polish, systemd unit / fly.toml / Hetzner bootstrap script

New docs:
- `docs/operations/runbook.md` — bot host ops, secret rotation, DB backups
- `docs/proposals/` — this file, future MGBIPs

## Migration plan

1. **Approve MGBIP-001** (this doc).
2. **Postgres swap.** Stand up a Supabase project. Add `MicroGrid.Infrastructure.Postgres` implementing `IStateStore`. Bot still runs as a single container. Verify trades, fills, and inventory land in Postgres with the same semantics as SQLite.
3. **Realtime.** Add a publisher from the bot to Supabase Realtime for: grid state diffs, fills, errors. No UI yet. Subscribe from a manual `psql`/`wscat` to verify.
4. **Control API.** Add a minimal HTTP API on the bot (start/pause/recenter/rescale/update config), gated by a shared secret + IP allowlist. No public exposure yet.
5. **Dashboard v1.** Stand up `MicroGrid.Web` (Next.js) on Vercel. Read state from Supabase, subscribe to Realtime, render live grid, fills feed, PnL summary, current config. No control yet.
6. **Dashboard control.** Wire control buttons to the bot's API, gated by Supabase Auth (magic link minimum, OAuth + TOTP optional).
7. **Host move.** Move the bot from local dev to Hetzner or Fly.io. Wire secrets via the host's secrets manager. Add health endpoint, restart policy, log shipping.
8. **Cutover.** Disable any local runs. Keep staging/canary if desired. Update `docs/operations/runbook.md`.

## Open questions

- Mobile-first or laptop-first dashboard? (Drives whether Auth must support OAuth + TOTP, or if magic link is enough.)
- Single-tenant (Mike only) or multi-user from day one? (Affects Supabase RLS design and the cost tier.)
- Paper mode first or live trading first? (Paper can run on free tiers indefinitely; live needs the production split immediately.)
- Notification channel on error: email, Discord webhook, Telegram, SMS? (Discord webhook is free and Mike already owns the channel.)
- Domain name? `microgrid.mike.au` or similar? (Drives DNS + Vercel project setup.)
- Should the bot be portable across multiple hosts (Hetzner + Fly.io) for failover, or single-host?
- Does the bot need an admin-only "danger zone" surface (force-recenter, kill switch, withdraw)? If yes, a separate auth tier is needed.

## Cost summary

| Tier | Vercel | Supabase | Bot host | Total/mo |
|---|---|---|---|---|
| Dev / paper | Free | Free | $0 (Mike's laptop) | $0 |
| Cheapest production | Free | Free | Hetzner CX22 €4.5 | ~$5 |
| Recommended production | Pro $20 | Pro $25 | Hetzner / Fly.io $5–10 | ~$50–55 |

## Status

`draft`. Pending review and approval before any code changes.
