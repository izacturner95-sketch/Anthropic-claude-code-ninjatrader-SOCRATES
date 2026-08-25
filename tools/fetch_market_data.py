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
from datetime import datetime, timedelta
from collections import Counter

# The Magnificent 7, plus the VIX. Order matches the strategy's default
# 'Leader symbols' so the file list can be pasted in the same order.
LEADERS = ["AAPL", "MSFT", "NVDA", "AMZN", "META", "GOOGL", "TSLA"]
DEFAULT_SYMBOLS = ["^VIX"] + LEADERS


def safe_name(symbol):
    """A filename NinjaTrader's parameter field will not fight you over."""
    return symbol.replace("^", "").replace("/", "-").replace("=", "-")


def fetch(symbol, interval, period, start, end, prepost):
    import yfinance as yf

    # A date range and a period are alternatives; the range wins when given.
    # yfinance treats end as exclusive, so a day is added upstream to make the
    # dates in the prompt mean what a person expects them to mean.
    kwargs = dict(
        interval=interval,
        prepost=prepost,
        auto_adjust=False,
        progress=False,
    )

    if start:
        kwargs["start"] = start
        kwargs["end"] = end
    else:
        kwargs["period"] = period

    frame = yf.download(symbol, **kwargs)

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


# How far back Yahoo serves each bar size for free. Requests beyond this come
# back empty, which without a warning is indistinguishable from a typo.
INTRADAY_REACH_DAYS = {
    "1m": 30, "2m": 60, "5m": 60, "15m": 60, "30m": 60,
    "90m": 60, "60m": 730, "1h": 730,
}


def parse_day(text):
    """YYYY-MM-DD, or None if it is not one."""
    try:
        return datetime.strptime(text.strip(), "%Y-%m-%d")
    except (ValueError, AttributeError):
        return None


def check_reach(interval, start):
    """Say up front when the range asks for more than Yahoo serves."""
    reach = INTRADAY_REACH_DAYS.get(interval)

    if reach is None or start is None:
        return

    days_back = (datetime.now() - start).days

    if days_back > reach:
        sys.stderr.write(
            "WARNING: %s bars only reach back about %d days on Yahoo, and %s is %d days ago.\n"
            "         Expect missing data at the start of the range. For older history use\n"
            "         --interval 1d, which reaches back decades.\n"
            % (interval, reach, start.strftime("%Y-%m-%d"), days_back))


def prompt_for_range():
    """Ask for dates when double-clicked. Enter keeps the last-60-days default."""
    sys.stderr.write("\nDate range, as YYYY-MM-DD YYYY-MM-DD (for example 2026-06-01 2026-08-20).\n")
    sys.stderr.write("Press Enter for the default: the last 60 days.\n> ")
    sys.stderr.flush()

    try:
        typed = sys.stdin.readline().strip()
    except (EOFError, KeyboardInterrupt):
        return None, None

    if not typed:
        return None, None

    parts = typed.replace(" to ", " ").split()
    start = parse_day(parts[0]) if len(parts) >= 1 else None
    end = parse_day(parts[1]) if len(parts) >= 2 else None

    if start is None:
        sys.stderr.write("Could not read a date from %r - using the last 60 days instead.\n" % typed)
        return None, None

    return start, end


def prompt_for_symbols():
    """Ask which tickers to fetch when double-clicked. Enter keeps the default set."""
    sys.stderr.write("\nWhich tickers? Separate with spaces (for example: ^VIX NVDA TSLA).\n")
    sys.stderr.write("Press Enter for the default: %s\n> " % " ".join(DEFAULT_SYMBOLS))
    sys.stderr.flush()

    try:
        typed = sys.stdin.readline().strip()
    except (EOFError, KeyboardInterrupt):
        return DEFAULT_SYMBOLS

    if not typed:
        return DEFAULT_SYMBOLS

    return typed.replace(",", " ").upper().split()


def default_out():
    """Where a NinjaTrader user most likely wants these, offered as the default."""
    return os.path.join(os.path.expanduser("~"), "Documents", "NinjaTrader 8", "SocratesData")


def prompt_for_out():
    """Ask, when the script was launched without arguments - a double-click, usually."""
    suggestion = default_out()

    sys.stderr.write("\nWhere should the CSV files go?\n")
    sys.stderr.write("Press Enter for: %s\n> " % suggestion)
    sys.stderr.flush()

    try:
        typed = sys.stdin.readline().strip().strip('"')
    except (EOFError, KeyboardInterrupt):
        return None

    return typed or suggestion


