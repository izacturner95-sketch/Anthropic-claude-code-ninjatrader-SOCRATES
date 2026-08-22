# What has actually been measured

Every figure here comes from a Strategy Analyzer run that was pasted back and read. It
exists because the same questions kept being re-answered from memory, and because a
strategy accumulates opinions faster than it accumulates evidence.

**Read the sample sizes.** Nothing here is large. The longest sample is two months.

**A number measured on data the live account cannot see is not a result.** It is an
interesting fact about a data set. Every figure below is tagged for whether it is
reproducible on a futures-only feed, because that is the only kind that can be forward
tested — and the best-performing configuration in both sessions is currently one that
cannot be.

---

## The controlled experiment: what the confirmations are worth

NQ 09-26, 5-minute, 2026-06-17 to 2026-08-18. 12,081 bars. Reversals only, continuations
off, stop band 20-250 ticks, consecutive-loss halt off, commission and 1 tick of slippage
modelled. Step 5 read `VIX.csv`; step 6's leader form read the seven equity CSVs.

Only step 6's source changes between rows. Everything upstream is identical — the funnels
match to within one setup.

| Configuration | Trades | Profit factor | Drawdown | Per trade | Return/DD |
|---|---|---|---|---|---|
| No confirmations | 80 | 1.71 | $6,887 | $324 | 3.8 |
| Step 5 from `VIX.csv` | 55 | 1.96 | $4,830 | $423 | 4.8 |
| Step 5 from `VIX.csv` + relative strength | 45 | 1.75 | $4,830 | $357 | 3.3 |
| **Step 5 from `VX 08-26`** | **50** | **2.03** | **$3,810** | **$458** | **6.0** |
| Step 5 from `VX 08-26` + relative strength | 45 | 2.13 | $3,495 | $511 | 6.6 |
| **Step 5 from `VIX.csv` + leaders** | **46** | **2.17** | **$3,021** | **$455** | **6.9** |

**Step 5 is worth having.** Alone it lifts profit factor from 1.71 to 1.96 and removes 30%
of the drawdown.

**And the futures beat the index at it.** `VX 08-26` outperformed the `^VIX` file on every
measure — 2.03 against 1.96, $3,810 of drawdown against $4,830, $458 per trade against
$423 — while being a platform series that needs no files at all.

The reason is in the skip counts: the file was stood aside on **52** setups, the futures on
**3**. `^VIX` is disseminated for about thirteen hours a day, so on a Globex chart it has
nothing to say overnight and those setups passed through unfiltered. VX trades nearly
23 hours, so it actually applies the test where the index could not. More coverage, not a
better signal — but the effect is the same and it is the tradeable one.

Two caveats on that number. `VX 08-26` was the *second* month for the first five weeks of
the window and only became front around 22 July, so the early data is thinner than live
trading would see; if anything that understates it. And live means `VX ##-##`, which rolls
monthly — the contract switch puts a discontinuity in the series, so expect one unreliable
reading per roll where a six-bar change is measured across it.

**The leader form of step 6 is worth having.** Added to step 5 it lifts 1.96 to 2.17 and
removes another 37% of drawdown. The nine trades it refused averaged $256 against a $423
book average — it is removing below-average trades, which is what a filter is for.

**Relative strength is unproven, in both directions.** Two tests, and they disagree:

| Added to | Trades removed | Profit factor | Net effect of the removed trades |
|---|---|---|---|
| Step 5 from `VIX.csv` | 10 | 1.96 → 1.75 | −$7,170, i.e. $717 each of *lost* profit |
| Step 5 from `VX 08-26` | 5 | 2.03 → 2.13 | +$90, i.e. essentially nothing |

On the first of those this file previously said relative strength was worse than nothing.
That was over-read from a ten-trade difference, and the second test contradicts it. What
both runs actually support is weaker and duller: **at a threshold low enough to keep a
usable trade count it removes five to ten trades and the result moves within noise.** It
has not been shown to help or to hurt.

It was also tested at three thresholds — 0.75 passed 25% and left too few trades to judge,
0.40 passed 85% and barely filtered. Choosing between them on this one window would be
fitting rather than measuring.

It ships off because an unproven filter should be off, not because it was shown to fail.

At matched selectivity the comparison is unusually clean: both forms of step 6 rejected
**exactly twelve** setups from the same pool of eighty, and on that run the leader form's
twelve were the better twelve to lose. That is the strongest single piece of evidence
between the two forms, and it is still one run and twelve trades.

---

## When it trades, and whether the fills are believable

Split from a single run at the best configuration — step 5 on `VX 08-26`, reversals only:

| | Trades | Profit factor | Per trade |
|---|---|---|---|
| Cash session 09:30–16:00 | 3 | — | −$952 |
| Overnight | 47 | 2.33 | $548 |

**Ignore the cash number** — three trades says nothing about whether the cash session
works. **Read the split.** Overnight is about 70% of the bars on a Globex chart and
supplies 94% of the trades, so this is an overnight strategy and always has been. Nothing
was measuring it. The sweep-and-reclaim sequence completes far more often in quiet
conditions; in the cash session, faster directional moves invalidate setups before they
retest.

That makes fill realism the first thing to check rather than a footnote, because the
strategy trades where the book is thinnest.

| Slippage | Trades | Profit factor | Net | Drawdown |
|---|---|---|---|---|
| 1 tick | 50 | 2.03 | $22,915 | $3,810 |
| 3 ticks | 50 | 1.98 | $22,225 | $3,930 |

**Tripling the slippage assumption costs 2.5% of profit factor** — $690 across 50 trades.
The mechanics predict $760: two extra ticks is $10 a fill, the 24 winners exit on a limit
so they pay it once, and the 26 losers pay it on the entry and again on the stop. Coming
in at $690 against that is the result behaving exactly as it should, with no hidden
nonlinearity, which matters more than the size of the number.

The edge has real margin over its execution assumptions rather than depending on them.
That was the open question about a strategy trading 94% of the time in the thinnest hours,
and it is answered.

Two things this does not cover. Prop firms commonly restrict overnight positions and
charge several times the day margin, which is a rule question rather than a measurement
one. And a strategy that only trades when the market is quiet is exposed to that
regime ending.

---

## The tightened overnight configuration

NQ, 5-minute, 2026-05-17 to 2026-08-19. Reversals only, step 5 on `VX 08-26`, step 6 off,
no files. `Max setup risk` 2.5 ATR, `Min reward:risk` 1.3, `Max stop` 200 ticks — tighter
than the configuration in the table above on all three.

| | |
|---|---|
| Trades | 46 (21 won, 46%) |
| Profit factor | **2.72** |
| Net | +$29,590 |
| Largest drawdown | $3,255 |
| Per trade | +$643 |
| Overnight / cash split | 42 trades at 2.81 / 4 trades at 1.81 |

Against the 2.03 recorded for `VX 08-26` on the looser settings, over a window a month
longer. Step 5 is behaving on the futures contract: 4 quiet-skips against 50 direction
rejections and 74 confirmations.

**It survives losing its best trade.** One trade returned +15.97R. Its dollar value is not
in the summary — risk ranged from $227 to $986 across the book — so the removal is bounded
rather than exact: at the mean risk of $650 it is worth about $10,400 and the remaining 45
trades come to profit factor 2.1; at the largest risk in the book, 1.8. Lower either way,
profitable either way. The result is concentrated, which is normal for swing-target exits,
but it does not rest on a single fill.

