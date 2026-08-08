# Socrates — Automated NQ Futures Strategy

An automated NinjaTrader 8 strategy for the CME E-mini Nasdaq-100 (NQ).

**Status: scaffolding.** The execution, sizing and risk layers are built. The trade
logic is a placeholder (a plain EMA cross) that exists only to prove the plumbing
works end to end. It is not an edge and must not be traded.

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
    SocratesNQ.cs          Strategy shell: lifecycle, orders, parameters
  AddOns/Socrates/
    RiskManager.cs         Session windows, daily loss limits, halts
    PositionSizer.cs       Fixed / fixed-risk / percent-of-equity sizing
    Signal.cs              The seam between signal logic and execution
docs/
  STRATEGY_SPEC.md         Template to define the trade logic
  SETUP.md                 Installing and running it in NinjaTrader
```

The folder structure mirrors NinjaTrader's own `Documents\NinjaTrader 8\bin\Custom\`
so files can be copied across without rearranging.

---

## Design notes

**Everything compiles inside NinjaTrader.** No Visual Studio, no external DLLs, no
build step. Copy the files in and press F5.

**Signal logic is isolated.** `EvaluateEntry()` and `ManageOpenPosition()` in
`SocratesNQ.cs` are the only places trade logic lives. The risk and execution layers
do not know or care what produced a signal, so the strategy can be rewritten without
touching anything that protects the account.

**Risk is a gate, not an afterthought.** Every entry passes through
`RiskManager.CanEnter()`. Daily loss limits, consecutive-loss halts, trade caps,
session windows and connection loss are all checked in one place.

**Bar-close evaluation by default.** `Calculate.OnBarClose` means backtest and live
behaviour agree. Intraday tick evaluation is available but is a deliberate choice
with real backtest-fidelity costs, not a default.

---

## Next steps

1. Fill in `docs/STRATEGY_SPEC.md`
2. Replace the placeholder logic with the real rules
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
