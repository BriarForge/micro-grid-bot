#!/usr/bin/env python3
"""Reproducible OKX BTC-USDT grid-strategy simulation.

Downloads public OKX candles (no API key) and simulates the currently documented
strategy helpers. This is deliberately separate from production code because the
repository has no trading engine or exchange execution adapter yet.
"""

from __future__ import annotations

import csv
import concurrent.futures
import json
import os
import threading
import time
import urllib.parse
import urllib.request
from dataclasses import dataclass
from datetime import datetime, timezone
from decimal import Decimal, ROUND_CEILING, ROUND_FLOOR, getcontext
from pathlib import Path

getcontext().prec = 34

ROOT = Path(__file__).resolve().parent
DATA = ROOT / "data"
RESULTS = ROOT / "results"
DATA.mkdir(parents=True, exist_ok=True)
RESULTS.mkdir(parents=True, exist_ok=True)

API = "https://www.okx.com/api/v5/market/history-candles"
SYMBOL = "BTC-USDT"
RUN_LABEL = os.environ.get("BACKTEST_LABEL", "2026_to_date")
DATA_LABEL = os.environ.get("BACKTEST_DATA_LABEL", RUN_LABEL)
ONLY_RECENTER = os.environ.get("BACKTEST_ONLY_RECENTER", "false").lower() == "true"
RECENTER_THRESHOLDS = [
    Decimal(value.strip())
    for value in os.environ.get("BACKTEST_RECENTER_THRESHOLDS", "").split(",")
    if value.strip()
]
START = datetime.fromisoformat(os.environ.get("BACKTEST_START", "2026-01-01T00:00:00+00:00"))
END_EXCLUSIVE_TEXT = os.environ.get("BACKTEST_END_EXCLUSIVE")
END_EXCLUSIVE = datetime.fromisoformat(END_EXCLUSIVE_TEXT) if END_EXCLUSIVE_TEXT else None
START_MS = int(START.timestamp() * 1000)
END_EXCLUSIVE_MS = int(END_EXCLUSIVE.timestamp() * 1000) if END_EXCLUSIVE else None
MAKER_FEE = Decimal("0.0008")       # Account-specific Lv1 rate read 2026-08-12.
TAKER_FEE = Decimal("0.001")        # Account-specific Lv1 rate read 2026-08-12.
CONFIGURED_SPACING = Decimal("0.0012")
# Mirrors FeeAwareSpacing.Resolve: maker round trip * grossProfitMultiple(2).
EFFECTIVE_SPACING = max(CONFIGURED_SPACING, MAKER_FEE * 2 * 2)
STARTING_USDT = Decimal(os.environ.get("BACKTEST_STARTING_USDT", "100"))
ACTIVE_PCT = Decimal("0.65")
LEVELS = 25
BUY_LEVELS = 12
SELL_LEVELS = 13
MAX_EXPOSURE = Decimal("0.65")
RESUME_EXPOSURE = Decimal("0.60")
TICK = Decimal("0.1")
LOT = Decimal("0.00000001")
MIN_QTY = Decimal("0.00001")


def utc_iso(timestamp_ms: int) -> str:
    return datetime.fromtimestamp(timestamp_ms / 1000, tz=timezone.utc).isoformat()