**The window is not as out-of-sample as it looks.** Two of its three months are the window
every threshold was chosen on. Only a month is genuinely new.

**The window claim here was wrong.** This run began 2026-05-17 and that was attributed to
`VX 08-26` history starting there. A later run with the same symbol reaches back to
2026-03-15 with `no data 0` — so either the history was extended in between or the
truncation had another cause. Do not treat VX as bounding the backtest window.

### The base system over five months

Same settings, `Vix mode` off, so nothing bounds the window but NQ's own bars:
2026-03-13 to 2026-08-19.

| | All hours | **Overnight only** | Cash only |
|---|---|---|---|
| Trades | 78 | **63** | 15 |
| Profit factor | 1.47 | **1.93** | 0.38 |
| Net | +$17,035 | **+$23,665** | −$6,630 |
| Per trade | +$218 | **+$376** | −$442 |

**The unfiltered reversal system is profitable across five months and 78 trades**, two of
those months outside the window every threshold was chosen on. That is the result the whole
exercise was for. It is the opposite of what the cash session returned under the same test,
and it is why one of these sessions is a strategy and the other is closed.

**The overnight instance is trading the cash session and losing money doing it.**
`Trading hours` = `ExtendedHours` runs 18:00 to 16:45, which contains the whole cash
session. Fifteen of the 78 trades were taken in it, at 0.38 and −$442 each, against $376
each overnight. Excluding them lifts the run from 1.47 to **1.93** and adds $6,630 — the
largest single improvement available here, and it requires no new belief about anything.
The cash session's own five-month test independently returned 0.91 unfiltered.

`TradingHoursMode.Custom` handles a window that wraps midnight. **But see below — the
obvious way to configure it is wrong.**

### Not entering during cash is not the same as not holding into it

Acting on the above with `Session start` 180000, `Session end` 090000, `Flatten` 092500 made
the result worse, not better. Same window as the 2.72 run, step 5 on, cash entries now zero:

| | Cash included | Cash excluded, flatten 09:25 |
|---|---|---|
| Trades | 46 | 41 |
| Profit factor | **2.72** | 2.07 |
| Net | +$29,590 | +$16,425 |
| Best trade | **+15.97R** | +7.14R |
| Overnight net | $28,340 | $16,425 |

Overnight entries barely moved — 42 to 41 — while overnight net fell by $11,915. The best
trade in the book disappeared and two of the four trades above +5R went with it.

**A flatten time truncates winners that were entered overnight and needed the cash session
to reach target.** The session gate stopped the losing cash *entries*, which was the point,
and the flatten window silently also stopped every overnight position from running past
09:25 — which was not. Those are two different decisions and one setting was doing both.

The correct configuration keeps the entry restriction and removes the flatten:

| Setting | Value |
|---|---|
| `Trading hours` | `Custom` |
| `Session start` | `180000` |
| `Session end` | `090000` |
| `Flatten` | **`180000`** — equal to `Session start`, which disables the flatten window |

`IsExitOnSessionCloseStrategy` still closes anything open at the daily session boundary, so
nothing is left running indefinitely. Untested as of writing: the prediction is roughly the
42 trades and $28,340 of the 2.72 run's overnight half, without its four cash entries.

**One risk note.** The largest single loss was $1,705 against a 200-tick cap worth $1,000 —
341 ticks, well past the stop. Twenty-two of 78 trades overran 1R, worst −2.46R, costing
0.08R a trade. That is the price of holding through thin hours and it is not a bug, but a
funded account should be sized on the $1,705 rather than the $1,000.

---

## The one out-of-sample test

Everything above was measured on 2026-06-17 to 2026-08-18, which is also the window every
parameter was chosen on. This is the same configuration — step 5 on VX, reversals only,
200-tick stop band, 3 ticks of slippage — run on **2026-03-13 to 2026-06-18**, which none
of it was tuned on.

| | Tuned window | Out of sample |
|---|---|---|
| Trades | 35 in 2 months | **12 in 3 months** |
| Rate | ~17 a month | **~4 a month** |
| Profit factor | 2.77 | 2.65 |
| Drawdown | $3,165 | $2,285 |
| Win rate | 51% | **33%** |
| Structure shifts discarded as too wide | 55 of 397 (14%) | **146 of 263 (55%)** |

**The parameters did not invert**, which is more than most over-fitted sets manage. That
is the encouraging part and it is the whole of it.

**One trade carries the window.** Best R was +15.97 against risk that ranged $337 to $980,
so that single trade was worth between $5,400 and $15,000 of a $14,985 gross profit —
somewhere between a third and all of it. Strip it out and the window is around break-even.
On twelve trades, a profit factor resting on one outcome is not a measurement.

**The strategy is regime-dependent, and was tuned in a generous regime.** Four trades a
month against seventeen, and the funnel thinned everywhere: `Max structure distance (ATR)`
rejected 55% of structure shifts in the earlier window against 14% in the tuned one. The
geometry this strategy looks for was simply rarer between March and June.

That matters beyond the profit factor. Four trades a month is a different proposition for
a prop account's activity rules, and for how long you would wait before knowing something
had broken.

**Verdict: not disconfirmed. Not confirmed either.** The next thing worth doing is more
history, not more parameters — a year out of sample would settle what three months cannot.

---

## Continuations

Measured four times, in four configurations, and negative or marginal in all of them.

| Sample | Result |
|---|---|
| 147 trades, unselective | −0.08R, profit factor 0.95 |
| 114 trades, major levels only | +0.49R, profit factor 1.40 |
| 63 trades, alongside reversals | profit factor 1.19, **$77** per trade |
| 6 trades, with relative strength | profit factor 0.85, **−$82** per trade |

The per-trade figures are the ones that matter, and they are marginal at best against
reversals earning $455 in the same runs.

**They also displace better trades, which no earlier test accounted for.** In one run
reversals produced 17 trades alongside continuations and **46** without them — a
continuation holding a position blocks every setup behind it. Their $4,840 of profit was
sitting on roughly $13,000 of opportunity cost.

**A fifth sample, and the largest.** Continuations run on the overnight session alone —
5-minute, 2026-03-13 to 2026-08-18, entries 18:00–09:00 only, no flatten window — returned
**0.83 over 115 trades**, −$7,950, with $15,590 of drawdown. Reversals over a comparable
window returned 1.47 to 1.93. The direction is not marginal any more.

**This also confirms the sessions want opposite setups.** Continuations are the cash
session's earner at 1.85 and the overnight's loser at 0.83; reversals are the reverse. That
symmetry has now been measured on five-month samples at both ends rather than inferred from
a few dozen trades.

Settled. Off overnight, on in cash.

---

## The cash session: four runs that looked decisive

A second instance was set up for regular hours only: 2-minute bars, `Trading hours`
Regular, otherwise the tuned configuration. Same window, 2026-06-17 to 2026-08-19.

**The 2-minute chart fixed detection.** On 5-minute bars the cash session produced about
10% of retests against the ~29% of bars it occupies — the sequence was too slow for fast
conditions, needing up to 41 bars (3.4 hours) against a 6.5-hour session. On 2-minute bars
cash retests came in at 27–31%, proportional. A cash 2-minute bar covers roughly what an
overnight 5-minute bar does, so every threshold meant again what it had been tuned to mean.

Detection was never the problem. The trades were.

