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

Settled. Off.

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
| `VIX.csv` | leader files | 54 | 1.35 | +$5,660 | $2,375 |
| feed | off | 52 | 0.81 | −$3,845 | $7,005 |
| off | NQ vs ES | 26 | 0.95 | −$485 | $3,970 |
| feed | NQ vs ES | 17 | **1.73** | +$3,510 | $2,380 |
| **off** | **off** | **not run** | | | |

**Neither confirmation is profitable alone, and together they are 1.73.** Step 5 by itself
loses money. Relative strength by itself loses money. Applied together they produce the
best profit factor in the table, on the smallest sample in the table. That combination —
two ingredients that fail separately, succeeding jointly on 17 observations — is what
overfitting looks like from the inside. It is not proof of overfitting; genuine
complementary filters exist. It is a reason to stop reading the number as a finding.

**The baseline has never been run.** Every cell above is a *subtraction* from a population
nobody has measured. With both confirmations off the cash session would produce roughly two
hundred trades — four times the largest sample here and the only run in this whole exercise
with enough observations to mean much on its own. Until it exists there is no answer to the
question every row above is implicitly claiming: does filtering help at all?

That is the next run, and it outranks any further tuning.

**A note on relative strength's threshold.** It rejected 183 of 230 setups, and the mean
spread (0.063%) sits just *below* the mean threshold (0.067%) — so more than half the time
the two indices have not diverged enough for the test to pass at `Min spread (multiple)`
0.75. That is a tunable, and tuning it against this window before the baseline exists would
be fitting a filter to noise.

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
