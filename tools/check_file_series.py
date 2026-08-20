#!/usr/bin/env python3
"""Report what a SocratesData CSV actually covers, day by day.

Written to answer one question the strategy could only hint at: step 5 reported
81 of 210 cash setups as "source quiet", meaning the VIX file read more than
fifteen minutes stale during hours when the index is live. Either the download
has holes or the timestamps are landing in the wrong place. This tells you
which, without another two-minute backtest.

    python3 check_file_series.py "C:/Users/you/Documents/NinjaTrader 8/SocratesData/VIX.csv"
    python3 check_file_series.py ./SocratesData --session 0930-1600
    python3 check_file_series.py ./SocratesData/VIX.csv --worst 20

Point it at a file or at the whole folder. No arguments works too - it asks.

On time zones
-------------
Epoch timestamps are converted with the machine's local time zone, which is
exactly what FileSeries does (it parses to UTC and calls ToLocalTime). So the
times printed here are the times the strategy sees. If they look shifted from
what you expect, that shift is real and it is what 'File time offset (minutes)'
exists to correct - the file is not wrong, the machine and the chart's exchange
time zone simply disagree.
"""

import argparse
import os
import sys
from datetime import datetime, timedelta

FRESH_MINUTES_DEFAULT = 15


def parse_timestamp(raw):
    """Mirror FileSeries.ParseTimestamp closely enough to be trusted.

    Epoch seconds or milliseconds, ISO-8601, or NinjaTrader's yyyyMMdd HHmmss.
    Returns naive local time, or None if the row is not a timestamp at all.
    """
    raw = raw.strip()

    if not raw:
        return None

    try:
        value = float(raw)
    except ValueError:
        pass
    else:
        # The same threshold FileSeries uses to tell millis from seconds.
        if value > 100000000000.0:
            value /= 1000.0
        try:
            return datetime.fromtimestamp(value)
        except (OverflowError, OSError, ValueError):
            return None

    for fmt in ("%Y%m%d %H%M%S", "%Y-%m-%d %H:%M:%S", "%Y-%m-%dT%H:%M:%S"):
        try:
            return datetime.strptime(raw[:19], fmt)
        except ValueError:
            continue

    return None


def read_rows(path):
    """Every parseable timestamp in the file, sorted, plus the reject count."""
    stamps = []
    rejected = 0
    first_reject = None

    with open(path, "r", encoding="utf-8-sig", errors="replace") as handle:
        for line in handle:
            if not line.strip():
                continue

            when = parse_timestamp(line.split(",")[0])

            if when is None:
                rejected += 1
                if first_reject is None:
                    first_reject = line.strip()[:60]
                continue

            stamps.append(when)

    stamps.sort()
    return stamps, rejected, first_reject


def minutes_of_day(text):
    return int(text[:2]) * 60 + int(text[2:])


