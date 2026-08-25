# Getting market data into Socrates

`fetch_market_data.py` downloads bars from Yahoo Finance and writes them as CSV files the
strategy reads directly (steps 5 and 6's `Vix file` / `Leader files` parameters). This is
the whole guide, from a clean Windows machine to usable files.

## One-time setup

**1. Install Python** — from [python.org/downloads](https://www.python.org/downloads/).
Run the installer and **tick "Add python.exe to PATH"** on the first screen; everything
below assumes it.

**2. Install yfinance.** Open a command prompt (press the Windows key, type `cmd`, Enter)
and run:

```
pip install yfinance
```

**3. Check it took:**

```
python --version
pip show yfinance
```

Both should print version numbers, not errors. That's the setup done — nothing else to
install, ever.

## The easy way: double-click

Double-click `fetch_market_data.bat`. It asks three questions, each with a default you can
accept by pressing Enter:

1. **Where to put the files** — defaults to `Documents\NinjaTrader 8\SocratesData`, which
   is where the strategy templates already point.
2. **Which tickers** — defaults to `^VIX` and the Magnificent 7. Type your own separated
   by spaces, e.g. `^VIX NVDA TSLA`. (Bare `VIX` is auto-corrected to `^VIX` — they are
   different Yahoo symbols and only the caret one is the index.)
3. **The date range** — as `YYYY-MM-DD YYYY-MM-DD`, e.g. `2026-06-01 2026-08-20`.
   Enter alone takes the last 60 days. Giving only a start date runs to today.

It prints one line per symbol — row count, date span, file path — and warns if a download
came back regular-hours-only, or if the range asks for more than Yahoo serves.

## The command-prompt way

```
python fetch_market_data.py --out "C:\Users\you\Documents\NinjaTrader 8\SocratesData" --start 2026-06-01 --end 2026-08-20
```

Useful variations:

| What | Command |
|---|---|
| Specific tickers | `--symbols ^VIX NVDA TSLA` |
| Only the VIX | `--symbols ^VIX` |
| Daily bars, years of them | `--interval 1d --period 5y` |
| Hourly, up to ~2 years | `--interval 1h --start 2025-01-01` |
| Regular hours only | `--no-prepost` |

Options combine, so "NVDA and TSLA, 5-minute, June through August" is:

```
python fetch_market_data.py --out ... --symbols NVDA TSLA --start 2026-06-01 --end 2026-08-20
```

## How far back Yahoo goes (free)

| Bar size | Reach |
|---|---|
| 1m | ~30 days |
| 5m, 15m, 30m | ~60 days |
| 1h | ~730 days |
| 1d | decades |

Asking for more comes back empty. The script warns before requesting when a range exceeds
the reach for its interval — if you need older history, drop to `--interval 1d`.

## Checking what a file actually covers

A download that succeeds is not a download without holes:

```
python check_file_series.py "C:\Users\you\Documents\NinjaTrader 8\SocratesData"
```

(or double-click `check_file_series.bat`). It reports the date span, weekdays with no
data, and every gap longer than the strategy's freshness window — pass `--fresh 15` to
match `Vix max data age (minutes)`. Gaps are minutes where step 5 reads the source as
quiet.

## The file format, for reference

```
# epoch,open,high,low,close
1755088200,21998.7500,22004.2500,21991.5000,21999.0000
```

Epoch seconds on purpose: every other timestamp format carries a time-zone question, and a
file an hour off loads perfectly and answers nothing. `FileSeries` also accepts ISO-8601
and NinjaTrader's `yyyyMMdd HHmmss` export format, so files converted with
`to_ninjatrader_csv.py` load too.

## Remember what these files are

A file is a snapshot. In a backtest it behaves exactly like live data; forward, it ages
from the moment it is written, and the strategy treats rows older than the freshness
window as quiet. The live cash configuration reads no files at all — these exist for
backtesting confirmations and for anything file-based you choose to experiment with.