| Test | Trades | Profit factor | Mean R | With perfect stops |
|---|---|---|---|---|
| Min stop 20 | 23 | 0.54 | −0.40 | −0.09 |
| Min stop 75 | 15 | 0.54 | −0.40 | −0.25 |
| Min stop 100 | 11 | 0.66 | −0.31 | −0.15 |
| Continuations on | 43 | 0.59 | −0.47 | −0.07 |

**Raising the minimum stop did nothing.** Worst case halved from −4.02R to −1.99R and
expectancy did not move — identical profit factor and mean R across materially different
trade populations. The counterfactual got *worse*, so the small-stop trades being filtered
out were among the better ones. Stop overruns were a symptom, not the cause.

**Both premises fail, and the better one is the opposite of overnight's:**

| Setup kind | Trades | Win rate | Profit factor | Per trade |
|---|---|---|---|---|
| Reversals | 23 | 13% | 0.40 | −$272 |
| Continuations | 20 | 35% | 0.90 | −$33 |

Continuations beat reversals decisively here, which is the mirror of the overnight result
where reversals earned $455 a trade and continuations lost. **Mean reversion belongs to
the quiet hours and momentum to the active ones** — the regime theory holds. It is simply
not enough: 0.90 is near breakeven, not above it.

**And the ceiling is breakeven regardless.** With stops that never slipped, the best cash
run would be −0.07R. Removing every overrun — which no live account can — reaches nothing
rather than profit. That is what closed the question.

Four runs, and at the time this read as decisive. It was not — see the run below, which
reached breakeven and changed the conclusion. What survives from these four is the
diagnosis, not the verdict: the 2-minute fix, the minimum stop being inert, and
continuations being the cash session's earner. All three held up.

---

## The cash session, reconsidered

The four runs above shared a flaw worth naming: they varied one parameter at a time
against a configuration that had not been ported. Loading the whole overnight
configuration and changing the session produced a materially different result.

**Run: cash template, 2026-06-17 to 2026-08-18, 2-minute bars, `NQ 09-26`, slippage 1.**

| | |
|---|---|
| Trades | 52 (18 won, 35%) |
| Net | +$290 |
| Profit factor | **1.02** |
| Per trade | +$5.58 |
| Largest drawdown | $5,455 |
| Mean R | −0.04 (risk varies 6x, so read the dollars) |

Breakeven, not profit. But the previous best was 0.59 with a −0.07R *ceiling*, and this
run cleared it with real stops rather than perfect ones. The ceiling claim was wrong.

**The split is now extreme:**

| Setup kind | Trades | Win rate | Profit factor | Per trade |
|---|---|---|---|---|
| Reversals | 4 | 25% | 0.12 | −$474 |
| Continuations | 48 | 35% | **1.14** | +$46 |

Four reversal trades lost $1,895 while 48 continuations made $2,185. The direction agrees
with the earlier runs and the magnitude is larger. **Four trades is not a sample** — the
finding here is that continuations alone are above 1.0, which the 20-trade sample could
not establish. `Trade reversals` was added as a switch so this can be tested rather than
inferred by subtraction.

**Detection is now proportionate and correctly gated.** Cash took 29% of sweeps, 26% of
shifts and 29% of retests against ~29% of bars, and produced 100% of setups — the overnight
hours generated 8,037 sweeps and zero setups, so the session gate is doing exactly its job.

**The confirmations are removing 64% of setups, unmeasured.** Of 197 completed setups,
step 5 rejected 70 and step 6 rejected 57, leaving 52 entries. Both were validated on the
overnight session and neither has been tested here. That is the largest untested lever in
the run and the obvious next experiment.

**Step 5 auto-passed 74 setups as "source quiet."** With `Vix max data age` at 15 minutes
and the index only trading 09:30–16:15, 39% of cash setups skipped step 5 entirely. During
cash hours the VIX file should be current, so this is either gaps in the downloaded data
or a threshold set for the overnight problem being applied where that problem does not
exist. Worth checking before reading anything into step 5's cash numbers.

**Stop overruns cost 0.06R a trade.** Six of 52 trades lost more than 1R, worst −2.96R.
Perfect stops would give +0.02R against the actual −0.04R. Smaller than overnight, as
expected in a liquid session, and not the thing standing between this and profit.

### Reversals off: the cash session's first profitable run

Same window, same everything, `Trade reversals` set false.

| | Reversals on | **Reversals off** |
|---|---|---|
| Trades | 52 | 54 |
| Win rate | 35% | 37% |
| Net | +$290 | **+$5,660** |
| Profit factor | 1.02 | **1.35** |
| Per trade | +$5.58 | **+$104.81** |
| Largest drawdown | $5,455 | **$2,375** |
| Worst trade | −2.96R | −1.40R |
| Largest loss | $1,310 | $820 |
| Average loss | $527.65 | $480.59 |
| Stops that overran | 6 | 4 |

**The gain is not the subtraction.** Removing four losing trades was worth $1,895, which
would have given $2,185. The run returned $5,660 — $3,475 more than removing them can
explain, and the continuation-only profit factor rose from 1.14 to 1.35 on a population of
the same kind.

Two mechanisms, both structural rather than statistical:

- **Reversals occupied entry slots.** A setup in progress blocks what is behind it, so
  continuations that would have qualified never got the chance. Completed setups rose from
  197 to 227 on identical input.
- **Reversals masked same-bar continuations.** The detector resolves one event per bar, so
  a reclaim and a break landing together meant the break was discarded. Continuation
  emissions rose from 4,436 to 5,201 — 765 events that existed all along and were never
  reported.

The drawdown and tail improvements come free with that: nothing in this run was aimed at
risk, and the worst trade still halved.

**`Structure shifts: 0` is correct here, not a fault.** A continuation skips step 3 by
design — the break through the level *is* the structural event, so the engine goes straight
to the retest. With reversals off nothing takes the step-3 path at all. The consequence
worth knowing: **`Min displacement (ATR)`, `Max bars sweep to shift` and `Max structure
distance (ATR)` do nothing in this configuration.** Do not spend runs tuning them.

**Read this at its sample size.** 54 trades, in-sample, one two-month window, one regime.
1.35 is the first cash number above breakeven, not a validated edge — and it was found by
looking at this same window, which is how overfitting begins. The out-of-sample question
is open and it is the one that matters now.

**And 1.35 is not a live number.** Step 6 rejected 66 setups in this run using leader data
that came from files — cash equity snapshots this feed cannot supply in real time, and
which the CME single stock futures do not substitute for at usable history. Whatever the
live configuration turns out to be, it is not this one. The two candidates that survive the
constraint are step 6 off entirely, and `Breadth source` = RelativeStrength, which reads
NQ against ES and is futures-only on both sides. Neither has been measured on this session.

### Moving off the files

Three runs, same window, same reversals-off baseline, varying only where the two
confirmations read from.

| Step 5 | Step 6 | Trades | Profit factor | Net | Drawdown | Per trade |
|---|---|---|---|---|---|---|
| `VIX.csv` | leader files | 54 | 1.35 | +$5,660 | $2,375 | +$105 |
| platform series | off | 52 | 0.81 | −$3,845 | $7,005 | −$74 |
| platform series | NQ vs `ES 09-26` | **17** | **1.73** | +$3,510 | $2,380 | +$206 |

**The file and the platform series are not the same step 5.** This was predicted to barely
move, on the reasoning that `^VIX` is the series the CSV holds. It moved a great deal:
direction rejections went from 75 to 149, quiet-skips from 81 to 7, and the largest
six-bar move the confirmation ever saw went from 0.81 to 2.66. Whatever the CSV contains,
it is a materially quieter series than the feed's — which also explains the 81 quiet-skips
that prompted the coverage checker in the first place. The prediction was wrong and the
file, not the feed, is the suspect source.

