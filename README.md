# Socrates — Automated NQ Futures Strategy

An automated NinjaTrader 8 strategy for the CME E-mini Nasdaq-100 (NQ).

A liquidity sweep is detected against a book of reference levels, confirmed by a
market structure shift, entered on the retest, and gated on the VIX moving inversely
and the Magnificent 7 participating in the same direction.

**Status: compiles, backtests, and has run on a sim account.** Over two months and 46–80
trades the profit factor runs 1.71 without confirmations, 1.96 with step 5, and 2.17 with
both — see `docs/MEASUREMENTS.md`, which also records what was tested and rejected. All of
it is in-sample, the sample is two months, and the best configuration reads files rather
than a live feed. No claim is made that any of it is profitable.

## The six steps

| Step | Rule | Code |
|---|---|---|
| 1 | Establish market context | `Levels.cs`, `Swings.cs` |
| 2 | Wait for liquidity — sweep beyond a level, then reclaim | `Sweeps.cs` |
| 3 | Wait for a market structure shift with displacement | `SetupEngine.cs` |
| 4 | Trade the retest, never the sweep | `SetupEngine.cs` |
| 5 | Confirm inverse agreement with the VIX | `Confirmations.cs` |
| 6 | Confirm the Magnificent 7 are participating | `Confirmations.cs` |

Steps run in sequence and each has a bar budget. A step that times out resets the
whole setup, so "everything must align" is enforced by the code rather than hoped for.

`docs/MECHANIZATION.md` documents every judgment call made turning the written rules
into arithmetic, and is the file to read first.

`docs/MEASUREMENTS.md` records what has actually been measured, with sample sizes — the
controlled comparison of what each confirmation is worth, and the settings that were
tested and rejected.

---

## Contract reference (NQ)

| | |
|---|---|
| Instrument | E-mini Nasdaq-100, CME Globex |
| Tick size | 0.25 index points |
| Tick value | $5.00 |
| Point value | $20.00 |
| Contract months | Mar, Jun, Sep, Dec (H, M, U, Z) |
| Globex hours | Sun 18:00 ET – Fri 17:00 ET, daily halt 17:00–18:00 ET |
| Regular hours | 09:30 – 16:00 ET |

At one contract, a 50-point adverse move is $1,000. Risk per trade is not a setting:
the stop goes below the previous low and the target at the previous high, so both are
whatever structure offers. **Max setup risk (ATR)** is the ceiling on it, and the
startup banner prices the widest admissible stop against your daily loss limit.

The Micro (MNQ) is one tenth the size — $0.50 per tick, $2.00 per point. Switching
the strategy to MNQ is a single parameter change (**Tick value**).

---

## Repository layout

```
src/NinjaTrader8/
  Strategies/
    SocratesNQ.cs          Wiring: data series, orders, parameters
  AddOns/Socrates/
    Swings.cs              Swing high/low detection
    Levels.cs              Reference levels, pivots, the level book
    OrderBlocks.cs         Supply and demand zones from displacement candles
    Sweeps.cs              Liquidity sweep detection
    MarketAnalyzer.cs      Per-instrument bundle of the three above
    SetupEngine.cs         Sequencing state machine for steps 2-4
    Confirmations.cs       VIX and Magnificent 7 gates
    RiskManager.cs         Session windows, daily loss limits, halts
    PositionSizer.cs       Fixed contract sizing under a ceiling
    RelativeStrength.cs    Step 6's second form: index spread instead of a leader count
    FileSeries.cs          Timestamped values from a file, for steps 5 and 6
    Signal.cs              Shared trade direction type
src/TradingView/
  SocratesNQ.pine          Pine Script v6 port of the same six steps
docs/
  MECHANIZATION.md         How the written rules became arithmetic
  MEASUREMENTS.md          What has actually been measured, with sample sizes
  STRATEGY_SPEC.md         Template for defining trade logic
  SETUP.md                 Installing and running it in NinjaTrader
tools/
  fetch_market_data.py     Download VIX and leader bars ready for the strategy to read
  fetch_market_data.bat    Windows double-click launcher for the above
  to_ninjatrader_csv.py    Convert OHLCV exports into NinjaTrader import format
```

The engine classes are plain C# with no NinjaTrader dependencies. Only
`SocratesNQ.cs` touches the platform.

