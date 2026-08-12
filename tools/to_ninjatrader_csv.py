#!/usr/bin/env python3
"""Convert OHLCV data into the text format NinjaTrader 8 imports.

NinjaTrader wants semicolon-delimited lines with no header:

    intraday   yyyyMMdd HHmmss;open;high;low;close;volume
    daily      yyyyMMdd;open;high;low;close;volume

Most free data sources give you something close but not that, and three details
decide whether an import lands correctly or silently produces bars in the wrong
place:

1. NinjaTrader stamps an intraday bar with the time it CLOSES. A bar covering
   09:30:00-09:34:59 on a 5-minute series is stamped 09:35:00. Many sources
   stamp the open instead, which shifts every bar by one period. Use
   --stamp open to correct for that.

2. Timestamps must be in the instrument's exchange time zone - Eastern for US
   equities. --shift-minutes moves them if your source is in UTC or local time.

3. Rows must be in ascending time order with no duplicates. The importer does
   not sort for you.

Usage
-----
    python3 to_ninjatrader_csv.py AAPL_5min.csv --period 5 --stamp open > AAPL.txt

Input needs a header row. Column names are matched case-insensitively and
common aliases are accepted (t/timestamp/date/datetime, o/open, v/volume, ...).
A separate date and time column works too.
"""

import argparse
import csv
import sys
from datetime import datetime, timedelta

TIMESTAMP_KEYS = ("timestamp", "datetime", "date_time", "time", "date", "t")
DATE_ONLY_KEYS = ("date", "day", "d")
TIME_ONLY_KEYS = ("time", "hhmmss", "t")

FIELD_ALIASES = {
    "open": ("open", "o", "op"),
    "high": ("high", "h", "hi"),
    "low": ("low", "l", "lo"),
    "close": ("close", "c", "last", "adj_close", "adjclose"),
    "volume": ("volume", "v", "vol", "qty"),
}

TIMESTAMP_FORMATS = (
    "%Y-%m-%d %H:%M:%S",
    "%Y-%m-%d %H:%M",
    "%Y-%m-%dT%H:%M:%S",
    "%Y-%m-%dT%H:%M:%SZ",
    "%Y-%m-%dT%H:%M",
    "%Y/%m/%d %H:%M:%S",
    "%Y/%m/%d %H:%M",
    "%m/%d/%Y %H:%M:%S",
    "%m/%d/%Y %H:%M",
    "%Y%m%d %H%M%S",
    "%Y%m%d %H:%M:%S",
    "%Y-%m-%d",
    "%Y/%m/%d",
    "%m/%d/%Y",
    "%Y%m%d",
)


class ConversionError(Exception):
    pass


def find_column(fieldnames, candidates):
    lowered = {name.strip().lower(): name for name in fieldnames if name}

    for candidate in candidates:
        if candidate in lowered:
            return lowered[candidate]

    return None


def parse_timestamp(text):
    text = text.strip()

    if not text:
        raise ConversionError("empty timestamp")

    # Epoch seconds or milliseconds, as several APIs return.
    if text.isdigit() and len(text) in (10, 13):
        seconds = int(text) / (1000.0 if len(text) == 13 else 1.0)
        return datetime.utcfromtimestamp(seconds)

    for fmt in TIMESTAMP_FORMATS:
        try:
            return datetime.strptime(text, fmt)
        except ValueError:
            continue

    raise ConversionError("unrecognised timestamp format: %r" % text)


def parse_number(text, field):
    text = (text or "").strip().replace(",", "")

    if not text:
        raise ConversionError("empty %s" % field)

    try:
        return float(text)
    except ValueError:
        raise ConversionError("%s is not a number: %r" % (field, text))