**`VIX` and `^VIX` appear to be the same data here.** The run made with the stock listing
and the run made with the index produced near-identical step 5 statistics — mean six-bar
move 0.18 against 0.17, and the same 2.66 maximum. An earlier note here blamed that run's
collapse on confirming against the wrong instrument. That was almost certainly wrong: the
difference was the file coming out, not the symbol changing. It is recorded because the
mistake is instructive — the banner now names the source, and a source that *looks* wrong
is not evidence that it *is*.

**Relative strength is doing real work, on a sample too small to bank.** Against the same
step 5, adding it moved 0.81 to 1.73 and turned −$3,845 into +$3,510. It also cut the run
to 17 trades, rejecting 57 of the 86 setups it saw. Seventeen trades cannot separate 1.73
from 1.35, and the configuration that earns the most total dollars is still the file-based
one. What can be said is that the futures-only path is not obviously dead, which was the
open question.

### The full decomposition, and what it exposes

| Step 5 | Step 6 | Trades | Profit factor | Net | Drawdown |
|---|---|---|---|---|---|
| **off** | **off** | **95** | **0.96** | **−$1,490** | **$9,340** |
| feed | off | 52 | 0.81 | −$3,845 | $7,005 |
| off | NQ vs ES | 26 | 0.95 | −$485 | $3,970 |
| feed | NQ vs ES | 17 | 1.73 | +$3,510 | $2,380 |
| `VIX.csv` | leader files | 54 | 1.35 | +$5,660 | $2,375 |

Read the first row first. It is the largest sample here and the only one that is not a
subtraction from something unmeasured.

**The unfiltered cash session is breakeven: 0.96 over 95 trades.** That is the signal
everything else has been trying to rescue. It is not an edge with a data problem; it is not
an edge.

**Step 5 on the platform series is actively harmful.** It removes 43 trades and takes 0.96
down to 0.81. Every version of "step 5 helps" in this document was measured on the overnight
session, where it does. In cash it costs money.

**Relative strength does nothing to the edge.** 0.95 against a 0.96 baseline, on a third of
the trades. What it does do is cut drawdown from $9,340 to $3,970 — real, but that is
exposure reduction, available for free by trading smaller, and not a reason to believe the
filter knows anything.

**So the 1.73 is two filters that individually do nothing and harm, combining on 17
observations to produce the best number in the table.** Before the baseline existed that
was a reason for caution. With it, the honest reading is noise. Nothing should be built on
that cell.

**And the 1.35 needs re-reading too.** It beats the baseline on 54 trades, which is the
only claim in this table that still stands up — but it depends on leader files that cannot
be supplied live, and it has never been tested outside this window.

### The five-month run, which closes it

With both confirmations off nothing limited the backtest to one contract's history any
more, so the window went back to 2026-03-13. **147 trades, profit factor 0.91,
−$4,985.**

The added three months can be separated by subtraction, since the two-month run is a subset
of this one:

| Window | Trades | Profit factor | Net | Per trade |
|---|---|---|---|---|
| Two months (in-sample) | 95 | 0.96 | −$1,490 | −$16 |
| **Three months added** | **52** | **0.82** | **−$3,495** | **−$67** |
| Five months combined | 147 | 0.91 | −$4,985 | −$34 |

**The out-of-sample period is worse than the window everything was tuned on**, and the
combined figure moved away from breakeven as the sample grew. That is the direction that
says 0.96 was the optimistic end of noise rather than a near miss.

**At this penetration threshold the cash session is finished.** Not for want of a filter,
and not for want of live data — the underlying continuation sequence loses money on the
largest sample taken at these settings. Every configuration above 1.0 in the table was a
subtraction from this population, and the two that looked best were 17 and 54 trades on the
tuning window.

**This verdict has since been overturned, and the qualifier is the whole point.** Requiring
24 points of penetration rather than 5 changes what counts as a broken level, and that
configuration returns 1.85 over five months with the out-of-sample third at 1.63 — see
*A deeper penetration requirement* below. What was closed was a threshold, not a session.
The general lesson stands: six runs of filter search could not rescue this population, and
one change to the entry premise did.

Worth stating what it cost to learn: about a week of evenings and no capital. The
alternative was discovering it on the funded account.

### A deeper penetration requirement

Unfiltered cash again — both confirmations off — but with the entry premise changed rather
than filtered. 2026-06-14 to 2026-08-18, 2-minute.

| | Original | **Deeper penetration** |
|---|---|---|
| Penetration required | max(0.05 ATR, **5 pts**) | max(0.07 ATR, **24 pts**) |
| Stop band | 20–200 ticks | 20–245 ticks |
| Trades | 95 | 90 |
| Win rate | 33% | **41%** |
| Profit factor | 0.96 | **1.95** |
| Net | −$1,490 | **+$30,790** |
| Per trade | −$16 | **+$342** |
| Largest drawdown | $9,340 | $4,665 |

**The mechanism is structural rather than a bolt-on**, which is the reason to take it more
seriously than the 1.73 that preceded it. Requiring 24 points of penetration instead of 5
changes what counts as a broken level: price poking five points through a level and holding
is not a break, it is noise, and the original threshold was treating the two the same. That
is a claim about the setup, not a filter applied after the fact.

**The sweep rate also doubled** — one per 3.4 bars against one per 5.8 — which a *higher*
penetration threshold should not cause. `Level merge (ATR)` was named here as the cause and
**that was wrong**: reverting it from 0.15 back to 0.45 moved the sweep count from 14,730 to
14,736, a difference of six, and the result from 1.85 to 1.86. It explains neither.

The likelier explanation is the one the deeper penetration itself produces. A candidate
occupies the detector's single slot per side until it either reclaims the level or runs out
of bars. Requiring 24 points of penetration means only decisive pokes start a candidate at
all — and a decisive poke is *less* likely to come back, so more candidates exhaust the clock
and emit a continuation instead of resolving as a reclaim. `Max bars to reclaim` falling from
6 to 4 shortens that clock, freeing the slot faster and raising throughput again. Both push
the same way, and neither has been isolated.

The full configuration is committed as `templates/SocratesNQ - Cash session.xml`.

### Isolating the parameters, one at a time

Each row is a single parameter moved against the same baseline, same window — not
cumulative. The baseline is `Min reward:risk` 1.2, level merge 0.45, everything else as
shipped.

| Parameter | Baseline | Reverted to | Sweeps | Trades | Profit factor | Net | Worst trade |
|---|---|---|---|---|---|---|---|
| — (baseline) | | | 14,736 | 134 | **1.86** | +$39,950 | −2.75R / $1,315 |
| `Min reward:risk` | 1.2 | 1.0 / 0.8 / 0.6 | 14,730 | 135–140 | 1.93 / 1.84 / 1.83 | flat | −2.75R |
| `Level merge (ATR)` | 0.45 | 0.15 | 14,730 | 135 | 1.85 | +$40,055 | −2.75R |
| **`Swing strength`** | **4** | **6** | 14,705 | 125 | **1.68** | **+$32,275** | −2.75R |
| **`ATR period`** | **8** | **16** | 14,739 | 134 | **1.54** | **+$25,640** | **−6.64R / $3,005** |

**`Min reward:risk` and `Level merge` are inert.** Neither moves the result beyond noise, and
level merge moves the sweep count by six in fourteen thousand.

