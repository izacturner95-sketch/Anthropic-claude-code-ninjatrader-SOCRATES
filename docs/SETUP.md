# Setup

Written for someone who has not used NinjaScript before. You will not need to write
or read any code to get this running.

---

## 1. Find your NinjaTrader custom folder

Everything NinjaTrader compiles lives in:

```
Documents\NinjaTrader 8\bin\Custom\
```

Inside it are folders including `Strategies\` and `AddOns\`. This repository mirrors
that structure so files map across one to one.

## 2. Copy the files

| From this repo | To |
|---|---|
| `src/NinjaTrader8/Strategies/SocratesNQ.cs` | `bin\Custom\Strategies\SocratesNQ.cs` |
| `src/NinjaTrader8/AddOns/Socrates\` (whole folder) | `bin\Custom\AddOns\Socrates\` |

Keep the `Socrates` subfolder — it stops these files mixing with NinjaTrader's own.

## 3. Compile

In NinjaTrader: **New → NinjaScript Editor**, then press **F5**.

The Errors panel at the bottom is what matters. If it is empty, the strategy
compiled. If not, copy the full text of every error — including file and line
numbers — and send it over. Compiler errors are normal on first import and are
usually quick to resolve.

> NinjaTrader compiles *everything* in the Custom folder at once. An unrelated
> broken script elsewhere will block this one from compiling.

## 4. Backtest it

**New → Strategy Analyzer**, then:

- **Strategy:** SocratesNQ
- **Instrument:** NQ (front month, or a continuous contract)
- **Bar type:** as specified in the strategy spec
- **Session template:** matching the spec (RTH vs 24-hour changes results a lot)
- **Commission:** set a realistic per-round-turn cost. Not optional — a strategy
  that is profitable only at zero commission is not profitable.
- **Slippage** is set in the strategy, not here: 1 tick by default.

> **1-minute data required.** Entries are submitted against a 1-minute series so their
> fills, and the stop and target attached to them, resolve on smaller bars than the
> chart's — every trade carries a stop and a target at once, and any 5-minute bar
> touching both otherwise leaves the backtest to assume which came first, generously.
>
> NinjaTrader's own `High` order fill resolution cannot do this: it is single-series
> only, and this strategy always loads several. Its error message tells you to program
> the finer resolution in yourself, which is what **Fill resolution (minutes)** is.
>
> If the range has no 1-minute history, download it rather than setting the parameter to
> 0 — that only hides the question.

Then **Run**.

## 5. Read the output

Open **New → NinjaScript Output** to see the strategy's log — every entry, every
skipped trade with the reason, every risk halt. When something behaves unexpectedly,
this window usually explains it, and it is the most useful thing to send me.

The first thing printed is a banner listing every data series with its bar count:

```
=== Socrates NQ ===================================================
  Instrument      : NQ 12-25
  Calculate       : OnBarClose, bars required 30
  Entry window    : 094500-154500, flatten 155500
  Sizing          : 1 contract(s), max 1, daily loss cap $1,000.00
  Stop            : below the previous low +/- 0.25 ATR, band 20-400 ticks (max $2,000.00 a contract)
  Step 5 (VIX)    : Off
  Step 6 (leaders): Off
  Data series     : 4
    [0] NQ primary                  4680 bars
    [1] NQ daily                      60 bars
    [2] NQ weekly                     13 bars
    [3] NQ 4-hour                    360 bars
===================================================================
```

Then, every `Status every N bars` bars, a heartbeat; at each session roll, a funnel
line for the day just finished; and at the end of the run, a summary of how far
setups got. Nothing produces trades until the whole sequence completes, so the funnel
is how you find out which step is stopping them.

## 6. Nothing is printed at all

If the Output window stays completely empty, the strategy is not running — the
problem is upstream of any of its logic. In order of likelihood:

1. **A data series failed to load.** Steps 5 and 6 need `^VIX` and seven equity
   symbols. If one is missing from your feed, NinjaTrader refuses to start the
   strategy and writes the reason to the **Control Center → Log** tab, not to
   Output. Both steps ship **Off** for this reason; turn them on only after each
   symbol opens on a chart of its own.
2. **The strategy is not enabled.** On a chart, the Strategies dialog has an
   *Enabled* checkbox separate from adding the strategy.
3. **It did not compile.** An unrelated broken script anywhere in the Custom folder
   blocks the whole compile, and the previously compiled version keeps running.
4. **Output window filtered.** It has a per-strategy filter dropdown.

Check the **Log** tab before anything else. Every one of these writes a line there.

## 7. Output appears but no trades

Read the run summary printed when the strategy stops:

```
=== Socrates NQ - run summary =====================================
  Bars evaluated       : 4650
  Sweeps (step 2)      : 214
  Structure shifts (3) : 31
  Retests reached (4)  : 12
  Entries submitted    : 4
  Rejected on stop band: 6
