---
status: draft
id: mgbip002
title: Native first-party agent control (Hermes / OpenClaw)
authors: [Aoife]
date: 2026-08-11
parent_thread: bf-github-repos / Micro Grid Bot
supersedes: (none)
related:
  - docs/scope/okx-spot-btc-micro-grid.md
  - docs/architecture/overview.md
  - docs/proposals/mgbip001-web-hosting-restructure.md
revision: 2026-08-11 — initial draft
---

# MGBIP-002: Native first-party agent control (Hermes / OpenClaw)

## Decision

Give agents (Hermes, OpenClaw, and any future first-party operator agent) a **first-party
control surface on the bot host** — not a screen-scraper, not a guess at HTTP, not a hand-shelled
git command. The surface is owned by `MicroGrid.Bot` and shares the same command bus as the
future MGBIP-001 UI, so the bot has **one brain** and agents are **not** a parallel order path.

```
Hermes / OpenClaw agent
        |
        |   (preferred: `microgrid` CLI on the bot host,
        |    optional: loopback HTTP for non-CLI agents)
        v
Agent control surface (first-party)
        |
        v
MicroGrid.Bot (authoritative engine)
   - shared command bus with MGBIP-001 UI
   - deny-by-default capabilities
   - append-only audit log (who / what / when / result)
        |
        v
Domain + IExchangeGateway  (keys stay here)
```

### Non-negotiables

- **Agents MUST NOT** call OKX directly. **MUST NOT** carry OKX secrets in prompts, env, or
  skill bodies. The engine is the only owner of `OKX_API_KEY` / `OKX_API_SECRET` /
  `OKX_PASSPHRASE`.
- **Agents MUST NOT** bypass `LocalBotSettings.Validate()` or the future risk layer.
  `TradingEnabled` remains engine-gated; agent API cannot enable trading without an explicit
  engine-side unlock that ships with Track A.
- **Agents are not a second brain.** MGBIP-001 (Supabase/Vercel) and MGBIP-002 (agents) speak
  the same command verbs and audit the same events.
- **Bound to `127.0.0.1` by default.** Control surface is not internet-exposed; remote agent
  access is a separate decision (Tailscale, mTLS, or cloud control plane later).

## Surfaces (pick the canonical one, document the rest)

### 1. `microgrid` CLI — preferred primary

First-party local CLI lives next to the engine (`src/MicroGrid.Bot/tools/microgrid/`). Pattern
matches the BriarForge sibling convention (`fiscavacli`, WhatsApp exporter first-party aliases).

```text
microgrid status                      # current RuntimeSnapshot (JSON with --json)
microgrid settings get|set <key>=val  # read / mutate LocalBotSettings
microgrid pause | resume | recenter | rescale
microgrid emergency-stop             # admin tier; same engine kill switch as MGBIP-001
microgrid orders list                # open / recent (when Track A lands)
microgrid fills tail [--follow]      # live fill stream after Track A
microgrid audit tail [--json]         # append-only agent audit log
microgrid whoami                      # capability tier for this token
```

Rules:

- **Exit codes are stable.** `0` ok, `2` invalid args, `3` denied by capability, `4` engine
  rejected (validation / risk), `5` transport / engine unavailable. Agent fleets depend on
  these.
- **JSON stdout** with `--json` for agents; human-readable text by default.
- **Stderr for diagnostics only.** Never mix diagnostic noise with the JSON payload.
- **`microgrid completion {bash|zsh|fish|powershell}`** ships in the repo for operator shells.

### 2. Loopback HTTP — secondary

Extends the existing `127.0.0.1` minimal API:

- `GET  /api/status`          (existing)
- `GET  /api/settings`        (existing)
- `PUT  /api/settings`        (existing — non-trading fields)
- `POST /api/commands`        (new — mirrors Supabase `bot_commands` types; idempotency key
                                required; shared secret in `Authorization: Bearer` header)
- `GET  /api/audit`           (new — agent audit log)

Binds `127.0.0.1` only. Optional Unix domain socket variant later for hardened hosts.

### 3. Optional KovaForge/Hermes skill — tertiary

Thin wrapper lives under the team skills repo when implemented:

- Tags: `microgrid`, `okx`, `grid`, `trading`, `localhostmgr`, `btc-usdt`
- Invokes the CLI; **never embeds keys**; documents `.env` requirements
- `related_skills`: `macos-localhost-supervisor`, `localhostmgr`, `openclaw-doctor-resilience`

### 4. Naming for fleet inventory

`microgrid` is the canonical name. First-party aliases (cosmetic symlinks, same binary) for
fleet inventories are fine — `mg`, `mgb`, `openclaw-microgrid`, `hermes-microgrid`. Do **not**
fork two implementations; one binary, many names.

