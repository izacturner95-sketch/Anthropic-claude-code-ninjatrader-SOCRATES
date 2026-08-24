# Strategy templates

| File | For |
|---|---|
| `SocratesNQ - Cash session.xml` | **The live cash configuration** — NQ, ATR 6, continuations only, no confirmations, no files. PF 2.13 over 139 trades, out-of-sample third at 1.98. |
| `SocratesNQ - Cash session MNQ.xml` | The same book on MNQ: tick value 0.50 and the daily cap scaled to $150 so the budget check refuses the same setups. |
| `SocratesNQ - Overnight.xml` | The overnight configuration. Its 4.48 is near-wholly in-sample; the base system measured 1.93. |
| `SocratesNQ - Cash session (feed only).xml` | Historical record of the feed-only experiment. Superseded by the no-confirmation configuration. |

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

## The feed-only variant

`SocratesNQ - Cash session (feed only).xml` is the cash configuration with both external
sources moved onto instruments the platform carries:

| | Cash session | Feed only |
|---|---|---|
| `Vix symbol` | `VIX` | **`^VIX`** |
| `Vix file` | a CSV path | **empty** |
| `Breadth source` | `Leaders` | **`RelativeStrength`** |
| `Comparison symbol` | — | **`ES 09-26`** |
| `Leader files` | seven CSV paths | **empty** |

Everything else is identical.

**`^VIX` is the index. `VIX` is a stock listing.** NinjaTrader carries both names and only
one of them is the volatility index. A step 5 pointed at `VIX` confirms against the wrong
instrument, returns bars, and produces a run in which nothing looks broken — which has
already cost one test here. `^VIX` is also what the CSV contains, so moving step 5 from the
file to the platform series changes where the data comes from without changing what the
data is.

`^VIX` is regular hours only, 09:30–16:15 ET. Complete for this session, useless overnight
— which is why the overnight configuration uses `VX ##-##` instead.

**Step 6 has no equivalent.** There is no index that means what counting seven leaders
means, so relative strength asks a different question: NQ against ES. The Nasdaq-100 is
roughly half Magnificent 7 by weight and the S&P 500 is not, so the spread between their
moves reads as whether big tech is leading. Both are futures, so it behaves the same live
as in a backtest. In a falling market the two forms disagree — NQ down *less* than ES is
leadership to one and no participation to the other.

**`ES 09-26` is hard-coded and will go stale.** Change it at every roll. NinjaTrader does
not carry the previous contract's history forward, so a backtest reaching back past a roll
loses the data — which is what produced the earlier runs where steps 5 and 6 silently had
nothing to read.

**Nothing here is measured**, but the two halves are not equally uncertain: step 5 should
land close to the file version because it is the same series, while step 6 is a genuinely
different test. If this run misses 1.35, step 6 is the likely reason.

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

## Where this stands

`SocratesNQ - Cash session.xml` is the measured configuration: **profit factor 2.13 over
139 trades and five months, with the out-of-sample third at 1.98**. Continuations only, both
confirmations off, no files, no external data. Size scales linearly once `Max daily loss ($)`
is scaled with the contract count.

`ATR period` is 6 here rather than the 8 the file was first saved with — measured as a
monotonic gradient (16 → 1.54, 8 → 1.86, 6 → 2.13) that improved the out-of-sample half more
than the tuning window. It is the only value changed from the configuration as supplied.

It is the strongest result in this project and the only configuration to survive extending
its window. Full evidence in `docs/MEASUREMENTS.md`.

### How it differs from the earlier repository template

This is a **diff, not a changelog.** The template it replaces was written here by
reconstruction from the overnight settings and was never run; the differences below are the
gap between that guess and a configuration that was actually tuned, not a list of edits made
in sequence. Do not read the count as the number of things that had to change.

| Parameter | Was | Now | Why it matters |
|---|---|---|---|
| `Min penetration (points)` | 5 | **24** | The headline change. A five-point poke through a level is noise, not a break. |
| `Min penetration (ATR)` | 0.05 | 0.07 | Same idea, scaled. |
| `Level merge (ATR)` | 0.45 | 0.15 | Measured as inert — reverting it moves the sweep count by 6 in 14,730 and the profit factor from 1.85 to 1.86. |
| `Swing strength` | 6 | 4 | More swings qualify, adding more levels on top. |
| `ATR period` | 16 | **6** | The largest measured effect. 16 → 1.54, 8 → 1.86, 6 → 2.13, with drawdown and the worst trade improving at every step. |
| `Max stop (ticks)` | 200 | 245 | |
| `Min reward:risk` | 1.0 | 1.2 | |
| `Zone half-width (ATR)` | 0.15 | 0.3 | |
| `Retest zone (ATR)` | 0.3 | 0.4 | |
| `Max bars to reclaim` | 6 | 4 | |
| `Max bars sweep to shift` | 45 | 56 | Inert — continuations skip step 3. |
| `Max bars shift to retest` | 15 | 12 | |
| `Order block displacement (ATR)` | 1 | 2.25 | |
| `Order block max lookback` | 10 | 3 | |
| `Four-hour pivots` | on | off | |
| `Opening range (minutes)` | 15 | 14 | |
| `Vix mode` / `Breadth mode` | Directional | **Off** | Measured as neutral-to-harmful here; see MEASUREMENTS. |

### Two settings that are inert now and would not stay inert

- **`Use confidence sizing`** is now off in both cash templates. It was on and inert — at
  one contract the code floors at one, and with the confirmations off strength is always
  1.0 — but any future confirmation plus size would have silently scaled entries down on
  weak signals.
- **`Vix skip when quiet` is off.** Harmless while `Vix mode` is Off. If step 5 is ever
  switched on, a quiet or absent `^VIX` will then *refuse* setups rather than stand aside.

`Shadow confirmations` is on and costs nothing while both steps are Off — it simply reports
that neither objected. Leave it on when testing a confirmation, since it is how they should
be judged.
