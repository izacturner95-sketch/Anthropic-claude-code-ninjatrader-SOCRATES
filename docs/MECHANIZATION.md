# Mechanization

How each step of the strategy was turned into arithmetic, and every place where a
judgment call had to be made on your behalf.

**Read the "Decisions that need your sign-off" column.** Where your spec described
something a human recognises by eye, I had to pick a formula. A different formula is
a different strategy. Every one of these is a parameter, so changing your mind is a
settings change, not a rewrite.

---

## Step 1 — Market context

> "Major supply, major demand, previous high, previous low, support, resistance,
> weekly/daily/4hr pivot. Is price approaching an important area?"

**Implemented in** `Levels.cs`, built each period in `SocratesNQ.RebuildSessionLevels`.

Levels computed exactly:

| Level | Source |
|---|---|
| Prior day high / low / close | Daily data series, last completed bar |
| Prior week high / low | Weekly data series, last completed bar |
| Daily pivot + R1/R2/R3 + S1/S2/S3 | Classic floor pivots from prior day H/L/C |
| Weekly pivots | Classic floor pivots from prior week H/L/C |
| 4-hour pivots | Classic floor pivots from the last completed 4-hour bar |
| Overnight high / low | Globex bars from 18:00 to 09:30 ET |
| Opening range high / low | First 15 minutes after 09:30 ET (configurable) |

The book holds two further kinds of area, and the distinction matters because your
Step 2 treats them as different events — "sweep previous highs" versus "push through
supply".

**Swing extremes are liquidity pools.** A confirmed swing high or low becomes a level
with a ±0.25 × ATR band, because price reacts to areas rather than exact numbers. Two
swings within 0.5 × ATR merge, and each merge counts as a "touch" — a level price keeps
respecting accumulates strength. Levels further than 12 × ATR from price are discarded.

**Order blocks are supply and demand** (`OrderBlocks.cs`). A bullish order block is the
last *down* candle before an up displacement; a bearish block is the last *up* candle
before a down displacement. Detection requires two things, because "last opposing
candle" on its own matches something on nearly every bar:

1. The displacement candle's range is at least 1.0 × ATR.
2. It leaves an **imbalance** — a three-bar fair value gap, where the bar before and the
   bar after do not overlap. Price moved fast enough to skip an entire range, which is
   the footprint of size going through rather than ordinary drift.

The zone spans the origin candle's full high to low by default, or its body only.
A block stays live no matter how often it is touched, and retires the moment price
**closes** clean through it. New blocks overlapping an existing one widen it instead of
stacking a duplicate. Twelve stay active at most.

Order blocks are detected on NQ only. The VIX uses pivots and swing levels, since
"reacting from an important technical level" there does not need candle-level structure.

| Decision I made | Parameter | Needs your sign-off |
|---|---|---|
| Order block = last opposing candle before displacement | `Use order blocks` | |
| Imbalance (FVG) required | `Order block needs imbalance` | Off gives many more, much weaker blocks |
| Displacement ≥ 1.0 × ATR | `Order block displacement (ATR)` | |
| Zone = full candle range | `Order block zone` | `Body` is tighter: better entries, more misses |
| Retires only on a **close** through | — | Say if a wick through should kill it |
| Swing = 3 bars either side | `Swing strength` | 3 is responsive. 5+ finds only major turns. |
| Classic floor pivots | — | There are also Camarilla, Woodie and Fibonacci formulas. Say if you use a different one. |
| Opening range = 15 min | `Opening range (minutes)` | |

There is deliberately **no separate "is price approaching an area" gate**. A sweep in
Step 2 can only be detected against a level in the book, so proximity to a level is
already a precondition for everything downstream.

---

## Step 2 — Liquidity

> "Don't chase breakouts. Wait for price to move beyond the nearest pivot zone."

**Implemented in** `Sweeps.cs`.

A sweep is two events, and the second is what distinguishes it from a breakout:

1. **Penetration** — a bar trades beyond a level's zone edge by at least
   `max(0.10 × ATR, 1.0 point)`. A one-tick poke is noise.
2. **Reclaim** — a bar closes back on the original side of that level, within 6 bars.

If the reclaim never comes within those 6 bars, the candidate is **discarded as a
genuine break** and no setup forms. That is your "don't chase breakouts" rule, enforced
structurally.

When one bar exceeds several levels at once, the furthest one is taken as the swept
level — that is the pool price actually reached for.

| Decision I made | Parameter | Needs your sign-off |
|---|---|---|
| Reclaim window of 6 bars | `Max bars to reclaim` | How long do you give a sweep to fail before calling it a real breakout? |
| Min penetration 0.10 × ATR or 1 pt | `Min penetration (ATR)` / `(points)` | Too small catches noise; too large misses shallow raids. |
| Reclaim = **close** back inside, not just a wick | — | Say if a wick back inside should count. |

---

## Step 3 — Structure shift

> "We need proof the move is failing. Higher low after a sell-side sweep, lower high
> after a buy-side sweep, break of the opposing structure, strong displacement.
> Without a structure shift — no trade."

**Implemented in** `SetupEngine.cs`.

After a sell-side sweep (bullish case), the market must **close above a reference
swing high**. Mirror image for the bearish case.

The reference swing is chosen one of two ways:

- `Use post-sweep swing = true` (default): the first swing high to form *after* the
  sweep low. Nearer, so shifts trigger earlier and more often.
- `false`: the swing high that existed *before* the sweep. Stricter and slower.

