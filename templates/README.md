# Strategy templates

**Make these in NinjaTrader, not by hand.** Configure a strategy's parameters, then use
**Template → Save As** in the same dialog. NinjaTrader writes the XML itself and knows its
own schema, including the parts that are not strategy parameters.

I tried generating one from the code and it would not load. The property names and values
were right; the surrounding structure was a reconstruction and evidently wrong. Rather than
guess at the schema again, this file lists what to enter. It takes two minutes once, and
then the template you save is guaranteed correct and reusable.

Saved templates live in:

```
Documents\NinjaTrader 8\templates\Strategy\SocratesNQ\
```

---

## Cash session

**Set the chart to 2-minute first.** Bar period is not a strategy parameter so no template
can carry it, and it is the single most important thing the cash runs established: it moved
cash retests from about 10% of the total on 5-minute bars to 27–31%, against the ~29% of
bars the session occupies.

Everything not listed keeps its shipped default.

| Parameter | Ships as | Set to |
|---|---|---|
| Trading hours | `ExtendedHours` | **`Custom`** |
| Session start (HHmmss) | `94500` | **`93000`** |
| Session end (HHmmss) | `154500` | **`154500`** |
| Flatten time (HHmmss) | `155500` | **`155500`** |
| Max daily loss ($) | `0` | **`1500`** |
| Max consecutive losses | `2` | **`2`** |
| Trade continuations | `false` | **`true`** |
| Max setup risk (ATR) | `2.5` | **`0`** |
| Max stop (ticks) | `400` | **`200`** |
| VIX mode | `Off` | **`Directional`** |
| VIX symbol | `VX ##-##` | **`VX ##-##`** |
| Status every N bars | `120` | **`12`** |

### Why each of these

| Setting | Reason |
|---|---|
| Session 09:30–15:45, flat 15:55 | Disjoint from the overnight instance, so the two can never hold positions at once. Neither knows about the other, and your account limits apply to the pair. |
| Trade continuations **on** | In cash they beat reversals 0.90 to 0.40 on profit factor and 35% to 13% on win rate — the mirror of overnight, where reversals earn $455 a trade and continuations lose. Momentum belongs to the active session. |
| VIX symbol `VX ##-##` | Measured better than the index file and needs no files. A bare root fails at startup with *Unknown instrument*; `##-##` is the platform's front-contract placeholder. For a historical run, name the contract that was front during the window instead. |
| Max stop 200, max setup risk 0 | Inherited from the overnight sweep where 200 was monotonically best. In cash the stop band did nothing, so this is not fitted to the session. |
| Consecutive losses 2, daily loss $1,500 | Constraints rather than predictions, so they need no validation. The cash runs reached $9,010 of drawdown without them. |
| Status every 12 bars | Frequent enough to read a live day. At the shipped 120 you get about one line a session. |

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
