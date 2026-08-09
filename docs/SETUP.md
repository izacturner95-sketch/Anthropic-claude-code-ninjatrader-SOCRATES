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
- **Slippage:** at least 1 tick. NQ is liquid but market orders still pay the spread.

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

## 8. Before going live

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
