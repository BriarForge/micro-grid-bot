---
status: proposed
id: mgbip001
title: Web-operated $0/mo topology (no local PC)
authors: [Vladislava, Aoife]
date: 2026-08-10
parent_thread: bf-github-repos / Micro Grid Bot
supersedes: Hetzner €4.5/mo plan; "laptop runs the bot" plan
related: [docs/scope/okx-spot-btc-micro-grid.md, docs/architecture/overview.md]
revision: 2026-08-10 — constraints tightened to $0/mo total + no home PC + web-operated UI
---

# MGBIP-001: Web-operated $0/mo topology (no local PC)

## Constraints (locked)

- **$0/mo total.** Free tiers only. No paid VM, no paid SaaS, no dev plan with a card.
- **No home PC.** The bot must run on a free always-on cloud VM, not on Mike's laptop.
- **Web-operated.** Monitoring and control from a browser. No local CLI for ops.
- **Stack stays .NET 10** for the engine. `CapitalAllocator`, `GeometricGrid`, `InventoryLedger` are coded and tested; ditching them gains nothing for $0/mo.
- **Multi-exchange boundary stays clean.** `IExchangeGateway` does not change. OKX demo first; Binance/Bybit slots kept open.

## Non-negotiable physics

A maker micro-grid bot is **not** a stateless workload:

- Private WS + order/user stream must stay connected for minutes/hours. Reconnect storms are an edge case the design must own, not paper over.
- Open orders must be cancellable from a kill switch at any moment. Latency to a kill switch you do not own is dangerous.
- API secrets must never reach a browser, a serverless function source, or a CDN edge.

**Conclusion:** the bot is a **long-lived stateful container** owned by the operator. Vercel serverless, Vercel cron, and Supabase Edge Functions are explicitly rejected as the grid engine runtime. Even with $0/mo to spend, the order loop cannot live in a function-with-timeout model.

The honest reading of "completely a web app" is therefore: **web-operated system** (UI + auth + realtime in Supabase/Vercel, engine in a free VM). Not "serverless-only monolith."

## Target architecture

```
┌────────────────────────────────────────────┐
│ Vercel (Next.js, free)                     │
│  - Dashboard UI (live grid, fills, PnL)    │
│  - Control actions (pause/resume/recenter) │
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
                 ▲ Postgres + Realtime WS
                 │
┌────────────────────────────────────────────┐
│ Bot (free always-on VM, ARM A1)            │
│  - MicroGrid.Bot (.NET 10 worker)          │
│  - Postgres adapter for IStateStore        │
│  - Realtime publisher                      │
│  - Control API (gated, env-only secrets)   │
│  - linux-arm64 image                       │
└────────────────────────────────────────────┘
                 │ OKX REST/WS
                 ▼
             OKX Exchange
```

| Layer | Choice | $0? | Role |
|---|---|---|---|
| Trading engine | .NET 10 worker in container | yes on free VM | Order loop, keys, WS, kill switch |
| Bot host | Oracle Cloud Always Free (ARM A1), Fly.io free as backup | yes | Replaces home PC |
| State / auth / realtime | Supabase free | yes | Postgres + magic-link + live UI |
| Dashboard | Vercel free (Next.js) | yes | Monitor + control only |
| Exchange | OKX demo first via OKX.Net | — | Behind `IExchangeGateway` |

## $0/mo cost table