**`Swing strength` matters, and 4 is right.** Raising it to 6 costs 0.18 of profit factor and
$7,675. It removes only nine trades — 134 to 125 — but nineteen percent of the profit, so the
setups it drops are well above average. A stronger swing requirement needs six bars either
side to confirm a pivot instead of four, which on 2-minute bars means the book only recognises
structure after twenty-four minutes rather than sixteen. In a session where the whole edge is
continuations off fresh breaks, that is too slow.

### ATR period: a real gradient

| ATR period | Trades | Win rate | Profit factor | Net | Drawdown | Worst trade | Largest loss |
|---|---|---|---|---|---|---|---|
| 16 | 134 | 42% | 1.54 | +$25,640 | $5,915 | −6.64R | $3,005 |
| 8 | 134 | 41% | 1.86 | +$39,950 | $5,055 | −2.75R | $1,315 |
| **6** | 139 | 45% | **2.13** | **+$47,010** | **$3,630** | −2.59R | $1,305 |
| 4 | 138 | **46%** | 2.07 | +$45,535 | $5,295 | **−2.46R** | $1,185 |

**Monotonic on every axis simultaneously** — profit factor up, net up, drawdown down, tail
less bad, win rate up. That is the shape this document accepts as evidence, and it is the
same standard the overnight stop band met at 200 > 250 > 300. Nothing here looks like the
single spike `Min reward:risk` produced.

The mechanism is the same one that made 16 bad, running the other way: ATR sets the stop
buffer, the retest zone, the zone half-width and the level merge distance all at once, so a
shorter period makes every one of those describe current conditions rather than the last half
hour. In a session whose whole edge is continuations off fresh breaks, describing now is
worth a great deal.

**It turns at 4, and gently.** Profit factor falls 2.13 to 2.07 — three percent, inside
noise, so on that axis 4 and 6 are the same. The separation is drawdown: **$3,630 at 6
against $5,295 at 4**, a 46% difference on the number a funded account is actually judged by.

That combination is worth reading carefully. Individual trades keep improving all the way
down — the worst trade runs −6.64R, −2.75R, −2.59R, −2.46R monotonically — while the *run's*
drawdown turns up. Better trades and a worse equity curve means the losses at ATR 4 arrive
closer together. A four-period ATR on 2-minute bars is close enough to bar range that its
thresholds move with each bar, so in a choppy stretch every setup is being measured against
a threshold that just moved, and the misjudgements correlate.

**Six is a rounded top, not a spike**, which is the shape worth trusting: the parameter can
drift a step in either direction without the strategy falling over. Settled at 6.

### ATR 6 holds out of sample, and improves the out-of-sample half most

The concern with tuning a parameter on the full five months is that the out-of-sample third
stops being out of sample. Re-running ATR 6 on the tuning window alone and subtracting:

| Window | Trades | Profit factor | Net | Per trade |
|---|---|---|---|---|
| Jun 14 – Aug 18 (tuning) | 91 | 2.19 | +$34,660 | +$381 |
| **Mar 13 – Jun 14 (out of sample)** | **48** | **1.98** | **+$12,350** | **+$257** |
| Combined | 139 | 2.13 | +$47,010 | +$338 |

Against the same split at ATR 8:

| | ATR 8 | ATR 6 | Change |
|---|---|---|---|
| In-sample | 1.95 | 2.19 | **+12%** |
| Out-of-sample | 1.63 | 1.98 | **+22%** |

**The out-of-sample half improved nearly twice as much as the window the parameter was chosen
on.** Overfitting produces the opposite: large in-sample gains that shrink or reverse outside
it. This is not proof — the period was still selected while looking at both halves — but it
is the pattern a real effect makes, and the one a fitted parameter almost never does.

Out-of-sample profit factor 1.98 over 48 trades, on a configuration needing no files, no
confirmations and no data the live account cannot see. That is the strongest result in this
document by a clear margin.

**`ATR period` 6 is now the committed template default.** ATR 4 remains untested; the
gradient must turn somewhere and finding the turn is still worth a run.

**`ATR period` matters most, and the profit factor understates it.** Sixteen instead of eight
costs 0.32 of profit factor on the same 134 trades — but the tail is where the damage is:
worst trade goes from −2.75R to **−6.64R**, largest single loss from $1,315 to **$3,005**, and
the widest stop the structure implied from 562 ticks to 726.

The mechanism is lag. Stops sit a quarter-ATR beyond the retest extreme, so the ATR sets the
buffer. A sixteen-period ATR on 2-minute bars averages the last thirty-two minutes; when
volatility rises the buffer is still describing the calm that preceded it, and the stop ends
up inside current noise. An eight-period ATR keeps up. For a funded account this is the more
important of the two findings on this page — a $3,005 loss against a $1,225 configured
maximum is the kind of trade that ends an evaluation.

**None of the four explains the sweep rate.** Every run sits at one sweep per 3.4 bars,
within thirty of each other. Whatever changed it is `Max bars to reclaim` 6 to 4, the
penetration change itself, or one of the smaller untested thresholds.

**Both parameters that matter are already at their better value in the shipped template.**
Four isolation runs, no change to make.

### Minimum reward:risk: a plateau, not a gradient

Four values, same window, same everything else. This parameter rejected nothing at any
setting — the `discarded, reward below R` line never appeared — so its only effect was which
target each setup used.

| Min R:R | Trades | Win rate | Gross wins | Gross losses | Net | Profit factor | Drawdown | Swing / fallback targets | Sub-1R wins |
|---|---|---|---|---|---|---|---|---|---|
| 1.2 | 135 | 41% | $87,145 | $47,090 | +$40,055 | 1.85 | $5,055 | 586 / 46 | 3 |
| **1.0** | 135 | 43% | $87,315 | $45,320 | **+$41,995** | **1.93** | $4,830 | 595 / 37 | 3 |
| 0.8 | 136 | 44% | $82,395 | $44,720 | +$37,675 | 1.84 | $5,355 | 601 / 31 | 10 |
| 0.6 | 140 | 44% | $82,175 | $44,995 | +$37,180 | 1.83 | **$4,420** | 608 / 24 | 13 |

**Profit factor spans 1.83 to 1.93 — a spike at 1.0, not a trend.** The 1.93 does not survive
its neighbours: 0.8 falls straight back to where 1.2 was. A single peak between two lower
readings is the shape of noise, and the same standard that accepted the overnight stop band
(200 > 250 > 300, monotonic) rejects this one.

**The underlying tradeoff is real and it cancels.** Win rate rises monotonically as the
threshold falls — 41, 43, 44, 44 — and gross wins fall with it, $87,145 down to $82,175.
Sub-1R wins go from 3 to 13. Nearer targets are hit more often and pay less, in almost exact
proportion. That is what a plateau looks like from the inside, and it is a more useful finding
than the 1.93 would have been: **the strategy is not sensitive to this parameter**, so it does
not need defending in live trading and does not need re-tuning when conditions change.

Left at 1.2. Any value between 0.6 and 1.2 is the same strategy.

### It passes out of sample

Extended to 2026-03-13, same settings. The two-month run is a subset, so the added months
separate by subtraction:

| Window | Trades | Profit factor | Net | Per trade |
|---|---|---|---|---|
| Jun 14 – Aug 18 (tuning window) | 90 | 1.95 | +$30,790 | +$342 |
| **Mar 13 – Jun 14 (out of sample)** | **45** | **1.63** | **+$9,265** | **+$206** |
| Five months combined | 135 | 1.85 | +$40,055 | +$297 |

