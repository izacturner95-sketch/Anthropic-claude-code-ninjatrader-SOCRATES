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
   `max(0.25 × ATR, 2.0 points)`. A one-tick poke is noise, and so is a two-point one.
2. **Reclaim** — a bar closes back on the original side of that level, within 6 bars.

If the reclaim never comes within those 6 bars, the candidate is **discarded as a
genuine break** and no setup forms. That is your "don't chase breakouts" rule, enforced
structurally.

When one bar exceeds several levels at once, the furthest one is taken as the swept
level — that is the pool price actually reached for.

| Decision I made | Parameter | Needs your sign-off |
|---|---|---|
| Reclaim window of 6 bars | `Max bars to reclaim` | How long do you give a sweep to fail before calling it a real breakout? |
| Min penetration 0.25 × ATR or 2 pts | `Min penetration (ATR)` / `(points)` | Started at 0.10 ATR / 1 pt and fired every four bars. Too small catches noise; too large misses shallow raids. |
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
  sweep low. A local high made while price is turning — near the sweep, so the stop
  that follows from it is small.
- `false`: the swing high that existed *before* the sweep. This is the origin of the
  entire leg that ended in the sweep, so breaking it means a full retracement.

**These are not two speeds of the same test**, which is how they were first written and
implemented. The stop is pinned beyond the swept extreme, so the reference swing sets
the trade's risk: the post-sweep swing gives a stop of about an ATR, the pre-sweep swing
gives one spanning the whole prior leg. The original code, when no post-sweep swing had
formed yet, quietly fell back to the pre-sweep one — so every setup became the widest
version of itself. Measured over a 47-day sample, stops ran 89 to 240 points. It now
waits for the near swing instead.

Two consequences of that fix:

- The bar budget rose from 12 to 20, because a swing needs `(2 × strength) + 1` = 7 bars
  to confirm and then has to be broken, which 12 bars rarely allowed.
- A ceiling was added on the distance itself: `Max setup risk (ATR)`, default 2.5. If
  the structure sits further than that from the swept extreme, the setup is discarded at
  the shift rather than carried to the retest and rejected on stop size — which reads as
  a sizing problem and is not one.

A third consequence, found once the near-swing rule exposed it: a newly confirmed sweep
used to restart the sequence unconditionally, on the reasoning that the newest liquidity
event is the most relevant one. Against a level book of thirty-odd lines, sweeps confirm
every few bars, so every setup was demolished and restarted long before it could
develop — 498 sweeps produced 4 structure evaluations. The anchor now holds unless the
new sweep is on the opposite side, or reaches further into the same liquidity and so
moves where the stop belongs. Everything else is left alone while the setup develops.

**"Strong displacement"** became: the bar that breaks structure must have a range of at
least 1.0 × ATR. If structure breaks on a weak bar, the setup is **not** discarded — it
waits for a stronger break, up to the timeout.

If no shift occurs within 20 bars of the sweep, the whole setup resets.

| Decision I made | Parameter | Needs your sign-off |
|---|---|---|
| Displacement = bar range ≥ 1.0 × ATR | `Min displacement (ATR)` | Would you rather measure the whole leg from the sweep extreme, or require a fair-value gap? |
| Shift = **close** beyond the swing | — | Or should a wick through it count? |
| 20-bar budget from sweep to shift | `Max bars sweep to shift` | |
| Risk ceiling of 2.5 × ATR from structure to swept extreme | `Max setup risk (ATR)` | This is the main thing standing between the strategy and very wide stops. Is 2.5 ATR the risk you would accept per trade? |

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

**Stop:** a fixed `Stop loss (ticks)` from entry, 60 by default — 15 points, $300 on one
NQ contract. Risk per trade is therefore decided by you rather than by whatever the
setup happened to look like, which is why position sizing has no dollar budget: ticks ×
tick value × contracts is the whole calculation.

Setting `Stop loss (ticks)` to 0 restores the original rule: **beyond the sweep extreme
by 0.25 × ATR**, on the reasoning that the wick is the point the whole premise is wrong
and so the only honest place for the stop. In that mode the `Min` / `Max stop (ticks)`
band applies and setups outside it are skipped.

The trade-off is real and worth stating. A fixed stop tighter than the structure sits
*between* entry and the swept extreme — so price returning to test that extreme, which
is what the setup expects it to do, stops the trade out first. The run summary reports
what fraction of setups had their structure inside the fixed stop, so this shows up as a
number rather than as unexplained losses.

**Target:** `Target (R multiple)` × the stop distance, 2R by default, measured from
whichever stop is in use. Set to 0 to target the next opposing liquidity level instead,
which is an absolute price and does not scale with the stop.

**Invalidation:** if price trades back through the sweep extreme before entry, the setup
is abandoned.

| Decision I made | Parameter | Needs your sign-off |
|---|---|---|
| Retest zone = broken structure level (confirmed) | `Retest zone mode` | Selected. The other two remain available for comparison. |
| Confirmation close required | `Require confirmation close` | Off gives better fills and more entries, with less evidence |
| 2R target | `Target (R multiple)` | Or target liquidity with 0 |
| Fixed 60-tick stop, structure stop available at 0 | `Stop loss (ticks)` | Chosen by you. The structure stop is the more faithful reading of the rule; the fixed stop is the one you can size around. |
| Stop 0.25 × ATR beyond the sweep wick, when the fixed stop is off | `Stop buffer (ATR)` | |

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

## The 5-minute NQ baseline

Every default is set for a **5-minute NQ chart, RTH session**. The reasoning is
recorded here so that changing timeframe later is a deliberate recalculation rather
than guesswork.

ATR(14) on 5-minute NQ typically runs 15–30 points, so read every ATR multiple below
as roughly that many points.

| Parameter | Default | In 5-minute terms |
|---|---|---|
| `Swing strength` | 3 | A swing confirms 15 minutes after it forms |
| `Min penetration (ATR)` | 0.25 | ~5 points beyond a zone edge |
| `Max bars to reclaim` | 6 | 30 minutes to fail, or it was a real breakout |
| `Max bars sweep to shift` | 20 | 100 minutes to break structure |
| `Max bars shift to retest` | 15 | 75 minutes to come back |
| `Min displacement (ATR)` | 1.0 | The breaking bar must be larger than an average bar |
| `Max setup risk (ATR)` | 2.5 | Structure no further than ~50 points from the swept extreme |
| `Level merge distance (ATR)` | 0.35 | Session levels within ~7 points are one area |
| `Order block displacement (ATR)` | 1.0 | Same test, plus an imbalance |
| `Order block origin lookback` | 10 | 50 minutes back to find the opposing candle |
| `Opening range (minutes)` | 15 | Completes at 09:45 |
| `Session start` | 09:45 | Deliberately the moment the opening range completes |

Longest possible setup lifetime is 6 + 12 + 15 = 33 bars, just under 3 hours, which
fits inside the 09:45–15:45 entry window.

**Risk numbers are meant to agree with each other**, and it is easy to break that by
changing one in isolation:

```
Max stop 100 ticks x $5/tick   = $500 worst case per trade
Max consecutive losses 2       = $1,000 before the halt fires
Max daily loss                 = $1,000
```

Raising `Max stop (ticks)` without raising the daily loss limit means a single trade
can breach the cap, which the risk manager can only detect after the fact. The three
numbers should be changed together.

**If you move to another timeframe**, the bar-count parameters are the ones that break
first: on 1-minute they are five times too permissive in wall-clock terms, on 15-minute
three times too strict. The ATR multiples mostly carry over, since ATR rescales itself.

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