def report(path, start_min, end_min, fresh_minutes, worst_count):
    name = os.path.basename(path)
    print("=" * 70)
    print(name)
    print("=" * 70)

    stamps, rejected, first_reject = read_rows(path)

    if not stamps:
        print("  No parseable rows. FileSeries will reject this file too.")
        if first_reject:
            print("  First unreadable line: %s" % first_reject)
        print()
        return

    print("  Rows            : %d parseable, %d rejected" % (len(stamps), rejected))
    if first_reject:
        print("  First rejected  : %s" % first_reject)
    print("  Range           : %s to %s" % (stamps[0], stamps[-1]))
    print("  Spans           : %d days" % ((stamps[-1] - stamps[0]).days + 1))

    # In-session share, which is how you tell RTH data from ETH at a glance.
    # Counted per row rather than per hour: a 09:30 open splits the 09:00 hour,
    # and bucketing by hour would file every opening bar as out of session.
    inside = sum(1 for when in stamps
                 if start_min <= when.hour * 60 + when.minute < end_min)
    outside = len(stamps) - inside
    print("  In session      : %d rows (%.0f%%), %d outside %04d-%04d"
          % (inside, 100.0 * inside / len(stamps), outside,
             start_min // 60 * 100 + start_min % 60,
             end_min // 60 * 100 + end_min % 60))

    if outside == 0:
        print("                    Regular hours only, as expected for a cash index.")

    # Per day, the gaps that would make the strategy call the source quiet.
    by_day = {}
    for when in stamps:
        if start_min <= when.hour * 60 + when.minute < end_min:
            by_day.setdefault(when.date(), []).append(when)

    weekdays = set()
    cursor = stamps[0].date()
    while cursor <= stamps[-1].date():
        if cursor.weekday() < 5:
            weekdays.add(cursor)
        cursor += timedelta(days=1)

    missing = sorted(weekdays - set(by_day))
    print("  Weekdays        : %d in range, %d with no in-session rows at all"
          % (len(weekdays), len(missing)))

    if missing:
        shown = ", ".join(str(d) for d in missing[:8])
        print("                    %s%s" % (shown, " ..." if len(missing) > 8 else ""))
        print("                    Holidays are expected here. Trading days are not.")

    # A gap longer than the freshness window is a stretch where the strategy
    # either skips step 5 or reads a stale row, depending on SkipWhenQuiet.
    gaps = []
    for day, times in by_day.items():
        times.sort()
        opening = datetime.combine(day, datetime.min.time()) + timedelta(minutes=start_min)
        closing = datetime.combine(day, datetime.min.time()) + timedelta(minutes=end_min)

        edges = [opening] + times + [closing]
        for i in range(1, len(edges)):
            span = (edges[i] - edges[i - 1]).total_seconds() / 60.0
            if span > fresh_minutes:
                gaps.append((span, edges[i - 1], edges[i]))

    gaps.sort(reverse=True)

    stale_minutes = sum(g[0] for g in gaps)
    session_minutes = len(by_day) * (end_min - start_min)

    print("  Gaps over %d min: %d, covering %.0f of %.0f in-session minutes (%.1f%%)"
          % (fresh_minutes, len(gaps), stale_minutes, session_minutes,
             100.0 * stale_minutes / session_minutes if session_minutes else 0.0))

    if gaps:
        print("  Worst:")
        for span, before, after in gaps[:worst_count]:
            print("    %6.0f min  %s -> %s" % (span, before, after))
        print()
        print("  Every minute in those gaps is a minute step 5 reads as quiet or stale.")
        print("  Compare that percentage against the run's quiet-skip share - if they")
        print("  agree, the data is the whole explanation and no threshold needs moving.")
    else:
        print("  No gaps. Whatever made step 5 go quiet, it was not this file.")

    print()


def main(argv=None):
    parser = argparse.ArgumentParser(
        description="Report coverage and gaps in a SocratesData CSV.")
    parser.add_argument("path", nargs="?",
                        help="A .csv file, or a folder of them. Asked for if omitted.")
    parser.add_argument("--session", default="0930-1600",
                        help="Session window as HHMM-HHMM. Default 0930-1600, the cash session.")
    parser.add_argument("--fresh", type=int, default=FRESH_MINUTES_DEFAULT,
                        help="Minutes before the strategy calls a row stale. Match this to "
                             "'Vix max data age (minutes)'. Default %d." % FRESH_MINUTES_DEFAULT)
    parser.add_argument("--worst", type=int, default=10,
                        help="How many of the largest gaps to list. Default 10.")

    args = parser.parse_args(argv)
    interactive = args.path is None

    if interactive:
        args.path = input("Path to a .csv file or the SocratesData folder: ").strip().strip('"')

    if not args.path:
        print("Nothing to check.")
        return 1

    if not os.path.exists(args.path):
        print("Not found: %s" % args.path)
        if interactive:
            input("\nPress Enter to close.")
        return 1

    try:
        start_text, end_text = args.session.split("-")
        start_min = minutes_of_day(start_text)
        end_min = minutes_of_day(end_text)
    except (ValueError, IndexError):
        print("Could not read --session %r. Expected something like 0930-1600." % args.session)
        return 1

    if os.path.isdir(args.path):
        targets = sorted(os.path.join(args.path, f) for f in os.listdir(args.path)
                         if f.lower().endswith(".csv"))
        if not targets:
            print("No .csv files in %s" % args.path)
            if interactive:
                input("\nPress Enter to close.")
            return 1
    else:
        targets = [args.path]

    for target in targets:
        report(target, start_min, end_min, args.fresh, args.worst)

    if interactive:
        input("Press Enter to close.")

    return 0


if __name__ == "__main__":
    sys.exit(main())