===================================================================
```

Each line is a step of the sequence, and the first one that reads zero is the one to
loosen. `Verbose logging` prints every state transition if you need the detail behind
a number.

`Retests reached` counts every touch of a retest zone; most of those time out or get
invalidated before completing. `Setups completed` is the number that produced a
tradeable signal, and it is that number the rejection counts below it add up to.

### Where the stop and target come from

Both are read off structure, so neither is a number you set directly:

- **Stop** goes below the previous low — above the previous high on a short — by
  `Stop buffer (ATR)`. That swing is what has to hold for the trade to be right.
- **Target** goes at, or `Target buffer (ticks)` short of, the previous high. The last
  ticks into a level are where it reverses, so the exit sits in front of it.

Which means risk per trade varies by setup. `Max setup risk (ATR)` is the ceiling that
keeps it sane; `Min / Max stop (ticks)` is a backstop behind that. The banner prices the
worst case against your daily loss limit:

```
  Stop            : below the previous low +/- 0.25 ATR, band 20-400 ticks (max $2,000.00 a contract)
  WARNING: 400 ticks x $5.00 x 1 contract(s) x 2 losses = $4,000.00,
           which overshoots the $1,000.00 daily cap.
```

Because the structural stop is not fixed, that warning uses the widest stop the band
would admit. Tighten `Max setup risk (ATR)` to bring it down, or run MNQ.

The summary then reports where the exits actually came from:

```
  Anchored to a previous swing: 11. Fell back to the swept extreme: 3.
  Targets: 9 from a previous swing, 5 from the R fallback, 0 from liquidity
```

Two things to watch there. If the **swept-extreme fallback dominates**, no swing is
confirming between the sweep and the retest, so the stop is not coming from where you
asked — lower `Swing strength` so swings confirm sooner. If the **R fallback
dominates**, the previous highs are not far enough away to pay for the stops, so either
the stops are too wide or `Min reward:risk` is set too high.

**Min displacement (ATR)**, default 1.0, is the other setting that thins the funnel
hard — requiring the structure-breaking bar to span a full ATR is demanding on a
5-minute chart.

## 8. Importing your own data

Step 6 now runs on the CME single stock futures — `SAAPL`, `SMSFT` and the rest — which
your feed does carry, so importing is no longer the only route to it. It stays worth
doing for one reason: those contracts carry only a few weeks of history each, and a
backtest with step 6 on cannot start before the youngest of them. Importing cash-equity
bars is how you test the step over a range longer than the current contract has existed.

`tools/to_ninjatrader_csv.py` converts most OHLCV exports into the format the importer
accepts.

```
python3 tools/to_ninjatrader_csv.py AAPL_5min.csv --period 5 --stamp open > AAPL.txt
```

Then **Tools → Historical Data → Import**, pick the file, and select the instrument and
bar type in the dialog.

The output format is semicolon-delimited with no header:

```
20260102 093500;185.5;185.75;185.2;185.6;120500     intraday
20260102;185.5;187.75;184.2;186.6;52000000          daily (--period 0)
```

Three details decide whether an import lands correctly or quietly puts bars in the wrong
place. The converter handles all three, but you have to tell it which case you are in:

- **NinjaTrader stamps a bar with the time it closes.** A 5-minute bar covering
  09:30–09:34 is stamped 09:35. Most sources stamp the open instead, which shifts every
  bar by one period. Pass `--stamp open` when that is what you have; it is the common
  case and the default is deliberately the other way so you have to look.
- **Timestamps must be in the exchange's time zone** — Eastern for US equities. Use
  `--shift-minutes -300` to move a UTC source to ET, or `-240` during daylight time.
  Getting this wrong puts your equity bars in the wrong session entirely.
- **Rows must ascend with no duplicates.** The importer does not sort. The converter
  refuses out-of-order input rather than writing a file that imports badly, and drops
  exact duplicate timestamps.

It also rejects rows where the open or close sits outside the high–low range, which is
the usual sign of a mangled export.

### Getting extended hours, and proving you did

Equity data defaults to regular hours almost everywhere, and a file that quietly contains
only 09:30–16:00 looks identical to a correct one until step 6 has nothing to say for two
thirds of the session. Three things have to line up.

**1. Ask the source for it.** Most APIs need it stated explicitly — `prepost=true`,
`extended_hours=true`, `session=extended`, depending on the vendor. A plain request
returns regular hours.

**2. Check the file before importing.** `--hours` prints a bar count per hour and says so
when there is nothing outside the cash session:

```
$ python3 tools/to_ninjatrader_csv.py AAPL_5min.csv --period 5 --stamp open --hours > AAPL.txt
wrote 12480 bars
bars by hour:
  04:00     390
  05:00     390
  ...
  19:00     390
