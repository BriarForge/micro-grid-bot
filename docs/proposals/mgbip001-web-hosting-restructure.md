---
status: draft
id: mgbip001
title: Web hosting and deployment restructure (revised for $0/mo target)
authors: [Vladislava]
date: 2026-08-10
parent_thread: bf-github-repos / Micro Grid Bot
supersedes: (none)
related: [docs/scope/okx-spot-btc-micro-grid.md, docs/architecture/overview.md]
revision: 2026-08-10 19:19 GMT+8 — target updated to $0/mo total
---

# MGBIP-001: Web hosting and deployment restructure (revised for $0/mo)

## Context

v1 is locked as a headless .NET 10 worker running in a single container (`docs/architecture/overview.md`: "single console worker running as a container, no GUI dependency"). The current layout is `Domain → Application → Bot`, with `IExchangeGateway` and `IStateStore` as application-layer ports, and a SQLite-backed `IStateStore`.

User wants the bot exposed as a web app so it can be monitored and controlled remotely.

**Revision (2026-08-10, 19:19 GMT+8):** user wants $0/mo total. The previous revision proposed a $5/mo container host (Hetzner CX22). This revision compares two genuinely-free paths and recommends the one that keeps .NET.

## Hard constraints

- **$0/mo total.** No paid tiers. Free tiers only.
- .NET 10 codebase stays if possible. The domain layer (`CapitalAllocator`, `GeometricGrid`, `InventoryLedger`) is coded and tested. Rewrite is a fallback, not a default.
- `IExchangeGateway` boundary stays clean. v2 Binance/Bybit adapters must not be blocked.
- Real money on the line. Uptime, secrets handling, and observability are non-negotiable.
- Single owner (Mike). Ops burden must stay low.
- House deposit goal is $2M AUD by 2029. Free tiers only.

## Options

### E. Vercel + Supabase + bot on Oracle Cloud Always Free (recommended, $0/mo, keep .NET)

- **Oracle Cloud Always Free** gives 4 ARM Ampere A1 cores (24GB RAM total), 200GB block storage, 10TB outbound/mo. Always free, no time limit.
- Bot runs as a Docker container on a free ARM A1 instance. .NET 10 supports linux-arm64 out of the box (already in `Directory.Build.props`).
- **Vercel hobby** hosts the Next.js dashboard (free tier).
- **Supabase free** provides Postgres (500MB), Auth (magic link + OAuth), Realtime.
- **Total: $0/mo forever.**

Pros: free in perpetuity, infrastructure already committed to. .NET 10 already supports arm64. No rewrite, no risk.
Cons: Oracle Cloud signup is more friction than Hetzner (KYC, region selection, capacity quirks). Account idle reclamation risk if consistently idle. ARM-only forces careful multi-arch image builds. Setup is one-time but real work.

**Oracle Cloud Always Free capacity (ARM A1):**
- 4 OCPUs total (split as 1×4, 2×2, 4×1)
- 24GB RAM total (1GB per OCPU)
- 200GB block storage total
- 10TB outbound/mo

A single bot needs ~1 OCPU + 2GB RAM. Plenty of headroom for monitoring, backups, or future bots.

**Oracle idle reclamation risk:** Oracle reclaims instances that are < 10% CPU, network, and memory for 7+ days. Mitigation: run a tiny monitoring sidecar (Prometheus exporter, Uptime Kuma agent, or a cron pinger) so the host shows non-trivial activity. A live trading bot with periodic fills, OKX websocket heartbeats, and dashboard polling almost always stays above 10% CPU. Idle reclamation is a real worry only for paper mode on a quiet market.

### F. Vercel + Supabase + bot on Fly.io free tier (alternative, $0/mo, requires Node/TS rewrite)

- **Fly.io free** offers 3 shared-cpu-1x VMs at 256MB RAM each, 3GB volume, 160GB outbound/mo. Always free.
- 256MB is too tight for .NET 10 (runtime ~80MB + working set + websocket connections). Fine for Node.js.
- Trade-off: rewrite the bot in Node.js or TypeScript. OKX has an official TypeScript SDK. The multi-exchange boundary stays (`IExchangeGateway` → `ExchangeGateway` TS interface).
- **Total: $0/mo forever, after rewrite.**

Pros: free forever, faster setup than Oracle, no idle reclamation risk.
Cons: rewrite the bot (estimated 3-5 days: domain + bot wiring + tests). `CryptoExchange.Net` is .NET-only; OKX has a TS SDK but the multi-exchange boundary becomes harder. Lose the existing .NET test coverage.

### G. Render free cron for the bot (rejected for live trading)

- Render free cron jobs are free and don't spin down. But cron jobs aren't suitable for a reactive trading bot that needs to react to fills 24/7.

Ruled out for live trading. Could be used for paper mode only.

### Old options (A–D, superseded)

- **A. Vercel + Supabase + bot on Hetzner CX22 (€4.5/mo)** — superseded by Option E ($0/mo, same architecture).
- **B. Self-contained single container** — superseded by Option E (no real benefit once Oracle is free).
- **C. Vercel + Supabase + bot on Mike's own hardware** — superseded by Option E (Oracle is more reliable than a home server).
- **D. Vercel + rewrite bot in TypeScript** — superseded by Option F (Fly.io free is the right home for the rewrite).

## Recommendation