def download_candles(bar: str, output: Path) -> list[dict[str, str | int]]:
    """Download descending pages until START, cache as ascending CSV."""
    if output.exists():
        with output.open(newline="", encoding="utf-8") as handle:
            return list(csv.DictReader(handle))

    rows: dict[int, dict[str, str | int]] = {}
    cursor: int | None = END_EXCLUSIVE_MS
    request_count = 0
    while True:
        params = {"instId": SYMBOL, "bar": bar, "limit": "300"}
        if cursor is not None:
            params["after"] = str(cursor)
        url = API + "?" + urllib.parse.urlencode(params)
        request = urllib.request.Request(url, headers={"User-Agent": "MicroGridBot-Backtest/1.0"})
        with urllib.request.urlopen(request, timeout=30) as response:
            payload = json.load(response)
        if payload.get("code") != "0":
            raise RuntimeError(f"OKX error: {payload}")
        page = payload.get("data", [])
        if not page:
            break
        request_count += 1
        for item in page:
            ts = int(item[0])
            if ts >= START_MS and (END_EXCLUSIVE_MS is None or ts < END_EXCLUSIVE_MS):
                rows[ts] = {
                    "timestamp_ms": ts,
                    "utc": utc_iso(ts),
                    "open": item[1],
                    "high": item[2],
                    "low": item[3],
                    "close": item[4],
                    "volume_btc": item[5],
                    "volume_quote": item[7],
                    "confirmed": item[8],
                }
        oldest = min(int(item[0]) for item in page)
        if oldest <= START_MS:
            break
        if cursor == oldest:
            raise RuntimeError("OKX pagination cursor did not advance")
        cursor = oldest
        # Stay below the documented 20 requests / 2 seconds limit.
        time.sleep(0.11)

    ordered = [rows[key] for key in sorted(rows)]
    if not ordered:
        raise RuntimeError(f"No {bar} candles downloaded")
    with output.open("w", newline="", encoding="utf-8") as handle:
        writer = csv.DictWriter(handle, fieldnames=list(ordered[0]))
        writer.writeheader()
        writer.writerows(ordered)
    print(f"Downloaded {len(ordered):,} {bar} candles in {request_count} requests -> {output}")
    return ordered