```

US extended hours run 04:00–20:00 ET, so a correct file has bars from hour 4 through 19.
Only 9 through 15 means regular hours, whatever you asked for. `--between 0400-2000`
trims a file that came back with more than you wanted.

**3. Give the instrument a session template that admits them.** This is the one that
catches people. NinjaTrader applies a session template per instrument, and **bars outside
it are ignored** — they import without error and then do not exist. A US equity instrument
defaults to a regular-hours template, so ETH bars land in the database and vanish.

Under **Tools → Instruments**, find the symbol, and set *Session Template* to one covering
04:00–20:00 ET. If none of the built-ins fits, **Tools → Session Templates → New** takes a
few minutes and is reusable across all seven leaders.

Then set **Leaders open / close** to `040000` and `200000` so step 6 applies across the
same window the data covers. The default of `0`/`0` — around the clock — is right for the
stock futures, which run Globex hours; with cash equities it asks the step to work through
20:00–04:00 when nothing trades.

### What to import

Step 6 reads each leader at `Leader bar minutes` (5 by default), so 5-minute bars over
the range you intend to test. Free intraday equity history is harder to come by than
daily — if you can only get daily, raise `Leader bar minutes` to 1440 and understand that
the question changes from "is this leader participating right now" to "was it up
yesterday", which is a different signal and worth judging on its own.

Whatever you import must cover the **whole** backtest range. A multi-series backtest
cannot start before its shortest series, so a leader beginning in August truncates
everything to August — the banner names the series responsible. This is exactly what the
stock futures do on their own, which is the reason to import at all.

If you import cash equities to get the range, point **Leader symbols** back at `AAPL,
MSFT, NVDA, AMZN, META, GOOGL, TSLA` and set **Leaders open / close** to `093000` and
`160000` — imported share data is cash-session only, and leaving the window at `0`/`0`
would have the step reaching for overnight bars that are not in the file.

## 9. Feeding steps 5 and 6 from a file

**VIX file** and **Leader files** take a full path to a CSV. A path there replaces that
platform series entirely, and that is the point: a multi-series backtest cannot begin
before its youngest series, so a VX contract listed three weeks ago was truncating every
step 5 run to three weeks. A file is not a series — the primary decides the range and the
file answers questions about timestamps inside it.

### Getting the files

`tools/fetch_market_data.py` downloads the VIX and all seven leaders and writes them in
the format below, ready to point at:

**On Windows**, double-click `tools/fetch_market_data.bat`. It finds Python, writes into
`Documents\NinjaTrader 8\SocratesData`, and holds the window open so you can read what
happened. Install yfinance first, once, from a command prompt:

```
pip install yfinance
```

**From a shell**, on any platform:

```
python3 tools/fetch_market_data.py --out "C:/Users/you/Documents/NinjaTrader 8/SocratesData"
```

Double-clicking the `.py` directly also works — it asks where to put the files and pauses
before closing. What it will not do is run silently and vanish, which is what a bare
double-click on a script with required arguments does.

It defaults to 5-minute bars, 60 days, extended hours on, and reports the row count and
date span for each symbol — plus a warning when a download came back regular-hours only,
which otherwise looks identical to a correct one.

### Checking what a file actually covers

A download that succeeds is not the same as a download with no holes, and the strategy can
only tell you the consequence — step 5 reading a source as quiet — not the cause.

```
python3 tools/check_file_series.py "C:/Users/you/Documents/NinjaTrader 8/SocratesData"
```

Or double-click `tools/check_file_series.bat`. Point it at one file or the whole folder. It
reports the row count and date span, the share of rows inside the session, weekdays with no
data at all, and every gap longer than the strategy's freshness window — which is the
number to compare against a run's quiet-skip count. Pass `--fresh` to match whatever
`Vix max data age (minutes)` is set to, and `--session` for a window other than the cash
session.

Times are converted with the machine's local time zone, which is what `FileSeries` itself
does. So the times it prints are the times the strategy sees. If they look shifted, the
shift is real and `File time offset (minutes)` is what corrects it — the file is not wrong,
the machine and the chart's exchange time zone simply disagree.

**`VIX` and `^VIX` are different things.** NinjaTrader lists a `VIX` instrument as a stock;
the volatility index is `^VIX`, which is what this script downloads and what belongs in the
file. A step 5 configured with `Vix symbol` = `VIX` and no usable file path is confirming
against the wrong instrument, and nothing in the run will look broken. The startup banner
names the source for exactly this reason — check it says `read from FILE`.

`^VIX` is regular-hours only, 09:30–16:15 ET. That makes the file a complete source for the
cash session and useless overnight, which is why the overnight configuration uses `VX ##-##`
instead.