**Option E: Vercel + Supabase + bot on Oracle Cloud Always Free.**

Rationale:
- Existing code is .NET 10, tested, working. Don't rewrite what works.
- Oracle Cloud Always Free is genuinely free in perpetuity (4 ARM cores, 24GB RAM). More than enough for a single trading bot.
- .NET 10 supports linux-arm64 (already in `Directory.Build.props`). No image build changes.
- $0/mo forever. Matches the user's hard constraint.
- Oracle Cloud setup is one-time work, not recurring cost.

Fallback: **Option F (Fly.io free + Node/TS rewrite)** if Oracle Cloud signup is too painful, or if the user prefers a lighter runtime. The rewrite cost is real (3-5 days) but the runtime is more idiomatic for a free-deck stack.

## Proposed target architecture

```
┌────────────────────────────────────────────┐
│ Vercel (Next.js, free)                     │
│  - Dashboard UI (live grid, fills, PnL)    │
│  - Control actions (start/pause/rescale)   │
│  - Auth via Supabase JS SDK                │
└────────────────────────────────────────────┘
                 │ HTTPS + WSS
                 ▼
┌────────────────────────────────────────────┐
│ Supabase (free)                            │
│  - Postgres (state, fills, orders, pnl)    │
│  - Auth (magic link + OAuth)               │
│  - Realtime (grid diffs, fills, errors)    │
└────────────────────────────────────────────┘
                 ▲ Postgres TCP + Realtime WS
                 │
┌────────────────────────────────────────────┐
│ Bot (Oracle Cloud Always Free, ARM A1)     │
│  - MicroGrid.Bot (existing .NET 10 worker) │
│  - New Postgres adapter for IStateStore    │
│  - New Realtime publisher                  │
│  - New minimal control API (gated)          │
│  - linux-arm64 image                       │
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
- `src/MicroGrid.Bot/` — add PostgreSQL + Realtime wiring, add minimal control API, secrets via env, linux-arm64 image
- `src/MicroGrid.Application/` — no port changes; same `IStateStore` interface, new adapter behind it
- `docs/architecture/overview.md` — bump to v2 with the new layers
- `docs/scope/okx-spot-btc-micro-grid.md` — supersede-or-amend to v2

New infra:
- `infra/supabase/` — Supabase project setup, migrations, RLS policies
- `infra/vercel/` — Vercel project config, env matrix
- `infra/bot-host/` — Oracle Cloud ARM A1 bootstrap, Dockerfile polish, Caddy reverse proxy, Let's Encrypt, restart policy

New docs:
- `docs/operations/runbook.md` — Oracle Cloud ops, secret rotation, DB backups, idle reclamation mitigation

## Migration plan

1. **Approve MGBIP-001** (this doc).
2. **Oracle Cloud setup.** Create a free-tier account, create an ARM A1 shape in Sydney (closest to Perth) or another region with capacity, install Docker, configure UFW. One-time.
3. **Postgres swap.** Stand up a Supabase project. Add `MicroGrid.Infrastructure.Postgres` implementing `IStateStore`. Bot still runs as a single container. Verify trades, fills, and inventory land in Postgres with the same semantics as SQLite.
4. **Realtime.** Add a publisher from the bot to Supabase Realtime for: grid state diffs, fills, errors. No UI yet.
5. **Control API.** Add a minimal HTTP API on the bot (start/pause/recenter/rescale/update config), gated by a shared secret + IP allowlist. Front with Caddy + Let's Encrypt.
6. **Dashboard v1.** Stand up `MicroGrid.Web` (Next.js) on Vercel. Read state from Supabase, subscribe to Realtime, render live grid, fills feed, PnL summary, current config. No control yet.
7. **Dashboard control.** Wire control buttons to the bot's API, gated by Supabase Auth (magic link minimum).
8. **Bot deploy.** Move the bot from local dev to Oracle ARM A1. Wire secrets via env. Add health endpoint, restart policy, log shipping.
9. **Cutover.** Disable any local runs. Update `docs/operations/runbook.md`.

## Open questions

- Oracle Cloud account creation: any prior experience? (Drives whether Option E or Option F is more attractive.)
- Mobile-first or laptop-first dashboard? (Drives whether Auth must support OAuth + TOTP, or if magic link is enough.)
- Single-tenant (Mike only) or multi-user from day one? (Affects Supabase RLS design.)
- Paper mode first or live trading first? (Paper can run on free tiers indefinitely; live needs the production split immediately.)
- Notification channel on error: email, Discord webhook, Telegram, SMS? (Discord webhook is free and Mike already owns the channel.)
- Domain name? (Drives DNS + Vercel + Oracle setup.)
- Confirm Oracle Cloud Always Free tier is acceptable, or prefer the Fly.io free + Node/TS rewrite path?

## Cost summary

| Tier | Vercel | Supabase | Bot host | Total/mo |
|---|---|---|---|---|
| Dev / paper | Free | Free | $0 (Mike's laptop) | $0 |
| Production (Option E) | Free | Free | Oracle Cloud Always Free | $0 |
| Production (Option F, after rewrite) | Free | Free | Fly.io free | $0 |
| Paid upgrade (if needed) | Pro $20 | Pro $25 | Hetzner CX22 €4.5 | ~$50 |

## Status

`draft`. Pending review and approval. Revision reflects $0/mo target.