def download_minute_candles(output: Path) -> list[dict[str, str | int]]:
    """Download restartable monthly chunks, then aggregate them locally."""
    if output.exists():
        with output.open(newline="", encoding="utf-8") as handle:
            return list(csv.DictReader(handle))

    if END_EXCLUSIVE is None or END_EXCLUSIVE_MS is None:
        raise ValueError("Chunked minute downloads require BACKTEST_END_EXCLUSIVE")

    interval_ms = 60_000
    page_span_ms = 300 * interval_ms
    rate_lock = threading.Lock()
    next_request_at = [time.monotonic()]

    def fetch(cursor: int | None) -> list[list[str]]:
        for attempt in range(6):
            with rate_lock:
                now = time.monotonic()
                delay = max(0.0, next_request_at[0] - now)
                next_request_at[0] = max(now, next_request_at[0]) + 0.12
            if delay:
                time.sleep(delay)
            params = {"instId": SYMBOL, "bar": "1m", "limit": "300"}
            if cursor is not None:
                params["after"] = str(cursor)
            url = API + "?" + urllib.parse.urlencode(params)
            request = urllib.request.Request(url, headers={"User-Agent": "MicroGridBot-Backtest/1.0"})
            try:
                with urllib.request.urlopen(request, timeout=30) as response:
                    payload = json.load(response)
                if payload.get("code") == "0":
                    return payload.get("data", [])
            except Exception:
                if attempt == 5:
                    raise
            time.sleep(2 ** attempt)
        raise RuntimeError("unreachable")

    def next_month(value: datetime) -> datetime:
        return datetime(
            value.year + (1 if value.month == 12 else 0),
            1 if value.month == 12 else value.month + 1,
            1,
            tzinfo=timezone.utc,
        )

    chunk_dir = DATA / "chunks" / RUN_LABEL
    chunk_dir.mkdir(parents=True, exist_ok=True)
    chunks: list[tuple[int, int, Path]] = []
    chunk_start = START
    while chunk_start < END_EXCLUSIVE:
        boundary = min(next_month(chunk_start), END_EXCLUSIVE)
        start_ms = int(chunk_start.timestamp() * 1000)
        end_ms = int(boundary.timestamp() * 1000)
        name = f"{chunk_start:%Y%m%dT%H%M}_{boundary:%Y%m%dT%H%M}.csv"
        chunks.append((start_ms, end_ms, chunk_dir / name))
        chunk_start = boundary

    fieldnames = [
        "timestamp_ms", "utc", "open", "high", "low", "close",
        "volume_btc", "volume_quote", "confirmed",
    ]

    def inspect_chunk(path: Path, start_ms: int, end_ms: int) -> int:
        expected = (end_ms - start_ms) // interval_ms
        count = 0
        first: int | None = None
        previous: int | None = None
        with path.open(newline="", encoding="utf-8") as handle:
            for row in csv.DictReader(handle):
                ts = int(row["timestamp_ms"])
                if first is None:
                    first = ts
                if previous is not None and ts - previous != interval_ms:
                    raise RuntimeError(f"Gap or duplicate inside chunk {path} at {ts}")
                previous = ts
                count += 1
        if count != expected or first != start_ms or previous != end_ms - interval_ms:
            raise RuntimeError(
                f"Incomplete chunk {path}: rows={count}/{expected}, first={first}, last={previous}"
            )
        return count

    total_pages = 0
    for chunk_number, (chunk_start_ms, chunk_end_ms, chunk_path) in enumerate(chunks, start=1):
        if chunk_path.exists():
            rows_in_chunk = inspect_chunk(chunk_path, chunk_start_ms, chunk_end_ms)
            print(
                f"Chunk {chunk_number}/{len(chunks)} cached and valid: "
                f"{chunk_path.name} ({rows_in_chunk:,} rows)",
                flush=True,
            )
            continue

        cursors = list(range(chunk_end_ms, chunk_start_ms, -page_span_ms))
        total_pages += len(cursors)
        rows: dict[int, dict[str, str | int]] = {}
        with concurrent.futures.ThreadPoolExecutor(max_workers=10) as pool:
            for page in pool.map(fetch, cursors):
                for item in page:
                    ts = int(item[0])
                    if chunk_start_ms <= ts < chunk_end_ms:
                        rows[ts] = {
                            "timestamp_ms": ts,
                            "utc": utc_iso(ts),
                            "open": item[1],
                            "high": item[2],
                            "low": item[3],
                            "close": item[4],
                            "volume_btc": item[5],
                            "volume_quote": item[7],
                            "confirmed": item[8],
                        }
        ordered = [rows[key] for key in sorted(rows)]
        temporary = chunk_path.with_suffix(".csv.tmp")
        with temporary.open("w", newline="", encoding="utf-8") as handle:
            writer = csv.DictWriter(handle, fieldnames=fieldnames)
            writer.writeheader()
            writer.writerows(ordered)
        temporary.replace(chunk_path)
        rows_in_chunk = inspect_chunk(chunk_path, chunk_start_ms, chunk_end_ms)
        print(
            f"Chunk {chunk_number}/{len(chunks)} downloaded: "
            f"{chunk_path.name} ({rows_in_chunk:,} rows, {len(cursors)} pages)",
            flush=True,
        )

    aggregate_temp = output.with_suffix(".csv.tmp")
    aggregate_count = 0
    with aggregate_temp.open("w", newline="", encoding="utf-8") as destination:
        writer = csv.DictWriter(destination, fieldnames=fieldnames)
        writer.writeheader()
        for chunk_start_ms, chunk_end_ms, chunk_path in chunks:
            inspect_chunk(chunk_path, chunk_start_ms, chunk_end_ms)
            with chunk_path.open(newline="", encoding="utf-8") as source:
                for row in csv.DictReader(source):
                    writer.writerow(row)
                    aggregate_count += 1
    aggregate_temp.replace(output)
    expected_total = (END_EXCLUSIVE_MS - START_MS) // interval_ms
    if aggregate_count != expected_total:
        raise RuntimeError(f"Aggregate row mismatch: {aggregate_count}/{expected_total}")
    print(
        f"Aggregated {len(chunks)} validated chunks and {aggregate_count:,} candles -> {output}",
        flush=True,
    )
    with output.open(newline="", encoding="utf-8") as handle:
        return list(csv.DictReader(handle))


def floor_step(value: Decimal, step: Decimal) -> Decimal:
    return (value / step).to_integral_value(rounding=ROUND_FLOOR) * step


def ceil_step(value: Decimal, step: Decimal) -> Decimal:
    return (value / step).to_integral_value(rounding=ROUND_CEILING) * step


@dataclass
class Order:
    side: str
    price: Decimal
    qty: Decimal


