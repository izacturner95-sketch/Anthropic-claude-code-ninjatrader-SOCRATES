# Setup

Written for someone who has not used NinjaScript before. You will not need to write
or read any code to get this running.

---

## 1. Find your NinjaTrader custom folder

Everything NinjaTrader compiles lives in:

```
Documents\NinjaTrader 8\bin\Custom\
```

Inside it are folders including `Strategies\` and `AddOns\`. This repository mirrors
that structure so files map across one to one.

## 2. Copy the files

| From this repo | To |
|---|---|
| `src/NinjaTrader8/Strategies/SocratesNQ.cs` | `bin\Custom\Strategies\SocratesNQ.cs` |
| `src/NinjaTrader8/AddOns/Socrates\` (whole folder) | `bin\Custom\AddOns\Socrates\` |

Keep the `Socrates` subfolder — it stops these files mixing with NinjaTrader's own.

## 3. Compile

In NinjaTrader: **New → NinjaScript Editor**, then press **F5**.

The Errors panel at the bottom is what matters. If it is empty, the strategy
compiled. If not, copy the full text of every error — including file and line
numbers — and send it over. Compiler errors are normal on first import and are
usually quick to resolve.

> NinjaTrader compiles *everything* in the Custom folder at once. An unrelated
> broken script elsewhere will block this one from compiling.

## 4. Backtest it

**New → Strategy Analyzer**, then:

- **Strategy:** SocratesNQ
- **Instrument:** NQ (front month, or a continuous contract)
- **Bar type:** as specified in the strategy spec
- **Session template:** matching the spec (RTH vs 24-hour changes results a lot)
- **Commission:** set a realistic per-round-turn cost. Not optional — a strategy
  that is profitable only at zero commission is not profitable.
- **Slippage:** at least 1 tick. NQ is liquid but market orders still pay the spread.

Then **Run**.

## 5. Read the output

Open **New → NinjaScript Output** to see the strategy's log — every entry, every
skipped trade with the reason, every risk halt. When something behaves unexpectedly,
this window usually explains it, and it is the most useful thing to send me.

## 6. Before going live

In order, no skipping:

1. **Backtest** with realistic costs, over at least a year including both trending
   and chopping periods.
2. **Out-of-sample** — hold back recent months, tune on the rest, then test on the
   held-back data. If results collapse, the parameters were fitted to noise.
3. **Market Replay** — NinjaTrader replays recorded tick data at real speed. This
   catches order-handling bugs that bar-based backtests cannot.
4. **Sim account, live data** — at least a few weeks. This is the first honest test.
5. **Live, one contract**, watched closely.

Steps 3 and 4 are where automated strategies actually fail. A backtest cannot show
you a partial fill, a rejected order, or a disconnect mid-position.

---

## Safety checklist for unattended operation

Before leaving the bot running without you at the screen:

- [ ] **Max daily loss** set to a number you can genuinely absorb
- [ ] **Flatten time** set, and verified working in sim
- [ ] Behaviour after a **disconnect and reconnect** tested — kill your internet mid-position in sim
- [ ] Behaviour on **restarting NinjaTrader with a position open** tested (`StartBehavior` is `WaitUntilFlat`, meaning the strategy will not adopt an existing position)
- [ ] You know how to **kill it fast**: right-click the chart → Strategies → disable, plus your broker's flatten button
- [ ] **Max contracts** set as a hard ceiling, so a sizing bug cannot scale the position
