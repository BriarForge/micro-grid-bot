---
status: accepted-v1
source: user message.txt / Discord scope
locked:
  language: .NET 10 (host SDK; LTS-up only)
  exchange_lib: CryptoExchange.Net + OKX.Net (v1)
  symbol: BTC-USDT spot
  host_target: container-first (Docker), os-neutral
  multi_exchange: planned (v1 = OKX only)
  workspace: /Users/mike/Projects/BriarForge/micro-grid-bot
out_of_band_overrides:
  - scope §4 said Python → superseded by AGENTS.md + user CryptoExchange.Net choice + hostability constraint
  - + hostable / OS-neutral → container-first .NET 8 (linux-x64, linux-arm64)
  - + multi-exchange later → IExchangeGateway boundary; CryptoExchange.Net family adapters
---

# Scope of Work: OKX Spot BTC Micro Grid Bot

## 1. Project Overview
Build an automated micro grid trading bot for **OKX Spot BTC-USDT** (or BTC-USDC) that starts with **$1,000** and automatically scales with new deposits and profits.

The bot must follow the principle:  
**Target gross profit per cycle ≈ 2 × round-trip fees**  
(At VIP 5 maker fee 0.025% → target spacing 0.10% – 0.12%).

## 2. Core Objectives
- Continuously place and manage tight limit buy/sell orders around the current BTC price.
- Automatically detect new deposits and increase in equity.
- Scale order sizes and optionally grid width as capital grows.
- Survive sudden large moves (especially a 10% drop) without running out of capital or becoming fully long.
- Remain almost exclusively maker (post-only orders).

## 3. Critical Functional Requirements

### 3.1 Capital & Allocation Rules
- Starting capital: $1,000
- Default split:
  - Active Grid: 65% of total equity
  - Safety Reserve: 35% of total equity
- Max BTC exposure: 65% of **current** total equity
- Bot must recalculate allocation every time total equity changes (new deposit or significant profit).

### 3.2 Grid Parameters (Initial)
- Spacing: **0.12%** (preferred) or 0.10%
- Number of grids: 20–25
- Order size style: Equal USDT value per level
- Mode: Geometric
- All orders must be **post-only** (maker only)
- Total active range: approximately ±1.4% to ±1.6%

### 3.3 Auto-Scaling Logic
The bot must automatically:
1. Detect increases in available balance (new deposits).
2. Detect growth in total equity from realized profits.
3. Recalculate:
   - Active capital = Total Equity × 0.65
   - Order size = Active capital / number of grids
4. Optionally increase number of grids when equity grows significantly.
5. Rebuild or adjust the grid after scaling.

### 3.4 Risk Management (Critical)
- Hard max BTC exposure of 65% of current total equity.
- When max exposure is reached → stop placing new buy orders.
- On large downward move (price drops > 2.5–3% below lowest buy order):
  - Use reserve capital to either:
    - Expand the grid lower, or
    - Fully recenter the grid at the new price.
- Maintain a permanent 30–40% reserve that is only used during significant drops or recentering.
- Kill switch / pause capability on extreme moves or API errors.

### 3.5 Order & Inventory Management
- On buy fill → immediately place corresponding sell order at next higher grid level.
- On sell fill → immediately place corresponding buy order at next lower grid level.
- Continuously track:
  - Current inventory (BTC + USDT)
  - Realized PnL
  - Open orders
  - Total equity

### 3.6 Monitoring & Control
- Real-time WebSocket for price, order book, and private account updates.
- Logging of all fills, equity changes, and grid rebuilds.
- Ability to pause, resume, or force recenter manually.

## 4. Technical Requirements
- Exchange: OKX Spot API (Unified Account preferred)
- Language: Python (preferred)
- Libraries: Official OKX SDK or well-maintained alternative (e.g. ccxt with post-only support)
- Must handle:
  - Rate limits
  - Reconnection logic
  - Partial fills
  - Minimum order size constraints

## 5. Acceptance Criteria
- Bot starts correctly with $1,000 and places a valid micro grid.
- Automatically detects a new deposit and scales order sizes.
- Respects 65% max BTC exposure during a simulated 10% drop.
- Uses reserve capital only when price breaks significantly lower.
- All orders are post-only.
- Survives and continues operating after equity increases.

## 6. Out of Scope (for this version)
- Futures / leverage
- Multiple pairs
- Complex AI prediction
- Mobile app / fancy UI (CLI or simple dashboard is acceptable)

---

**Priority Order for Development:**
1. Correct capital allocation + max exposure logic
2. Auto-scaling on deposit / equity increase
3. Tight 0.12% geometric grid with post-only orders
4. Drop protection + reserve usage
5. Clean logging and monitoring