## Data requirements

| Series | Purpose | Loaded when |
|---|---|---|
| NQ intraday | Primary | always |
| NQ daily, weekly | Prior period levels and pivots | always |
| NQ 4-hour | 4-hour pivots | **Use 4-hour pivots** on |
| `VX` | Step 5 | **VIX mode** not Off |
| `SAAPL`, `SMSFT`, `SNVDA`, `SAMZN`, `SMETA`, `SGOOG`, `STSLA` | Step 6 | **Breadth mode** not Off |

**Steps 5 and 6 ship Off.** With both on the strategy loads twelve series, and every
one must exist in your feed or NinjaTrader will not start the strategy at all — it
writes the reason to the Log tab, prints nothing, and trades nothing. Turn each step on
once you have confirmed its symbols open on a chart.

**Trading hours** defaults to Extended — 18:00 to 16:45 ET, the full Globex session,
flat by 16:55. Regular is 09:45–15:45; Custom takes the three HHmmss times, and may wrap
midnight.

The two confirmations handle the overnight session differently, because the data does.

**Step 5 uses VIX futures**, `VX`, not the `^VIX` index. The index is only
published around the cash session, so on a Globex chart it is dark for most of the night
and could not confirm anything; VX trades close to 23 hours. It prices in contango
rather than tracking spot exactly, which does not matter here — the step reads direction
and levels, not the absolute number. Point **VIX symbol** back at `^VIX` if you want the
index, and the banner will warn you when that is combined with extended hours.

**Open is not the same as trading.** VX is listed nearly 23 hours but a bar only forms
when somebody trades, and overnight it can go well over an hour without a print. So the
staleness limit is a parameter of its own — **VIX max data age (minutes)**, 90 by
default — rather than being derived from the bar period, which is right for an index that
either publishes or is shut and much too tight for a thin futures tape.

When the source has gone quiet past that limit, **VIX skip when quiet** makes step 5 stand
aside instead of refusing the setup. It is the same judgement step 6 makes about a closed
equity market: a source with nothing to say is not evidence against a trade, and refusing
on staleness is not a filter, it is the step deciding the strategy may not trade at night.
A symbol that has *never* produced a bar still fails loudly — that is a feed problem, not
a quiet one. The run summary counts the skips, and says so when the step is standing aside
more often than it speaks.

**Step 6 uses CME single stock futures**, `SAAPL` and the rest — not the cash shares.
NinjaTrader's feed carries no US equities at all, so `AAPL` was never going to supply
this step here. The futures suit it better anyway: they run Globex hours, so breadth can
confirm an overnight setup instead of standing aside for two thirds of the session.

**Leaders open / close** therefore ships at `0` / `0`, meaning around the clock. The step
is skipped where the leaders have no data, decided by the staleness guard reading whether
bars actually arrived rather than by a hardcoded clock. Set a window to narrow it —
`093000` to `160000` restricts step 6 to the cash session.

That skip is deliberately narrow: it triggers when leaders traded and then stopped, not
when they never produced a bar at all. A symbol your feed does not carry still fails
loudly instead of quietly disabling the step.

Two costs come with the futures. History is **contract-based and short** — a contract
listed at the start of the month carries data only from then — so a backtest with step 6
on truncates to the youngest series, and the startup banner names the one responsible.
And they are thinner than the shares, so a leader can read flat while the underlying is
moving. Judge **Min leader move (%)** against that, not against equity behaviour.

The prior-day and prior-week levels need history: three weeks of loaded data before
weekly pivots exist. Short loads are not fatal — the missing levels are skipped, a
line says so, and swing zones and order blocks carry the sequence — but the level
book is thinner and there will be fewer sweeps.

## Diagnostics

Trades only happen when all six steps complete in order, so silence is the normal
state and needs to be explainable. The strategy prints:

- a **startup banner** naming every series with its bar count, before any bar is processed
- a **live handover banner** at the switch from replay to live data, reporting what the
  replay carried into the live session — trades already counted against the day, a halt
  already armed, a position it ended holding
- a **heartbeat** every `Status every N bars` bars: bar count, ATR, level count, setup state
- a **daily funnel** at each session roll: sweeps → structure shifts → retests → entries, plus what was rejected and by which gate
- a **run summary** when it stops, with a pointer to the first step that produced nothing
- a **results block**: win rate, net, profit factor, drawdown, and the realised R multiple