class Simulation:
    def __init__(
        self,
        initial_price: Decimal,
        bootstrap_btc: bool,
        path: str,
        corrected_inverse: bool = False,
        compound: bool = False,
        recenter_threshold: Decimal | None = None,
    ):
        self.path = path
        self.corrected_inverse = corrected_inverse
        self.compound = compound
        self.recenter_threshold = recenter_threshold
        self.center_price = initial_price
        self.recenter_count = 0
        self.rebalance_fees = Decimal(0)
        self.first_recenter_timestamp_ms: int | None = None
        self.last_recenter_timestamp_ms: int | None = None
        self.usdt = STARTING_USDT
        self.btc = Decimal(0)
        self.average_cost = Decimal(0)
        self.realized_pnl = Decimal(0)
        self.fees = Decimal(0)
        self.buy_fills = 0
        self.sell_fills = 0
        self.rejected_min_qty = 0
        self.compounded_replacements = 0
        self.min_compounded_notional: Decimal | None = None
        self.max_compounded_notional: Decimal | None = None
        self.current_timestamp_ms: int | None = None
        self.first_fill_timestamp_ms: int | None = None
        self.last_fill_timestamp_ms: int | None = None
        self.allow_buy = True
        self.orders: list[Order] = []
        self.min_equity = STARTING_USDT
        self.max_equity = STARTING_USDT
        self.max_exposure = Decimal(0)

        order_notional = STARTING_USDT * ACTIVE_PCT / Decimal(LEVELS)
        if corrected_inverse:
            buy_prices = [
                floor_step(initial_price / ((Decimal(1) + EFFECTIVE_SPACING) ** i), TICK)
                for i in range(BUY_LEVELS, 0, -1)
            ]
        else:
            buy_prices = [
                floor_step(initial_price * ((Decimal(1) - EFFECTIVE_SPACING) ** i), TICK)
                for i in range(BUY_LEVELS, 0, -1)
            ]
        sell_prices = [
            ceil_step(initial_price * ((Decimal(1) + EFFECTIVE_SPACING) ** i), TICK)
            for i in range(1, SELL_LEVELS + 1)
        ]
        for price in buy_prices:
            self._append_order("buy", price, floor_step(order_notional / price, LOT))

        if bootstrap_btc:
            sell_orders = [Order("sell", price, floor_step(order_notional / price, LOT)) for price in sell_prices]
            required_btc = sum((order.qty for order in sell_orders), Decimal(0))
            # A market/taker bootstrap is required because a USDT-only deposit contains no BTC.
            bootstrap_notional = required_btc * initial_price
            bootstrap_fee = bootstrap_notional * TAKER_FEE
            self.usdt -= bootstrap_notional + bootstrap_fee
            self.btc += required_btc
            self.average_cost = (bootstrap_notional + bootstrap_fee) / required_btc
            self.fees += bootstrap_fee
            self.orders.extend(sell_orders)

    def _recenter(self, price: Decimal) -> None:
        """Cancel/rebuild around a closing price and restore the 65/35 allocation envelope."""
        equity = self.usdt + self.btc * price
        order_notional = equity * ACTIVE_PCT / Decimal(LEVELS)
        buy_prices = [
            floor_step(price / ((Decimal(1) + EFFECTIVE_SPACING) ** i), TICK)
            for i in range(BUY_LEVELS, 0, -1)
        ]
        sell_prices = [
            ceil_step(price * ((Decimal(1) + EFFECTIVE_SPACING) ** i), TICK)
            for i in range(1, SELL_LEVELS + 1)
        ]
        buy_orders = [Order("buy", level, floor_step(order_notional / level, LOT)) for level in buy_prices]
        sell_orders = [Order("sell", level, floor_step(order_notional / level, LOT)) for level in sell_prices]
        target_btc = sum((order.qty for order in sell_orders), Decimal(0))
        delta = target_btc - self.btc
        if delta > 0:
            notional = delta * price
            fee = notional * TAKER_FEE
            total_cost = notional + fee
            if total_cost > self.usdt:
                raise RuntimeError("Recenter BTC bootstrap exceeds available USDT")
            old_cost = self.average_cost * self.btc
            self.usdt -= total_cost
            self.btc += delta
            self.average_cost = (old_cost + total_cost) / self.btc
            self.fees += fee
            self.rebalance_fees += fee
        elif delta < 0:
            qty = -delta
            notional = qty * price
            fee = notional * TAKER_FEE
            net = notional - fee
            self.realized_pnl += net - self.average_cost * qty
            self.usdt += net
            self.btc -= qty
            if self.btc == 0:
                self.average_cost = Decimal(0)
            self.fees += fee
            self.rebalance_fees += fee

        self.orders = []
        for order in buy_orders + sell_orders:
            self._append_order(order.side, order.price, order.qty)
        self.center_price = price
        self.allow_buy = True
        self.recenter_count += 1
        if self.current_timestamp_ms is not None:
            if self.first_recenter_timestamp_ms is None:
                self.first_recenter_timestamp_ms = self.current_timestamp_ms
            self.last_recenter_timestamp_ms = self.current_timestamp_ms
        self._mark(price)

    def _append_order(self, side: str, price: Decimal, qty: Decimal) -> None:
        if qty < MIN_QTY:
            self.rejected_min_qty += 1
            return
        self.orders.append(Order(side, price, qty))

    def _mark(self, price: Decimal) -> None:
        equity = self.usdt + self.btc * price
        exposure = Decimal(0) if equity <= 0 else self.btc * price / equity
        self.min_equity = min(self.min_equity, equity)
        self.max_equity = max(self.max_equity, equity)
        self.max_exposure = max(self.max_exposure, exposure)
        if self.allow_buy and exposure >= MAX_EXPOSURE:
            self.allow_buy = False
        elif not self.allow_buy and exposure <= RESUME_EXPOSURE:
            self.allow_buy = True

    def _fill(self, order: Order) -> None:
        if self.current_timestamp_ms is not None:
            if self.first_fill_timestamp_ms is None:
                self.first_fill_timestamp_ms = self.current_timestamp_ms
            self.last_fill_timestamp_ms = self.current_timestamp_ms
        notional = order.price * order.qty
        fee = notional * MAKER_FEE
        self.fees += fee
        if order.side == "buy":
            total_cost = notional + fee
            if total_cost > self.usdt:
                raise RuntimeError("Simulation buy exceeds available USDT")
            old_cost = self.average_cost * self.btc
            self.usdt -= total_cost
            self.btc += order.qty
            self.average_cost = (old_cost + total_cost) / self.btc
            self.buy_fills += 1
            replacement = ceil_step(order.price * (Decimal(1) + EFFECTIVE_SPACING), TICK)
            self._append_order("sell", replacement, order.qty)
        else:
            if order.qty > self.btc + LOT / 2:
                raise RuntimeError("Simulation attempted to sell more BTC than held")
            net = notional - fee
            self.realized_pnl += net - self.average_cost * order.qty
            self.usdt += net
            self.btc -= order.qty
            if self.btc == 0:
                self.average_cost = Decimal(0)
            self.sell_fills += 1
            if self.allow_buy:
                if self.corrected_inverse:
                    replacement = floor_step(order.price / (Decimal(1) + EFFECTIVE_SPACING), TICK)
                else:
                    # Mirrors GeometricGrid.BuyForSell, including its downward drift.
                    replacement = floor_step(order.price * (Decimal(1) - EFFECTIVE_SPACING), TICK)
                if self.compound:
                    current_equity = self.usdt + self.btc * order.price
                    next_notional = current_equity * ACTIVE_PCT / Decimal(LEVELS)
                    next_qty = floor_step(next_notional / replacement, LOT)
                    actual_notional = next_qty * replacement
                    self.compounded_replacements += 1
                    self.min_compounded_notional = (
                        actual_notional
                        if self.min_compounded_notional is None
                        else min(self.min_compounded_notional, actual_notional)
                    )
                    self.max_compounded_notional = (
                        actual_notional
                        if self.max_compounded_notional is None
                        else max(self.max_compounded_notional, actual_notional)
                    )
                else:
                    next_qty = order.qty
                self._append_order("buy", replacement, next_qty)

    def move(self, start: Decimal, end: Decimal) -> None:
        if end == start:
            self._mark(end)
            return
        side = "sell" if end > start else "buy"
        if side == "sell":
            crossed = sorted(
                (order for order in self.orders if order.side == side and start < order.price <= end),
                key=lambda order: order.price,
            )
        else:
            crossed = sorted(
                (order for order in self.orders if order.side == side and end <= order.price < start),
                key=lambda order: order.price,
                reverse=True,
            )
        # Snapshot means replacement orders cannot fill on the same monotonic price segment.
        for order in crossed:
            self.orders.remove(order)
            self._mark(order.price)
            self._fill(order)
        self._mark(end)

    def run(self, candles: list[dict[str, str | int]]) -> dict[str, str | int | bool]:
        previous = Decimal(str(candles[0]["open"]))
        for candle in candles:
            self.current_timestamp_ms = int(candle["timestamp_ms"])
            opened = Decimal(str(candle["open"]))
            high = Decimal(str(candle["high"]))
            low = Decimal(str(candle["low"]))
            close = Decimal(str(candle["close"]))
            self.move(previous, opened)
            points = [opened, low, high, close] if self.path == "OLHC" else [opened, high, low, close]
            for start, end in zip(points, points[1:]):
                self.move(start, end)
            if self.recenter_threshold is not None and (
                close >= self.center_price * (Decimal(1) + self.recenter_threshold)
                or close <= self.center_price * (Decimal(1) - self.recenter_threshold)
            ):
                self._recenter(close)
            previous = close
        final_price = Decimal(str(candles[-1]["close"]))
        final_equity = self.usdt + self.btc * final_price
        return {
            "path": self.path,
            "corrected_inverse": self.corrected_inverse,
            "compounding": self.compound,
            "recenter_threshold": (
                None if self.recenter_threshold is None else str(self.recenter_threshold)
            ),
            "recenter_count": self.recenter_count,
            "rebalance_fees_usdt": str(self.rebalance_fees.quantize(Decimal("0.000001"))),
            "final_equity_usdt": str(final_equity.quantize(Decimal("0.000001"))),
            "return_pct": str(((final_equity / STARTING_USDT - 1) * 100).quantize(Decimal("0.0001"))),
            "usdt": str(self.usdt.quantize(Decimal("0.000001"))),
            "btc": str(self.btc.quantize(Decimal("0.00000001"))),
            "realized_pnl_usdt": str(self.realized_pnl.quantize(Decimal("0.000001"))),
            "fees_usdt": str(self.fees.quantize(Decimal("0.000001"))),
            "buy_fills": self.buy_fills,
            "sell_fills": self.sell_fills,
            "open_orders": len(self.orders),
            "min_equity_usdt": str(self.min_equity.quantize(Decimal("0.000001"))),
            "max_equity_usdt": str(self.max_equity.quantize(Decimal("0.000001"))),
            "max_btc_exposure_pct": str((self.max_exposure * 100).quantize(Decimal("0.0001"))),
            "negative_cash": self.usdt < 0,
            "min_qty_rejections": self.rejected_min_qty,
            "compounded_replacements": self.compounded_replacements,
            "min_compounded_order_usdt": (
                None if self.min_compounded_notional is None else str(self.min_compounded_notional.quantize(Decimal("0.000001")))
            ),
            "max_compounded_order_usdt": (
                None if self.max_compounded_notional is None else str(self.max_compounded_notional.quantize(Decimal("0.000001")))
            ),
            "first_fill_utc": (
                None if self.first_fill_timestamp_ms is None else utc_iso(self.first_fill_timestamp_ms)
            ),
            "last_fill_utc": (
                None if self.last_fill_timestamp_ms is None else utc_iso(self.last_fill_timestamp_ms)
            ),
            "first_recenter_utc": (
                None if self.first_recenter_timestamp_ms is None else utc_iso(self.first_recenter_timestamp_ms)
            ),
            "last_recenter_utc": (
                None if self.last_recenter_timestamp_ms is None else utc_iso(self.last_recenter_timestamp_ms)
            ),
        }


