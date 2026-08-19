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
| Step 5 only | 55 | 1.96 | $4,830 | $423 | 4.8 |
| Step 5 + relative strength | 45 | 1.75 | $4,830 | $357 | 3.3 |
| **Step 5 + leaders** | **46** | **2.17** | **$3,021** | **$455** | **6.9** |

**Step 5 is worth having.** Alone it lifts profit factor from 1.71 to 1.96 and removes 30%
of the drawdown.

**The leader form of step 6 is worth having.** Added to step 5 it lifts 1.96 to 2.17 and
removes another 37% of drawdown. The nine trades it refused averaged $256 against a $423
book average — it is removing below-average trades, which is what a filter is for.

**Relative strength is worse than nothing.** Added to step 5 it drops 1.96 to 1.75 with no
drawdown benefit. The ten trades it refused averaged **$717** — it removes
better-than-average trades. It was tested at three thresholds; at 0.75 it passed 25% and
left too few trades to judge, at 0.40 it passed 85% and barely filtered. Tuning further on
this one window would be fitting, not measuring.

At matched selectivity the comparison is unusually clean: both forms of step 6 rejected
**exactly twelve** setups from the same pool of eighty, and the leader form's twelve were
the right twelve. Whatever the Mag 7 count reads, the NQ/ES spread does not read it.

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

**The best configuration is not tradeable as measured.** Steps 5 and 6 read files, and a
file is a snapshot. Live, NinjaTrader has no US equity feed at all, and its VIX futures
history does not survive a contract roll. The live-tradeable configurations are the ones
with no step 6, and step 5 pointed at something that updates.

**Two months is two months.** Roughly one trade a day at these settings, in one market
regime.
