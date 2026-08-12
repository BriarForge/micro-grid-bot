import importlib.util
import sys
import unittest
from decimal import Decimal
from pathlib import Path


MODULE_PATH = Path(__file__).resolve().parents[1] / "grid_backtest.py"
SPEC = importlib.util.spec_from_file_location("grid_backtest", MODULE_PATH)
assert SPEC and SPEC.loader
backtest = importlib.util.module_from_spec(SPEC)
sys.modules[SPEC.name] = backtest
SPEC.loader.exec_module(backtest)


class GridBacktestTests(unittest.TestCase):
    def setUp(self):
        self.original_starting_usdt = backtest.STARTING_USDT
        backtest.STARTING_USDT = Decimal("10000")

    def tearDown(self):
        backtest.STARTING_USDT = self.original_starting_usdt

    def test_corrected_grid_uses_exact_inverse_ratio(self):
        simulation = backtest.Simulation(
            Decimal("100000"), bootstrap_btc=True, path="OHLC", corrected_inverse=True
        )
        closest_buy = max(order.price for order in simulation.orders if order.side == "buy")
        first_sell = min(order.price for order in simulation.orders if order.side == "sell")

        self.assertEqual(closest_buy, backtest.floor_step(Decimal("100000") / Decimal("1.0032"), backtest.TICK))
        self.assertEqual(first_sell, backtest.ceil_step(Decimal("100000") * Decimal("1.0032"), backtest.TICK))

    def test_compounding_resizes_replacement_buy_from_current_equity(self):
        simulation = backtest.Simulation(
            Decimal("100000"), bootstrap_btc=True, path="OHLC", corrected_inverse=True, compound=True
        )
        sell = min((order for order in simulation.orders if order.side == "sell"), key=lambda order: order.price)
        simulation.orders.remove(sell)
        simulation._fill(sell)
        expected_price = backtest.floor_step(sell.price / Decimal("1.0032"), backtest.TICK)
        replacement = next(order for order in simulation.orders if order.side == "buy" and order.price == expected_price)
        equity = simulation.usdt + simulation.btc * sell.price
        expected_qty = backtest.floor_step(
            (equity * backtest.ACTIVE_PCT / Decimal(backtest.LEVELS)) / expected_price,
            backtest.LOT,
        )

        self.assertEqual(replacement.qty, expected_qty)
        self.assertEqual(simulation.compounded_replacements, 1)

    def test_recenter_preserves_equity_except_taker_fee_and_rebuilds_25_orders(self):
        simulation = backtest.Simulation(
            Decimal("100000"),
            bootstrap_btc=True,
            path="OHLC",
            corrected_inverse=True,
            compound=True,
            recenter_threshold=Decimal("0.40"),
        )
        price = Decimal("140000")
        equity_before = simulation.usdt + simulation.btc * price
        fees_before = simulation.fees
        simulation._recenter(price)
        equity_after = simulation.usdt + simulation.btc * price
        new_fee = simulation.fees - fees_before

        self.assertEqual(len(simulation.orders), backtest.LEVELS)
        self.assertEqual(simulation.recenter_count, 1)
        self.assertEqual(equity_before - equity_after, new_fee)
        self.assertLess(simulation.btc * price / equity_after, backtest.MAX_EXPOSURE)
        self.assertGreaterEqual(simulation.usdt, Decimal(0))


if __name__ == "__main__":
    unittest.main()