def main(argv=None):
    parser = argparse.ArgumentParser(
        description="Download VIX and leader bars for Socrates steps 5 and 6.")
    # Not required, so that double-clicking the file on Windows reaches the prompt
    # below instead of dying in argparse and taking the console window with it.
    parser.add_argument("--out", metavar="DIR",
                        help="directory to write the CSVs into; prompted for if omitted")
    parser.add_argument("--symbols", nargs="+", default=DEFAULT_SYMBOLS,
                        help="symbols to fetch (default: ^VIX and the Magnificent 7)")
    parser.add_argument("--interval", default="5m",
                        help="bar size: 1m, 5m, 15m, 1h, 1d (default 5m)")
    parser.add_argument("--start", metavar="YYYY-MM-DD",
                        help="first day to fetch. With --end, an exact date range; "
                             "prompted for on a double-click")
    parser.add_argument("--end", metavar="YYYY-MM-DD",
                        help="last day to fetch, inclusive (default: today)")
    parser.add_argument("--period", default="60d",
                        help="how far back: 7d, 60d, 1y, 5y, max (default 60d, "
                             "which is as much 5-minute history as Yahoo serves)")
    parser.add_argument("--no-prepost", action="store_true",
                        help="regular hours only. Off by default: step 6 wants "
                             "extended hours, or it is blind outside 09:30-16:00")
    args = parser.parse_args(argv)

    out = args.out or prompt_for_out()

    if not out:
        sys.stderr.write("error: no output directory given.\n")
        return 1

    start = parse_day(args.start) if args.start else None
    end = parse_day(args.end) if args.end else None

    if args.start and start is None:
        sys.stderr.write("error: could not read --start %r as YYYY-MM-DD.\n" % args.start)
        return 1

    if args.end and end is None:
        sys.stderr.write("error: could not read --end %r as YYYY-MM-DD.\n" % args.end)
        return 1

    # Double-click flow: nothing on the command line, so ask for the rest too.
    if args.out is None:
        if args.symbols is parser.get_default("symbols"):
            args.symbols = prompt_for_symbols()

        if start is None:
            start, end = prompt_for_range()

    if start is not None and end is None:
        end = datetime.now()

    if start is not None and end < start:
        sys.stderr.write("error: the range runs backwards (%s to %s).\n"
                         % (start.strftime("%Y-%m-%d"), end.strftime("%Y-%m-%d")))
        return 1

    if start is not None:
        check_reach(args.interval, start)
        sys.stderr.write("Range: %s to %s, %s bars.\n"
                         % (start.strftime("%Y-%m-%d"), end.strftime("%Y-%m-%d"), args.interval))

    # yfinance treats end as exclusive; add the day back so ours is inclusive.
    start_arg = start.strftime("%Y-%m-%d") if start else None
    end_arg = (end + timedelta(days=1)).strftime("%Y-%m-%d") if start else None

    try:
        import yfinance  # noqa: F401
    except ImportError:
        sys.stderr.write(
            "error: yfinance is not installed.\n"
            "       Open a command prompt and run:  pip install yfinance\n")
        return 1

    if not os.path.isdir(out):
        os.makedirs(out)

    failures = 0

    for symbol in args.symbols:
        # The volatility index needs its caret; "VIX" alone is a different Yahoo
        # symbol. Typing it bare is the likeliest mistake, so correct it aloud.
        if symbol.upper() == "VIX":
            sys.stderr.write("NOTE: treating VIX as ^VIX, the index.\n")
            symbol = "^VIX"

        path = os.path.join(out, safe_name(symbol) + ".csv")

        try:
            frame = fetch(symbol, args.interval, args.period, start_arg, end_arg, not args.no_prepost)
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
    # A double-click on Windows gives no arguments and closes the console the moment
    # the process ends, which turns every message this script prints - including the
    # one telling you what went wrong - into a flash. Hold the window open in that
    # case, and only in that case, so running it from a shell stays scriptable.
    launched_by_click = len(sys.argv) == 1
    code = 1

    try:
        code = main()
    except KeyboardInterrupt:
        sys.stderr.write("\ncancelled.\n")
    except Exception as error:
        sys.stderr.write("\nunexpected error: %s\n" % error)
    finally:
        if launched_by_click:
            sys.stderr.write("\nDone. Press Enter to close.\n")
            try:
                sys.stdin.readline()
            except Exception:
                pass

    sys.exit(code)