| Tier | Vercel | Supabase | Bot host | Total/mo |
|---|---|---|---|---|
| Dev / paper | Free | Free | $0 (Mike's laptop, optional) | $0 |
| Prod (primary) | Free | Free | Oracle Cloud Always Free | **$0** |
| Prod (backup)  | Free | Free | Fly.io free VMs | **$0** |
| What blows the free tier | Pro features, team seats, custom domains on paid tier (free has limited domains) | >500MB DB, >2GB egress/mo, Pro add-ons | Idle-reclaim on Oracle if host sits idle 7+ days | — |

**Oracle idle reclamation note:** Always Free ARM instances may be reclaimed if CPU/network/mem < 10% for 7+ days. Mitigation: a live trading bot with periodic fills, OKX WS heartbeats, and dashboard polling is rarely idle. Add a tiny cron pinger or Prometheus exporter for paper mode on quiet markets.

## What "no local PC" actually means

- **Bot:** runs in the free VM. Owns OKX keys. Owns the kill switch.
- **Dev laptop:** optional, for coding. Not on the order path.
- **Dashboard:** browser-only, served by Vercel. No local install.
- **State:** Supabase Postgres. Backup snapshot on a schedule.

## Build order (unchanged — hosting does not reorder engine work)

1. **Track A — paper `IExchangeGateway` + grid orchestrator.** Place → fill → re-arm → rescale → exposure block. No network. Green tests for the full loop.
2. **Drop-protection policy.** Expand lower first, full recenter only on second trigger or manual command.
3. **OKX demo adapter** behind `IExchangeGateway`. Demo keys, trade-only, IP allowlist.
4. **Thin control API on the bot.** `/status`, `/pause`, `/resume`, `/recenter`, `/rescale`. Shared-secret or Tailscale in front. No public internet exposure.
5. **Supabase mirror.** Postgres adapter for `IStateStore`. Realtime publisher for grid diffs, fills, errors.
6. **Vercel dashboard v1.** Next.js. Reads Supabase, subscribes Realtime. Control buttons call the bot's API through Supabase Auth.
7. **Bot deploy to Oracle Cloud ARM A1.** Multi-arch image (linux-x64 + linux-arm64). Restart policy, log shipping, health endpoint.

## Security

- OKX keys live **only** on the bot VM, mounted as env or a docker secret. Never in Vercel, Supabase config, or repo.
- Trade-only OKX keys with IP allowlist. Withdraw disabled.
- Supabase RLS so the dashboard can only see its own user's data (single-tenant for v1, but RLS designed in).
- Control API auth'd (Supabase JWT for dashboard calls, shared-secret for direct API access). Rate-limited.
- Discord webhook for alerts (free, Mike already owns the channel).

## Explicitly rejected

- **Vercel serverless / cron as the grid engine.** Timeout model + cold start + no long-lived WS.
- **Supabase Edge Functions as the grid engine.** Same timeout model. Worse for secrets handling.
- **"Pure serverless trading bot" on any vendor.** Fantasy for this scope.
- **Rewriting domain to TS only so it can sit on Vercel.** Doesn't solve the long-lived process problem; throws away 13 green tests and OKX.Net.

## Proposed repo changes (when implementation starts)

New:
- `src/MicroGrid.Web/` — Next.js (deployed to Vercel)
- `src/MicroGrid.Infrastructure.Postgres/` — Postgres adapter for `IStateStore`
- `src/MicroGrid.Infrastructure.Realtime/` — Supabase Realtime publisher
- `infra/supabase/` — migrations + RLS policies
- `infra/vercel/` — project config + env matrix
- `infra/bot-host/` — Oracle ARM A1 bootstrap, Caddy reverse proxy, restart policy
- `docs/operations/runbook.md` — Oracle ops, secret rotation, DB backups, idle mitigation

Modified:
- `src/MicroGrid.Bot/` — Postgres + Realtime wiring, control API, linux-arm64 image
- `src/MicroGrid.Application/` — **no port changes**; same `IStateStore`, new adapter behind it
- `docs/architecture/overview.md` — short subsection for prod topology (dev vs prod)
- `docs/scope/okx-spot-btc-micro-grid.md` — supersede-or-amend to v2 only when implementation lands

Untouched:
- `src/MicroGrid.Domain/` — pure logic, no change
- 13 domain tests — no change

## Open questions

- Oracle Cloud account OK, or prefer Fly.io free (slightly tighter VM resources, no idle-reclaim risk)?
- Domain name? (Drives DNS + Vercel custom domain + Oracle setup.)
- Discord webhook for alerts vs email/SMS? (Discord is free.)
- Single-tenant (Mike only) from day one, or multi-user RLS?
- Paper mode first (free forever) or jump straight to live?
- Vercel custom domain on free tier — OK or do we need Pro for the domain?

## Status

`proposed`. Direction is locked; **implementation is deferred** until Track A (paper engine) is green. MGBIP-001 changes hosting, not engine — it does not reprioritize paper → drop-protection → OKX demo → thin control API → Supabase → Vercel.