**"Strong displacement"** became: the bar that breaks structure must have a range of at
least 1.0 × ATR. If structure breaks on a weak bar, the setup is **not** discarded — it
waits for a stronger break, up to the timeout.

If no shift occurs within 12 bars of the sweep, the whole setup resets.

| Decision I made | Parameter | Needs your sign-off |
|---|---|---|
| Displacement = bar range ≥ 1.0 × ATR | `Min displacement (ATR)` | Would you rather measure the whole leg from the sweep extreme, or require a fair-value gap? |
| Shift = **close** beyond the swing | — | Or should a wick through it count? |
| 12-bar budget from sweep to shift | `Max bars sweep to shift` | |

"Higher low after a sell-side sweep" is implied rather than tested separately: if price
takes out a low, then closes above a subsequent swing high, a higher low necessarily
formed in between.

---

## Step 4 — Retest

> "Trade the retest, do not trade during the sweep. Wait for price to return to the
> area that institutions defended."

**Implemented in** `SetupEngine.cs`, the `AwaitingRetest` state.

After the shift, the engine waits up to 15 bars for price to return to a zone. Three
definitions of "the area that institutions defended" are available:

- **BrokenStructure** (default) — the swing level the shift broke, now expected to flip
- **SweptLevel** — the original level whose liquidity was taken
- **FibRetrace** — 50% of the leg from the sweep extreme to the shift extreme

Entry requires price to trade into the zone **and then** a bar to close in the trade's
direction. Without that confirmation the entry is a limit order into a zone with no
evidence it is holding.

**Stop:** beyond the sweep extreme by 0.25 × ATR. That wick is the point the whole
premise is wrong, which makes it the only honest place for the stop.

**Target:** 2R by default, or the next opposing liquidity level if `Target (R multiple)`
is set to 0.

**Invalidation:** if price trades back through the sweep extreme before entry, the setup
is abandoned.

Setups needing a stop tighter than 20 ticks or wider than 200 are skipped.

| Decision I made | Parameter | Needs your sign-off |
|---|---|---|
| Retest zone = broken structure level (confirmed) | `Retest zone mode` | Selected. The other two remain available for comparison. |
| Confirmation close required | `Require confirmation close` | Off gives better fills and more entries, with less evidence |
| 2R target | `Target (R multiple)` | Or target liquidity with 0 |
| Stop 0.25 × ATR beyond the sweep wick | `Stop buffer (ATR)` | |

---

## Step 5 — VIX

> "Only take trades when the Nasdaq and VIX show inverse agreement... the VIX itself
> should be reacting from an important technical level. If it's in the middle of
> nowhere, its signal carries less weight."

**Implemented in** `Confirmations.cs`. The VIX gets its **own** `MarketAnalyzer`, so the
same level and sweep machinery built for NQ runs on the VIX. "VIX rejects a resistance
zone" is literally a buy-side sweep on the VIX series.

For a long NQ trade:
- VIX must have fallen at least 0.10 points over the last 6 bars, **and**
- VIX must be within 0.35 of one of its own levels, or have recently rejected one

Mirror image for shorts.

Your "carries less weight" line describes a weight, not a gate, so it is one. Three modes:

- `Strict` — both conditions required
- `Directional` — direction only; the level test just reduces strength
- `Off` — skip Step 5

With `Scale size by confirmation strength` enabled, a weak confirmation reduces
position size instead of blocking the trade.

> **Data dependency.** This step needs `^VIX` in your data feed. Kinetick carries it;
> several broker feeds do not. The VIX index also only publishes during CBOE hours, so
> it cannot confirm anything overnight. If your feed lacks it, the alternatives are VIX
> futures (`VX`, nearly 24-hour) or setting VIX mode to Off.

---

## Step 6 — Magnificent 7

> "Check the Magnificent 7 — if the market leaders are participating in the same
> direction as we plan to trade."

**Implemented in** `Confirmations.cs`. Each symbol is added as its own data series.
A leader counts as participating if it is above its session open for a long, below for
a short, by at least 0.05%.

Requires 5 of 7 aligned by default. Unanimity scores full strength; a bare majority
scores 0.6, which matters when confidence sizing is on.

If some symbols have no data, the threshold scales down proportionally rather than
blocking every trade.

| Decision I made | Parameter | Needs your sign-off |
|---|---|---|
| Participation = above/below **session open** | — | Or would you rather use above VWAP, or momentum over N bars? |
| 5 of 7 required | `Min leaders aligned` | |
| Symbol list is editable text | `Leader symbols` | Add or drop names freely |

---

## Consequences worth knowing

**This is a regular-hours strategy now.** Both the VIX index and the Magnificent 7 only
trade 09:30–16:00 ET. With Steps 5 and 6 on, overnight setups cannot be confirmed. The
default entry window is 09:45–15:45 with a 15:55 flatten. Turning both confirmations off
is what makes overnight trading possible.

**Data cost.** Twelve data series load with everything enabled: NQ intraday, daily,
weekly, 4-hour, VIX, and seven equities. Backtests will be noticeably slower, and every
symbol needs data in your feed or the strategy will fail to start.

**This is a selective strategy.** Six sequential gates, each with a timeout, on top of a
trade cap and a consecutive-loss halt. Expect few trades. If a backtest produces almost
none, that is the filters compounding, not necessarily a bug — turn on `Verbose logging`
and read where setups are dying.

**The tuning risk is real.** There are roughly thirty parameters here. That is enough
freedom to fit any historical period perfectly and learn nothing. Change one thing at a
time, and hold back data you never look at until the end.
