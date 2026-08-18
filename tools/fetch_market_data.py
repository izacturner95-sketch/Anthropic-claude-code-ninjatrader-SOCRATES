#!/usr/bin/env python3
"""Download VIX and leader data for steps 5 and 6, ready for the strategy to read.

Writes one CSV per symbol in the format FileSeries parses directly:

    <epoch seconds>,<open>,<high>,<low>,<close>

Epoch on purpose. Every other timestamp format carries a time zone question, and
a file an hour out loads perfectly and answers nothing - which is the single
most likely way this goes wrong. Epoch has no such question.

Requires yfinance:

    pip install yfinance

Usage
-----
    python3 fetch_market_data.py --out "C:/NinjaTrader 8/SocratesData"

    python3 fetch_market_data.py --out ./data --symbols ^VIX
    python3 fetch_market_data.py --out ./data --interval 1d --period 5y

The free intraday window is the binding constraint, not this script. Yahoo serves
roughly 60 days of 5-minute bars and 7 days of 1-minute, whatever you ask for.
For a longer backtest use --interval 1d, and set the strategy's bar-minutes
parameter to 1440 to match - which changes step 5's question from "is the VIX
moving inversely right now" to "is it down on the day". A weaker signal, but a
testable one, and testable beats unmeasured.
"""

import argparse
import os
import sys
from collections import Counter

# The Magnificent 7, plus the VIX. Order matches the strategy's default
# 'Leader symbols' so the file list can be pasted in the same order.
LEADERS = ["AAPL", "MSFT", "NVDA", "AMZN", "META", "GOOGL", "TSLA"]
DEFAULT_SYMBOLS = ["^VIX"] + LEADERS


def safe_name(symbol):
    """A filename NinjaTrader's parameter field will not fight you over."""
    return symbol.replace("^", "").replace("/", "-").replace("=", "-")


def fetch(symbol, interval, period, prepost):
    import yfinance as yf

    frame = yf.download(
        symbol,
        interval=interval,
        period=period,
        prepost=prepost,
        auto_adjust=False,
        progress=False,
    )

    if frame is None or frame.empty:
        return None

    # Multiple tickers give a column MultiIndex; one ticker sometimes does too,
    # depending on the version. Flatten either way rather than special-casing.
    if hasattr(frame.columns, "nlevels") and frame.columns.nlevels > 1:
        frame.columns = frame.columns.get_level_values(0)

    return frame


def write(frame, path):
    """Rows sorted ascending, incomplete ones dropped, epoch seconds throughout."""
    written = 0
    hours = Counter()

    with open(path, "w", newline="") as handle:
        handle.write("# epoch,open,high,low,close\n")

        for stamp, row in frame.sort_index().iterrows():
            values = []
            ok = True

            for column in ("Open", "High", "Low", "Close"):
                value = row.get(column)

                # A bar with a gap in it is worse than a missing bar: it becomes a
                # reading the confirmations treat as real.
                if value is None or value != value:
                    ok = False
                    break

                values.append(float(value))

            if not ok:
                continue

            epoch = int(stamp.timestamp())
            handle.write("%d,%s\n" % (epoch, ",".join("%.4f" % v for v in values)))

            written += 1
            hours[stamp.hour] += 1

    return written, hours


def main(argv=None):
    parser = argparse.ArgumentParser(
        description="Download VIX and leader bars for Socrates steps 5 and 6.")
    parser.add_argument("--out", required=True, metavar="DIR",
                        help="directory to write the CSVs into")
    parser.add_argument("--symbols", nargs="+", default=DEFAULT_SYMBOLS,
                        help="symbols to fetch (default: ^VIX and the Magnificent 7)")
    parser.add_argument("--interval", default="5m",
                        help="bar size: 1m, 5m, 15m, 1h, 1d (default 5m)")
    parser.add_argument("--period", default="60d",
                        help="how far back: 7d, 60d, 1y, 5y, max (default 60d, "
                             "which is as much 5-minute history as Yahoo serves)")
    parser.add_argument("--no-prepost", action="store_true",
                        help="regular hours only. Off by default: step 6 wants "
                             "extended hours, or it is blind outside 09:30-16:00")
    args = parser.parse_args(argv)

    try:
        import yfinance  # noqa: F401
    except ImportError:
        sys.stderr.write("error: yfinance is not installed. Run: pip install yfinance\n")
        return 1

    if not os.path.isdir(args.out):
        os.makedirs(args.out)

    failures = 0

    for symbol in args.symbols:
        path = os.path.join(args.out, safe_name(symbol) + ".csv")

        try:
            frame = fetch(symbol, args.interval, args.period, not args.no_prepost)
        except Exception as error:
            sys.stderr.write("%-8s FAILED: %s\n" % (symbol, error))
            failures += 1
            continue

        if frame is None:
            sys.stderr.write("%-8s no data returned\n" % symbol)
            failures += 1
            continue

        written, hours = write(frame, path)

        if written == 0:
            sys.stderr.write("%-8s no usable rows\n" % symbol)
            failures += 1
            continue

        span = "%s to %s" % (frame.index[0].strftime("%Y-%m-%d"),
                             frame.index[-1].strftime("%Y-%m-%d"))
        sys.stderr.write("%-8s %6d rows  %s  -> %s\n" % (symbol, written, span, path))

        # The check that matters for step 6. Regular hours are 09:30-16:00, so
        # anything before 09:00 or from 16:00 on is extended-hours data. A source
        # that ignored the request looks identical to a correct one without this.
        if not args.no_prepost and args.interval.endswith(("m", "h")):
            extended = sum(n for hour, n in hours.items() if hour < 9 or hour >= 16)

            if extended == 0:
                sys.stderr.write("         NOTE: nothing outside 09:00-16:00 - "
                                 "this is regular hours only.\n")

    if failures:
        sys.stderr.write("\n%d symbol(s) failed.\n" % failures)
        return 1

    return 0


if __name__ == "__main__":
    sys.exit(main())