## Capability model

The engine gates every mutating call. Tiers are explicit, deny-by-default, and recorded per
token in the audit log.

| Tier     | Allowed                                                                                | Default                 |
|----------|----------------------------------------------------------------------------------------|-------------------------|
| observe  | `status`, `settings get`, `audit tail` (read-only), future `orders list`, `fills tail` | **on** for local agents |
| operate  | `pause`, `resume`, `recenter`, `rescale`, `settings set` (non-trading fields)          | explicit grant          |
| trade    | arm grid / enable `TradingEnabled`                                                     | **off**; requires explicit unlock after Track A + OKX demo |
| admin    | `emergency-stop`, rotate control secret, revoke tokens                                 | owner-only, single token |

Token rules:

- Capability tokens are issued by the owner; the engine stores **hashes** (Argon2id), not
  secrets.
- Tokens are scoped per agent id (e.g. `aoife`, `declan`, `openclaw-session-…`) and optional
  expiry.
- Revocation is immediate (in-memory + file); no waiting on a 5-minute JWT window.

## Command bus (shared with MGBIP-001)

Verbs are the existing Supabase `bot_commands` enum so MGBIP-001 UI and agents stay on one
bus:

```
pause | resume | recenter | rescale | emergency_stop
```

Every command — from the Vercel UI, from Supabase, or from an agent — goes through the same
in-process handler. Idempotency key required; duplicate keys return the prior result.

## Audit log

Append-only file at `$MICROGRID_STATE_DIR/audit.ndjson` (one JSON object per line):

```json
{
  "ts": "2026-08-11T03:14:15.000Z",
  "actor": "aoife",
  "surface": "cli",
  "command": "pause",
  "args": {"reason": "manual"},
  "idempotency_key": "9c0a…",
  "capability": "operate",
  "result": "accepted",
  "duration_ms": 4,
  "engine_correlation": "local"
}
```

When MGBIP-001 lands, the same events mirror into `bot_events` so the dashboard sees agent
actions alongside UI actions.

## Build order (does not replace Track A)

1. **Track A — paper `IExchangeGateway` + orchestrator.** First for *trading* correctness.
2. **MGBIP-002 observe + operate (on today's read-only bot)** can land in **parallel**.
   `pause`/`resume`/`recenter`/`rescale` against the existing `RuntimeState` are safe —
   `TradingEnabled` is still false.
3. **`trade` tier** only after paper engine + OKX demo adapter.
4. **MGBIP-001 remote UI** consumes the same command port; MGBIP-002 is not a parallel stack.

## Security boundary

- Control surface binds `127.0.0.1`. No `0.0.0.0`. No public ports.
- Tokens live in the same secret envelope as OKX keys (env / docker secret); never in repo.
- Tokens do **not** grant OKX access. They grant control-plane verbs only.
- Engine logs the actor, command, capability tier, and result. No token or secret material in
  logs.
- `emergency-stop` is always honoured by the engine — even from `observe`-only tokens? **No.**
  Admin tier only. But a separate belt-and-braces kill switch (single physical command or
  process signal) is acceptable and out of scope for this proposal.

## Explicitly rejected

- Agents calling OKX REST/WS directly.
- Agents holding `OKX_*` env in their own process.
- Serverless or cron-based "agent" wrappers for the order loop.
- Two implementations of the CLI (one for Hermes, one for OpenClaw).
- Shipping `trade` tier before Track A + OKX demo passes review.
- Putting the control surface behind Supabase only — agents must work offline against a
  loopback host without depending on the cloud plane.

## Out of scope for this proposal

- Implementing the CLI / skill
- Enabling `localhostmgr` or writing `.env`
- Changing `OkxMonitorWorker` to place orders
- Accepting MGBIP-001 implementation sprint
- A cloud-hosted agent control plane (separate proposal if/when needed)

## Open questions

- Capability storage: single shared secret vs per-agent tokens? *Default if no answer:
  per-agent tokens hashed with Argon2id.*
- v1 CLI-only, or CLI + HTTP together? *Default if no answer: CLI first, HTTP second pass.*
- Should `localhostmgr restart` be agent-callable or stay supervisor-only?
  *Default if no answer: supervisor-only.*
- Audit sink: local file first, then mirror to Supabase `bot_events` when MGBIP-001 ships?
  *Default if no answer: yes, in that order.*

## Success criteria

- File pushed via `git-aoife` with the conventions above.
- Reviewer can answer: where does the control surface live, who can call what, how is misuse
  prevented, and how does this compose with MGBIP-001 without duplication.
- Next user message either approves, defers, or asks for revisions. Engineering work on
  MGBIP-002 starts only on explicit "implement 002".