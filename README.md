# Socrates — Automated NQ Futures Strategy

An automated NinjaTrader 8 strategy for the CME E-mini Nasdaq-100 (NQ).

A liquidity sweep is detected against a book of reference levels, confirmed by a
market structure shift, entered on the retest, and gated on the VIX moving inversely
and the Magnificent 7 participating in the same direction.

**Status: first implementation, not yet compiled.** It has never been through the
NinjaTrader compiler, so expect a round of errors on first import. Nothing here has
been backtested, and no claim is made that it is profitable.

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
    Signal.cs              Shared trade direction type
docs/
  MECHANIZATION.md         How the written rules became arithmetic
  STRATEGY_SPEC.md         Template for defining trade logic
  SETUP.md                 Installing and running it in NinjaTrader
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
| AAPL, MSFT, NVDA, AMZN, META, GOOGL, TSLA | Step 6 | **Breadth mode** not Off |

**Steps 5 and 6 ship Off.** With both on the strategy loads twelve series, and every
one must exist in your feed or NinjaTrader will not start the strategy at all — it
writes the reason to the Log tab, prints nothing, and trades nothing. A futures-only
feed carries none of the eight index and equity symbols. Turn each step on once you
have confirmed its symbols open on a chart.

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

**Step 6 is skipped outside the leaders' session.** There is no overnight ticker for
AAPL — it is AAPL in every session, and from 20:00 to 04:00 ET it is dark everywhere.
So the step applies inside **Leaders open / close** (09:30–16:00 ET by default) and is
skipped outside it, rather than failing every overnight setup for want of data it was
never going to have. A shut equity market is not evidence against a trade. The run
summary counts the skips.

That skip is deliberately narrow: it triggers when leaders traded and then stopped, not
when they never produced a bar at all. A symbol your feed does not carry still fails
loudly instead of quietly disabling the step.

The prior-day and prior-week levels need history: three weeks of loaded data before
weekly pivots exist. Short loads are not fatal — the missing levels are skipped, a
line says so, and swing zones and order blocks carry the sequence — but the level
book is thinner and there will be fewer sweeps.

## Diagnostics

Trades only happen when all six steps complete in order, so silence is the normal
state and needs to be explainable. The strategy prints:

- a **startup banner** naming every series with its bar count, before any bar is processed
- a **heartbeat** every `Status every N bars` bars: bar count, ATR, level count, setup state
- a **daily funnel** at each session roll: sweeps → structure shifts → retests → entries, plus what was rejected and by which gate
- a **run summary** when it stops, with a pointer to the first step that produced nothing
- a **results block**: win rate, net, profit factor, drawdown, and the realised R multiple

The R multiple is the one to judge by. Both exits come from structure, so the ratio each
trade offered is the thing under test, and expectancy in R is the only figure that
survives a change of position size.

`docs/SETUP.md` sections 6 and 7 work through an empty Output window and a run with
output but no trades.

The folder structure mirrors NinjaTrader's own `Documents\NinjaTrader 8\bin\Custom\`
so files can be copied across without rearranging.

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