**This is the first configuration in this document to survive that test.** Every previous
cash candidate degraded when the window grew — the original threshold went 0.96 in-sample to
0.82 out, and the combined figure moved *away* from breakeven as the sample grew. This one
gives up profit factor out of sample, as anything tuned on a window should, and stays
comfortably profitable. Win rate held at 41% across both.

**It survives losing its best trade.** One trade returned +15.46R. Its dollar value is not
in the summary and risk ranges elevenfold, so this is bounded rather than exact: at mean
risk it is worth about $9,545 and the remaining 134 trades come to 1.65; at the largest
risk in the book, 1.45. Profitable either way.

**Drawdown is $5,055 across five months**, against $9,340 for the original threshold over a
shorter window.

**Two things still owed on this result.** The upstream change that doubled the sweep rate is
still unrecorded, so the run is not reproducible from this document alone — the penetration
threshold cannot account for it. And 21 of 135 trades overran 1R, worst −2.75R, with the
largest single loss $1,315 against a $1,225 cap; perfect stops would give +0.62R against the
actual +0.53R.

---

### One thing that is structurally interesting, and is not a reason to reopen this

At a 32% win rate against roughly 2R targets, this system sits almost exactly on its
arithmetic breakeven line by construction — and stop overruns push it under. Thirty-two of
147 trades lost more than 1R; with stops that always held, mean R would be +0.04 instead of
−0.04. So the binding constraint here was never the entry filter that six runs went looking
for. It was the exit.

That is a genuine observation and it belongs to whatever gets built next, not to this. A
system that needs perfect stop execution to reach breakeven does not have an edge to
protect.

---

## Risk settings

**The stop band is the largest single lever on drawdown**, and the relationship is
monotonic. Swept at the best configuration, 3 ticks of slippage:

| Max stop | Trades | Profit factor | Drawdown | Per trade | Return/DD | Max risk |
|---|---|---|---|---|---|---|
| **200 ticks** | 35 | **2.77** | **$3,165** | **$578** | **6.4** | $986 |
| 250 ticks | 50 | 1.98 | $3,930 | $445 | 5.7 | $1,234 |
| 300 ticks | 58 | 1.85 | $5,545 | $413 | 4.3 | $1,479 |
| 2.5 ATR, 400 backstop | 59 | 1.76 | $7,655 | $401 | $1,931 |

Profit factor, per-trade, drawdown and return-to-drawdown all move consistently across the
range. A monotonic gradient is better evidence than a spiky optimum would be, and there is
a mechanism to go with it: the stop comes from the retest pullback, so a wide stop means
the retest went a long way against the setup before confirming — a structurally worse
setup, not merely a larger one. Win rate rises as the band tightens, which a pure risk
control would not do.

**Normalising it to ATR made it worse, and that is the interesting result.** `Max setup
risk (ATR)` at 2.5 was tried on the reasoning that a fixed tick count is calibrated to one
window's volatility and will not travel. It came last: profit factor 1.76 and the worst
drawdown of the four. An ATR-scaled cap admits large stops *precisely when volatility is
high*, which is what normalising means, and those trades lost money.

So the fixed cap is doing something the normalised one cannot: it is a volatility filter
as much as a risk filter, and the strategy is worse in volatile conditions. The tension is
unresolved — 200 ticks is still a constant fitted to two months, and only out-of-sample
data can say whether it captures a real property or that window's ATR.

**The consecutive-loss halt costs money and buys drawdown.** Removing it added nine trades
and $7,190 of net, and $1,630 of drawdown — with the worst trade deteriorating from −1.12R
to −1.55R and stop overruns rising from 8 to 11. It refuses trades after two losses, which
is when conditions are worst, and the extra trades it lets through are the bad ones.

---

## Earlier samples, for context

Different settings, so not directly comparable, but the profit factor has been stable
across every honest sample the strategy has produced:

| Range | Trades | Profit factor | Notes |
|---|---|---|---|
| 7 months | 43 | 1.67 | steps 5 and 6 off |
| 2 months | 41 | 1.71 | steps 5 and 6 off |
| 2 months | 80 | 1.71 | steps 5 and 6 off, stop band 250 |

Three independent windows between 1.67 and 1.71 without confirmations. That consistency is
worth more than any single result in this file.

**Ignore the twelve-day runs.** Profit factors of 7.39 and 9.19 were recorded on 15 and 16
trades. They are noise and they are recorded here only so nobody goes looking for them
again.

---

## Size and the daily cap have to scale together

The cash configuration at two contracts instead of one, everything else identical:

| | 1 contract | 2 contracts |
|---|---|---|
| Trades | 135 | **90** |
| Net | +$40,055 | +$36,890 |
| **Net per contract** | **+$40,055** | **+$18,445** |
| Profit factor | 1.85 | 1.67 |
| `Daily risk budget` rejections | 27 | **84** |
| Mean risk per trade | $617 | $938 *(2× would be $1,235)* |
| Max risk per trade | $1,214 | $1,470 *(2× would be $2,429)* |

**Two contracts made less money than one.** Not less per contract — less in total. Taking
the same 135 trades at double size would have returned about $80,110; the run returned
$36,890, so the sizing removed roughly $43,000 of profit.

**The cause is a pre-trade check, not the market.** An entry is refused when its own intended
risk exceeds what is left of the day's budget:

```
intendedRisk   = stopDistanceTicks * TickValueDollars * contracts
remainingBudget = MaxDailyLossDollars + min(0, dailyRealisedPnL)
```

`intendedRisk` scales with contracts. `MaxDailyLossDollars` does not. So doubling size halves
the stop distance the budget can afford — at $5 a tick, a $1,500 cap affords 300 ticks at one
contract and 150 at two, against a stop band reaching 245 and a median setup implying 130–190.

**The damage is selective, which is why it hurts more than it should.** The rejections are not
spread across the book: they take the widest-stopped setups first. The mean and maximum risk
per trade both came in far below double, which is the fingerprint — the large-risk trades were
never submitted. Those are also where the outsized winners live, so raising size quietly
converts the strategy into a tight-stop-only version of itself and then trades that version.

**Scaling the cap with the contract count restores it exactly.** Two contracts against a
$3,000 cap, against one contract at $1,500:

| | 1ct @ $1,500 | 2ct @ $3,000 | Ratio |
|---|---|---|---|
| Setups / entries / trades | 244 / 135 / 135 | 244 / 135 / 135 | 1.000 |
| Stop band, halted, budget rejections | 61 / 18 / 27 | 61 / 18 / 27 | 1.000 |
| Profit factor | 1.85 | 1.85 | 1.000 |
| Net | +$40,055 | +$80,110 | **2.000** |
| Largest drawdown | $5,055 | $10,110 | **2.000** |
| Largest loss | $1,315 | $2,630 | **2.000** |
| Risk per trade, min / mean / max | $107.82 / $617.37 / $1,214.31 | $215.64 / $1,234.75 / $2,428.62 | **2.000** |

Identical book, identical funnel, every dollar figure exactly doubled. Size is neutral once
the cap scales with it, which is what it should have been all along — the earlier collapse
was entirely the fixed cap, not the market.

**So the rule is simply that `Max daily loss ($)` is a per-contract figure in disguise.**
Set it as *(cap you want per contract) × contracts* and results stay comparable across
sizes. If the account's real daily limit will not stretch that far, that is the binding
constraint and it should be read as one: this stop band does not support that size. Trade
one contract, or use MNQ at ten micros per NQ with `Tick value ($)` set to 0.50, which
reaches the same exposure in tenths and leaves the dollar arithmetic unchanged.

