# Strategy templates

Two ready-to-load templates:

| File | For |
|---|---|
| `SocratesNQ - Overnight.xml` | The tuned overnight configuration, saved out of NinjaTrader. Also the schema reference. |
| `SocratesNQ - Cash session.xml` | A starting point for the regular-hours session. Read the warning at the bottom before running it. |

## Installing one

Copy the file into:

```
Documents\NinjaTrader 8\templates\Strategy\SocratesNQ\
```

Then in the Strategy Analyzer or the strategy dialog, **Template → Load** and pick it by
name. The file name is the template name, so rename the file if you want it to read
differently in the list.

To save your own after changing settings: **Template → Save As** in the same dialog.
NinjaTrader writes the XML itself. A hand-written one has to match its schema exactly —
root `<StrategyTemplate>`, then `<StrategyType>` and a `<Strategy>` wrapper around the
`<SocratesNQ>` element, with the `xmlns:xsd` and `xmlns:xsi` attributes on that element.
An earlier attempt here used a different root and would not load at all.

### What a template carries

More than just the strategy's own parameters. The base-class block near the top holds the
**bar period** (`BarsPeriodSerializable`), the **instrument**, the **backtest date range**
(`From`/`To`), `Slippage`, `OrderFillResolution` and `StartBehavior`. So loading the cash
template also puts you on 2-minute bars — you do not have to remember to change the chart.

Adjust `<InstrumentOrInstrumentList>` and `<From>`/`<To>` to whatever contract and window
you are testing; both files currently say `NQ 09-26` over 2026-06-18 to 2026-08-18.

The two files carry the leader and VIX **file paths** under
`C:\Users\izact\Documents\NinjaTrader 8\SocratesData\`. Change those if your user folder
differs.

`SocratesNQ - Cash session.xml` also contains six elements the overnight file does not —
`BreadthSource`, the four `RelativeStrength*` values and `BreadthMaxDataAgeMinutes`. Those
properties exist in the current source; the overnight file was saved from a build that
predates them. Older builds ignore XML elements they do not recognise, so it loads either
way.

---

## What the cash template changes, and why

Against the overnight configuration:

| Parameter | Overnight | Cash | Reason |
|---|---|---|---|
| Bar period | 5 min | **2 min** | The single most important thing the cash runs established. It moved cash retests from about 10% of the total to 27–31%, against the ~29% of bars the session occupies — i.e. from badly under-detected to proportionate. |
| Trading hours | `ExtendedHours` | **`Custom`** | So the times below are used exactly as entered. |
| Session start | 09:45 | **09:30** | The open is the session. |
| Session end / flatten | 15:45 / 15:55 | 15:45 / 15:55 | Unchanged, and deliberately disjoint from the overnight instance so the two can never hold positions at once. Neither knows about the other, and your account limits apply to the pair. |
| Max daily loss | $0 (off) | **$1,500** | A constraint, not a prediction, so it needs no validation. The cash runs reached $9,010 of drawdown without one. |
| Max consecutive losses | 0 (off) | **2** | Same reasoning. |
| Max setup risk (ATR) | 3.5 | **0** (off) | The stop band did nothing in cash across the runs, so the ATR ceiling is dead weight there; the tick cap below is the live constraint. |
| Max stop (ticks) | 250 | **200** | Inherited from the overnight sweep, where 200 was monotonically best (PF 2.77 at 200, 1.98 at 250, 1.85 at 300). Not fitted to cash. |
| Breadth active window | 04:00–20:00 | **09:30–16:00** | Step 6 only reads leaders while the session it is meant to describe is open. |

Kept from the overnight configuration, worth knowing you are running:

- **Continuations on.** In cash they beat reversals 0.90 to 0.40 on profit factor and 35%
  to 13% on win rate — the mirror of overnight, where reversals earn $455 a trade and
  continuations lose. Momentum belongs to the active session.
- **Step 5 on, `VixSymbol` = `VIX` with the file behind it.** Cash is the one session where
  the VIX index is genuinely live (09:30–16:15 ET), so staleness is not the problem it is
  overnight. `VixSkipWhenQuiet` stays on regardless.
- **Status every 12 bars.** On 2-minute bars that is roughly every 25 minutes — frequent
  enough to read a live day.

---

## This is a starting point, not a configuration

**The cash session was unprofitable across four runs.** These settings encode what those
runs established so exploration starts from the best-understood point — not because the
result is expected to be positive.

### Already settled — do not spend runs on these

Recorded with the numbers in `docs/MEASUREMENTS.md`.

- **`Min stop (ticks)`** at 20, 75 and 100 gave identical expectancy. The worst trade
  halved from −4.02R to −1.99R and the mean did not move at all.
- **Reversals in cash** — profit factor 0.40 over 23 trades.
- **Anything resting on stops holding.** With stops that never slipped, the best cash run
  is −0.07R. Breakeven is the ceiling there, not the floor.

The open question is whether a different **premise** works in the cash session, not whether
a different parameter does. Four runs answered the parameter question.
