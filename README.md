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

At one contract, a 50-point adverse move is $1,000. Sizing and stop distance are
the same decision, which is why they are handled together.

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
    PositionSizer.cs       Fixed / fixed-risk / percent-of-equity sizing
    Signal.cs              Shared trade direction type
docs/
  MECHANIZATION.md         How the written rules became arithmetic
  STRATEGY_SPEC.md         Template for defining trade logic
  SETUP.md                 Installing and running it in NinjaTrader
```

The engine classes are plain C# with no NinjaTrader dependencies. Only
`SocratesNQ.cs` touches the platform.

## Data requirements

With everything enabled the strategy loads twelve series, and **every one must exist
in your data feed or it will not start**:

| Series | Purpose | Notes |
|---|---|---|
| NQ intraday | Primary | |
| NQ daily, weekly, 4-hour | Prior period levels and pivots | 4-hour is optional |
| `^VIX` | Step 5 | Not carried by every broker feed |
| AAPL, MSFT, NVDA, AMZN, META, GOOGL, TSLA | Step 6 | Editable list |

Steps 5 and 6 can each be set to Off, which also stops their series from loading.
Because the VIX index and equities trade regular hours only, running either
confirmation makes this a 09:30–16:00 ET strategy.

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