The startup banner now warns when a fresh day's budget cannot afford the full stop band at the
configured size, and separately when it cannot afford even the minimum stop.

**What scales with it is the loss, and that is the number a funded account is judged on.**
At two contracts this configuration draws down $10,110 and its worst single trade is $2,630.
Profit factor does not care about size; a trailing drawdown limit does.

---

## The deeper penetration does not travel to the overnight session

Overnight instance, reversals only, entries 18:00–09:00, step 5 on, 5-minute,
2026-03-13 to 2026-08-18. Only the penetration threshold changed.

| | 5 points / 0.05 ATR | 24 points / 0.07 ATR |
|---|---|---|
| Trades | 63 | 71 |
| Profit factor | **1.93** | 1.28 |
| Per trade | +$376 | +$131 |
| Mean stop implied | ~164 ticks | **291 ticks** |
| Largest stop implied | — | 1,253 ticks |
| Largest drawdown | — | $9,895 |

*(The 1.93 column is the overnight share of an all-hours run rather than a session-gated one,
so it is indicative rather than exactly matched.)*

**The mechanism is stops, and it is the mirror of why it works in cash.** Requiring 24 points
of penetration means the sweep must travel further past the level before failing — and the
stop sits below that extreme. Mean implied stop went from about 164 ticks to **291**, with
half the book above 224 and a maximum of 1,253. Wider stops on the same targets is worse
reward-to-risk, and the profit factor follows. In the cash session the same threshold buys
selectivity cheaply because 24 points is an ordinary poke there; overnight it is a violent
move, and the only sweeps that qualify are the ones that leave nowhere sensible to put a stop.

**Keep the thresholds per-session:** 24 points in cash, 5 overnight. Along with 2-minute
against 5-minute bars and continuations against reversals, that is now the third parameter
the two sessions want set opposite ways.

### Step 5 behaves differently here, and correctly

The shadow figures are the reverse of the cash result. Step 5 refused 46 trades averaging
**$76.85** and kept 25 averaging **$230.00** — it is selecting, keeping trades three times
better than the ones it drops. In cash it refused trades averaging $303.96 against a book
of $296.70 and was indistinguishable from random.

The total-dollar reading and the per-trade reading disagree here, and both are true: running
step 5 live gives 25 trades worth $5,750 in place of 71 worth $9,285. Better trades, fewer
of them, less money. At 25 trades that is under the sixty-trade bar this document sets for
believing anything, so it settles nothing on its own — but it is consistent with step 5
earning its place overnight, which was measured separately at 1.71 to 1.96.

---

## The overnight session with its hours corrected

Session gated to 18:00–09:00 with the flatten window disabled, reversals only, step 5 on
`VX 08-26`, step 6 on relative strength at multiple 0.3, slippage 3 modelled.
2026-03-15 to 2026-08-19, 5-minute.

| | |
|---|---|
| Trades | **33** |
| Win rate | 58% |
| Profit factor | **4.48** |
| Net | +$34,265 |
| Per trade | +$1,038 |
| Largest drawdown | **$1,520** |
| Mean R | +1.79 |

**The window is not five months.** `VX 08-26` history starts in mid-May, and the first trade
of this run was 2026-05-28 — so the effective range is **2.7 months, not 5.2**. Two
consequences, and they pull opposite ways. The trade *rate* is fine at roughly twelve a
month; the earlier reading of 1.5 a week was wrong. The *out-of-sample* position is worse
than stated: the overnight thresholds were tuned on 06-17 to 08-18, so all but three weeks
of this run sits inside the tuning window. There is essentially no out-of-sample content in
the 4.48 at all.

**And the progression is still the warning.** The overnight session has gone 1.93 on 63
trades, to 2.72 on 46, to 4.48 on 33. Profit factor rising as the sample shrinks is exactly
the shape that produced the cash session's 1.73-on-17-trades, which turned out to be noise.

**It does survive losing its best trade**, which is the one point in its favour. The +16.11R
trade is worth about $10,400 at mean risk; removing it leaves 32 trades at **3.42** and $745
each, or 2.85 at the largest risk in the book. So this is not one fill carrying a mediocre
book.

**The session gate is doing real work.** Forty-eight setups were refused as `OutsideSession`
— a third of everything the engine completed — and those are the cash-hours reversals that
the five-month split measured at −$442 each.

**Step 6 is inert here.** At `Min spread (multiple)` 0.3 it refused 6 setups and confirmed
53. It is switched on and filtering essentially nothing, which is worth knowing before any
weight is placed on it.

**The test the cash configuration passed cannot be run here.** Splitting this window leaves
three weeks and a handful of trades outside the tuning period — not a sample. And the data to
extend it does not exist: `VX ##-##` history dies with the contract, so no VX-based
configuration can ever reach back further than the current contract's life. This is a
permanent property of the instrument, not a gap to be filled.

That leaves two honest routes, and neither is a rerun:

- **Validate the base system instead.** `Vix mode` off has no data dependency, so the
  reversal sequence can be tested across as much NQ 5-minute history as exists. That is what
  settled the cash session, and it measures the part of the strategy that is actually
  testable. Step 5's contribution stays a shorter-window measurement — the controlled test
  put it at 1.71 to 1.96 — rather than something the long run can confirm.
- **Accept that step 5's out-of-sample evidence has to come forward, not backward.** Market
  Replay and forward testing are the only instruments that can produce it.

---

## The cash configuration does not travel to 24 hours

Same settings — 2-minute, 24-point penetration, continuations only — with the session opened
to the full day instead of 09:30–16:00.

| | Cash only | Full day |
|---|---|---|
| Trades | 135 | 223 |
| Profit factor | **1.85** | 1.23 |
| Net | +$40,055 | +$16,830 |
| Per trade | +$297 | +$75 |
| Largest drawdown | $5,055 | $10,600 |
| Stops that overran 1R | 21 of 135 (16%) | **74 of 223 (33%)** |
| Worst trade | −2.75R | **−5.29R** |

**And the cash session itself got worse, not just diluted.** Its own share fell from 135
trades at 1.85 to **52 trades at 1.34**. The cause is in the rejection table: the daily loss
halt blocked **336** setups, against 18 in the cash-only run. Overnight losses were spending
the daily budget before the cash session opened, so the profitable session was being switched
off by the unprofitable one. Fifty-six percent of all setups in this run never got a chance.

**Two-minute bars do not survive the overnight hours.** Mean stop distance fell from 186
ticks to 138 and the minimum to 15, because overnight ATR is smaller and every threshold
here scales off ATR. A fifteen-tick stop in thin hours is not a stop. A third of all trades
overran 1R, the worst by more than five, and perfect stops would have given +0.46R against
the actual +0.17R — overruns cost 0.29R a trade, against 0.09R in the cash-only run.

**This is why the two sessions have separate instances.** Cash wants 2-minute bars and the
overnight wants 5-minute; that was established early and this run is what ignoring it costs.
The daily risk limit is per-account, so two sessions sharing one instance also share one
budget, and the worse session spends it first.

---

## Tuning the confirmations: the answer was don't

Shadow run on the 1.85 configuration, five months, both steps Directional — step 5 on the
platform `^VIX`, step 6 on relative strength against `ES 09-26`. Every trade taken, verdicts
recorded, nothing blocked.

