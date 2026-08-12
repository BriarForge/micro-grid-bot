# Backtesting workspace

This directory preserves the reproducible evidence behind the BTC-USDT grid audit and subsequent
strategy iterations.

## Layout

- `grid_backtest.py` — parameterized OKX downloader, chunk validator, and minute-level simulator
- `tests/` — fast regression tests for grid inversion, compounding, and recenter accounting
- `reports/` — human-readable findings and limitations for each experiment
- `results/` — compact machine-readable outputs, including parameter sweeps
- `data/` — committed daily extracts; monthly chunks and aggregated minute files remain local and
  gitignored because they exceed normal source-control size

## Current finding

For 10,000 USDT from 2022-08-13 through 2025-10-07, the static compounded grid ended at
10,556–10,571 USDT. Iteration v2 added causal two-sided recentering. The best in-sample candidate,
a 40% close-based threshold, ended at 13,990–14,045 USDT. The threshold response was unstable, so
it is a walk-forward candidate rather than a production default. See
`reports/2022-08-13_to_2025-10-07_v2.md`.

## Run

PowerShell example using the cached multi-year minute aggregate:

```powershell
$env:BACKTEST_LABEL='2022-08-13_to_2025-10-07_v2-recheck'
$env:BACKTEST_DATA_LABEL='20220813_to_20251007'
$env:BACKTEST_START='2022-08-13T00:00:00+00:00'
$env:BACKTEST_END_EXCLUSIVE='2025-10-08T00:00:00+00:00'
$env:BACKTEST_STARTING_USDT='10000'
$env:BACKTEST_ONLY_RECENTER='true'
$env:BACKTEST_RECENTER_THRESHOLDS='0.35,0.40,0.45'
python analysis/grid_backtest.py
```

If the aggregate is absent, the runner downloads restartable monthly chunks, validates every
boundary, and aggregates them locally before simulating.

## Validate

```powershell
python -m unittest discover -s analysis/tests -v
dotnet test MicroGrid.sln -c Release
```