**Free intraday history is the binding constraint**, not the tooling. Yahoo serves roughly
60 days of 5-minute bars and 7 days of 1-minute, whatever you ask for. Options:

| Source | Free intraday | Free daily | Notes |
|---|---|---|---|
| Yahoo (this script) | ~60 days at 5m | decades | one command, extended hours included |
| Alpha Vantage | ~2 years at 5m | decades | free key, rate limited to a handful of calls a day |
| CBOE | none | VIX back to 1990 | official, and the best long VIX history there is |

For a backtest longer than 60 days, `--interval 1d --period 5y` gives years of daily bars.
Set the strategy's bar-minutes parameter to 1440 to match. That changes step 5's question
from "is the VIX moving inversely right now" to "is it down on the day" — a weaker signal,
and a different one, but a testable one. Testable beats unmeasured, which is what both
steps have been so far.

### The format

One row per bar. Blank lines and lines starting with `#` are ignored. Commas or
semicolons, so the output of `tools/to_ninjatrader_csv.py` works unchanged.

```
timestamp,open,high,low,close
1755604500,23.10,23.25,23.02,23.18
```

`timestamp,close` alone is accepted for a source that only publishes a level. You lose
the ATR scaling in step 5's threshold and nothing else. Extra trailing columns are ignored.

**Timestamps** may be:

| Form | Example | Read as |
|---|---|---|
| NinjaTrader import format | `20260817 093500` | exchange local time, unconverted |
| Epoch seconds or milliseconds | `1755604500` | UTC |
| Date-time with a zone | `2026-08-17T13:35:00Z` | that zone |
| Date-time without a zone | `2026-08-17 13:35:00` | UTC |

The first row is the important one: `tools/to_ninjatrader_csv.py` already emits exactly
that, so its output can be pointed at directly without a second conversion — and because
that format is exchange local time by definition, it is deliberately *not* shifted.
Everything else is converted from UTC to the machine's local time.

### Checking it worked

Two places say so, and both matter — a file that loads perfectly can still be read zero
times. The banner reports what is in each file and how it sits against the chart:

```
--- file-backed sources ---
Chart covers    : 2026-06-18 18:00 to 2026-08-19 16:45
VIX            : 17640 rows, 2026-06-18 18:00 to 2026-08-19 16:45
SAAPL          : 12480 rows, 2026-01-02 09:30 to 2026-08-19 16:00
Re-read         : every 60s once live, and only when the file has changed
```

and the run summary reports whether anything ever came out of them:

```
--- file-backed sources: were they read? ---
VIX            : 17612 of 17640 lookups answered (99.8%)
SAAPL          : 4210 of 17640 lookups answered (23.9%)
                 FEWER THAN HALF ANSWERED. If the row count above looked
                 healthy, the timestamps do not line up with the chart - check
                 the time zone before reading anything into this step's results.
```

**Hit rate is the number to look at.** Loading 12,000 rows proves the file parsed; it
proves nothing about whether a single row was read. A file a time zone away loads
perfectly and answers nothing, and every other line of output looks identical either way.
Under 50% means the timestamps are wrong, not the data — fix it with **File time offset
(minutes)** rather than by tuning the step.

A low rate can also be honest: a leader file covering only the cash session will answer
about a third of lookups on a 24-hour chart, and that is correct. The banner's date ranges
tell you which of the two you are looking at.

### Live

The file is loaded once at startup, blocking, so an unreadable path reports itself before
a single bar is processed. While live it is checked every **File re-read (seconds)** on a
background thread, and only re-parsed when the file's modified time has actually moved —
so pointing this at something a script appends to works, and polling a static file costs
nothing. A backtest never re-reads.