| | Trades | Won | Net | Per trade |
|---|---|---|---|---|
| **Neither step objected** | **4** | **0%** | **−$3,435** | **−$858.75** |
| Step 5 would refuse | 115 | 41% | +$34,955 | +$303.96 |
| Step 6 would refuse | 116 | 41% | +$41,065 | **+$354.01** |
| Both objected | 100 | | | |
| Refused by either (distinct) | 131 | | +$43,490 | +$331.98 |
| The book | 135 | 41% | +$40,055 | +$296.70 |

**Running both steps live would have left four trades, none of them winners, losing
$3,435.** That is the whole answer and it needed one run.

**Neither step changes the win rate at all.** Both refused sets won exactly 41%, the book's
own figure. Whatever these confirmations are measuring, it is uncorrelated with whether the
trade works.

**Step 5 samples; step 6 is worse than sampling.** Step 5's refused trades made $303.96 each
against a book average of $296.70 — indistinguishable from removing trades at random, and it
removes 85% of them. Step 6's made **$354.01**, nineteen percent *above* the book. It is
selecting against the largest payoffs, which is the exact inverse of a filter's job.

Since the win rates match, the difference is entirely in the size of the winners: the trades
step 6 refuses are the ones that ran furthest. There is a tempting hypothesis in that — that
the best cash continuations happen precisely when NQ is *not* leading ES, so the inverse of
step 6 would be a filter. It is refused here for the same reason every other post-hoc
inversion in this document was: a mechanism invented to fit a result on 116 observations is
fitting, and nothing about the strategy predicted it in advance.

**Why this differs from the overnight result, where step 5 is worth 1.71 → 1.96.** The
confirmations were built to rescue a noisy population — they were the answer to entries that
were not selective enough. The deeper penetration threshold now does that job at the entry,
so there is nothing left for a confirmation to strain out. Two filters doing the same work
in series is not twice the filtering, it is one filter and one tax.

**Steps 5 and 6 stay off for the cash configuration.** No tuning was performed and none is
warranted: a filter whose refused set matches the book average has nothing to tune toward.

### A fourth: the verdict congratulated a filter for keeping the worst trades

The overnight continuations run above put the shadow verdict in a case it got wrong. Step 5
refused 95 trades that lost $2,890 between them — **−$30.42 each** — leaving 20 trades that
lost $5,060, **−$253.00 each**. The set it kept was 8.3 times worse per trade. The verdict
read:

> the refused trades lost $2,890.00 between them. Running the steps live would have avoided
> that. On this sample the filters are earning their place.

Technically true on the total and backwards on the substance. When every trade loses,
refusing any of them "avoids a loss", so the old test rewarded a filter for cutting the
*least* bad trades and keeping the worst. It now compares per-trade averages, prints both,
and states the counterfactual — how many trades running the steps live would leave and what
they are worth — rather than the sign of a subtotal.

### Three reporting bugs this exposed

The shadow output that produced the above was internally inconsistent, and all three are now
fixed.

- **The verdict double-counted.** A trade both steps object to lands in both buckets by
  design, but the verdict added the buckets — reporting 231 refusals worth $76,020 where
  there were 131 worth $43,490. One hundred trades were counted twice, profit included. The
  verdict now subtracts the clean bucket from the total.
- **Step 6's shadow line never printed.** It was guarded on the leaders object being
  non-null, and relative strength is a different object, so the entire step 6 row was absent
  from a run where step 6 objected to 116 trades.
- **`Setups completed` was summed from the rejection buckets**, which only partition the
  setups when a rejection actually stops one. Under shadow confirmations nothing stops, so
  vetoed setups were counted twice and doubly-vetoed ones three times: 599 printed against a
  true 244, contradicting both the long/short split and the stop histogram in the same
  output. It is now counted directly.

---

## How to tune a confirmation without contaminating the answer

**Use `Shadow confirmations`, not a run per setting.** Every trade is taken; the steps
record a verdict and block nothing. The summary then reports what each step *would* have
refused, in trades, wins and dollars, against what neither objected to — with a verdict
line comparing them.

The reason this matters is feedback. A blocked trade frees the position slot, so different
later setups become reachable and each filtered run measures a *different population*. This
is not theoretical: turning reversals off moved completed setups from 197 to 227 and
surfaced 765 continuation events that had been masked. Comparing two filtered runs compares
two different books. Shadow mode holds the book fixed and varies only the verdict.

**What the VIX knobs actually do**, since one is counterintuitive:

- `move` is the VIX close now minus its close `Vix lookback bars` ago.
- `threshold` is `max(Vix min directional move, VIX ATR × Vix min directional move (ATR))`.
- A long needs `move <= -threshold`; a short needs `move >= +threshold`.

So it is a **signed test with a magnitude floor**, and a flat VIX refuses everything.
Roughly half of all rejections are direction disagreement alone — which is why it rejected
64% of cash setups. **Raising the threshold makes it stricter, not more permissive**;
lowering it toward zero approaches a pure direction test at about 50% rejection. The points
floor barely binds — measured threshold mean was 0.06 against a 0.02 floor — so the ATR
multiple is the knob, not the floor.

**What to accept.** A filter earns its place only if the trades it refuses average clearly
worse than the book. Compare against the book's own per-trade figure in the same run, and
require the effect to survive in both the tuning window and the out-of-sample window
separately — two shadow runs, not one. Treat any configuration that leaves fewer than about
sixty trades as unmeasured regardless of what its profit factor says: the 1.73 in this
document was seventeen trades and it was noise.

---

## Break even and trailing stops

Added, shipped **off**, and unmeasured. Both are exit management rather than entry logic, and
this document has no evidence about either.

**Both accept two triggers and fire on whichever comes first.** `Break even at (R)` /
`(ticks)`, and `Trail from (R)` / `(ticks)`. Zero disables that half; both zero disables the
feature. R is measured against the stop set at entry, not the one currently resting — once a
stop has moved, the distance to it is no longer what was risked.

**What to expect before running it.** The R distribution in every run here shows where the
money is: 5 trades better than +5R and 11 between +3R and +5R carried the cash configuration,
against 43 stopped out near −1R. A trail tight enough to protect the −1R trades will also cut
the +5R ones short, and those are not evenly matched. Break even is the milder of the two —
it converts some losses into scratches without capping anything — but it also turns trades
that would have dipped and recovered into scratches, and this strategy enters on retests,
which are precisely trades that dip before working.

The run summary reports how many trades armed each, and the R distribution is where to look
for the cost. Compare against a run with both off rather than against expectations.

**One optimism to know about.** Triggers are tested against the bar's high or low, so a bar
that reached the level intraday arms the move, but the stop is placed on the close. A trade
that ran to the trigger and reversed inside the same bar is credited with a stop it would not
have had time to place. On 2-minute bars that window is small; it is not zero.

---

## What this does not tell you

**Everything here is in-sample.** The penetration thresholds, the stop band, step 6's
alignment count and relative strength's multiple were all chosen while looking at this
window.

**The best configuration is still not tradeable as measured** — step 6's leader form reads
files, and a file is a snapshot. But the gap has closed a long way: step 5 on `VX ##-##`
with step 6 off is a platform-only configuration at 2.03 and $3,810, against 2.17 and
$3,021 for the file-based best. That is the live candidate, and it costs six percent of
profit factor rather than the third it would have cost a week ago.

**Two months is two months.** Roughly one trade a day at these settings, in one market
regime.
