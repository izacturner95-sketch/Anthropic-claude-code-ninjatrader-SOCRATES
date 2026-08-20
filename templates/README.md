# Strategy templates

Drop these in:

```
Documents\NinjaTrader 8\templates\Strategy\SocratesNQ\
```

Create the `SocratesNQ` folder if it is not there. They then appear in the Strategy
Analyzer and the chart Strategies dialog under **Template → Load**.

| File | For |
|---|---|
| `SocratesNQ - Cash session.xml` | Regular-hours exploration on a 2-minute chart |

## A caveat worth reading before you trust one

NinjaTrader writes these files itself when you configure a strategy and use
**Template → Save As**, and that is the reliable way to produce one — the platform knows
its own schema, including anything it expects that is not a strategy parameter.

This file was generated from the property list in `SocratesNQ.cs`, so every name and value
matches the code exactly. The surrounding structure is a reconstruction. **Load it and
check the parameter grid against the table below before running anything**, and if it
refuses to load, configure one instance by hand and save your own template — then this
file is still useful as the list of what to enter.

## What the cash template sets, and why

The cash session was measured as **unprofitable across four runs**. This is not a tuned
configuration and is not expected to be profitable as it stands. It encodes what those
runs established so that exploration starts from the best-understood point rather than
from the overnight settings, which are known to be wrong here.

| Setting | Value | Why |
|---|---|---|
| Chart period | **2 minute** (set on the chart, not here) | Established. Cash retests went from ~10% of the total on 5-minute bars to 27–31%, against the ~29% of bars the session occupies. A cash 2-minute bar covers roughly what an overnight 5-minute bar does, so the bar budgets mean again what they were tuned to mean. |
| Trading hours | Custom, 09:30–15:45, flat 15:55 | Disjoint from the overnight instance, so the two can never hold positions at once. |
| Trade continuations | **On** | In cash they beat reversals 0.90 to 0.40 on profit factor and 35% to 13% on win rate — the mirror of overnight, where reversals earn $455 a trade and continuations lose. Momentum belongs to the active session. |
| VIX mode | Directional, `VX ##-##` | Measured better than the index file and needs no files. A bare root fails at startup with *Unknown instrument*. |
| Breadth mode | Off | Neither form is proven, and the leader form needs files. |
| Max stop | 200 ticks | Inherited from the overnight sweep, where it was monotonically best. In cash the stop band did nothing, so this is not fitted to the session. |
| Consecutive losses / daily loss | 2 / $1,500 | Constraints, not predictions. The cash runs reached $9,010 of drawdown without them. |
| Status every N bars | 12 | Frequent enough to read a live day. |

## What is already known not to work

Do not re-run these. They are recorded in `docs/MEASUREMENTS.md` with the numbers.

- **Raising `Min stop (ticks)`** — 20, 75 and 100 all gave the same expectancy. The worst
  case halved and the mean did not move at all.
- **Reversals in cash** — profit factor 0.40 over 23 trades.
- **Anything relying on stops holding** — with stops that never slipped, the best cash run
  is −0.07R. Breakeven is the ceiling, not the floor.

The open question is whether a different *premise* works in the cash session, not whether
a different parameter does.