def validate_candles(candles: list[dict[str, str | int]], interval_ms: int) -> dict[str, int | str]:
    timestamps = [int(row["timestamp_ms"]) for row in candles]
    duplicates = len(timestamps) - len(set(timestamps))
    gaps = sum(1 for left, right in zip(timestamps, timestamps[1:]) if right - left != interval_ms)
    invalid_ohlc = sum(
        1
        for row in candles
        if not (
            Decimal(str(row["low"])) <= min(Decimal(str(row["open"])), Decimal(str(row["close"])))
            <= max(Decimal(str(row["open"])), Decimal(str(row["close"]))) <= Decimal(str(row["high"]))
        )
    )
    return {
        "rows": len(candles),
        "first_utc": str(candles[0]["utc"]),
        "last_utc": str(candles[-1]["utc"]),
        "duplicate_timestamps": duplicates,
        "interval_gaps": gaps,
        "invalid_ohlc_rows": invalid_ohlc,
        "unconfirmed_rows": sum(1 for row in candles if str(row["confirmed"]) != "1"),
    }


def main() -> None:
    daily = download_candles("1Dutc", DATA / f"okx_btc_usdt_1d_{DATA_LABEL}.csv")
    minute = download_minute_candles(DATA / f"okx_btc_usdt_1m_{DATA_LABEL}.csv")
    initial_price = Decimal(str(minute[0]["open"]))
    final_price = Decimal(str(minute[-1]["close"]))

    scenarios = {}
    if not ONLY_RECENTER:
        for bootstrap_name, bootstrap in (("usdt_only", False), ("full_grid_bootstrap", True)):
            for path in ("OLHC", "OHLC"):
                key = f"{bootstrap_name}_{path.lower()}"
                scenarios[key] = Simulation(initial_price, bootstrap, path).run(minute)
        for path in ("OLHC", "OHLC"):
            key = f"corrected_inverse_full_grid_{path.lower()}"
            scenarios[key] = Simulation(initial_price, True, path, corrected_inverse=True).run(minute)
            key = f"compounding_corrected_full_grid_{path.lower()}"
            scenarios[key] = Simulation(
                initial_price, True, path, corrected_inverse=True, compound=True
            ).run(minute)
    for threshold in RECENTER_THRESHOLDS:
        threshold_label = str((threshold * 100).normalize()).replace(".", "p")
        for path in ("OLHC", "OHLC"):
            key = f"adaptive_recenter_{threshold_label}pct_{path.lower()}"
            scenarios[key] = Simulation(
                initial_price,
                True,
                path,
                corrected_inverse=True,
                compound=True,
                recenter_threshold=threshold,
            ).run(minute)

    buy_hold_btc = (STARTING_USDT / (Decimal(1) + TAKER_FEE)) / initial_price
    buy_hold_final = buy_hold_btc * final_price
    results = {
        "generated_at_utc": datetime.now(timezone.utc).isoformat(),
        "source": API,
        "symbol": SYMBOL,
        "period": {"start": minute[0]["utc"], "end": minute[-1]["utc"]},
        "parameters": {
            "starting_usdt": str(STARTING_USDT),
            "maker_fee": str(MAKER_FEE),
            "taker_fee": str(TAKER_FEE),
            "configured_spacing": str(CONFIGURED_SPACING),
            "effective_spacing": str(EFFECTIVE_SPACING),
            "levels": LEVELS,
            "buy_levels": BUY_LEVELS,
            "sell_levels": SELL_LEVELS,
            "active_pct": str(ACTIVE_PCT),
            "tick_size": str(TICK),
            "lot_size": str(LOT),
            "min_qty": str(MIN_QTY),
        },
        "prices": {
            "initial": str(initial_price),
            "final": str(final_price),
            "change_pct": str(((final_price / initial_price - 1) * 100).quantize(Decimal("0.0001"))),
        },
        "data_quality": {
            "daily": validate_candles(daily, 86_400_000),
            "minute": validate_candles(minute, 60_000),
        },
        "benchmarks": {
            "cash_final_usdt": str(STARTING_USDT.quantize(Decimal("0.000001"))),
            "buy_hold_final_usdt": str(buy_hold_final.quantize(Decimal("0.000001"))),
        },
        "scenarios": scenarios,
        "limitations": [
            "Repository has no trading engine; these are proxy strategy simulations.",
            "A candle price crossing is assumed to fill the entire maker order; queue position is unknown.",
            "Minute OHLC ordering is unknown, so both OLHC and OHLC paths are reported.",
            "No spread, slippage, latency, rejects, disconnects, partial fills, or tax are modeled.",
            "Adaptive scenarios recenter only after a completed minute close crosses the configured threshold.",
            "Recenter cancellation, inventory rebalance, and replacement are modeled atomically; live exchange races are excluded.",
            "Compounding resizes the next buy after each completed sell using current equity * active percentage / levels; it does not rebuild all open orders.",
            "Fees are modeled in quote currency, matching InventoryLedger rather than reconstructing OKX fee-currency rules.",
        ],
    }
    output = RESULTS / f"{RUN_LABEL}.json"
    output.write_text(json.dumps(results, indent=2), encoding="utf-8")
    print(json.dumps(results, indent=2))
    print(f"Results -> {output}")


if __name__ == "__main__":
    main()
