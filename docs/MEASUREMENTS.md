# What has actually been measured

Every figure here comes from a Strategy Analyzer run that was pasted back and read. It
exists because the same questions kept being re-answered from memory, and because a
strategy accumulates opinions faster than it accumulates evidence.

**Read the sample sizes.** Nothing here is large. The longest sample is two months.

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
| 2 ticks | 50 | 1.98 | $22,225 | $3,930 |

**Doubling the slippage assumption costs 2.5% of profit factor** — $690 across 50 trades,
about $14 each, which is roughly one extra tick on an entry plus a stop exit. It degrades
linearly and predictably. The edge has margin over its execution assumptions.

Two things this does not cover. Prop firms commonly restrict overnight positions and
charge several times the day margin, which is a rule question rather than a measurement
one. And a strategy that only trades when the market is quiet is exposed to that
regime ending.

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

## Risk settings

**The stop band is the largest single lever on drawdown.** From the same 195-setup
distribution:

| Max stop | Setups kept | Worst loss | Result |
|---|---|---|---|
| 400 ticks | 100% | $2,000 | net $31,230, drawdown $8,810 |
| 250 ticks | 85% | $1,250 | net $20,285, drawdown **$4,370** |

Net fell by a third and drawdown halved. Return-to-drawdown went from 3.5 to 4.6.

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
