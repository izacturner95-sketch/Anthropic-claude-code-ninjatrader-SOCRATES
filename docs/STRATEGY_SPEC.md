# Strategy Specification

Fill this in and send it back. Anywhere you write something a computer could not
evaluate to true or false on a specific bar, that is a spot where I will have to
guess — and my guess is where the bot will diverge from what you actually meant.

You do not need to know any code to fill this in. Write it in plain English, but
write it precisely.

---

## 1. What is the idea, in one paragraph?

Plain English, no rules yet. What inefficiency or behaviour are you trying to
capture? Something like "NQ tends to run stops beyond the overnight high in the
first 30 minutes and then reverse" is exactly the right level.

> _Your answer:_

---

## 2. Chart setup

- **Bar type and size:** (e.g. 5-minute, 1000-tick, 4-range, Renko)
- **Session template:** (RTH only? 24-hour Globex? custom?)
- **Indicators used, with exact settings:** (e.g. "EMA 21 on close", "VWAP anchored to session open", "ATR 14")
- **Any higher-timeframe context?** (e.g. "only long if price is above the daily 50 EMA")

> _Your answer:_

---

## 3. Entry rules

State every condition that must be true. Number them. Be explicit about **when**
the check happens: on bar close, on every tick, at a specific clock time?

Example of the level of precision needed:

> LONG entry, all must be true, evaluated on close of each 5-minute bar:
> 1. Time is between 09:35 and 11:00 ET
> 2. Bar close is above the opening range high (09:30–09:35 high)
> 3. The prior bar's close was at or below the opening range high
> 4. ATR(14) is at least 8 points
>
> Order type: market, on the open of the next bar.

**Long entry:**

> _Your answer:_

**Short entry:**

> _Your answer (or "mirror image of long" if it is symmetrical):_

**Order type:** market / limit / stop. If limit or stop, at exactly what price,
and what happens if it does not fill within N bars?

> _Your answer:_

---

## 4. Stop loss

- Where is the initial stop? (fixed ticks, a multiple of ATR, below a swing low, the other side of a range...)
- Does it move? If so, when and to where? Be exact — "move to breakeven after
  +20 points" is usable; "trail it up" is not.

> _Your answer:_

---

## 5. Profit target / exit

- Fixed target? If so, how far?
- Multiple targets with partial exits? (e.g. "half off at +20, rest trails")
- Any exit rule that is not a stop or target? (time-based, indicator flip, opposite signal)
- What happens if an opposite signal fires while in a position — ignore it, exit flat, or reverse?

> _Your answer:_

---

## 6. Filters and blackouts

- Times of day to avoid entirely?
- Behaviour around scheduled news (CPI, FOMC, NFP)? Note: the strategy has no
  news feed unless we build one — the practical version is a hard-coded time blackout.
- Maximum trades per day?
- One position at a time, or can it pyramid?

> _Your answer:_

---

## 7. Risk limits

These are already implemented and just need numbers:

- **Max daily loss ($):** trading halts for the day when realised loss hits this
- **Daily profit target ($), and do you want it to stop trading when hit?**
- **Max consecutive losses before halting?**
- **Latest time to open a new trade:**
- **Time to force-flatten any open position:**

> _Your answer:_

---

## 8. Position sizing

Starting at 1 contract. What should the sizing options do beyond that?

- Fixed count, dollar risk per trade, or percent of equity?
- Hard ceiling on contracts?

> _Your answer:_

---

## 9. Where did this come from?

Honest answer helps me know how hard to stress-test it:

- Traded it manually? For how long, roughly how many trades?
- From a book, video, forum, or your own observation?
- Already backtested it somewhere else?

> _Your answer:_

---

## 10. Anything you already know breaks it

Conditions where you know it performs badly. Very useful — these usually become filters.

> _Your answer:_

---

## Ambiguities to watch for

Phrases I will have to come back to you about if they appear:

| Vague | Needs to become |
|---|---|
| "when the trend is up" | a specific, computable condition |
| "strong momentum" | a threshold on a named indicator |
| "near support" | within N ticks of a defined price level |
| "wait for confirmation" | exactly what event, within how many bars |
| "if it looks weak" | a rule, or dropped |
| "trail the stop" | trigger condition and new stop location |