def read_rows(handle):
    reader = csv.DictReader(handle)

    if not reader.fieldnames:
        raise ConversionError("input has no header row")

    columns = {}

    for field, aliases in FIELD_ALIASES.items():
        column = find_column(reader.fieldnames, aliases)

        if column is None and field != "volume":
            raise ConversionError(
                "could not find a %s column among %s" % (field, reader.fieldnames))

        columns[field] = column

    stamp_column = find_column(reader.fieldnames, TIMESTAMP_KEYS)
    date_column = find_column(reader.fieldnames, DATE_ONLY_KEYS)
    time_column = find_column(reader.fieldnames, TIME_ONLY_KEYS)

    # A separate time column only counts when it is distinct from the date one.
    split_stamp = date_column and time_column and date_column != time_column

    if not stamp_column and not split_stamp:
        raise ConversionError(
            "could not find a timestamp column among %s" % (reader.fieldnames,))

    for line_number, row in enumerate(reader, start=2):
        if not any((value or "").strip() for value in row.values()):
            continue

        try:
            if split_stamp:
                stamp = parse_timestamp(
                    "%s %s" % (row[date_column].strip(), row[time_column].strip()))
            else:
                stamp = parse_timestamp(row[stamp_column])

            values = {
                field: parse_number(row[column], field)
                for field, column in columns.items() if column
            }
        except ConversionError as error:
            raise ConversionError("line %d: %s" % (line_number, error))

        volume = values.get("volume", 0.0)
        yield line_number, stamp, values["open"], values["high"], values["low"], values["close"], volume


def convert(handle, out, period_minutes, stamp, shift_minutes):
    written = 0
    skipped_duplicate = 0
    previous = None
    offset = timedelta(minutes=shift_minutes)

    # Stamping the close is NinjaTrader's convention; a source that stamps the
    # open is one period early throughout.
    close_shift = timedelta(minutes=period_minutes) if (
        stamp == "open" and period_minutes > 0) else timedelta(0)

    for line_number, ts, o, h, l, c, v in read_rows(handle):
        ts = ts + offset + close_shift

        if previous is not None:
            if ts == previous:
                skipped_duplicate += 1
                continue

            if ts < previous:
                raise ConversionError(
                    "line %d: %s is earlier than the previous row (%s). "
                    "Sort the input ascending before converting."
                    % (line_number, ts, previous))

        if not (l <= o <= h and l <= c <= h):
            raise ConversionError(
                "line %d: open/close outside the high-low range (o=%s h=%s l=%s c=%s)"
                % (line_number, o, h, l, c))

        if period_minutes > 0:
            key = ts.strftime("%Y%m%d %H%M%S")
        else:
            key = ts.strftime("%Y%m%d")

        out.write("%s;%s;%s;%s;%s;%d\n" % (
            key, trim(o), trim(h), trim(l), trim(c), int(round(v))))

        previous = ts
        written += 1

    return written, skipped_duplicate


def trim(value):
    """Plain decimal, no exponent, no trailing zeros."""
    text = ("%.10f" % value).rstrip("0").rstrip(".")
    return text if text else "0"


def main(argv=None):
    parser = argparse.ArgumentParser(
        description="Convert OHLCV data to NinjaTrader 8 import format.")
    parser.add_argument("input", nargs="?", help="input CSV; omit to read stdin")
    parser.add_argument("--period", type=int, default=1, metavar="MINUTES",
                        help="bar size in minutes; 0 for daily bars (default 1)")
    parser.add_argument("--stamp", choices=("open", "close"), default="close",
                        help="whether the input timestamps the bar's open or close "
                             "(default close, which is NinjaTrader's own convention)")
    parser.add_argument("--shift-minutes", type=int, default=0, metavar="N",
                        help="add N minutes to every timestamp, to move a source in "
                             "UTC or local time into the exchange's time zone")
    args = parser.parse_args(argv)

    handle = open(args.input, newline="") if args.input else sys.stdin

    try:
        written, duplicates = convert(
            handle, sys.stdout, args.period, args.stamp, args.shift_minutes)
    except ConversionError as error:
        sys.stderr.write("error: %s\n" % error)
        return 1
    finally:
        if args.input:
            handle.close()

    sys.stderr.write("wrote %d bars%s\n" % (
        written,
        ", skipped %d duplicate timestamps" % duplicates if duplicates else ""))

    if written == 0:
        sys.stderr.write("error: no rows converted\n")
        return 1

    return 0


if __name__ == "__main__":
    sys.exit(main())