The R multiple is the one to judge by. Both exits come from structure, so the ratio each
trade offered is the thing under test, and expectancy in R is the only figure that
survives a change of position size.

`docs/SETUP.md` sections 6 and 7 work through an empty Output window and a run with
output but no trades.

## On a chart

With **Show chart visuals** on, each trade draws its stop and target as lines running
from the entry bar, an arrow at entry, and the planned reward-to-risk beside it. The
retest zone is shaded while a setup waits in it — blue for a reversal, purple for a
continuation — so the chart shows what the strategy is watching, not only what it did.
A panel in the top-right carries running totals: trades, win rate, net, profit factor,
per-trade dollars, planned R:R, and the day's funnel.

None of it runs in the Strategy Analyzer. Drawing is skipped whenever there is no chart,
so a 42,000-bar backtest is not paying to draw rectangles nobody will look at.

The folder structure mirrors NinjaTrader's own `Documents\NinjaTrader 8\bin\Custom\`
so files can be copied across without rearranging.

## On TradingView

`src/TradingView/SocratesNQ.pine` is the same six steps in Pine Script v6 — same
parameter names, same defaults, so settings carry across. Paste it into the Pine Editor
and add it to an NQ chart.

It is a port, not a second source of truth. Two differences matter before comparing
numbers: there is no 1-minute fill series, so a bar touching both stop and target
resolves on TradingView's own assumption; and commission and slippage live in the
strategy's Properties tab rather than in the script, so they are zero until you set them.
**Alerts** are TradingView-only, and are the reason to run this build even if NinjaTrader
is doing the trading: entries, exits, the end-of-day flatten and the risk halt can all
reach a phone or a webhook. Set **Message format** to JSON if a bridge has to parse it.
One alert covers every event — the setup instructions are a comment block at the bottom
of the `.pine` file, and the one step people miss is putting
`{{strategy.order.alert_message}}` in the alert's message box.

The trade is that TradingView carries VIX and US equity data, which makes steps 5 and 6
testable there — on a futures-only NinjaTrader feed they are not. Step 6 also has a
**Leader hours** setting the NinjaTrader build has no equivalent for: on Extended it
requests pre- and post-market data, 04:00–20:00 ET, so breadth applies across most of the
Globex night instead of only the cash session. Nothing trades 20:00–04:00, so the step is
still skipped through the small hours. The full list of differences is a comment block at
the bottom of the file.

---

## Design notes

**Everything compiles inside NinjaTrader.** No Visual Studio, no external DLLs, no
build step. Copy the files in and press F5.

**Risk is a gate, not an afterthought.** Every entry passes through
`RiskManager.CanEnter()`. Daily loss limits, consecutive-loss halts, trade caps,
session windows and connection loss are all checked in one place.

**Bar-close evaluation by default.** `Calculate.OnBarClose` means backtest and live
behaviour agree. Intraday tick evaluation is available but is a deliberate choice
with real backtest-fidelity costs, not a default.

**Entries are submitted on a 1-minute series**, so their fills resolve on smaller bars
than the chart's, and slippage is 1 tick.
Every trade carries a stop and a target simultaneously, so a bar touching both leaves
the backtest to assume a sequence — and the assumption flatters precisely this kind of
strategy. Needs 1-minute history for the tested range.

**Every threshold is a parameter.** Nothing about "how far is a real sweep" or "how
strong is displacement" is hard-coded, because those are the numbers that will need
tuning against real results.

---

## Next steps

1. Compile it and work through the first round of errors
2. Review `docs/MECHANIZATION.md` and correct the definitions that do not match how
   you actually trade this
3. Backtest with realistic commission and slippage, then Market Replay, then sim
4. Live, small

---

## Honest warnings

- **Backtests on NQ flatter you.** Without Tick Replay, intrabar fills are estimated,
  and any strategy whose stop and target could both be touched inside one bar will
  report results it cannot achieve. Model commission and slippage from day one.
- **A profitable backtest is not evidence of an edge** until it survives out-of-sample
  data, Market Replay, and a stretch of live sim.
- **Automation converts a bad strategy into losses faster than manual trading does.**
  The risk halts are there because unattended software with market access needs a
  floor that does not depend on anyone watching.