Rows key by timestamp, so rewriting the file with an overlapping window updates rows
rather than duplicating them. Append or rewrite, whichever is easier.

A file that stops updating falls through to the staleness logic step 5 already has: past
**VIX max data age** the step stands aside rather than confirming against a reading that
has stopped moving. A stalled file degrades the strategy to steps 1–4 instead of poisoning
it, and the handover to live data warns when a file ends more than three days back.

### Backtest and forward are not the same problem

A file is a snapshot. It has history — that is the whole reason to use one — and it stops
at the moment it was written. In a backtest that is invisible and correct. Going live, it
means the confirmations read a number from whenever you last exported.

Three ways to run, and the choice is a real one:

| | Backtest | Live | Cost |
|---|---|---|---|
| **File both** | full history | current, if something keeps writing it | you have to run that something |
| **Platform both** | weeks only | current | steps 5 and 6 stay unvalidated |
| **File back, platform live** | full history | current | **you validate on one source and trade on another** |

The third looks like the best of both and is the one to be careful with. Cash-equity
history and CME single stock futures are not the same instrument: the futures are thinner,
so a leader reads flat while the share moves, and `Min leader move (%)` calibrated on one
is wrong for the other. The same applies to `^VIX` history against live VX, which prices
in contango and moves on its own schedule.

If you go that way, treat the backtest as evidence the *logic* is sound, not as a measured
expectation for the live configuration — and re-check the thresholds once the platform
instruments have enough history to be measured on directly.

---

## 10. Steps 5 and 6 cannot be backtested, and what to do about it

Both read contract-based instruments — `VX` for the VIX, `SAAPL` and the rest for the
leaders — and NinjaTrader does not carry their history across a contract roll. A backtest
of either step therefore reaches back only as far as the current contract was listed,
which is weeks, against the months the rest of the strategy has. The longer run has no
step 5 or step 6 data at all, and never will.

Two things help.

**Set a merge policy.** In **Tools → Instruments**, find the instrument, and set *Merge
Policy* to `Merge Non Back Adjusted`. NinjaTrader then stitches expired contracts together
when you request the root symbol without a month, so `VX` returns continuous history
rather than one contract's worth. Back-adjusting is the wrong choice here — it shifts
historical prices to remove roll gaps, and step 5 reads levels as well as direction.
Expect a discontinuity at each roll either way; a six-bar change measured across one is
not meaningful, which is a small and bounded cost.

**Or measure them forward, with Shadow confirmations.** Turn it on and both steps evaluate
every setup and record their verdict, but refuse nothing — every trade is taken. The run
summary then reports what the trades they *would* have refused actually did:

```
--- shadow confirmations: what steps 5 and 6 would have done ---
Neither step objected : 24 trades, 15 won, $8,200.00, $341.67 each
Step 5 would refuse   : 17 trades, 6 won, -$1,430.00, -$84.12 each
VERDICT: the refused trades lost $1,430.00 between them. Running the steps live would
         have avoided that. On this sample the filters are earning their place.
```

That is the question a filter actually has to answer. Counting how many setups it refused
says nothing — a filter that refuses everything scores best on that. What matters is
whether the refused trades were worse than the ones let through, and finding out means
taking them.

**Shadow mode removes protection while it is on.** It is a measuring instrument, not a
setting to leave running on a funded account. Ship it off, turn it on when you want a
reading, turn it off again.

---

## 11. Disabling and re-enabling on a live chart

Turning the strategy off and on again paints trades on bars that have already printed.
Those are not orders that were sent, and nothing went wrong. Enabling a strategy makes
NinjaTrader replay every loaded bar through it first and fill its trades against that
history; only after the replay does it switch to live data. Restart at 14:00 and the
whole morning becomes replay, so the morning's setups get simulated fills and drawn
markers that were not there before. It is the same mechanism as the Strategy Analyzer,
running on the same chart.

Two consequences are worth knowing, because both are silent.

**The replay's trades used to count, and it cost two live days.** They incremented the
day's trade counter, armed the consecutive-loss halt and moved the day's P/L — all of it
inside the strategy, none of it real. Enable the strategy on a morning the replay scores
as two losses and it would refuse every genuine setup until the session rolled, printing
nothing about why. The symptom is exactly the one that sends you looking in the wrong
place: a full day of no trades, then a restart that shows the trades it "should" have
taken, because the second replay simulates them again.

That state is now **discarded at the handover to live data**, and the banner reports what
it threw away. **Carry replay risk state** puts the old behaviour back, for a restart
mid-session where the replay approximates trades that genuinely happened.

**A replay that ends holding a position still blocks live orders.** `StartBehavior` is
`WaitUntilFlat`, so nothing is submitted until that simulated position closes — and it
closes only when its simulated stop or target is hit. If neither is near, the strategy
does nothing for the rest of the day. The banner warns when the replay ends in a
position; if you see it, restart the strategy flat rather than waiting.

**Set `Status every N bars` low enough to see a live day.** It ships at 120, which on a
5-minute chart is ten hours — one line per session. At 12 you get an hourly heartbeat,
and each one states outright whether entries are currently allowed and what is blocking
them if not. Rejection lines only print when a setup completes, so a shut gate during a
quiet market leaves no other trace.

### Reading a live day that produced nothing

Four lines, in order. The first one missing localises the problem.

| Look for | Present means | Absent means |
|---|---|---|
| `=== Socrates NQ - live from here ===` | Realtime was reached | The strategy never left replay — check **Control Center → Log** |
| `Status: ... Entries: allowed` | Bars are being evaluated live, gate open | No heartbeat at all: `OnBarUpdate` is not running. `Entries: BLOCKED - reason` names the gate |
| `ENTRY Long x1 @ ...` | The sequence completed and the order went out | The sequence found nothing — read the funnel in the run summary |
| `FILL: ... Position now Long 1` | The order became a position | `ORDER REJECTED` names the broker's reason; silence means it is still working |

The gap between `ENTRY` and `FILL` is the one nothing used to report. A prop firm's risk
layer sits between this strategy and the exchange and refuses orders on its own terms —
size, product permissions, its own daily loss rule, its own trading windows — and
NinjaTrader records that in the Orders tab, not the Output window. Both outcomes are now
logged.

### Testing the live path without waiting a day

**Market Replay** is the answer to "will it actually trade tomorrow". It feeds recorded
tick data through the strategy at speed, and — this is the part that matters — it runs the
**realtime** code path: `State.Realtime`, live bar processing, real order submission
against the simulator. A backtest exercises none of that, which is exactly why a strategy
can backtest perfectly and do nothing live.

Download a replay day for NQ (**Tools → Historical Data → Load**, Market Replay tab),
connect to the Playback connection, and run the strategy against it. If it trades in
replay, the realtime path works. If it does not, you have the same four lines above to
read and no money at risk while you read them.

The chart drawings behave the same way and are worth reading with the same caution: a
stop line, target line and entry arrow on a past bar mean "the sequence completed here",
not "an order existed here".

---

## 12. Before going live

In order, no skipping:

1. **Backtest** with realistic costs, over at least a year including both trending
   and chopping periods.
2. **Out-of-sample** — hold back recent months, tune on the rest, then test on the
   held-back data. If results collapse, the parameters were fitted to noise.
3. **Market Replay** — NinjaTrader replays recorded tick data at real speed. This
   catches order-handling bugs that bar-based backtests cannot.
4. **Sim account, live data** — at least a few weeks. This is the first honest test.
5. **Live, one contract**, watched closely.

Steps 3 and 4 are where automated strategies actually fail. A backtest cannot show
you a partial fill, a rejected order, or a disconnect mid-position.

---

## Safety checklist for unattended operation

Before leaving the bot running without you at the screen:

- [ ] **Max daily loss** set at all — it ships at **0, disabled**, which is a
      backtesting default and not one to go live with. Set a number you can genuinely
      absorb, **and larger than a single stop-out**. The stop comes from structure, so its size varies; if a routine
      losing trade costs more than the cap, the cap cannot do its job by halting after
      the fact. Entries whose risk exceeds what is left of the day's budget are refused,
      so a cap set too low shows up as trades quietly not being taken — the run summary
      counts them under `Daily risk budget`.
- [ ] **Flatten time** set, and verified working in sim
- [ ] Behaviour after a **disconnect and reconnect** tested — kill your internet mid-position in sim
- [ ] Behaviour on **restarting NinjaTrader with a position open** tested (`StartBehavior` is `WaitUntilFlat`, meaning the strategy will not adopt an existing position)
- [ ] You know how to **kill it fast**: right-click the chart → Strategies → disable, plus your broker's flatten button
- [ ] **Max contracts** set as a hard ceiling, so a sizing bug cannot scale the position
