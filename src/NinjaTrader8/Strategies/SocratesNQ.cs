// Socrates NQ - automated strategy for the CME E-mini Nasdaq-100.
//
// Implements a six-step sequence. Each step must complete before the next is considered,
// and any step that times out resets the whole setup:
//
//   1. Context    - build a book of reference levels (prior day/week, pivots, swing zones)
//   2. Liquidity  - price sweeps beyond a level and closes back inside
//   3. Structure  - the failing move breaks opposing structure with displacement
//   4. Retest     - price returns to the defended area; entry happens here, not on the sweep
//   5. VIX        - the VIX must move inversely, ideally reacting from its own key level
//   6. Breadth    - the market leaders must be participating in the same direction
//
// Steps 1-4 live in the Socrates.Market engine classes. This file wires them to
// NinjaTrader's data, orders and parameters.

#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript.DrawingTools;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
using Socrates.Data;
using Socrates.Market;
using Socrates.Risk;
using Socrates.Strategy;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
	public class SocratesNQ : Strategy
	{
		// --- Engine ---
		private MarketAnalyzer nq;
		private SetupEngine setup;
		private VixConfirmation vix;
		private BreadthConfirmation breadth;
		private RiskManager risk;
		private PositionSizer sizer;
		private RiskManagerSettings riskSettings;
		private PositionSizerSettings sizerSettings;
		private ContractSpec contract;

		// --- Data series indices, assigned in Configure ---
		private int idxDaily = -1;
		private int idxWeekly = -1;
		private int idxFourHour = -1;
		private int idxVix = -1;
		private int idxBreadthStart = -1;
		private int idxFill = -1;
		private int expectedSeriesCount;
		private string[] breadthSymbols = new string[0];

		// File-backed sources for steps 5 and 6. When one is in play its platform series is
		// not added at all, which is the point: a multi-series backtest cannot begin before
		// its youngest series, and that is what has truncated every step 5 and 6 run to the
		// life of the current contract.
		// Step 6's alternative form. One series instead of seven files, on the feed rather
		// than on disk, so it behaves the same in a backtest and live.
		private RelativeStrengthConfirmation relStrength;
		private int idxRelStrength = -1;

		private FileSeries vixFile;
		private FileSeries[] leaderFiles = new FileSeries[0];
		private bool filesActive;
		private double[] breadthSessionOpen = new double[0];

		// --- Session tracking ---
		private int effectiveSessionStart;
		private int effectiveSessionEnd;
		private int effectiveFlatten;
		private DateTime currentSessionDate = DateTime.MinValue;
		private double overnightHigh = double.MinValue;
		private double overnightLow = double.MaxValue;
		private double openingRangeHigh = double.MinValue;
		private double openingRangeLow = double.MaxValue;
		private bool sessionLevelsBuilt;

		// Session levels only change on period boundaries. Rebuilding every bar would churn
		// through several dozen objects per bar across a multi-year backtest, and would also
		// discard the touch counts accumulated on each level.
		private bool levelsDirty = true;
		private int lastDailyBar = -1;
		private int lastWeeklyBar = -1;
		private int lastFourHourBar = -1;

		private int processedTradeCount;
		private bool flattenedForDay;
		private bool haltCloseLogged;

		// The signal name of the entry currently holding the position, so an exit can be
		// matched to it. Cleared when the position goes flat.
		private string activeEntryLabel;

		// --- Diagnostics ---
		//
		// A strategy that prints nothing is indistinguishable from a strategy that never
		// ran, so every path that can silence it either logs once or is counted. The
		// funnel counters answer the only question that matters when there are no trades:
		// how far down the six steps did price actually get?
		private bool firstBarLogged;
		private DateTime firstBarTime;
		private DateTime lastBarTime;
		private bool warmupLogged;
		private bool dailyContextWarned;
		private bool weeklyContextWarned;
		private bool zeroAtrWarned;
		private bool previousZoneTouched;
		private int barsProcessed;
		private int lastStatusBar = int.MinValue;

		private int daySweeps;
		private int dayShifts;
		private int dayZoneTouches;
		private int dayEntries;
		private int dayBlockedByRisk;
		private int dayRejectedVix;
		private int dayRejectedBreadth;
		private int dayRejectedStop;
		private int dayRejectedSizing;
		private int dayRejectedRiskBudget;

		// The funnel split by session. The results split showed the strategy takes 94% of its
		// trades overnight; it could not show why. "The cash session loses money" and "the
		// cash session never produces a setup" look identical in a results table and need
		// completely different work - one is a filter problem, the other means the sequence
		// never completes in fast conditions. This is the difference.
		private int cashSweeps, nightSweeps;
		private int cashShifts, nightShifts;
		private int cashRetests, nightRetests;
		private int cashSetups, nightSetups;

		private int totalSweeps;
		private int totalShifts;
		private int totalZoneTouches;
		private int totalEntries;
		private int totalBlockedByRisk;
		private int totalRejectedVix;
		private int totalRejectedBreadth;
		private int totalRejectedStop;
		private int totalRejectedSizing;
		private int totalRejectedRiskBudget;

		// "Blocked by risk" spans four very different situations - a session window that
		// excludes most of a 24-hour chart is not the same problem as a daily loss halt.
		private readonly int[] blockReasonCounts = new int[8];

		// Every completed setup's stop distance, whether or not it passed the band. Without
		// the distribution, choosing MaxStopTicks is guesswork; with it the setting reads
		// straight off the histogram.
		private const int StopBucketTicks = 25;
		private const int StopBucketCount = 12;
		private readonly int[] stopTickBuckets = new int[StopBucketCount];
		private int stopSamples;
		private double stopTicksMin;
		private double stopTicksMax;
		private double stopTicksSum;

		// A strategy that only ever trades one way is either reading a one-way market or has
		// an asymmetry in it. Counting both makes the difference visible.
		private int longSetups;
		private int shortSetups;
		private int longEntries;
		private int shortEntries;
		private int reversalEntries;
		private int continuationEntries;

		// Chart visuals
		private int visualEntryBar = -1;
		private int visualZoneStartBar;
		private double visualStop;
		private double visualTarget;
		private string visualTag = string.Empty;
		private string lastZoneTag;
		private double plannedRrSum;
		private int plannedRrCount;
		private int dayBreaks;
		private int totalBreaks;
		private int breadthSkippedClosed;
		private int vixBarsSeen;
		private int vixUpdatesApplied;

		// Results. The funnel answers "why no trades"; none of it answers "are the trades
		// any good", and with both exits read off structure the R multiple actually realised
		// is the number that says whether the geometry works.
		private readonly List<double> pendingRiskDollars = new List<double>();

		// Shadow mode. Steps 5 and 6 read contract-based data that NinjaTrader does not carry
		// across a roll, so neither can ever be backtested over more than the current
		// contract's life - a fortnight, against the months the rest of the strategy has. A
		// filter that cannot be measured is a filter taken on faith.
		//
		// With shadow on, both steps evaluate and record their verdict but refuse nothing.
		// Every trade is taken, and each one carries a note of which step would have vetoed
		// it. The run summary then reports what the vetoed trades actually did, which is the
		// only honest way to price a filter on data this short: forwards, on trades that
		// really closed, accumulating a sample instead of waiting for one.
		// Results split by setup kind. Continuations have been on and off three times now on
		// the strength of aggregate numbers, which cannot answer the question: a run can be
		// profitable while one of its two setup types loses money, and blending them hides
		// exactly that. 56 of 82 entries being continuations makes the split the difference
		// between measuring the strategy and measuring an average of two strategies.
		// Results split by when the trade was taken. The strategy runs the full Globex
		// session, and whether the overnight half earns its keep is a structural question -
		// not a parameter to sweep. Answering it by running the strategy twice conflates it
		// with everything else that differs between two runs; splitting the same run does
		// not. The summary's own note about "the cost of holding through thin hours" was a
		// hypothesis nothing was testing.
		private readonly List<bool> pendingWasCash = new List<bool>();
		private int cashTrades, cashWon;
		private double cashGrossProfit, cashGrossLoss;
		private int nightTrades, nightWon;
		private double nightGrossProfit, nightGrossLoss;

		private readonly List<bool> pendingIsContinuation = new List<bool>();
		private int revTrades, revWon;
		private double revGrossProfit, revGrossLoss;
		private int contTrades, contWon;
		private double contGrossProfit, contGrossLoss;

		private readonly List<bool> pendingVixVeto = new List<bool>();
		private readonly List<bool> pendingBreadthVeto = new List<bool>();
		private bool shadowVixVetoed;
		private bool shadowBreadthVetoed;

		private int shadowVixVetoTrades;
		private int shadowVixVetoWon;
		private double shadowVixVetoPnL;
		private int shadowBreadthVetoTrades;
		private int shadowBreadthVetoWon;
		private double shadowBreadthVetoPnL;
		private int shadowCleanTrades;
		private int shadowCleanWon;
		private double shadowCleanPnL;
		private int shadowTotalTrades;
		private double shadowTotalPnL;
		private int tradesWon;
		private int tradesLost;
		private int tradesScratch;
		private double grossProfit;
		private double grossLoss;
		private double largestLoss;
		private double runningEquity;
		private double equityPeak;
		private double maxDrawdown;
		private double riskDollarsSum;
		private double riskDollarsMin = double.MaxValue;
		private double riskDollarsMax;
		private int rSamples;
		private double rSum;
		private double rMin = double.MaxValue;
		private double rMax = double.MinValue;

		// A stop is supposed to cap a loss at 1R. Anything worse means price left the stop
		// behind - a gap, or a bar that opened through it - and that is not visible in a mean.
		// A stop-out lands slightly past -1R once slippage is paid, so a band boundary at
		// exactly -1R sweeps every ordinary loss into the band above it. The first cut of
		// this put 20 of 43 trades in "-2R to -1R" while separately reporting 3 stops that
		// did not hold - both true, and together badly misleading. Normal stop-outs get
		// their own band.
		private static readonly string[] RBucketLabels =
		{
			"worse than -2R", "-2R to -1R (overrun)", "stopped out, ~-1R", "-1R to 0 (cut early)",
			"0 to +1R", "+1R to +2R", "+2R to +3R", "+3R to +5R", "better than +5R"
		};

		private readonly int[] rBuckets = new int[9];
		private int stopOverruns;
		private double rSumCappedAtStop;

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description = "Liquidity sweep, structure shift and retest strategy for NQ, gated on VIX inversion and Magnificent 7 breadth.";
				Name = "SocratesNQ";

				Calculate = Calculate.OnBarClose;
				EntriesPerDirection = 1;
				EntryHandling = EntryHandling.AllEntries;
				IsExitOnSessionCloseStrategy = true;
				ExitOnSessionCloseSeconds = 30;
				IsFillLimitOnTouch = false;
				MaximumBarsLookBack = MaximumBarsLookBack.TwoHundredFiftySix;

				// Has to stay Standard: High resolution is only available to single-series
				// strategies, and this one loads four at a minimum. NinjaTrader's own advice
				// when it refuses is to program the finer resolution in yourself, which is
				// what 'Fill resolution (minutes)' below does - a 1-minute series of the same
				// instrument, with entries submitted against it so their fills, and the stop
				// and target attached to them, resolve on 1-minute bars instead of 5.
				OrderFillResolution = OrderFillResolution.Standard;

				// One tick. Entries are market orders on a bar close, so they pay something;
				// zero was never a defensible figure, just an unexamined one.
				Slippage = 1;

				// Every trade carries a stop and a target at once, so any 5-minute bar
				// touching both leaves the backtest to assume which came first - generously,
				// and in exactly the way that flatters a strategy with a structural stop and
				// a distant structural target. 0 disables the extra series and accepts
				// whatever the primary bar size implies.
				FillResolutionMinutes = 1;
				StartBehavior = StartBehavior.WaitUntilFlat;
				TimeInForce = TimeInForce.Gtc;
				TraceOrders = false;
				BarsRequiredToTrade = 30;
				RealtimeErrorHandling = RealtimeErrorHandling.StopCancelClose;
				StopTargetHandling = StopTargetHandling.PerEntryExecution;

				// --- Sizing ---
				FixedContracts = 1;
				MaxContracts = 1;
				TickValueDollars = 5.00;
				UseConfidenceSizing = false;

				// --- Risk ---
				//
				// Extended hours by default. On a 24-hour Globex chart the cash window is
				// about a quarter of the bars, and the last run refused 10 of 14 completed
				// setups as OutsideSession - the sequence was finding trades all night and
				// then declining them.
				TradingHours = TradingHoursMode.ExtendedHours;

				// Only read when Trading hours is Custom; the presets supply their own.
				SessionStartTime = 94500;
				SessionEndTime = 154500;
				FlattenTime = 155500;
				// Off. A cap smaller than a routine stop-out cannot be honoured, and at $1,000
				// against structural stops averaging 228 ticks it was refusing most setups -
				// which is not a risk control, it is a mute button on the strategy. Set a
				// number here to switch it back on; it then halts the day when reached and
				// refuses any entry risking more than the day has left.
				//
				// This is a backtesting default. Put a real number on it before anything
				// trades unattended.
				MaxDailyLossDollars = 0;
				DailyProfitTargetDollars = 0;
				StopForDayOnProfitTarget = false;
				MaxTradesPerDay = 3;
				MaxConsecutiveLosses = 2;

				// Off, meaning the replay's trades are discarded at the handover to live data.
				// They are simulated fills against bars that had already printed, and letting
				// them arm the consecutive-loss halt or spend the trade cap means enabling the
				// strategy on a day it has already 'lost' silences it until the session rolls.
				// That has cost two full live days. On is the old behaviour, for a restart
				// mid-session where the replay approximates trades that genuinely happened.
				CarryReplayRiskState = false;

				// --- Step 1: context ---
				AtrPeriod = 14;
				SwingStrength = 3;
				UseFourHourPivots = true;
				OpeningRangeMinutes = 15;
				ZoneHalfWidthAtr = 0.25;
				LevelMergeAtr = 0.35;
				UseOrderBlocks = true;
				OrderBlockRequireImbalance = true;
				OrderBlockDisplacementAtr = 1.0;
				OrderBlockMaxLookback = 10;
				OrderBlockZone = OrderBlockZoneMode.FullRange;

				// --- Step 2: liquidity ---
				//
				// 0.10 ATR and 1 point were noise thresholds: on 5-minute NQ that is a two
				// point poke, which price does constantly. Against a level book of thirty-odd
				// lines it produced a confirmed sweep every four bars - 498 in a 2,065 bar
				// sample - and nothing downstream survived the churn. A sweep is supposed to
				// be a raid on liquidity, not a wiggle.
				MinPenetrationAtr = 0.25;
				MinPenetrationPoints = 2.0;
				MaxBarsToReclaim = 6;

				// Off. Implemented, available, and measured: over 21,590 bars it produced 144
				// of 147 entries at -0.08R and a 0.95 profit factor, while crowding reversals
				// out of the pipeline almost entirely - structure shifts fell from 246 to 40
				// because a continuation holding a position blocks everything behind it.
				//
				// Turn it on to test the selective version: continuations now only fire on
				// major reference levels, which is the change that has not been measured yet.
				EnableContinuations = false;
				ContinuationsOnMajorLevelsOnly = true;

				// On, because the reclaim setup is what the overnight session earns from.
				// The switch exists for the cash session, where it is the losing half.
				EnableReversals = true;

				// --- Step 3: structure ---
				//
				// A post-sweep swing needs (2 x SwingStrength) + 1 bars to confirm, so the
				// budget has to leave room for one to form and then be broken. At 12 bars it
				// mostly did not, which is how every setup ended up anchored to the pre-sweep
				// swing instead.
				MaxBarsSweepToShift = 20;
				UsePostSweepSwing = true;
				MinDisplacementAtr = 1.0;
				// The trade's real risk, entry to stop. Checked at entry.
				MaxSetupRiskAtr = 2.5;

				// A coherence test on the setup, not a risk test - the stop stopped coming from
				// the swept extreme when it moved to the retest pullback, so this distance no
				// longer sets the risk. At 2.5 it was discarding 573 of 819 structure breaks.
				MaxStructureDistanceAtr = 5.0;

				// --- Step 4: retest ---
				MaxBarsShiftToRetest = 15;
				ZoneMode = RetestZoneMode.BrokenStructure;
				RetestZoneAtr = 0.30;
				RequireConfirmationClose = true;

				// Both legs are read off structure: the stop goes below the previous low,
				// the target at or just short of the previous high. Mirror image on a short.
				StopBufferAtr = 0.25;
				TargetBufferTicks = 4;
				MinRewardRisk = 1.0;

				// Only used when no previous swing sits far enough away to aim at.
				TargetRMultiple = 2.0;

				MinStopTicks = 20;

				// Only consulted when Stop loss (ticks) is 0 and the stop comes from structure.
				//
				// This band has twice been the thing that silently vetoed every trade - first
				// at 100 ticks, then at 200, against measured structure stops of 169 to 810.
				// It is a backstop, not the ceiling: 'Max setup risk (ATR)' is what actually
				// bounds risk, and it does so in the units the market moves in rather than a
				// fixed tick count. So this is set wide enough to stop being the binding
				// constraint, and the banner prints what it implies against the daily cap.
				MaxStopTicks = 400;

				// --- Step 5: VIX ---
				//
				// Off by default. Steps 5 and 6 need twelve data series between them, and a
				// futures-only feed carries none of the eight index and equity symbols. A
				// missing series is not a soft failure in NinjaTrader: the strategy refuses
				// to start, logs to the Log tab rather than the Output window, and produces
				// no prints and no trades at all. Turn these on once you have confirmed the
				// symbols load on a chart of their own.
				VixMode = ConfirmationMode.Off;

				// VIX futures rather than the ^VIX index. The index is only disseminated
				// around the cash session, so on a Globex chart it goes dark for most of the
				// night and cannot confirm anything; VX trades close to 23 hours. It prices
				// in contango rather than tracking spot exactly, which does not matter here -
				// the step reads direction and levels, not the absolute number.
				//
				// Plain "VX" rather than "VX ##-##": the ##-## form asks NinjaTrader to build
				// a continuous series, which it can only do with the history and merge policy
				// to back it, and it returned an empty series here. The bare name resolves
				// through the instrument list instead.
				// "VX ##-##", not "VX". A bare futures root is not an instrument name in
				// NinjaTrader and fails at startup with "Unknown instrument" - which stops
				// the strategy before it prints anything. ##-## is the platform's own
				// placeholder for the front contract.
				//
				// Note what that means for a backtest: the front contract is the one that is
				// front *now*, so a run over an earlier window gets a contract that barely
				// existed then. For a historical test, name the contract that was front
				// during it - VX 08-26 for a June-to-August window.
				VixSymbol = "VX ##-##";
				VixBarMinutes = 5;
				VixLookbackBars = 6;
				// An absolute floor only. The real test scales to the VIX's own ATR, because
				// a fixed number set for the cash session is unreachable overnight.
				VixMinDirectionalMove = 0.02;
				VixMinDirectionalMoveAtr = 0.5;
				VixKeyLevelTolerance = 0.35;

				// 0 derives the limit from the bar period, which is right for the index and
				// far too tight for VX. 90 minutes suits the futures overnight.
				VixMaxDataAgeMinutes = 90;
				VixSkipWhenQuiet = true;

				// --- File-backed sources ---
				//
				// Blank means use the platform's series, which is the previous behaviour.
				// A path here replaces that series entirely, and the backtest range stops
				// being dragged down to the youngest contract's history.
				VixFile = string.Empty;
				LeaderFiles = string.Empty;
				FileReloadSeconds = 60;
				FileTimeOffsetMinutes = 0;

				// --- Step 6: breadth ---
				//
				// CME single stock futures, not the cash shares. NinjaTrader's feed carries no
				// US equities at all - support confirmed it - so AAPL and the rest could never
				// have supplied this step here. The futures are the only route to the data on
				// this platform, and they are better suited anyway: they run Globex hours
				// rather than 09:30-16:00, so breadth can confirm an overnight setup instead
				// of standing aside for two thirds of the session.
				//
				// Two costs come with them. History is contract-based and short - a contract
				// listed at the start of the month carries data only from then - so a backtest
				// with step 6 on truncates to the youngest series, and the startup banner names
				// it. And they are thinner than the shares, so a leader can be genuinely flat
				// while the underlying is moving.
				BreadthMode = ConfirmationMode.Off;
				// SGOOG, not SGOOGL - the futures root drops the share class the cash ticker
				// carries. Confirmed against a run that resolved all seven to 09-26 contracts.
				BreadthSymbols = "SAAPL,SMSFT,SNVDA,SAMZN,SMETA,SGOOG,STSLA";
				BreadthBarMinutes = 5;
				BreadthMinAligned = 5;
				BreadthMinMovePercent = 0.05;

				// Around the clock. The window existed because the cash shares were dark
				// 20:00-04:00 and no ticker fixed it; futures trade nearly 23 hours, so a
				// clock-based skip would now be turning the step off during hours it can
				// actually answer. Start equal to end means always on, and the staleness guard
				// inside the step does the skipping instead - it reads whether data arrived
				// rather than whether the clock says it should have, which is the right test
				// for a session this file does not hardcode. Set a window here to narrow it.
				BreadthActiveStart = 0;
				BreadthActiveEnd = 0;

				// 0 derives it from the bar period, as before. Raise it when the leaders come
				// from a delayed source - a file refreshed on a timer, or a free feed - where
				// "current" legitimately means several minutes old.
				BreadthMaxDataAgeMinutes = 0;

				// Leaders by default, which is what has been measured. RelativeStrength is
				// the same question asked of instruments the feed actually carries, and is
				// unmeasured - see the note on the parameter.
				BreadthSource = BreadthSourceMode.Leaders;
				RelativeStrengthSymbol = "ES";
				RelativeStrengthLookback = 12;
				RelativeStrengthMinSpread = 0.02;
				RelativeStrengthMultiple = 0.75;

				ShowChartVisuals = true;
				ShowSetupZones = true;
				ShowStatsPanel = true;

				// Off. Turning it on stops steps 5 and 6 refusing anything, which is a
				// deliberate loss of protection - it is a measuring instrument, not a setting
				// to leave on a funded account.
				ShadowConfirmations = false;

				EnableLogging = true;
				VerboseLogging = false;
				StatusEveryBars = 120;
			}
			else if (State == State.Configure)
			{
				contract = new ContractSpec(0.25, TickValueDollars);

				sizerSettings = new PositionSizerSettings
				{
					FixedContracts = FixedContracts,
					MaxContracts = MaxContracts
				};

				ResolveTradingHours();

				riskSettings = new RiskManagerSettings
				{
					SessionStartTime = effectiveSessionStart,
					SessionEndTime = effectiveSessionEnd,
					FlattenTime = effectiveFlatten,
					MaxDailyLossDollars = MaxDailyLossDollars,
					DailyProfitTargetDollars = DailyProfitTargetDollars,
					StopForDayOnProfitTarget = StopForDayOnProfitTarget,
					MaxTradesPerDay = MaxTradesPerDay,
					MaxConsecutiveLosses = MaxConsecutiveLosses
				};

				sizer = new PositionSizer(contract);
				risk = new RiskManager(riskSettings);

				MarketAnalyzerSettings nqSettings = new MarketAnalyzerSettings
				{
					SwingStrength = SwingStrength,
					ZoneHalfWidthAtr = ZoneHalfWidthAtr,
					Sweep = new SweepSettings
					{
						MinPenetrationAtr = MinPenetrationAtr,
						MinPenetrationPoints = MinPenetrationPoints,
						MaxBarsToReclaim = MaxBarsToReclaim,
						EmitReversals = EnableReversals,
						EmitContinuations = EnableContinuations,
						ContinuationsOnMajorLevelsOnly = ContinuationsOnMajorLevelsOnly
					},
					OrderBlocks = new OrderBlockSettings
					{
						Enabled = UseOrderBlocks,
						RequireImbalance = OrderBlockRequireImbalance,
						MinDisplacementAtr = OrderBlockDisplacementAtr,
						MaxLookbackForOrigin = OrderBlockMaxLookback,
						ZoneMode = OrderBlockZone,
						InvalidateOnCloseThrough = true
					}
				};

				nq = new MarketAnalyzer(nqSettings);

				setup = new SetupEngine(new SetupEngineSettings
				{
					MaxBarsSweepToShift = MaxBarsSweepToShift,
					MaxBarsShiftToRetest = MaxBarsShiftToRetest,
					UsePostSweepSwing = UsePostSweepSwing,
					MinDisplacementAtr = MinDisplacementAtr,
					MaxSetupRiskAtr = MaxSetupRiskAtr,
					MaxStructureDistanceAtr = MaxStructureDistanceAtr,
					ZoneMode = ZoneMode,
					RetestZoneAtr = RetestZoneAtr,
					RequireConfirmationClose = RequireConfirmationClose,
					StopBufferAtr = StopBufferAtr,
					TargetRMultiple = TargetRMultiple,
					// contract.TickSize rather than the platform's TickSize: Instrument is not
					// reliably resolved this early in Configure, and both NQ and MNQ are 0.25.
					TargetBufferPoints = TargetBufferTicks * contract.TickSize,
					MinRewardRisk = MinRewardRisk
				});

				// Series are added only when the step that needs them is enabled, so a data
				// feed without index or equity coverage can still run steps 1-4.
				StopFileWatchers();
				vixFile = null;
				leaderFiles = new FileSeries[0];
				filesActive = false;

				relStrength = null;
				idxRelStrength = -1;

				idxFill = idxDaily = idxWeekly = idxFourHour = idxVix = idxBreadthStart = -1;
				breadthSymbols = new string[0];
				breadthSessionOpen = new double[0];
				vix = null;
				breadth = null;

				int next = 1;

				// Added first so its index is stable regardless of what else is switched on.
				// Same instrument as the primary, just finer: it carries no signal logic and
				// exists only so orders submitted against it fill on smaller bars.
				if (FillResolutionMinutes > 0)
				{
					AddDataSeries(BarsPeriodType.Minute, FillResolutionMinutes);
					idxFill = next++;
				}

				AddDataSeries(BarsPeriodType.Day, 1);
				idxDaily = next++;

				AddDataSeries(BarsPeriodType.Week, 1);
				idxWeekly = next++;

				if (UseFourHourPivots)
				{
					AddDataSeries(BarsPeriodType.Minute, 240);
					idxFourHour = next++;
				}

				// A file replaces the platform series entirely. Adding both would put the
				// contract's short history back into the range calculation for nothing.
				bool vixFromFile = VixMode != ConfirmationMode.Off && FileSeries.LooksUsable(VixFile);

				if (vixFromFile)
				{
					vixFile = new FileSeries(new FileSeriesSettings
					{
						Name = "VIX",
						Path = VixFile,
						AtrPeriod = AtrPeriod,
						FreshMinutes = VixMaxDataAgeMinutes > 0 ? VixMaxDataAgeMinutes : VixBarMinutes * 3,
						ReloadSeconds = FileReloadSeconds,
						TimestampOffsetMinutes = FileTimeOffsetMinutes
					});

					filesActive = true;
				}

				if (VixMode != ConfirmationMode.Off)
				{
					if (!vixFromFile)
					{
						AddDataSeries(VixSymbol, BarsPeriodType.Minute, VixBarMinutes);
						idxVix = next++;
					}

					// The VIX is used only to confirm direction from its own pivots and swing
					// levels, so order block detection is off for it.
					MarketAnalyzerSettings vixSettings = new MarketAnalyzerSettings
					{
						SwingStrength = SwingStrength,
						ZoneHalfWidthAtr = 0.15,
						PruneDistanceAtr = 20.0,
						Sweep = new SweepSettings
						{
							MinPenetrationAtr = 0.10,
							MinPenetrationPoints = 0.02,
							MaxBarsToReclaim = MaxBarsToReclaim
						},
						OrderBlocks = new OrderBlockSettings { Enabled = false }
					};

					vix = new VixConfirmation(new VixConfirmationSettings
					{
						Mode = VixMode,
						MinDirectionalMove = VixMinDirectionalMove,
						MinDirectionalMoveAtr = VixMinDirectionalMoveAtr,
						KeyLevelTolerance = VixKeyLevelTolerance,
						// 0 derives it from the bar period, which suits the index. VX needs a
						// number of its own - see the parameter's description.
						MaxDataAgeMinutes = VixMaxDataAgeMinutes > 0 ? VixMaxDataAgeMinutes : VixBarMinutes * 3,
						SkipWhenQuiet = VixSkipWhenQuiet
					}, new MarketAnalyzer(vixSettings));
				}

				if (BreadthMode != ConfirmationMode.Off && BreadthSource == BreadthSourceMode.RelativeStrength)
				{
					// Added at the primary's own period so the two series step together and a
					// lookback of N bars means the same span on both. Asking for a fixed
					// minute period here would silently compare a 12-bar move on one against
					// a different span on the other.
					AddDataSeries(RelativeStrengthSymbol, BarsPeriod.BarsPeriodType, BarsPeriod.Value);
					idxRelStrength = next++;

					relStrength = new RelativeStrengthConfirmation(new RelativeStrengthSettings
					{
						Mode = BreadthMode,
						LookbackBars = RelativeStrengthLookback,
						MinSpreadPercent = RelativeStrengthMinSpread,
						MinSpreadMultiple = RelativeStrengthMultiple,
						MaxDataAgeMinutes = BreadthMaxDataAgeMinutes > 0
							? BreadthMaxDataAgeMinutes
							: BreadthBarMinutes * 3,
						SkipWhenQuiet = true
					});
				}
				else if (BreadthMode != ConfirmationMode.Off)
				{
					breadthSymbols = ParseSymbols(BreadthSymbols);

					if (breadthSymbols.Length > 0)
					{
						// One path per leader, in the same order as the symbols - position is
						// what pairs the two lists. All or none: the leaders left on platform
						// series need contiguous BarsInProgress indices, so this cannot be
						// mixed symbol by symbol.
						string[] paths = ParseSymbols(LeaderFiles);
						bool allFromFiles = paths.Length >= breadthSymbols.Length;

						for (int i = 0; i < breadthSymbols.Length && allFromFiles; i++)
						{
							if (!FileSeries.LooksUsable(paths[i]))
								allFromFiles = false;
						}

						if (allFromFiles)
						{
							leaderFiles = new FileSeries[breadthSymbols.Length];

							for (int i = 0; i < breadthSymbols.Length; i++)
							{
								leaderFiles[i] = new FileSeries(new FileSeriesSettings
								{
									Name = breadthSymbols[i],
									Path = paths[i],
									AtrPeriod = AtrPeriod,
									FreshMinutes = BreadthBarMinutes * 3,
									ReloadSeconds = FileReloadSeconds,
									TimestampOffsetMinutes = FileTimeOffsetMinutes
								});
							}

							filesActive = true;
						}
						else
						{
							idxBreadthStart = next;

							for (int i = 0; i < breadthSymbols.Length; i++)
							{
								AddDataSeries(breadthSymbols[i], BarsPeriodType.Minute, BreadthBarMinutes);
								next++;
							}
						}

						breadthSessionOpen = new double[breadthSymbols.Length];

						breadth = new BreadthConfirmation(new BreadthConfirmationSettings
						{
							Mode = BreadthMode,
							MinAligned = BreadthMinAligned,
							MinMovePercent = BreadthMinMovePercent,
							// 0 derives it from the bar period. That suits a platform series, which
							// is either live or absent. It is wrong for a delayed source: a free
							// equity feed running fifteen minutes behind is fifteen minutes behind
							// a limit of fifteen minutes, so every reading arrives just as it
							// expires and the step silently never confirms.
							MaxDataAgeMinutes = BreadthMaxDataAgeMinutes > 0
								? BreadthMaxDataAgeMinutes
								: BreadthBarMinutes * 3,
							SkipWhenClosed = true
						}, breadthSymbols);
					}
				}

				// Every index above is a running count of AddDataSeries calls, so anything that
				// adds a series NinjaTrader also exposes would shift them all and silently
				// point the daily, weekly and VIX lookups at the wrong data. Recorded here and
				// checked once the series have loaded.
				expectedSeriesCount = next;

				processedTradeCount = 0;
				flattenedForDay = false;
				haltCloseLogged = false;
				sessionLevelsBuilt = false;
				activeEntryLabel = null;

				ResetDiagnostics();
			}
			else if (State == State.DataLoaded)
			{
				// Loaded here rather than on the first bar: blocking is acceptable in this
				// state, and a file that cannot be read should say so before a single bar is
				// processed rather than becoming a step that mysteriously never confirms.
				LoadFiles();
				LogStartupBanner();
			}
			else if (State == State.Realtime)
			{
				// Watching starts only now. A backtest reads once; a background thread
				// re-reading through a 40,000 bar run would be pure waste.
				StartFileWatchers();
				LogRealtimeHandover();
			}
			else if (State == State.Terminated)
			{
				StopFileWatchers();

				// The template instance NinjaTrader builds to read SetDefaults also passes
				// through Terminated, so only report for an instance that actually ran.
				if (barsProcessed > 0)
					LogRunSummary();
			}
		}

		protected override void OnBarUpdate()
		{
			if (idxVix >= 0 && BarsInProgress == idxVix)
			{
				UpdateVix();
				return;
			}

			if (idxBreadthStart >= 0
				&& BarsInProgress >= idxBreadthStart
				&& BarsInProgress < idxBreadthStart + breadthSymbols.Length)
			{
				UpdateBreadthComponent(BarsInProgress - idxBreadthStart);
				return;
			}

			// Secondary series that only supply reference prices need no per-bar work, and
			// the fill series carries no logic at all - it exists purely so orders fill on
			// smaller bars.
			if (BarsInProgress != 0)
				return;

			lastBarTime = Time[0];

			// Proof of life on the very first bar, before any guard below can swallow it.
			if (!firstBarLogged)
			{
				firstBarLogged = true;
				firstBarTime = Time[0];
				Log(string.Format("First bar received at {0:yyyy-MM-dd HH:mm}. Warming up {1} bars before trading.",
					Time[0], BarsRequiredToTrade));
			}

			if (CurrentBar < BarsRequiredToTrade)
				return;

			if (!warmupLogged)
			{
				warmupLogged = true;
				Log(string.Format("Warm-up complete at bar {0}. Evaluating setups from here.", CurrentBar));
			}

			barsProcessed++;

			DrainCompletedTrades();

			// Flat means the entry that was holding the position is finished with. Read after
			// DrainCompletedTrades so a halt fired in there still has the label to exit with.
			if (Position.MarketPosition == MarketPosition.Flat)
				activeEntryLabel = null;

			int timeOfDay = ToTime(Time[0]);

			// The session identifier only has to be unique per session and change exactly
			// once when the session rolls; the platform's session template decides when
			// that is, which keeps this correct for both RTH and 24-hour Globex templates.
			if (Bars.IsFirstBarOfSession || currentSessionDate == DateTime.MinValue)
				currentSessionDate = Time[0].Date;

			// SyncTradingDay clears the day's realised P/L and halt state, so the outgoing
			// day has to be captured before the roll if it is to be reported afterwards.
			DateTime closingDay = risk.CurrentTradingDay;
			double closingPnL = risk.DailyRealisedPnL;
			string closingHalt = risk.IsHaltedForDay ? risk.HaltReason : null;

			if (risk.SyncTradingDay(currentSessionDate))
				OnNewTradingDay(closingDay, closingPnL, closingHalt);

			TrackSessionRanges(timeOfDay);

			if (filesActive)
				PullFiles();

			UpdateRelativeStrength();

			double atr = ATR(AtrPeriod)[0];
			if (atr <= 0)
			{
				if (!zeroAtrWarned)
				{
					zeroAtrWarned = true;
					Log(string.Format("ATR({0}) is {1} - skipping bars until it is positive. Every threshold in this strategy scales off ATR.",
						AtrPeriod, atr));
				}

				return;
			}

			// Step 1: refresh the level book, then let the analyzer look for sweeps against it.
			if (HasNewReferenceBar())
				levelsDirty = true;

			if (levelsDirty)
			{
				RebuildSessionLevels(atr);
				levelsDirty = false;
			}

			nq.Update(CurrentBar, Time[0], Open[0], High[0], Low[0], Close[0], atr);

			if (VerboseLogging && nq.OrderBlocks.LastFormedCount > 0 && nq.OrderBlocks.Active.Count > 0)
			{
				OrderBlock formed = nq.OrderBlocks.Active[nq.OrderBlocks.Active.Count - 1];
				Log(string.Format("{0} order block {1:N2}-{2:N2}{3}. {4} active.",
					formed.Kind, formed.Low, formed.High,
					formed.HasImbalance ? " with imbalance" : string.Empty,
					nq.OrderBlocks.Active.Count));
			}

			// Steps 2-4.
			SetupState stateBefore = setup.State;
			SetupResult result = setup.Update(nq, CurrentBar, Time[0], Open[0], High[0], Low[0], Close[0], atr);

			TrackFunnel(stateBefore, result);

			if (VerboseLogging && !string.IsNullOrEmpty(setup.LastTransition))
				Log(setup.LastTransition);

			LogStatusIfDue(atr);

			if (CanDraw)
			{
				UpdateTradeVisuals();
				DrawSetupZone();
				DrawStatsPanel();
			}

			// End-of-day flatten outranks everything.
			//
			// The close is re-issued on every bar it is still needed, rather than once. A
			// single attempt means one ignored or rejected exit carries the position through
			// the halt and into the next session; NinjaTrader ignores a duplicate exit while
			// one is already working, so repeating it costs nothing and is self-healing. The
			// log line stays a one-off - a nightly message is a message, a nightly stream of
			// them is wallpaper.
			if (risk.ShouldFlatten(timeOfDay))
			{
				if (Position.MarketPosition != MarketPosition.Flat)
				{
					if (!flattenedForDay)
					{
						Log(string.Format("Flatten time {0:000000} reached - closing position.", effectiveFlatten));
						flattenedForDay = true;
					}

					CloseCurrentPosition();
				}

				return;
			}

			if (Position.MarketPosition != MarketPosition.Flat)
				return;

			if (!result.HasEntry)
				return;

			// Sampled here rather than in SubmitEntry so the distribution covers every
			// completed setup, including the ones the risk gate turns away below.
			RecordStopDistance(Math.Abs(Close[0] - result.StopPrice) / TickSize);

			if (result.Direction == TradeDirection.Long)
				longSetups++;
			else
				shortSetups++;

			if (InCashSession())
				cashSetups++;
			else
				nightSetups++;

			string blockDetail;
			EntryBlockReason block = risk.CanEnter(timeOfDay, out blockDetail);
			if (block != EntryBlockReason.None)
			{
				dayBlockedByRisk++;
				totalBlockedByRisk++;

				int reason = (int)block;
				if (reason >= 0 && reason < blockReasonCounts.Length)
					blockReasonCounts[reason]++;

				Log(string.Format("Setup complete but entry blocked ({0}): {1}", block, blockDetail));
				return;
			}

			EvaluateConfirmationsAndEnter(result, atr, timeOfDay);
		}

		/// <summary>
		/// Counts how far price got down the sequence on this bar. Without this, "no trades"
		/// has no diagnosis: a run that never produced a single sweep and a run that produced
		/// forty sweeps rejected at step 5 look identical from the outside.
		/// </summary>
		/// <summary>The cash session by the clock, so the split means the same thing on any session template.</summary>
		private bool InCashSession()
		{
			int t = ToTime(Time[0]);
			return t >= 93000 && t <= 160000;
		}

		private void TrackFunnel(SetupState stateBefore, SetupResult result)
		{
			bool cash = InCashSession();

			if (nq.LastUpdateSweep.IsValid)
			{
				daySweeps++;
				totalSweeps++;

				if (cash)
					cashSweeps++;
				else
					nightSweeps++;
			}

			// A continuation enters AwaitingRetest at the break, without ever passing through
			// a structure shift. Counting that transition as a shift put 1,949 in a column
			// that had held 246, and the number described nothing.
			if (stateBefore != SetupState.AwaitingRetest && setup.State == SetupState.AwaitingRetest)
			{
				visualZoneStartBar = CurrentBar;
				lastZoneTag = null;

				if (setup.ActiveIsContinuation)
				{
					dayBreaks++;
					totalBreaks++;
				}
				else
				{
					dayShifts++;
					totalShifts++;

					if (cash)
						cashShifts++;
					else
						nightShifts++;
				}
			}

			// An entry on the same bar the zone is first touched leaves ZoneTouched already
			// cleared by the engine's reset, so the result stands in for the touch.
			if ((setup.ZoneTouched || result.HasEntry) && !previousZoneTouched)
			{
				dayZoneTouches++;
				totalZoneTouches++;

				if (cash)
					cashRetests++;
				else
					nightRetests++;
			}

			previousZoneTouched = setup.ZoneTouched;
		}

		private void LogStatusIfDue(double atr)
		{
			if (StatusEveryBars <= 0)
				return;

			if (lastStatusBar != int.MinValue && CurrentBar - lastStatusBar < StatusEveryBars)
				return;

			lastStatusBar = CurrentBar;

			// The gate is asked outright rather than inferred from the counters. A day that
			// produces no entries looks identical whether the sequence found nothing or
			// something is refusing everything, and the second case has no other symptom -
			// the rejection lines only print when a setup completes, so a gate that is shut
			// while the market is quiet leaves no trace at all until it is too late.
			string blockDetail;
			EntryBlockReason block = risk.CanEnter(ToTime(Time[0]), out blockDetail);

			Log(string.Format(
				"Status: bar {0}, ATR {1:N2}, {2} levels, setup {3}. Today: {4} sweeps, {5} shifts, {6} retests, {7} entries. Day P/L {8:C}. Entries: {9}",
				CurrentBar, atr, nq.Levels.Count, setup.State,
				daySweeps, dayShifts, dayZoneTouches, dayEntries,
				risk.DailyRealisedPnL,
				block == EntryBlockReason.None
					? "allowed"
					: string.Format("BLOCKED - {0}: {1}", block, blockDetail)));
		}

		#region Steps 5 and 6

		private void EvaluateConfirmationsAndEnter(SetupResult result, double atr, int timeOfDay)
		{
			double strength = 1.0;

			shadowVixVetoed = false;
			shadowBreadthVetoed = false;

			// Step 5.
			if (vix != null)
			{
				ConfirmationResult vixResult = vix.Evaluate(result.Direction, Time[0]);

				if (!vixResult.Agrees)
				{
					dayRejectedVix++;
					totalRejectedVix++;

					if (!ShadowConfirmations)
					{
						Log(string.Format("Setup rejected at step 5. {0} | {1}", vixResult.Detail, result.Detail));
						return;
					}

					shadowVixVetoed = true;
					Log(string.Format("Step 5 disagreed but shadow mode is on - taking the trade anyway. {0}", vixResult.Detail));
				}

				strength = Math.Min(strength, vixResult.Strength);

				if (VerboseLogging && !shadowVixVetoed)
					Log("Step 5 passed: " + vixResult.Detail);
			}

			// Step 6, only while the leaders are open. Outside that window there is no data
			// to be had at any ticker, so the step is skipped rather than failed - a shut
			// equity market is not evidence against an overnight NQ trade.
			// Relative strength replaces the leader count when selected. It carries no
			// session window: both instruments are futures on the same hours as the one
			// being traded, so there is no shut market to stand aside for.
			if (relStrength != null)
			{
				ConfirmationResult rsResult = relStrength.Evaluate(result.Direction, Time[0]);

				if (!rsResult.Agrees)
				{
					dayRejectedBreadth++;
					totalRejectedBreadth++;

					if (!ShadowConfirmations)
					{
						Log(string.Format("Setup rejected at step 6. {0} | {1}", rsResult.Detail, result.Detail));
						return;
					}

					shadowBreadthVetoed = true;
					Log("Step 6 disagreed but shadow mode is on - taking the trade anyway. " + rsResult.Detail);
				}

				strength = Math.Min(strength, rsResult.Strength);

				if (VerboseLogging && !shadowBreadthVetoed)
					Log("Step 6 passed: " + rsResult.Detail);
			}
			else if (breadth != null && !IsWithinWindow(timeOfDay, BreadthActiveStart, BreadthActiveEnd))
			{
				breadthSkippedClosed++;

				if (VerboseLogging)
					Log(string.Format("Step 6 skipped: leaders closed at {0:000000}.", timeOfDay));
			}
			else if (breadth != null)
			{
				ConfirmationResult breadthResult = breadth.Evaluate(result.Direction, Time[0]);

				if (!breadthResult.Agrees)
				{
					dayRejectedBreadth++;
					totalRejectedBreadth++;

					if (!ShadowConfirmations)
					{
						Log(string.Format("Setup rejected at step 6. {0} | {1}", breadthResult.Detail, result.Detail));
						return;
					}

					shadowBreadthVetoed = true;
					Log(string.Format("Step 6 disagreed but shadow mode is on - taking the trade anyway. {0}", breadthResult.Detail));
				}

				strength = Math.Min(strength, breadthResult.Strength);

				if (VerboseLogging && !shadowBreadthVetoed)
					Log("Step 6 passed: " + breadthResult.Detail);
			}

			SubmitEntry(result, strength);
		}

		/// <summary>
		/// Each index's own percent move over the same lookback, differenced.
		///
		/// Percent, not points: NQ trades near 23,000 and ES near 6,400, so a point spread
		/// would be almost entirely NQ's move and would say nothing about leadership. Fed
		/// from the primary bar rather than a BarsInProgress branch, because it needs both
		/// series aligned at the same instant and only the primary's close defines that.
		/// </summary>
		private void UpdateRelativeStrength()
		{
			if (relStrength == null || idxRelStrength < 0)
				return;

			int lookback = Math.Max(1, RelativeStrengthLookback);

			if (CurrentBar < lookback || CurrentBars[idxRelStrength] < lookback)
				return;

			double leadNow = Close[0];
			double leadThen = Close[lookback];
			double baseNow = Closes[idxRelStrength][0];
			double baseThen = Closes[idxRelStrength][lookback];

			if (leadThen <= 0 || baseThen <= 0)
				return;

			double leadPercent = ((leadNow - leadThen) / leadThen) * 100.0;
			double basePercent = ((baseNow - baseThen) / baseThen) * 100.0;

			// The comparison series' own timestamp, so a series that has stopped printing
			// is seen as stale rather than silently carried forward at the chart's clock.
			relStrength.Update(Times[idxRelStrength][0], leadPercent, basePercent);
		}

		#region File-backed sources

		private void LoadFiles()
		{
			if (vixFile != null)
			{
				string error;

				if (!vixFile.Load(out error))
					Log("VIX file FAILED: " + error + ". Step 5 has no data.");
			}

			for (int i = 0; i < leaderFiles.Length; i++)
			{
				if (leaderFiles[i] == null)
					continue;

				string error;

				if (!leaderFiles[i].Load(out error))
					Log(string.Format("Leader file {0} FAILED: {1}.", leaderFiles[i].Name, error));
			}
		}

		private void StartFileWatchers()
		{
			if (vixFile != null)
				vixFile.StartWatching();

			for (int i = 0; i < leaderFiles.Length; i++)
			{
				if (leaderFiles[i] != null)
					leaderFiles[i].StartWatching();
			}
		}

		private void StopFileWatchers()
		{
			if (vixFile != null)
				vixFile.Stop();

			for (int i = 0; i < leaderFiles.Length; i++)
			{
				if (leaderFiles[i] != null)
					leaderFiles[i].Stop();
			}
		}

		/// <summary>
		/// Pull the file-backed sources forward to this bar's time.
		///
		/// Called from the primary series rather than a BarsInProgress branch, because a
		/// file is not a series and has no bar event to hang off. The lookup asks for the
		/// most recent row at or before now, so a file on a different period, or one that
		/// simply stopped, degrades to a stale reading rather than a wrong one - and
		/// staleness is already step 5's business.
		/// </summary>
		private void PullFiles()
		{
			if (vixFile != null && vix != null)
			{
				FileBar bar;
				double fileAtr;
				double reference;

				if (vixFile.TryGetAt(Time[0], VixLookbackBars, out bar, out fileAtr, out reference))
				{
					vixBarsSeen++;

					// The row's own timestamp is passed through, not the chart's. That is
					// what lets the staleness guard see a file that has stopped updating -
					// stamping it "now" would make a frozen file look perfectly live.
					vix.Update(CurrentBar, bar.Time, bar.Open, bar.High, bar.Low, bar.Close, reference, fileAtr);
					vixUpdatesApplied++;
				}
			}

			if (breadth == null)
				return;

			for (int i = 0; i < leaderFiles.Length; i++)
			{
				if (leaderFiles[i] == null)
					continue;

				FileBar bar;
				double fileAtr;
				double reference;

				if (!leaderFiles[i].TryGetAt(Time[0], 1, out bar, out fileAtr, out reference))
					continue;

				double sessionOpen;

				if (!leaderFiles[i].TryGetSessionOpen(Time[0], out sessionOpen) || sessionOpen <= 0)
					continue;

				breadthSessionOpen[i] = sessionOpen;
				breadth.SetComponent(i, bar.Close, sessionOpen, bar.Time);
			}
		}

		#endregion

		private void UpdateVix()
		{
			// Counted separately so "no VIX data" can be resolved without a second run. A
			// series that produced no bars, one that produced too few to warm up, and one
			// feeding fine but rejected downstream are three different problems.
			vixBarsSeen++;

			if (vix == null || CurrentBars[idxVix] < Math.Max(VixLookbackBars, AtrPeriod) + 1)
				return;

			double vixAtr = ATR(BarsArray[idxVix], AtrPeriod)[0];
			double reference = Closes[idxVix][VixLookbackBars];

			vix.Update(CurrentBars[idxVix], Times[idxVix][0], Opens[idxVix][0],
				Highs[idxVix][0], Lows[idxVix][0], Closes[idxVix][0], reference, vixAtr);

			vixUpdatesApplied++;
		}

		private void UpdateBreadthComponent(int component)
		{
			if (breadth == null)
				return;

			int series = idxBreadthStart + component;
			if (CurrentBars[series] < 1)
				return;

			// The session open is the reference for "is this leader participating today".
			if (BarsArray[series].IsFirstBarOfSession)
				breadthSessionOpen[component] = Opens[series][0];

			if (breadthSessionOpen[component] > 0)
				breadth.SetComponent(component, Closes[series][0], breadthSessionOpen[component], Times[series][0]);
		}

		#endregion

		#region Step 1 support

		private void OnNewTradingDay(DateTime closingDay, double closingPnL, string closingHalt)
		{
			// Report the day that just ended before the counters are cleared. Skipped on the
			// first roll of the run, where there is no completed day behind us.
			if (closingDay > DateTime.MinValue)
				LogDaySummary(closingDay, closingPnL, closingHalt);

			daySweeps = 0;
			dayShifts = 0;
			dayZoneTouches = 0;
			dayEntries = 0;
			dayBlockedByRisk = 0;
			dayRejectedVix = 0;
			dayRejectedBreadth = 0;
			dayRejectedStop = 0;
			dayRejectedSizing = 0;
			dayRejectedRiskBudget = 0;
			previousZoneTouched = false;

			flattenedForDay = false;
			haltCloseLogged = false;
			overnightHigh = double.MinValue;
			overnightLow = double.MaxValue;
			openingRangeHigh = double.MinValue;
			openingRangeLow = double.MaxValue;
			sessionLevelsBuilt = false;
			levelsDirty = true;

			nq.ResetForNewSession();
			setup.Reset("New trading day.");

			if (breadth != null)
			{
				breadth.ResetSession();

				for (int i = 0; i < breadthSessionOpen.Length; i++)
					breadthSessionOpen[i] = 0;
			}

			Log("New trading day - session levels and setup state reset.");
		}

		/// <summary>True when a daily, weekly or 4-hour bar has closed since the last rebuild.</summary>
		private bool HasNewReferenceBar()
		{
			bool changed = false;

			if (CurrentBars[idxDaily] != lastDailyBar)
			{
				lastDailyBar = CurrentBars[idxDaily];
				changed = true;
			}

			if (CurrentBars[idxWeekly] != lastWeeklyBar)
			{
				lastWeeklyBar = CurrentBars[idxWeekly];
				changed = true;
			}

			if (idxFourHour >= 0 && CurrentBars[idxFourHour] != lastFourHourBar)
			{
				lastFourHourBar = CurrentBars[idxFourHour];
				changed = true;
			}

			return changed;
		}

		private void TrackSessionRanges(int timeOfDay)
		{
			// Globex overnight runs from the 18:00 open through to the 09:30 cash open.
			bool isOvernight = timeOfDay >= 180000 || timeOfDay < 93000;

			if (isOvernight)
			{
				if (High[0] > overnightHigh)
				{
					overnightHigh = High[0];
					levelsDirty = true;
				}

				if (Low[0] < overnightLow)
				{
					overnightLow = Low[0];
					levelsDirty = true;
				}
			}

			int openingRangeEnd = AddMinutesToTime(93000, OpeningRangeMinutes);

			if (timeOfDay >= 93000 && timeOfDay < openingRangeEnd)
			{
				if (High[0] > openingRangeHigh)
				{
					openingRangeHigh = High[0];
					levelsDirty = true;
				}

				if (Low[0] < openingRangeLow)
				{
					openingRangeLow = Low[0];
					levelsDirty = true;
				}
			}
		}

		private void RebuildSessionLevels(double atr)
		{
			nq.Levels.ClearSessionLevels();

			double halfWidth = atr * ZoneHalfWidthAtr;
			DateTime now = Time[0];

			// Daily, weekly and 4-hour pivot sets routinely land on top of each other. Left
			// stacked they triple-count one area and give the sweep detector three chances to
			// fire on the same wiggle.
			nq.Levels.SessionLevelMinSeparation = atr * LevelMergeAtr;

			// Each reference series is optional. A chart loaded with five days of history has
			// one weekly bar, and requiring a completed prior week there would silence the
			// whole strategy - no levels, no sweeps, no prints. Missing history costs the
			// levels it would have produced and nothing else: swing zones and order blocks
			// come from the primary series and are enough for the sequence to run.
			double pdh = 0, pdl = 0, pdc = 0;
			bool haveDaily = CurrentBars[idxDaily] >= 1;

			if (haveDaily)
			{
				pdh = Highs[idxDaily][1];
				pdl = Lows[idxDaily][1];
				pdc = Closes[idxDaily][1];

				nq.Levels.AddSessionLevel(LevelKind.PriorDayHigh, LevelTimeframe.Daily, pdh, halfWidth, now);
				nq.Levels.AddSessionLevel(LevelKind.PriorDayLow, LevelTimeframe.Daily, pdl, halfWidth, now);
				nq.Levels.AddSessionLevel(LevelKind.PriorDayClose, LevelTimeframe.Daily, pdc, halfWidth, now);

				AddPivotSet(LevelTimeframe.Daily, pdh, pdl, pdc, halfWidth, now);
			}
			else if (!dailyContextWarned)
			{
				dailyContextWarned = true;
				Log("No completed prior daily bar yet - prior-day levels and daily pivots are unavailable. Load more history if this persists.");
			}

			bool haveWeekly = CurrentBars[idxWeekly] >= 1;

			if (haveWeekly)
			{
				double pwh = Highs[idxWeekly][1];
				double pwl = Lows[idxWeekly][1];
				double pwc = Closes[idxWeekly][1];

				nq.Levels.AddSessionLevel(LevelKind.PriorWeekHigh, LevelTimeframe.Weekly, pwh, halfWidth, now);
				nq.Levels.AddSessionLevel(LevelKind.PriorWeekLow, LevelTimeframe.Weekly, pwl, halfWidth, now);

				AddPivotSet(LevelTimeframe.Weekly, pwh, pwl, pwc, halfWidth, now);
			}
			else if (!weeklyContextWarned)
			{
				weeklyContextWarned = true;
				Log("No completed prior weekly bar yet - prior-week levels and weekly pivots are unavailable. Three weeks of history removes this.");
			}

			if (idxFourHour >= 0 && CurrentBars[idxFourHour] >= 1)
			{
				AddPivotSet(LevelTimeframe.FourHour,
					Highs[idxFourHour][1], Lows[idxFourHour][1], Closes[idxFourHour][1], halfWidth, now);
			}

			if (overnightHigh > double.MinValue)
				nq.Levels.AddSessionLevel(LevelKind.OvernightHigh, LevelTimeframe.Intraday, overnightHigh, halfWidth, now);

			if (overnightLow < double.MaxValue)
				nq.Levels.AddSessionLevel(LevelKind.OvernightLow, LevelTimeframe.Intraday, overnightLow, halfWidth, now);

			if (openingRangeHigh > double.MinValue)
				nq.Levels.AddSessionLevel(LevelKind.OpeningRangeHigh, LevelTimeframe.Intraday, openingRangeHigh, halfWidth, now);

			if (openingRangeLow < double.MaxValue)
				nq.Levels.AddSessionLevel(LevelKind.OpeningRangeLow, LevelTimeframe.Intraday, openingRangeLow, halfWidth, now);

			if (!sessionLevelsBuilt)
			{
				sessionLevelsBuilt = true;

				Log(haveDaily
					? string.Format("Context built: PDH {0:N2}, PDL {1:N2}, PDC {2:N2}, {3} levels in play ({4} merged as duplicates).",
						pdh, pdl, pdc, nq.Levels.Count, nq.Levels.SessionLevelsMerged)
					: string.Format("Context built without prior-day data: {0} levels in play ({1} merged as duplicates).",
						nq.Levels.Count, nq.Levels.SessionLevelsMerged));
			}
		}

		private void AddPivotSet(LevelTimeframe timeframe, double high, double low, double close, double halfWidth, DateTime now)
		{
			if (high <= 0 || low <= 0 || close <= 0 || high < low)
				return;

			double p, r1, r2, r3, s1, s2, s3;
			FloorPivots.Classic(high, low, close, out p, out r1, out r2, out r3, out s1, out s2, out s3);

			nq.Levels.AddSessionLevel(LevelKind.Pivot, timeframe, p, halfWidth, now);
			nq.Levels.AddSessionLevel(LevelKind.R1, timeframe, r1, halfWidth, now);
			nq.Levels.AddSessionLevel(LevelKind.R2, timeframe, r2, halfWidth, now);
			nq.Levels.AddSessionLevel(LevelKind.R3, timeframe, r3, halfWidth, now);
			nq.Levels.AddSessionLevel(LevelKind.S1, timeframe, s1, halfWidth, now);
			nq.Levels.AddSessionLevel(LevelKind.S2, timeframe, s2, halfWidth, now);
			nq.Levels.AddSessionLevel(LevelKind.S3, timeframe, s3, halfWidth, now);
		}

		#endregion

		#region Order handling

		private void SubmitEntry(SetupResult result, double confirmationStrength)
		{
			bool bullish = result.Direction == TradeDirection.Long;
			double entryPrice = Close[0];
			double stopPrice = result.StopPrice;
			double targetPrice = result.TargetPrice;
			double stopDistanceTicks = Math.Abs(entryPrice - stopPrice) / TickSize;

			if (stopDistanceTicks < MinStopTicks)
			{
				dayRejectedStop++;
				totalRejectedStop++;
				Log(string.Format("Entry skipped: stop {0:N0} ticks is below the {1} tick minimum. {2}",
					stopDistanceTicks, MinStopTicks, result.Detail));
				return;
			}

			if (stopDistanceTicks > MaxStopTicks)
			{
				dayRejectedStop++;
				totalRejectedStop++;
				Log(string.Format("Entry skipped: stop {0:N0} ticks exceeds the {1} tick maximum. {2}",
					stopDistanceTicks, MaxStopTicks, result.Detail));
				return;
			}

			string sizingReason;
			int contracts = sizer.GetContracts(sizerSettings, stopDistanceTicks, out sizingReason);

			if (contracts > 0 && UseConfidenceSizing && confirmationStrength < 1.0)
			{
				int scaled = (int)Math.Floor(contracts * confirmationStrength);
				contracts = Math.Max(1, scaled);
				sizingReason += string.Format(" Scaled to {0} on confirmation strength {1:P0}.", contracts, confirmationStrength);
			}

			if (contracts <= 0)
			{
				dayRejectedSizing++;
				totalRejectedSizing++;
				Log("Entry skipped. " + sizingReason);
				return;
			}

			// The daily loss limit only ever looked backwards: RecordClosedTrade halts trading
			// once the day is already down, which cannot stop a single trade from blowing
			// through the cap on its own. With structural stops averaging more than the cap,
			// that is not a corner case - it is every trade. Checked here, before the order,
			// against what this trade actually stands to lose.
			double intendedRisk = stopDistanceTicks * TickValueDollars * contracts;
			double remainingBudget = MaxDailyLossDollars + Math.Min(0, risk.DailyRealisedPnL);

			if (MaxDailyLossDollars > 0 && intendedRisk > remainingBudget)
			{
				dayRejectedRiskBudget++;
				totalRejectedRiskBudget++;
				Log(string.Format(
					"Entry skipped: risking {0:C} ({1:N0} ticks x {2} contract(s)) against {3:C} left of today's {4:C} limit. {5}",
					intendedRisk, stopDistanceTicks, contracts, remainingBudget, MaxDailyLossDollars, result.Detail));
				return;
			}

			string label = result.Label;

			SetStopLoss(label, CalculationMode.Price, stopPrice, false);

			// Set methods are sticky: a price registered against a signal name stays registered
			// until it is overwritten. Skipping the call when there is no target left the
			// previous trade's target attached to the next entry sharing the label - a target
			// derived from a different swing, at a price with no relationship to this entry.
			// Every path through the engine produces a target, so this is a guard rather than a
			// live bug, but a silent one is exactly what it would have been.
			if (targetPrice <= 0)
			{
				Log(string.Format("Entry skipped: no target price was produced for {0}. {1}", label, result.Detail));
				return;
			}

			SetProfitTarget(label, CalculationMode.Price, targetPrice);

			// Submitted against the fine series when there is one, so the fill - and the stop
			// and target attached to this entry - resolve on its bars rather than the
			// primary's. Same instrument, so it is the same position either way.
			if (idxFill >= 0)
			{
				if (bullish)
					EnterLong(idxFill, contracts, label);
				else
					EnterShort(idxFill, contracts, label);
			}
			else if (bullish)
			{
				EnterLong(contracts, label);
			}
			else
			{
				EnterShort(contracts, label);
			}

			activeEntryLabel = label;
			risk.RecordEntry();
			dayEntries++;
			totalEntries++;

			// Queued so the realised result can be divided by the risk that was actually
			// taken. One position at a time and one entry per direction, so pairing the
			// oldest unmatched entry with the next closed trade holds.
			pendingRiskDollars.Add(stopDistanceTicks * TickValueDollars * contracts);
			// The cash session by the clock, not by the platform's session template, so the
			// split means the same thing whatever template the chart is on.
			int entryTime = ToTime(Time[0]);
			pendingWasCash.Add(entryTime >= 93000 && entryTime <= 160000);

			pendingIsContinuation.Add(result.IsContinuation);
			pendingVixVeto.Add(shadowVixVetoed);
			pendingBreadthVeto.Add(shadowBreadthVetoed);

			double plannedReward = targetPrice > 0 ? Math.Abs(targetPrice - entryPrice) : 0;
			double plannedRisk = Math.Abs(entryPrice - stopPrice);

			if (plannedRisk > 0 && plannedReward > 0)
			{
				plannedRrSum += plannedReward / plannedRisk;
				plannedRrCount++;
			}

			visualEntryBar = CurrentBar;
			visualStop = stopPrice;
			visualTarget = targetPrice;
			visualTag = "t" + CurrentBar;
			DrawEntryMarker(result, entryPrice, stopPrice, targetPrice);

			if (bullish)
				longEntries++;
			else
				shortEntries++;

			// Counted here, at the order, rather than in the engine. The engine increments
			// when a setup completes, which includes every one the gates below it went on to
			// refuse - so "by kind" summed to 202 against 39 entries submitted.
			if (result.IsContinuation)
				continuationEntries++;
			else
				reversalEntries++;

			Log(string.Format("ENTRY {0} x{1} @ ~{2:N2}, stop {3:N2} ({4:N0} ticks), target {5:N2}. {6} {7}",
				result.Direction, contracts, entryPrice, stopPrice, stopDistanceTicks, targetPrice,
				result.Detail, sizingReason));
		}

		/// <summary>
		/// Flatten, on the same Bars object the entry was submitted against.
		///
		/// This used to call the no-argument ExitLong()/ExitShort(), which submit against the
		/// series currently in progress - always the primary, since that is the only branch
		/// that reaches here. With entries routed to the fill series that is the wrong Bars
		/// object, and the managed approach ignores an exit whose Bars object does not match
		/// the entry's. The flatten and the risk halt were both liable to do nothing at all
		/// and log that they had closed the position. Naming the entry signal as well means
		/// the exit is matched to its entry rather than to whatever happens to be open.
		/// </summary>
		private void CloseCurrentPosition()
		{
			if (Position.MarketPosition == MarketPosition.Flat)
				return;

			bool longPosition = Position.MarketPosition == MarketPosition.Long;
			int quantity = Position.Quantity;
			int series = idxFill >= 0 ? idxFill : 0;
			string exitName = longPosition ? "exitLong" : "exitShort";

			// Empty means "whatever entry is holding this", which is the right answer when no
			// entry of ours is on record - a position left by a previous instance, say.
			string fromEntry = activeEntryLabel ?? string.Empty;

			if (longPosition)
				ExitLong(series, quantity, exitName, fromEntry);
			else
				ExitShort(series, quantity, exitName, fromEntry);
		}

		private void DrainCompletedTrades()
		{
			int total = SystemPerformance.AllTrades.Count;

			while (processedTradeCount < total)
			{
				Trade trade = SystemPerformance.AllTrades[processedTradeCount];
				processedTradeCount++;

				risk.RecordClosedTrade(trade.ProfitCurrency);
				RecordTradeResult(trade.ProfitCurrency);

				Log(string.Format("Trade closed: {0:C}. Day P/L {1:C}, trades {2}, consecutive losses {3}.",
					trade.ProfitCurrency, risk.DailyRealisedPnL, risk.TradesToday, risk.ConsecutiveLosses));
			}

			// Re-issued each bar for the same reason as the flatten, and logged once so a
			// position that takes several bars to close does not fill the window.
			if (risk.IsHaltedForDay && Position.MarketPosition != MarketPosition.Flat)
			{
				if (!haltCloseLogged)
				{
					haltCloseLogged = true;
					Log("Risk halt with open position - closing. " + risk.HaltReason);
				}

				CloseCurrentPosition();
			}
		}

		/// <summary>
		/// What the broker did with an order, which until now nothing reported.
		///
		/// The log could say ENTRY and the account could show nothing, with no line anywhere
		/// explaining the gap. A prop firm's risk layer sits between this strategy and the
		/// exchange and will refuse orders on its own terms - position limits, product
		/// permissions, a daily loss rule of its own, trading windows - and NinjaTrader
		/// records that in the Orders tab rather than in the Output window anyone is reading.
		///
		/// Realtime only. A backtest fills everything and would drown the window.
		/// </summary>
		protected override void OnOrderUpdate(Order order, double limitPrice, double stopPrice,
			int quantity, int filled, double averageFillPrice, OrderState orderState, DateTime time,
			ErrorCode error, string comment)
		{
			if (State != State.Realtime || order == null)
				return;

			if (orderState == OrderState.Rejected)
			{
				Log(string.Format("ORDER REJECTED: {0} {1} x{2} - {3} {4}",
					order.Name, order.OrderAction, order.Quantity, error, comment));
				Log("        The strategy asked for this trade and the broker refused it. Nothing in the");
				Log("        strategy can fix that - check the Orders and Log tabs, and your account's own");
				Log("        rules on size, product and trading hours.");
				return;
			}

			// A working order disappearing without a fill is the quieter version of the same
			// problem, and the one that looks like the strategy simply never traded.
			if (orderState == OrderState.Cancelled && filled == 0)
			{
				Log(string.Format("Order cancelled unfilled: {0} {1} x{2}. {3}",
					order.Name, order.OrderAction, order.Quantity,
					string.IsNullOrEmpty(comment) ? "No reason given." : comment));
			}
		}

		/// <summary>
		/// Fills. The line that says a decision became a position, so its absence after an
		/// ENTRY line localises the problem to order routing rather than to the sequence.
		/// </summary>
		protected override void OnExecutionUpdate(Execution execution, string executionId, double price,
			int quantity, MarketPosition marketPosition, string orderId, DateTime time)
		{
			if (State != State.Realtime || execution == null || execution.Order == null)
				return;

			if (execution.Order.OrderState != OrderState.Filled
				&& execution.Order.OrderState != OrderState.PartFilled)
				return;

			Log(string.Format("FILL: {0} {1} x{2} @ {3:N2}. Position now {4} {5}.",
				execution.Order.Name, marketPosition, quantity, price,
				Position.MarketPosition, Position.Quantity));
		}

		/// <summary>
		/// Connection health, which gates new entries.
		///
		/// Only while live. This fires during startup too, and a status arriving mid-replay
		/// used to latch the gate shut for the rest of the historical run - so the replay
		/// would silently stop taking setups partway through and the funnel would blame the
		/// risk gate. A replay has no connection to lose: every bar in it already happened.
		/// </summary>
		protected override void OnConnectionStatusUpdate(ConnectionStatusEventArgs e)
		{
			if (risk == null || State != State.Realtime)
				return;

			bool down = e.Status != ConnectionStatus.Connected || e.PriceStatus != ConnectionStatus.Connected;
			risk.SetConnectionDown(down);

			Log(string.Format("Connection update: order={0}, price={1}. New entries {2}.",
				e.Status, e.PriceStatus, down ? "BLOCKED" : "allowed"));
		}

		#endregion

		#region Chart visuals

		// Drawing is skipped entirely when there is no chart, which is how the Strategy
		// Analyzer runs. A 42,000-bar optimisation should not be paying to draw rectangles
		// nobody will look at.
		private bool CanDraw
		{
			get { return ShowChartVisuals && ChartControl != null; }
		}

		/// <summary>
		/// Stop and target as lines running from the entry bar to wherever the trade is now,
		/// redrawn each bar under the same tag so they extend rather than accumulate. Drawn
		/// from past bars only - projecting into the future needs bars that do not exist yet.
		/// </summary>
		private void UpdateTradeVisuals()
		{
			if (!CanDraw || visualEntryBar < 0)
				return;

			int barsAgo = CurrentBar - visualEntryBar;

			if (barsAgo < 0 || barsAgo > 400)
			{
				visualEntryBar = -1;
				return;
			}

			Draw.Line(this, "stop" + visualTag, barsAgo, visualStop, 0, visualStop, Brushes.Crimson);

			if (visualTarget > 0)
				Draw.Line(this, "targ" + visualTag, barsAgo, visualTarget, 0, visualTarget, Brushes.SeaGreen);

			// Once flat the trade is history: leave the last drawing in place and stop
			// extending it, so the chart keeps a record of where the levels were.
			if (Position.MarketPosition == MarketPosition.Flat)
				visualEntryBar = -1;
		}

		private void DrawEntryMarker(SetupResult result, double entryPrice, double stopPrice, double targetPrice)
		{
			if (!CanDraw)
				return;

			bool bullish = result.Direction == TradeDirection.Long;
			double risk = Math.Abs(entryPrice - stopPrice);
			double reward = targetPrice > 0 ? Math.Abs(targetPrice - entryPrice) : 0;

			if (bullish)
				Draw.ArrowUp(this, "in" + visualTag, true, 0, Low[0] - (TickSize * 8), Brushes.DodgerBlue);
			else
				Draw.ArrowDown(this, "in" + visualTag, true, 0, High[0] + (TickSize * 8), Brushes.Orange);

			Draw.Text(this, "lbl" + visualTag,
				string.Format("{0}  {1:N1}R", result.IsContinuation ? "cont" : "sweep",
					risk > 0 ? reward / risk : 0),
				0,
				bullish ? Low[0] - (TickSize * 20) : High[0] + (TickSize * 20),
				bullish ? Brushes.DodgerBlue : Brushes.Orange);
		}

		/// <summary>The retest zone while a setup is waiting in it, so a chart shows what the strategy is watching rather than only what it did.</summary>
		private void DrawSetupZone()
		{
			if (!CanDraw || !ShowSetupZones || setup == null)
				return;

			if (setup.State != SetupState.AwaitingRetest || setup.ZoneHalfWidth <= 0)
			{
				lastZoneTag = null;
				return;
			}

			if (lastZoneTag == null)
				lastZoneTag = "zone" + CurrentBar;

			int startBarsAgo = CurrentBar - visualZoneStartBar;

			if (startBarsAgo < 0 || startBarsAgo > 400)
				startBarsAgo = 0;

			Draw.Rectangle(this, lastZoneTag,
				startBarsAgo, setup.ZoneCenter - setup.ZoneHalfWidth,
				0, setup.ZoneCenter + setup.ZoneHalfWidth,
				setup.ActiveIsContinuation ? Brushes.MediumPurple : Brushes.SteelBlue);
		}

		private void DrawStatsPanel()
		{
			if (!CanDraw || !ShowStatsPanel)
				return;

			int closed = tradesWon + tradesLost + tradesScratch;
			double factor = grossLoss > 0 ? grossProfit / grossLoss : 0;

			string text = string.Format(
				"SOCRATES NQ\n"
				+ "state      {0}{1}\n"
				+ "-----------------------\n"
				+ "trades     {2}  ({3}W / {4}L)\n"
				+ "win rate   {5:N0}%\n"
				+ "net        {6:C0}\n"
				+ "factor     {7}\n"
				+ "per trade  {8}\n"
				+ "planned    1 : {9:N1}\n"
				+ "-----------------------\n"
				+ "today      {10} sweeps, {11} shifts\n"
				+ "           {12} entries, {13:C0}",
				setup != null ? setup.State.ToString() : "-",
				setup != null && setup.ActiveIsContinuation ? " (cont)" : string.Empty,
				closed, tradesWon, tradesLost,
				closed > 0 ? (tradesWon * 100.0) / closed : 0,
				grossProfit - grossLoss,
				factor > 0 ? string.Format("{0:N2}", factor) : "-",
				closed > 0 ? string.Format("{0:C0}", (grossProfit - grossLoss) / closed) : "-",
				plannedRrCount > 0 ? plannedRrSum / plannedRrCount : 0,
				daySweeps, dayShifts, dayEntries, risk != null ? risk.DailyRealisedPnL : 0);

			Draw.TextFixed(this, "socratesPanel", text, TextPosition.TopRight,
				Brushes.Gainsboro, new SimpleFont("Consolas", 12), Brushes.Transparent,
				Brushes.Black, 60);
		}

		#endregion

		#region Helpers

		private static string[] ParseSymbols(string csv)
		{
			if (string.IsNullOrEmpty(csv))
				return new string[0];

			string[] raw = csv.Split(',');
			List<string> cleaned = new List<string>();

			for (int i = 0; i < raw.Length; i++)
			{
				string symbol = raw[i].Trim();

				if (symbol.Length > 0)
					cleaned.Add(symbol);
			}

			return cleaned.ToArray();
		}

		/// <summary>
		/// Inclusive HHmmss window, wrapping midnight when start is after end. Equal start
		/// and end means always open.
		/// </summary>
		private static bool IsWithinWindow(int timeOfDay, int start, int end)
		{
			if (start == end)
				return true;

			if (start < end)
				return timeOfDay >= start && timeOfDay <= end;

			return timeOfDay >= start || timeOfDay <= end;
		}

		/// <summary>Adds minutes to an HHmmss integer without leaving the HHmmss form.</summary>
		private static int AddMinutesToTime(int hhmmss, int minutes)
		{
			int hours = hhmmss / 10000;
			int mins = (hhmmss / 100) % 100;
			int secs = hhmmss % 100;

			int total = (hours * 60) + mins + minutes;
			total = ((total % 1440) + 1440) % 1440;

			return ((total / 60) * 10000) + ((total % 60) * 100) + secs;
		}

		/// <summary>
		/// Timestamped output to the NinjaScript Output window. The timestamp is taken from
		/// the current bar when there is one - OnStateChange and OnConnectionStatusUpdate can
		/// both fire before any bar exists, and dereferencing Time[0] there throws, which
		/// disables the strategy and produces exactly the silence this is meant to prevent.
		/// </summary>
		private void Log(string message)
		{
			if (!EnableLogging)
				return;

			string stamp = State.ToString();

			try
			{
				if (BarsInProgress >= 0 && CurrentBar >= 0)
					stamp = Time[0].ToString("yyyy-MM-dd HH:mm:ss");
			}
			catch (Exception)
			{
				// OnConnectionStatusUpdate fires off the bar thread and can arrive before any
				// series is addressable. The state name is a fine substitute; losing the line
				// entirely is not.
			}

			Print(string.Format("[{0}] Socrates: {1}", stamp, message));
		}

		private void ResetDiagnostics()
		{
			firstBarLogged = false;
			firstBarTime = DateTime.MinValue;
			lastBarTime = DateTime.MinValue;
			warmupLogged = false;
			dailyContextWarned = false;
			weeklyContextWarned = false;
			zeroAtrWarned = false;
			previousZoneTouched = false;
			barsProcessed = 0;
			lastStatusBar = int.MinValue;

			daySweeps = dayShifts = dayZoneTouches = dayEntries = 0;
			dayBlockedByRisk = dayRejectedVix = dayRejectedBreadth = dayRejectedStop = dayRejectedSizing = 0;
			dayRejectedRiskBudget = 0;
			dayBreaks = 0;

			totalSweeps = totalShifts = totalZoneTouches = totalEntries = 0;
			totalBlockedByRisk = totalRejectedVix = totalRejectedBreadth = totalRejectedStop = totalRejectedSizing = 0;
			totalRejectedRiskBudget = 0;

			Array.Clear(blockReasonCounts, 0, blockReasonCounts.Length);
			Array.Clear(stopTickBuckets, 0, stopTickBuckets.Length);
			stopSamples = 0;
			stopTicksMin = 0;
			stopTicksMax = 0;
			stopTicksSum = 0;
			longSetups = shortSetups = longEntries = shortEntries = 0;
			cashSweeps = nightSweeps = cashShifts = nightShifts = 0;
			cashRetests = nightRetests = cashSetups = nightSetups = 0;
			reversalEntries = continuationEntries = 0;
			visualEntryBar = -1;
			visualZoneStartBar = 0;
			lastZoneTag = null;
			plannedRrSum = 0;
			plannedRrCount = 0;
			dayBreaks = totalBreaks = 0;
			breadthSkippedClosed = 0;
			vixBarsSeen = 0;
			vixUpdatesApplied = 0;

			pendingRiskDollars.Clear();
			pendingWasCash.Clear();
			cashTrades = cashWon = nightTrades = nightWon = 0;
			cashGrossProfit = cashGrossLoss = nightGrossProfit = nightGrossLoss = 0;

			pendingIsContinuation.Clear();
			revTrades = revWon = contTrades = contWon = 0;
			revGrossProfit = revGrossLoss = contGrossProfit = contGrossLoss = 0;

			pendingVixVeto.Clear();
			pendingBreadthVeto.Clear();
			shadowVixVetoed = shadowBreadthVetoed = false;
			shadowVixVetoTrades = shadowVixVetoWon = 0;
			shadowBreadthVetoTrades = shadowBreadthVetoWon = 0;
			shadowCleanTrades = shadowCleanWon = 0;
			shadowTotalTrades = 0;
			shadowTotalPnL = 0;
			shadowVixVetoPnL = shadowBreadthVetoPnL = shadowCleanPnL = 0;
			tradesWon = tradesLost = tradesScratch = 0;
			grossProfit = grossLoss = largestLoss = 0;
			runningEquity = equityPeak = maxDrawdown = 0;
			rSamples = 0;
			rSum = 0;
			riskDollarsSum = 0;
			riskDollarsMin = double.MaxValue;
			riskDollarsMax = 0;
			rMin = double.MaxValue;
			rMax = double.MinValue;
			Array.Clear(rBuckets, 0, rBuckets.Length);
			stopOverruns = 0;
			rSumCappedAtStop = 0;
		}

		/// <summary>
		/// Printed once every series has loaded. This is the line that proves the file
		/// compiled, the strategy is enabled and the Output window is pointed at it - and it
		/// names every series with its bar count, because a series that loaded empty is the
		/// most common reason a correct strategy does nothing.
		/// </summary>
		private void LogStartupBanner()
		{
			if (!EnableLogging)
				return;

			Print("=== Socrates NQ ===================================================");
			Print(string.Format("  Instrument      : {0}", Instrument != null ? Instrument.FullName : "unknown"));
			Print(string.Format("  Calculate       : {0}, bars required {1}", Calculate, BarsRequiredToTrade));
			Print(string.Format("  Trading hours   : {0}", DescribeSession()));
			Print(string.Format("  Sizing          : {0} contract(s), max {1}, daily loss cap {2}", FixedContracts, MaxContracts, DescribeDailyCap()));
			WarnOnTickValueMismatch();
			Print(string.Format("  Stop            : {0}", DescribeStop()));
			Print(string.Format("  Step 5 (VIX)    : {0}", DescribeVixSource()));
			Print(string.Format("  Step 6 (leaders): {0}", DescribeBreadthSource()));
			Print(string.Format("  Fills           : {0}, slippage {1} tick(s)",
				FillResolutionMinutes > 0
					? string.Format("orders submitted on a {0}-minute series", FillResolutionMinutes)
					: "orders on the primary series - intrabar sequence is assumed",
				Slippage));
			Print(string.Format("  Data series     : {0}", BarsArray != null ? BarsArray.Length : 0));

			if (BarsArray != null)
			{
				// A multi-series backtest cannot start before every series has data, so one
				// short series silently truncates the whole run - which reads as a strategy
				// that found fewer setups, not as a data problem. The first timestamp of each
				// series is what identifies the culprit.
				DateTime latestStart = DateTime.MinValue;
				string latestStartSeries = null;

				for (int i = 0; i < BarsArray.Length; i++)
				{
					int count = BarsArray[i] != null ? BarsArray[i].Count : 0;
					string from = "-";

					if (count > 0)
					{
						DateTime start = BarsArray[i].GetTime(0);
						from = start.ToString("yyyy-MM-dd");

						if (start > latestStart)
						{
							latestStart = start;
							latestStartSeries = DescribeSeries(i);
						}
					}

					Print(string.Format("    [{0}] {1,-24} {2,7} bars from {3}{4}", i, DescribeSeries(i), count, from,
						count == 0 ? "   <-- EMPTY, this series has no data" : string.Empty));
				}

				if (latestStartSeries != null && BarsArray.Length > 1)
				{
					Print(string.Format("  Backtest cannot begin before {0:yyyy-MM-dd} - that is where '{1}' starts.",
						latestStart, latestStartSeries));
				}

				if (idxDaily >= 0 && BarsArray[idxDaily] != null && BarsArray[idxDaily].Count < 2)
					Print("  NOTE: fewer than two daily bars. Prior-day levels and daily pivots will be skipped.");

				if (idxWeekly >= 0 && BarsArray[idxWeekly] != null && BarsArray[idxWeekly].Count < 2)
					Print("  NOTE: fewer than two weekly bars. Prior-week levels and weekly pivots will be skipped.");

				// If this ever trips, every reference below index 0 is reading the wrong
				// series and nothing downstream can be trusted. Loud, because the symptom
				// otherwise is levels that look plausible and are simply wrong.
				if (BarsArray.Length != expectedSeriesCount)
				{
					Print(string.Format("  WARNING: expected {0} series, got {1}. The indices this strategy uses are",
						expectedSeriesCount, BarsArray.Length));
					Print("           positional, so daily, weekly and VIX lookups are now pointing at the wrong");
					Print("           data. Do not trust this run. Report it with the series list above.");
				}
			}

			LogFileBanner();
			LogRiskConsistency();

			if (BreadthMode != ConfirmationMode.Off)
			{
				Print(BreadthActiveStart == BreadthActiveEnd
					? "  Step 6 applies around the clock; the staleness guard skips it where the leaders have no data."
					: string.Format("  Step 6 applies {0:000000}-{1:000000} only; outside it the step is skipped, not failed.",
						BreadthActiveStart, BreadthActiveEnd));

				// A cash ticker here is not a slow step, it is a strategy that will not start:
				// NinjaTrader refuses to run when a series cannot be resolved, and writes the
				// reason to the Log tab rather than the Output window anyone is watching.
				if (!string.IsNullOrEmpty(BreadthSymbols) && BreadthSymbols.IndexOf('S') != 0)
				{
					Print("  NOTE: leader symbols should be the CME single stock futures (SAAPL, SMSFT, ...).");
					Print("        This feed carries no cash equities, and an unresolvable symbol stops the");
					Print("        strategy before it prints anything - check the Log tab if it goes quiet.");
				}
			}

			// The ^VIX index is only disseminated around the cash session, so on a Globex
			// chart it is absent for most of the night and its staleness guard would refuse
			// every overnight setup. VX futures run close to 23 hours.
			if (TradingHours == TradingHoursMode.ExtendedHours
				&& VixMode != ConfirmationMode.Off
				&& VixSymbol != null
				&& VixSymbol.TrimStart().StartsWith("^"))
			{
				Print("  WARNING: extended hours with the ^VIX index. It is not published overnight, so");
				Print("           step 5 will refuse every setup outside the cash session. Use VX instead.");
			}

			// NinjaTrader keeps the parameter values you configured on an instance, so a
			// changed default does not reach a strategy that already exists on a chart or in
			// an Analyzer template. Printing what is actually in effect is the only way to
			// tell a threshold that did not work from one that was never applied.
			Print("  --- thresholds in effect ---");
			Print(string.Format("  Sweep        : penetration max({0:N2} ATR, {1:N2} pts), reclaim within {2} bars",
				MinPenetrationAtr, MinPenetrationPoints, MaxBarsToReclaim));
			Print(string.Format("  Continuations: {0}", EnableContinuations
				? (ContinuationsOnMajorLevelsOnly ? "on, major levels only" : "on, any level in the book")
				: "off"));
			Print(string.Format("  Reversals    : {0}", EnableReversals ? "on" : "off"));
			Print(string.Format("  Levels       : zone half-width {0:N2} ATR, merge within {1:N2} ATR, opening range {2} min",
				ZoneHalfWidthAtr, LevelMergeAtr, OpeningRangeMinutes));
			Print(string.Format("  Structure    : swing strength {0}, displacement {1:N2} ATR, max setup risk {2:N2} ATR, {3} bars to shift",
				SwingStrength, MinDisplacementAtr, MaxSetupRiskAtr, MaxBarsSweepToShift));
			Print(string.Format("  Retest       : zone {0} +/- {1:N2} ATR, {2} bars to retest, confirmation close {3}",
				ZoneMode, RetestZoneAtr, MaxBarsShiftToRetest, RequireConfirmationClose ? "required" : "not required"));
			Print(string.Format("  Exits        : stop {0:N2} ATR past the previous swing, target {1} ticks short of the next one, min {2:N2}R",
				StopBufferAtr, TargetBufferTicks, MinRewardRisk));

			Print("===================================================================");
		}

		/// <summary>
		/// Check the configured tick value against what the instrument actually pays.
		///
		/// Nothing enforces agreement, and a template carried from one contract to another
		/// keeps the old number: NQ pays $5.00 a tick and MNQ pays $0.50, so a template
		/// moved from the full contract to the micro reports every risk figure and every R
		/// multiple ten times too large, and prices any dollar-denominated risk cap ten
		/// times too loose. The run looks entirely normal.
		/// </summary>
		private void WarnOnTickValueMismatch()
		{
			if (Instrument == null || Instrument.MasterInstrument == null)
				return;

			double actual = Instrument.MasterInstrument.PointValue * Instrument.MasterInstrument.TickSize;

			if (actual <= 0)
				return;

			// A tolerance rather than equality: point values are doubles and some
			// instruments carry values that do not divide cleanly.
			if (Math.Abs(actual - TickValueDollars) <= actual * 0.01)
				return;

			Print(string.Format(
				"  *** TICK VALUE MISMATCH: 'Tick value ($)' is {0:C} but {1} pays {2:C} a tick.",
				TickValueDollars, Instrument.MasterInstrument.Name, actual));
			Print(string.Format(
				"      Every risk figure and R multiple below is off by {0:N1}x, and any dollar risk cap is priced wrong. Set it to {1:C}.",
				TickValueDollars / actual, actual));
		}

		/// <summary>
		/// Where step 5 is reading from, said plainly.
		///
		/// Same silent fallback the leader path has: an unusable file path drops step 5 onto
		/// the platform series without complaint. It is a different confirmation with
		/// different rejection behaviour, and a run that switched sources without anyone
		/// intending it is not the experiment it looks like.
		/// </summary>
		private string DescribeVixSource()
		{
			if (VixMode == ConfirmationMode.Off)
				return "Off";

			return vixFile != null
				? string.Format("{0}, read from FILE {1}", VixMode, VixFile)
				: string.Format("{0}, platform series {1} at {2} minutes", VixMode, VixSymbol, VixBarMinutes);
		}

		/// <summary>
		/// Which form of step 6 is actually in force, said plainly.
		///
		/// The leader path falls back from files to platform series silently when the paths
		/// are missing or unreadable, which is the right behaviour and the wrong thing to
		/// leave unstated: a run configured for single stock futures that quietly kept
		/// reading yesterday's CSVs looks like a successful test of something it never
		/// tested.
		/// </summary>
		private string DescribeBreadthSource()
		{
			if (BreadthMode == ConfirmationMode.Off)
				return "Off";

			if (BreadthSource == BreadthSourceMode.RelativeStrength)
				return string.Format("{0}, relative strength of {1} against {2} over {3} bars",
					BreadthMode, Instrument != null ? Instrument.MasterInstrument.Name : "the traded instrument",
					RelativeStrengthSymbol, RelativeStrengthLookback);

			return string.Format("{0}, {1} leaders read from {2} - {3}",
				BreadthMode, breadthSymbols.Length, filesActive ? "FILES" : "platform series",
				BreadthSymbols);
		}

		/// <summary>
		/// Whether the files are current enough to be worth anything live.
		///
		/// The one failure a file source has that a platform series does not: it is a
		/// snapshot, and going live with a stale one means the confirmations stand aside
		/// for the rest of the session. That is the safe behaviour and it is still not the
		/// behaviour anyone intended, so it is said plainly at the moment it starts to
		/// matter rather than left for the summary.
		/// </summary>
		private void WarnOnStaleFiles()
		{
			if (!filesActive)
				return;

			DateTime now = DateTime.Now;

			WarnIfStale(vixFile, now, "Step 5");

			for (int i = 0; i < leaderFiles.Length; i++)
				WarnIfStale(leaderFiles[i], now, "Step 6");
		}

		private void WarnIfStale(FileSeries file, DateTime now, string step)
		{
			if (file == null || file.RowCount == 0)
				return;

			double hoursBehind = (now - file.LastTime).TotalHours;

			// Overnight gaps and weekends are normal for an equity file, so the threshold is
			// generous. This is meant to catch a file nobody is updating, not one waiting
			// for Monday.
			if (hoursBehind < 72)
				return;

			Print(string.Format("  WARNING: file '{0}' ends {1:N0} hours ago ({2:yyyy-MM-dd HH:mm}).",
				file.Name, hoursBehind, file.LastTime));
			Print(string.Format("           Nothing is updating it, so {0} will stand aside for the whole", step));
			Print("           session rather than confirm against a frozen reading. Either point the");
			Print("           parameter back at the platform series for live trading, or run whatever");
			Print("           writes this file - it is re-read automatically when it changes.");
		}

		/// <summary>
		/// What each file actually contains, and whether it lines up with the chart.
		///
		/// A file an hour out does not fail. It answers every lookup with a row from the
		/// wrong hour, the confirmations read the wrong volatility, and the only symptom is
		/// results that are quietly worse for no reason anyone can point at. Printing the
		/// file's first row beside the chart's first bar makes that one line instead of a
		/// week, which is the whole reason this section exists.
		/// </summary>
		private void LogFileBanner()
		{
			if (!filesActive)
				return;

			DateTime chartStart = BarsArray != null && BarsArray[0] != null && BarsArray[0].Count > 0
				? BarsArray[0].GetTime(0)
				: DateTime.MinValue;

			DateTime chartEnd = BarsArray != null && BarsArray[0] != null && BarsArray[0].Count > 0
				? BarsArray[0].GetTime(BarsArray[0].Count - 1)
				: DateTime.MinValue;

			Print("  --- file-backed sources ---");
			Print(string.Format("  Chart covers    : {0:yyyy-MM-dd HH:mm} to {1:yyyy-MM-dd HH:mm}", chartStart, chartEnd));

			if (vixFile != null)
				PrintFileLine(vixFile, chartStart, chartEnd);

			for (int i = 0; i < leaderFiles.Length; i++)
			{
				if (leaderFiles[i] != null)
					PrintFileLine(leaderFiles[i], chartStart, chartEnd);
			}

			Print(string.Format("  Re-read         : {0}",
				FileReloadSeconds > 0
					? string.Format("every {0}s once live, and only when the file has changed", FileReloadSeconds)
					: "off - the startup load is all there is"));

			if (FileTimeOffsetMinutes != 0)
				Print(string.Format("  Time offset     : {0:+0;-0} minutes applied to every row.", FileTimeOffsetMinutes));
		}

		private void PrintFileLine(FileSeries file, DateTime chartStart, DateTime chartEnd)
		{
			if (file.RowCount == 0)
			{
				Print(string.Format("  {0,-14} : NO DATA. {1}", file.Name,
					string.IsNullOrEmpty(file.LastError) ? "No rows parsed." : file.LastError));

				if (file.RowsRejected > 0)
					Print(string.Format("                   {0} rows rejected, first was: {1}",
						file.RowsRejected, file.FirstRejectExample));

				return;
			}

			Print(string.Format("  {0,-14} : {1} rows, {2:yyyy-MM-dd HH:mm} to {3:yyyy-MM-dd HH:mm}{4}",
				file.Name, file.RowCount, file.FirstTime, file.LastTime,
				file.RowsRejected > 0 ? string.Format(", {0} rejected", file.RowsRejected) : string.Empty));

			if (file.RowsRejected > 0)
				Print(string.Format("                   first rejected row: {0}", file.FirstRejectExample));

			// The two ways a file is the wrong shape for the range being tested, stated in
			// days rather than left for the reader to subtract two timestamps in their head.
			if (chartStart > DateTime.MinValue && file.FirstTime > chartStart.AddHours(12))
				Print(string.Format("                   STARTS {0:N0} days after the chart - the step is blind before then.",
					(file.FirstTime - chartStart).TotalDays));

			if (chartEnd > DateTime.MinValue && file.LastTime < chartEnd.AddHours(-12))
				Print(string.Format("                   ENDS {0:N0} days before the chart - the step is blind after then.",
					(chartEnd - file.LastTime).TotalDays));
		}

		/// <summary>
		/// Printed at the handover from historical replay to live data.
		///
		/// Enabling a strategy replays every loaded bar first and fills its trades against
		/// them. Those fills are simulated - no order was ever sent - but they are real to
		/// everything inside this instance: they count towards the day's trade cap, they arm
		/// the consecutive-loss halt, and if the replay ends holding a position, StartBehavior
		/// makes the strategy wait for that virtual position to close before it will trade
		/// live. Deactivating and reactivating therefore hands the live session a set of
		/// counters it did not earn, and the only symptom is a strategy that quietly declines
		/// to trade. This states what was inherited, so that is visible rather than mysterious.
		/// </summary>
		private void LogRealtimeHandover()
		{
			if (!EnableLogging)
				return;

			// Done before anything is reported, so the banner describes the state the live
			// session actually starts from rather than the one it is about to discard.
			string discarded = null;

			if (risk != null && !CarryReplayRiskState)
				discarded = risk.ResetDayCounters();

			Print("=== Socrates NQ - live from here ==================================");
			Print(string.Format("  Sizing          : {0} contract(s), max {1}, {2}", FixedContracts, MaxContracts, DescribeStop()));
			Print(string.Format("  Daily loss cap  : {0}", DescribeDailyCap()));
			Print(string.Format("  Confirmations   : VIX {0}, leaders {1}", VixMode, BreadthMode));

			// Repeated here rather than left in the startup banner. By the time this prints,
			// the banner has scrolled past several thousand lines of replay output, and this
			// is the last moment before real orders that anyone is going to read.
			if (MaxDailyLossDollars <= 0)
			{
				Print("  WARNING: no daily loss cap. Nothing refuses a trade for risking more than the");
				Print("           day can afford, because there is no figure for what the day can afford.");
				Print(string.Format("           A full-width stop is {0:C} on this configuration.",
					MaxStopTicks * TickValueDollars * Math.Max(1, MaxContracts)));
			}

			// Everything below came out of the replay, not out of the market.
			Print(string.Format("  Simulated first : {0} bar(s) replayed, {1} trade(s) filled against history.",
				barsProcessed, processedTradeCount));

			if (discarded != null)
			{
				Print("  Replay risk state : DISCARDED - " + discarded);
				Print("                      None of that was real, so none of it counts against today.");
				Print("                      Trading starts from a clean day. Set 'Carry replay risk state'");
				Print("                      to keep it instead.");
			}
			else if (risk != null)
			{
				Print(string.Format("  Carried into today: {0} of {1} trades used, {2} consecutive loss(es), day P/L {3:C}.",
					risk.TradesToday,
					MaxTradesPerDay > 0 ? MaxTradesPerDay.ToString() : "unlimited",
					risk.ConsecutiveLosses,
					risk.DailyRealisedPnL));

				if (risk.IsHaltedForDay)
				{
					Print("  HALTED for the day on replayed trades: " + risk.HaltReason);
					Print("           No live entry will be taken until the session rolls. Clear");
					Print("           'Carry replay risk state' if that is not what you want.");
				}
				else if (MaxTradesPerDay > 0 && risk.TradesToday >= MaxTradesPerDay)
				{
					Print("  Trade cap already reached on replayed trades - no live entry until the roll.");
				}
			}

			// A file is a snapshot. It has history, which is the whole reason for using one,
			// and it stops at whatever moment it was written - which in a backtest is
			// invisible and correct, and going live means a confirmation reading a number
			// from last Tuesday. The staleness guard catches it and stands the step aside,
			// so nothing is confirmed against a frozen value, but "step 5 quietly stopped
			// applying" is not something to discover from a run summary a week later.
			WarnOnStaleFiles();

			if (Position.MarketPosition != MarketPosition.Flat)
			{
				Print(string.Format("  WARNING: replay ended holding {0} {1} @ {2:N2}, and StartBehavior is {3}.",
					Position.Quantity, Position.MarketPosition, Position.AveragePrice, StartBehavior));
				Print("           No live order will be submitted until that simulated position closes, and");
				Print("           it closes only when its simulated stop or target is reached. If neither is");
				Print("           near, this strategy will do nothing for the rest of the day. Restart it");
				Print("           flat, or wait for the position to resolve, and check back here.");
			}

			if (setup != null && setup.State != SetupState.Idle)
				Print(string.Format("  Setup in flight : {0}{1} - carried over from the replay.",
					setup.State, setup.ActiveIsContinuation ? " (continuation)" : string.Empty));

			Print("===================================================================");
		}

		/// <summary>
		/// The stop band and the daily loss limit are two ways of saying the same thing, and
		/// they can be set to contradict each other. Worst case is a full-width stop taken
		/// MaxConsecutiveLosses times at MaxContracts; if that exceeds the daily cap, the cap
		/// is unreachable and the halt it is meant to trigger will never fire in time.
		/// </summary>
		private void LogRiskConsistency()
		{
			// The stop is structural, so its size is not known in advance. MaxStopTicks is the
			// only hard ceiling on it, which makes it the worst case for this arithmetic.
			int worstStopTicks = MaxStopTicks;
			int contracts = Math.Max(1, MaxContracts);
			double worstTrade = worstStopTicks * TickValueDollars * contracts;

			// Printed whether or not a cap is set. With no cap this is the only statement of
			// what a single trade can cost, and it should not go missing simply because
			// nothing is checking it.
			if (MaxDailyLossDollars <= 0)
			{
				Print(string.Format("  Worst trade     : {0} ticks x {1:C} x {2} contract(s) = {3:C}. No daily loss cap set.",
					worstStopTicks, TickValueDollars, contracts, worstTrade));

				// The number a funded or evaluation account is actually judged against. Two
				// halts bound the day - consecutive losses and the trade cap - and the smaller
				// of them decides how many full-width losses can land before trading stops.
				// Neither is a dollar limit, so without a cap this is the real exposure and
				// nothing in the strategy is comparing it to anything.
				int lossesBeforeHalt = int.MaxValue;

				if (MaxConsecutiveLosses > 0)
					lossesBeforeHalt = MaxConsecutiveLosses;

				if (MaxTradesPerDay > 0 && MaxTradesPerDay < lossesBeforeHalt)
					lossesBeforeHalt = MaxTradesPerDay;

				if (lossesBeforeHalt == int.MaxValue)
				{
					Print("                    Nothing halts the day. Every setup that passes is taken, and");
					Print("                    the day's loss is unbounded. Do not run this on a funded account.");
					return;
				}

				Print(string.Format("  Worst day       : {0} full-width losses before trading halts = {1:C}.",
					lossesBeforeHalt, worstTrade * lossesBeforeHalt));
				Print("                    Compare that to your account's daily loss limit and trailing");
				Print("                    drawdown before running this unattended. If it is larger than");
				Print("                    either, set 'Max daily loss ($)' or lower 'Max stop (ticks)'.");

				return;
			}

			// The check that actually bites when the contract count goes up. A trade is
			// refused before submission when its own intended risk exceeds what is left of
			// the day's budget - and intended risk scales with contracts while the cap does
			// not. Doubling size halves the stop distance the budget can afford, so the
			// surviving trades are the tightest-stopped ones, which are also the ones noise
			// takes out. Both symptoms - far fewer trades and worse realised R - come from
			// here, and nothing in the output named it.
			int affordableTicks = (int)Math.Floor(MaxDailyLossDollars / (TickValueDollars * contracts));

			if (affordableTicks < MinStopTicks)
			{
				Print(string.Format(
					"  WARNING: a fresh day's {0:C} budget affords {1} ticks at {2} contract(s), below the {3}-tick",
					MaxDailyLossDollars, affordableTicks, contracts, MinStopTicks));
				Print("           minimum stop. Every entry will be refused on the daily risk budget.");
				Print(string.Format("           Raise the cap to at least {0:C} or cut size.",
					MinStopTicks * TickValueDollars * contracts));
			}
			else if (affordableTicks < worstStopTicks)
			{
				Print(string.Format(
					"  WARNING: a fresh day's {0:C} budget affords {1} of the {2}-tick stop band at {3} contract(s).",
					MaxDailyLossDollars, affordableTicks, worstStopTicks, contracts));
				Print("           Wider setups are refused before submission, so size is silently selecting");
				Print("           for tight stops rather than trading the same book larger. Scale the cap");
				Print(string.Format("           with the contract count - {0:C} here - or the results will not compare.",
					worstStopTicks * TickValueDollars * contracts));
			}

			if (MaxConsecutiveLosses <= 0)
			{
				Print(string.Format("  Worst trade     : {0:C}, against a {1:C} daily cap with no consecutive-loss halt.",
					worstTrade, MaxDailyLossDollars));
				return;
			}

			double worstCase = worstTrade * MaxConsecutiveLosses;

			if (worstCase <= MaxDailyLossDollars)
			{
				Print(string.Format("  Worst run       : {0} ticks x {1:C} x {2} contract(s) x {3} losses = {4:C}, inside the {5:C} cap.",
					worstStopTicks, TickValueDollars, contracts, MaxConsecutiveLosses, worstCase, MaxDailyLossDollars));
				return;
			}

			Print(string.Format(
				"  WARNING: {0} ticks x {1:C} x {2} contract(s) x {3} losses = {4:C}, which overshoots the {5:C} daily cap.",
				worstStopTicks, TickValueDollars, contracts, MaxConsecutiveLosses, worstCase, MaxDailyLossDollars));
			Print(string.Format(
				"           Raise the cap to {0:C}, switch to MNQ (Tick value 0.50), or lower the stop to {1} ticks.",
				worstCase,
				(int)Math.Floor(MaxDailyLossDollars / (TickValueDollars * contracts * MaxConsecutiveLosses))));
		}

		/// <summary>
		/// Turns the trading-hours preset into the three times the risk manager works with.
		/// Custom passes the entered values straight through.
		/// </summary>
		private void ResolveTradingHours()
		{
			switch (TradingHours)
			{
				case TradingHoursMode.RegularHours:
					effectiveSessionStart = 94500;
					effectiveSessionEnd = 154500;
					effectiveFlatten = 155500;
					break;

				case TradingHoursMode.ExtendedHours:
					// The Globex day opens at 18:00 ET and runs to the 17:00 halt. Entries
					// stop at 16:45 and the position is closed by 16:55, ahead of both the
					// halt and NinjaTrader's own exit-on-session-close.
					effectiveSessionStart = 180000;
					effectiveSessionEnd = 164500;
					effectiveFlatten = 165500;
					break;

				default:
					effectiveSessionStart = SessionStartTime;
					effectiveSessionEnd = SessionEndTime;
					effectiveFlatten = FlattenTime;
					break;
			}
		}

		private string DescribeSession()
		{
			return string.Format("{0}, entries {1:000000}-{2:000000}, flat by {3:000000}",
				TradingHours, effectiveSessionStart, effectiveSessionEnd, effectiveFlatten);
		}

		private string DescribeDailyCap()
		{
			return MaxDailyLossDollars > 0 ? string.Format("{0:C}", MaxDailyLossDollars) : "none";
		}

		private string DescribeStop()
		{
			return string.Format("below the previous low +/- {0:N2} ATR, band {1}-{2} ticks (max {3:C} a contract)",
				StopBufferAtr, MinStopTicks, MaxStopTicks, MaxStopTicks * TickValueDollars);
		}

		private string DescribeSeries(int index)
		{
			if (index == 0)
				return "NQ primary";

			if (index == idxFill)
				return "NQ fill resolution";

			if (index == idxDaily)
				return "NQ daily";

			if (index == idxWeekly)
				return "NQ weekly";

			if (index == idxFourHour)
				return "NQ 4-hour";

			if (index == idxVix)
				return "VIX " + VixSymbol;

			if (index == idxRelStrength)
				return "comparison " + RelativeStrengthSymbol;

			if (idxBreadthStart >= 0 && index >= idxBreadthStart && index < idxBreadthStart + breadthSymbols.Length)
				return "leader " + breadthSymbols[index - idxBreadthStart];

			return "unknown";
		}

		private void LogDaySummary(DateTime day, double realisedPnL, string haltReason)
		{
			Log(string.Format(
				"Day {0:yyyy-MM-dd} funnel: {1} sweeps -> {2} structure shifts -> {3} retests -> {4} entries. "
				+ "Rejected: risk {5}, VIX {6}, leaders {7}, stop band {8}, sizing {9}. Realised {10:C}{11}.",
				day, daySweeps, dayShifts, dayZoneTouches, dayEntries,
				dayBlockedByRisk, dayRejectedVix, dayRejectedBreadth, dayRejectedStop, dayRejectedSizing,
				realisedPnL,
				string.IsNullOrEmpty(haltReason) ? string.Empty : ", halted: " + haltReason));
		}

		private void RecordTradeResult(double profitDollars)
		{
			runningEquity += profitDollars;

			if (runningEquity > equityPeak)
				equityPeak = runningEquity;

			double drawdown = equityPeak - runningEquity;

			if (drawdown > maxDrawdown)
				maxDrawdown = drawdown;

			if (profitDollars > 0)
			{
				tradesWon++;
				grossProfit += profitDollars;
			}
			else if (profitDollars < 0)
			{
				tradesLost++;
				grossLoss += -profitDollars;

				if (-profitDollars > largestLoss)
					largestLoss = -profitDollars;
			}
			else
			{
				tradesScratch++;
			}

			if (pendingRiskDollars.Count == 0)
				return;

			double riskDollars = pendingRiskDollars[0];
			pendingRiskDollars.RemoveAt(0);

			if (pendingWasCash.Count > 0)
			{
				bool wasCash = pendingWasCash[0];
				pendingWasCash.RemoveAt(0);

				if (wasCash)
				{
					cashTrades++;

					if (profitDollars > 0)
					{
						cashWon++;
						cashGrossProfit += profitDollars;
					}
					else
					{
						cashGrossLoss += -profitDollars;
					}
				}
				else
				{
					nightTrades++;

					if (profitDollars > 0)
					{
						nightWon++;
						nightGrossProfit += profitDollars;
					}
					else
					{
						nightGrossLoss += -profitDollars;
					}
				}
			}

			if (pendingIsContinuation.Count > 0)
			{
				bool wasContinuation = pendingIsContinuation[0];
				pendingIsContinuation.RemoveAt(0);

				if (wasContinuation)
				{
					contTrades++;

					if (profitDollars > 0)
					{
						contWon++;
						contGrossProfit += profitDollars;
					}
					else
					{
						contGrossLoss += -profitDollars;
					}
				}
				else
				{
					revTrades++;

					if (profitDollars > 0)
					{
						revWon++;
						revGrossProfit += profitDollars;
					}
					else
					{
						revGrossLoss += -profitDollars;
					}
				}
			}

			// Popped in step with the risk, so the lists cannot drift apart.
			bool vixVetoed = false;
			bool breadthVetoed = false;

			if (pendingVixVeto.Count > 0)
			{
				vixVetoed = pendingVixVeto[0];
				pendingVixVeto.RemoveAt(0);
			}

			if (pendingBreadthVeto.Count > 0)
			{
				breadthVetoed = pendingBreadthVeto[0];
				pendingBreadthVeto.RemoveAt(0);
			}

			// A trade can be vetoed by both steps, so it lands in both buckets. The clean
			// bucket is the counterfactual: what the run would have been with the filters on.
			// The totals are kept separately because summing the two veto buckets counts
			// every doubly-vetoed trade twice, which overstates what the steps refuse.
			shadowTotalTrades++;
			shadowTotalPnL += profitDollars;

			if (vixVetoed)
			{
				shadowVixVetoTrades++;
				shadowVixVetoPnL += profitDollars;

				if (profitDollars > 0)
					shadowVixVetoWon++;
			}

			if (breadthVetoed)
			{
				shadowBreadthVetoTrades++;
				shadowBreadthVetoPnL += profitDollars;

				if (profitDollars > 0)
					shadowBreadthVetoWon++;
			}

			if (!vixVetoed && !breadthVetoed)
			{
				shadowCleanTrades++;
				shadowCleanPnL += profitDollars;

				if (profitDollars > 0)
					shadowCleanWon++;
			}

			if (riskDollars <= 0)
				return;

			riskDollarsSum += riskDollars;

			if (riskDollars < riskDollarsMin)
				riskDollarsMin = riskDollars;

			if (riskDollars > riskDollarsMax)
				riskDollarsMax = riskDollars;

			double r = profitDollars / riskDollars;
			rSamples++;
			rSum += r;

			// What the run would have returned had every stop held at exactly 1R. The gap
			// between this and the actual mean is the price of slippage through stops, which
			// is otherwise invisible - it hides inside a worsened average.
			rSumCappedAtStop += Math.Max(r, -1.0);

			if (r < -1.05)
				stopOverruns++;

			rBuckets[RBucketFor(r)]++;

			if (r < rMin)
				rMin = r;

			if (r > rMax)
				rMax = r;
		}

		private static int RBucketFor(double r)
		{
			if (r < -2) return 0;
			if (r < -1.05) return 1;
			if (r <= -0.95) return 2;
			if (r < 0) return 3;
			if (r < 1) return 4;
			if (r < 2) return 5;
			if (r < 3) return 6;
			if (r < 5) return 7;
			return 8;
		}

		/// <summary>
		/// What the trades actually did. Kept alongside NinjaTrader's own performance report
		/// because the R multiple is the number this strategy is steered by - both exits come
		/// from structure, so the ratio each trade offered is the thing being tested.
		/// </summary>
		private void LogPerformance()
		{
			int closed = tradesWon + tradesLost + tradesScratch;

			if (closed == 0)
			{
				if (totalEntries > 0)
					Print(string.Format("  {0} entries submitted, none closed within the run.", totalEntries));

				return;
			}

			Print("  --- results ---");
			Print(string.Format("  Trades closed        : {0}  ({1} won, {2} lost{3})", closed, tradesWon, tradesLost,
				tradesScratch > 0 ? string.Format(", {0} scratch", tradesScratch) : string.Empty));
			Print(string.Format("  Win rate             : {0:N0}%", (tradesWon * 100.0) / closed));
			Print(string.Format("  Net                  : {0:C}   (gross +{1:C} / -{2:C})",
				grossProfit - grossLoss, grossProfit, grossLoss));
			Print(string.Format("  Profit factor        : {0}", grossLoss > 0 ? string.Format("{0:N2}", grossProfit / grossLoss) : "n/a, no losses"));
			Print(string.Format("  Per trade            : {0:C}", (grossProfit - grossLoss) / closed));
			Print(string.Format("  Largest drawdown     : {0:C}  (cumulative across the run, not one day)", maxDrawdown));

			if (tradesLost > 0)
			{
				double averageLoss = grossLoss / tradesLost;

				Print(string.Format("  Loss per trade       : average {0:C}, largest {1:C}", averageLoss, largestLoss));

				// The comparison that matters for the daily limit. A cap smaller than a
				// routine stop-out cannot be honoured by halting after the fact, which is
				// why the limit is now also checked before each order goes out.
				if (MaxDailyLossDollars > 0 && averageLoss > MaxDailyLossDollars)
				{
					Print(string.Format("  NOTE: an average losing trade costs more than the whole {0:C} daily limit.", MaxDailyLossDollars));
					Print("        Entries are now refused when the trade's risk exceeds what is left of the day's");
					Print("        budget, so raise the cap or switch to MNQ, or most setups will be turned away.");
				}
			}

			if (rSamples > 0)
			{
				Print(string.Format("  R multiple           : mean {0:+0.00;-0.00}, best {1:+0.00;-0.00}, worst {2:+0.00;-0.00}, over {3} trades",
					rSum / rSamples, rMax, rMin, rSamples));

				for (int i = 0; i < rBuckets.Length; i++)
				{
					if (rBuckets[i] > 0)
						Print(string.Format("      {0,-16} {1,4}", RBucketLabels[i], rBuckets[i]));
				}

				// Expectancy is the only figure here that survives a change of position size,
				// so it is the one to judge the geometry by.
				if (rSum / rSamples <= 0)
					Print("  NOTE: negative expectancy. The sequence is finding setups; they are not paying.");

				// R only compares across trades that risked similar amounts. With a structural
				// stop the risk varies by whatever the chart offered, and a fat R on a tight
				// stop earns a fraction of what a 1R loss on a wide one costs - so a healthy
				// mean R can sit on top of a mediocre profit factor and mean very little.
				if (rSamples > 0 && riskDollarsMin > 0)
				{
					Print(string.Format("      risk per trade: {0:C} to {1:C}, mean {2:C}",
						riskDollarsMin, riskDollarsMax, riskDollarsSum / rSamples));

					if (riskDollarsMax > riskDollarsMin * 4)
					{
						Print(string.Format("      NOTE: risk varies {0:N0}x between trades, so R is not comparable across them.",
							riskDollarsMax / riskDollarsMin));
						Print("            Judge this run on profit factor and per-trade dollars, not on mean R.");
					}
				}

				if (stopOverruns > 0)
				{
					Print(string.Format("  Stops that did not hold: {0} of {1} trades lost more than 1R, worst {2:+0.00;-0.00}R.",
						stopOverruns, rSamples, rMin));
					Print(string.Format("      Had every stop held at exactly 1R, mean would be {0:+0.00;-0.00}R instead of {1:+0.00;-0.00}R.",
						rSumCappedAtStop / rSamples, rSum / rSamples));
					Print("      That gap is price gapping or running through the stop, not a coding fault -");
					Print("      a stop is an order, not a guarantee. It is the cost of holding through thin hours.");
				}
			}

			LogKindVerdict();
			LogSessionVerdict();
			LogFileVerdict();
			LogShadowVerdict();

			Print("  These are backtest fills. Model commission and slippage before believing any of it.");
		}

		/// <summary>
		/// Reversals and continuations, priced separately.
		///
		/// These are two different trades wearing one set of results. A continuation runs
		/// with a break that held; a reversal trades against a raid that failed. Nothing
		/// says they should earn at the same rate, and when one setup type supplies most of
		/// the entries the blended profit factor is mostly describing that one - so turning
		/// continuations on and off by watching the total has been answering a different
		/// question each time, depending on the mix.
		/// </summary>
		private void LogKindVerdict()
		{
			if (revTrades + contTrades == 0)
				return;

			Print("  --- by setup kind ---");
			PrintKind("Reversals", revTrades, revWon, revGrossProfit, revGrossLoss);
			PrintKind("Continuations", contTrades, contWon, contGrossProfit, contGrossLoss);

			// The comparison the totals cannot make. Stated in dollars per trade because
			// risk varies many-fold here and R does not survive that.
			if (revTrades > 0 && contTrades > 0)
			{
				double revPer = (revGrossProfit - revGrossLoss) / revTrades;
				double contPer = (contGrossProfit - contGrossLoss) / contTrades;

				Print(string.Format("  Per trade       : reversals {0:C0}, continuations {1:C0}.", revPer, contPer));

				if (revPer > 0 && contPer <= 0)
					Print("  The continuations are losing money the reversals are making. Turn them off.");
				else if (contPer > 0 && revPer <= 0)
					Print("  The continuations are carrying this. The reversals are the ones to question.");
				else if (revPer > 0 && contPer > 0)
					Print("  Both kinds are paying. The mix is a preference, not a correction.");
				else
					Print("  Neither kind is paying. The problem is upstream of the mix.");
			}
		}

		/// <summary>
		/// Cash session against overnight, priced separately.
		///
		/// Trading hours is not a parameter to sweep - it is a question about whether the
		/// overnight half of the Globex session is worth holding through, and running the
		/// strategy twice to find out changes the sample as well as the setting. This
		/// splits one run, so the two halves are measured on the same trades that actually
		/// happened.
		/// </summary>
		private void LogSessionVerdict()
		{
			if (cashTrades + nightTrades == 0)
				return;

			Print("  --- by session ---");
			PrintKind("Cash 09:30-16:00", cashTrades, cashWon, cashGrossProfit, cashGrossLoss);
			PrintKind("Overnight", nightTrades, nightWon, nightGrossProfit, nightGrossLoss);

			// The funnel beside the results, because a session with no trades has two very
			// different explanations and the results table cannot tell them apart.
			Print("                    sweeps  shifts  retests  setups");
			Print(string.Format("  Cash            : {0,6}  {1,6}  {2,7}  {3,6}",
				cashSweeps, cashShifts, cashRetests, cashSetups));
			Print(string.Format("  Overnight       : {0,6}  {1,6}  {2,7}  {3,6}",
				nightSweeps, nightShifts, nightRetests, nightSetups));

			// Cash is about 6.5 of the ~23 traded hours, so a little under 30% of the bars.
			// A stage falling well below its share is the stage that does not survive fast
			// conditions, and that is the one to work on.
			int sweepTotal = cashSweeps + nightSweeps;
			int setupTotal = cashSetups + nightSetups;

			if (sweepTotal > 0 && setupTotal > 0)
			{
				Print(string.Format("  Cash share      : {0:N0}% of sweeps, {1:N0}% of shifts, {2:N0}% of retests, {3:N0}% of setups (bars are ~29%)",
					(cashSweeps * 100.0) / sweepTotal,
					cashShifts + nightShifts > 0 ? (cashShifts * 100.0) / (cashShifts + nightShifts) : 0,
					cashRetests + nightRetests > 0 ? (cashRetests * 100.0) / (cashRetests + nightRetests) : 0,
					(cashSetups * 100.0) / setupTotal));
			}

			if (cashTrades > 0 && nightTrades > 0)
			{
				double cashPer = (cashGrossProfit - cashGrossLoss) / cashTrades;
				double nightPer = (nightGrossProfit - nightGrossLoss) / nightTrades;

				Print(string.Format("  Per trade       : cash {0:C0}, overnight {1:C0}.", cashPer, nightPer));

				if (nightPer <= 0 && cashPer > 0)
					Print("  The overnight half is not paying. Try Trading hours on Regular.");
				else if (cashPer <= 0 && nightPer > 0)
					Print("  The cash session is not paying; the overnight half is carrying this.");
			}
		}

		private void PrintKind(string label, int trades, int won, double grossProfit, double grossLoss)
		{
			if (trades == 0)
			{
				Print(string.Format("  {0,-15} : none", label));
				return;
			}

			double factor = grossLoss > 0 ? grossProfit / grossLoss : 0;

			Print(string.Format("  {0,-15} : {1} trades, {2} won ({3:N0}%), net {4:C0}, factor {5}, {6:C0} each",
				label, trades, won, (won * 100.0) / trades, grossProfit - grossLoss,
				factor > 0 ? string.Format("{0:N2}", factor) : "-",
				(grossProfit - grossLoss) / trades));
		}

		/// <summary>
		/// Whether the files were actually answering questions.
		///
		/// Loading twelve thousand rows proves the file parsed. It does not prove a single
		/// one was ever read, and a file whose timestamps are a day or a time zone away
		/// from the chart's loads perfectly and is never hit. Hit rate is the only number
		/// that distinguishes "the data is there" from "the data is being used", and the
		/// two look identical in every other line of output.
		/// </summary>
		private void LogFileVerdict()
		{
			if (!filesActive)
				return;

			Print("  --- file-backed sources: were they read? ---");

			if (vixFile != null)
				PrintFileUsage(vixFile);

			for (int i = 0; i < leaderFiles.Length; i++)
			{
				if (leaderFiles[i] != null)
					PrintFileUsage(leaderFiles[i]);
			}
		}

		private void PrintFileUsage(FileSeries file)
		{
			if (file.Lookups == 0)
			{
				Print(string.Format("  {0,-14} : never queried - the step it feeds did not run.", file.Name));
				return;
			}

			double hitRate = (file.Hits * 100.0) / file.Lookups;

			Print(string.Format("  {0,-14} : {1} of {2} lookups answered ({3:N1}%){4}",
				file.Name, file.Hits, file.Lookups, hitRate,
				file.Loads > 1 ? string.Format(", re-read {0} times", file.Loads - 1) : string.Empty));

			// Split, because the two halves answer different questions and only the first
			// one is about the file being wired up correctly. A source that is shut for
			// part of the session answers every lookup and is current for none of them.
			if (file.Hits > 0)
			{
				Print(string.Format("                   of those, {0} current and {1} from an older row{2}",
					file.HitsCurrent, file.HitsStale,
					file.HitsStale > 0
						? string.Format(" (mean {0:N0} min behind, worst {1:N0})", file.StaleMinutesMean, file.StaleMinutesMax)
						: string.Empty));

				if (file.HitsCurrent == 0)
					Print("                   NOTE: not one reading was current. The file's hours do not");
				else if (file.HitsStale > file.HitsCurrent)
					Print("                   NOTE: mostly answered from stale rows - the source covers a");
			}

			// A miss before the file's first row is a history problem; a miss after its
			// last is a file that stopped. Different fixes, so they are counted apart.
			if (file.MissesBefore > 0)
				Print(string.Format("                   {0} lookups fell before the first row - not enough history.",
					file.MissesBefore));

			if (file.Hits > 0 && (file.HitsCurrent == 0 || file.HitsStale > file.HitsCurrent))
				Print("                   smaller part of the session than the chart does.");

			if (hitRate < 50.0)
			{
				Print("                   FEWER THAN HALF ANSWERED. If the row count above looked");
				Print("                   healthy, the timestamps do not line up with the chart - check");
				Print("                   the time zone before reading anything into this step's results.");
			}

			if (file.LoadFailures > 0)
				Print(string.Format("                   {0} load failure(s), last: {1}", file.LoadFailures, file.LastError));
		}

		/// <summary>
		/// What steps 5 and 6 would have cost or saved, measured on trades that actually
		/// closed rather than on a backtest that cannot reach far enough back to hold one.
		///
		/// The question a filter has to answer is not "how many trades did it refuse" - that
		/// is just a count, and a filter that refuses everything scores best on it. It is
		/// whether the refused trades were worse than the ones let through. That needs their
		/// results, which means taking them, which is what shadow mode is for.
		/// </summary>
		private void LogShadowVerdict()
		{
			if (!ShadowConfirmations)
				return;

			int total = shadowCleanTrades + shadowVixVetoTrades + shadowBreadthVetoTrades;

			if (total == 0)
			{
				Print("  --- shadow confirmations ---");
				Print("  On, but no trade closed with a verdict recorded. Both steps are Off, or nothing traded.");
				return;
			}

			Print("  --- shadow confirmations: what steps 5 and 6 would have done ---");
			Print("  Every trade below was taken. The steps recorded a verdict and blocked nothing.");

			Print(string.Format("  Neither step objected : {0} trades, {1} won, {2:C}{3}",
				shadowCleanTrades, shadowCleanWon, shadowCleanPnL,
				shadowCleanTrades > 0 ? string.Format(", {0:C} each", shadowCleanPnL / shadowCleanTrades) : string.Empty));

			if (vix != null)
			{
				Print(string.Format("  Step 5 would refuse   : {0} trades, {1} won, {2:C}{3}",
					shadowVixVetoTrades, shadowVixVetoWon, shadowVixVetoPnL,
					shadowVixVetoTrades > 0 ? string.Format(", {0:C} each", shadowVixVetoPnL / shadowVixVetoTrades) : string.Empty));
			}

			if (breadth != null || relStrength != null)
			{
				Print(string.Format("  Step 6 would refuse   : {0} trades, {1} won, {2:C}{3}",
					shadowBreadthVetoTrades, shadowBreadthVetoWon, shadowBreadthVetoPnL,
					shadowBreadthVetoTrades > 0 ? string.Format(", {0:C} each", shadowBreadthVetoPnL / shadowBreadthVetoTrades) : string.Empty));
			}

			// The verdict, stated so it cannot be read the flattering way by accident. A
			// filter earns its place by refusing trades that lost money; refusing trades that
			// made money is a cost, however sound the reasoning behind it sounds.
			// Distinct, by subtraction. A trade both steps objected to is one refusal, not
			// two, and adding the buckets reported it as two - along with its profit twice.
			double refusedPnL = shadowTotalPnL - shadowCleanPnL;
			int refusedTrades = shadowTotalTrades - shadowCleanTrades;

			if (shadowVixVetoTrades > 0 && shadowBreadthVetoTrades > 0)
			{
				Print(string.Format("  Both steps objected   : {0} trades (counted once below, twice above)",
					shadowVixVetoTrades + shadowBreadthVetoTrades - refusedTrades));
			}

			if (refusedTrades == 0)
			{
				Print("  Neither step objected to anything that traded. No evidence either way yet.");
				return;
			}

			// Judged per trade, not on the sign of the refused total. When everything loses,
			// refusing anything "avoids a loss" and a filter that kept the very worst trades
			// would be congratulated for it - which is exactly what happened on an overnight
			// run where the refused set averaged -$30 and the set kept averaged -$253.
			double refusedEach = refusedPnL / refusedTrades;
			double cleanEach = shadowCleanTrades > 0 ? shadowCleanPnL / shadowCleanTrades : 0.0;

			Print(string.Format("  Kept {0:C} each against {1:C} each refused.", cleanEach, refusedEach));

			if (shadowCleanTrades == 0)
			{
				Print("  VERDICT: the steps would have refused every trade. Nothing is left to judge.");
			}
			else if (cleanEach > refusedEach)
			{
				Print(string.Format("  VERDICT: the steps keep the better trades, by {0:C} each. On this sample they",
					cleanEach - refusedEach));
				Print(string.Format("           are selecting. Running them live gives {0} trades worth {1:C} in place",
					shadowCleanTrades, shadowCleanPnL));
				Print(string.Format("           of {0} worth {1:C}.", shadowTotalTrades, shadowTotalPnL));
			}
			else
			{
				Print(string.Format("  VERDICT: the steps keep the WORSE trades, by {0:C} each. Running them live",
					refusedEach - cleanEach));
				Print(string.Format("           gives {0} trades worth {1:C} in place of {2} worth {3:C} - the filters",
					shadowCleanTrades, shadowCleanPnL, shadowTotalTrades, shadowTotalPnL));
				Print("           are selecting against this strategy, not for it.");
			}

			Print(string.Format("           {0} trades of evidence. Keep accumulating before acting on it.", refusedTrades));
		}

		private void RecordStopDistance(double stopTicks)
		{
			if (stopTicks <= 0 || double.IsNaN(stopTicks) || double.IsInfinity(stopTicks))
				return;

			if (stopSamples == 0 || stopTicks < stopTicksMin)
				stopTicksMin = stopTicks;

			if (stopTicks > stopTicksMax)
				stopTicksMax = stopTicks;

			stopTicksSum += stopTicks;
			stopSamples++;

			int bucket = (int)(stopTicks / StopBucketTicks);
			if (bucket >= StopBucketCount)
				bucket = StopBucketCount - 1;

			stopTickBuckets[bucket]++;
		}

		private void LogRunSummary()
		{
			if (!EnableLogging)
				return;

			// Counted directly rather than summed from the rejection buckets. Those only
			// partition the setups when a rejection actually stops one: under shadow
			// confirmations the steps record a verdict and the setup proceeds anyway, so
			// the sum counted every vetoed setup twice and every doubly-vetoed one three
			// times - 599 against a true 244 on the run that exposed this.
			int completedSetups = longSetups + shortSetups;

			Print("=== Socrates NQ - run summary =====================================");

			// Printed because these summaries get compared against each other, and a run on a
			// different bar size or a shorter range looks identical in every other respect.
			if (firstBarTime > DateTime.MinValue)
			{
				Print(string.Format("  Range                : {0:yyyy-MM-dd HH:mm} to {1:yyyy-MM-dd HH:mm}  ({2})",
					firstBarTime, lastBarTime,
					BarsArray != null && BarsArray[0] != null ? BarsArray[0].BarsPeriod.ToString() : "unknown period"));
			}

			Print(string.Format("  Bars evaluated       : {0}", barsProcessed));
			Print(string.Format("  Sweeps (step 2)      : {0}{1}", totalSweeps,
				barsProcessed > 0 ? string.Format("  (one per {0:N1} bars)", barsProcessed / (double)Math.Max(1, totalSweeps)) : string.Empty));

			if (setup != null)
			{
				Print(string.Format("      adopted / ignored  : {0} / {1}", setup.SweepsAdopted, setup.SweepsIgnored));

				// A sweep every few bars is not a market taking liquidity that often, it is
				// the level book firing on noise. Nothing downstream can develop through it.
				// Only worth flagging when the funnel downstream is actually starved. A high
				// sweep rate on its own turned out to be harmless: raising penetration from
				// ~4 points to ~10 left the rate identical, because with a level every few
				// points there is always something nearby to poke past - depth is not what
				// drives the count. The sequencing filters it out regardless, so this is now
				// a note about a starved funnel, not about the number itself.
				if (totalSweeps > 0 && barsProcessed / (double)totalSweeps < 8.0 && totalEntries * 200 < barsProcessed)
				{
					Print(string.Format("      NOTE: sweeps are frequent (penetration max({0:N2} ATR, {1:N2} pts)) and few reach an entry.",
						MinPenetrationAtr, MinPenetrationPoints));
					Print("            Level count, not penetration depth, is what drives the rate.");
				}
			}

			Print(string.Format("  Structure shifts (3) : {0}", totalShifts));

			if (totalBreaks > 0)
				Print(string.Format("  Breaks held (cont.)  : {0}", totalBreaks));
			Print(string.Format("      discarded, too wide: {0}", setup != null ? setup.DiscardedTooWide : 0));
			Print(string.Format("  Retests reached (4)  : {0}", totalZoneTouches));

			if (setup != null && setup.DiscardedPoorReward > 0)
				Print(string.Format("      discarded, reward below {0:N2}R: {1}", MinRewardRisk, setup.DiscardedPoorReward));

			Print(string.Format("  Setups completed     : {0}  ({1} long / {2} short)", completedSetups, longSetups, shortSetups));
			Print(string.Format("  Entries submitted    : {0}  ({1} long / {2} short)", totalEntries, longEntries, shortEntries));

			if (continuationEntries > 0 || reversalEntries > 0)
			{
				Print(string.Format("      by kind            : {0} reversal / {1} continuation",
					reversalEntries, continuationEntries));
			}

			if (nq != null && nq.Sweeps.ContinuationsFound > 0)
				Print(string.Format("      breaks reported as continuations: {0}", nq.Sweeps.ContinuationsFound));

			if (nq != null && nq.Sweeps.AmbiguousBars > 0)
				Print(string.Format("  Bars sweeping both sides at once: {0} (resolved to the deeper raid)", nq.Sweeps.AmbiguousBars));

			if (setup != null && setup.TargetsFromSwing + setup.TargetsFromRMultiple + setup.TargetsFromLiquidity > 0)
			{
				Print(string.Format("  Targets: {0} from a previous swing, {1} from the R fallback, {2} from liquidity",
					setup.TargetsFromSwing, setup.TargetsFromRMultiple, setup.TargetsFromLiquidity));

				// The point of the change was structural targets. If the fallback dominates,
				// the previous highs are not far enough away to pay for the stops.
				if (setup.TargetsFromSwing < setup.TargetsFromRMultiple + setup.TargetsFromLiquidity)
					Print("  NOTE: most targets came from the fallback, not a previous swing. Lower 'Min reward:risk' or check stop sizes.");
			}
			Print("  --- of the completed setups, rejected by ---");
			Print(string.Format("  Risk gate            : {0}", totalBlockedByRisk));

			for (int i = 0; i < blockReasonCounts.Length; i++)
			{
				if (blockReasonCounts[i] > 0)
					Print(string.Format("      {0,-18} {1}", (EntryBlockReason)i, blockReasonCounts[i]));
			}

			Print(string.Format("  Step 5 (VIX)         : {0}{1}", totalRejectedVix,
				vix != null && vix.SkippedQuiet > 0
					? string.Format("   ({0} skipped, source quiet)", vix.SkippedQuiet)
					: string.Empty));

			if (vix != null && totalRejectedVix + vix.Confirmed > 0)
			{
				Print(string.Format("      no data {0}, stale {1}, quiet-skipped {2}, direction {3}, not at a level {4}, confirmed {5}",
					vix.RejectedNoData, vix.RejectedStale, vix.SkippedQuiet, vix.RejectedDirection, vix.RejectedNotAtLevel, vix.Confirmed));

				if (vix.MoveSamples > 0)
				{
					Print(string.Format("      |move| over {0} bars: min {1:N2}, mean {2:N2}, max {3:N2}",
						VixLookbackBars, vix.MoveAbsMin, vix.MoveAbsMean, vix.MoveAbsMax));
					Print(string.Format("      threshold in force : min {0:N2}, mean {1:N2}, max {2:N2}",
						vix.ThresholdMin, vix.ThresholdMean, vix.ThresholdMax));

					// A threshold above everything the source ever did is not a filter, it is
					// an off switch, and it should not take a backtest to notice.
					if (vix.MoveAbsMax < vix.ThresholdMin)
						Print("      NOTE: the threshold is above every move measured. Lower 'VIX min move (ATR)'.");

					// The reverse failure, and the easier one to miss: a gate that lets
					// everything through still looks like a working confirmation.
					if (vix.ThresholdMax <= VixMinDirectionalMove)
						Print(string.Format("      NOTE: the ATR term never bound - the {0:N2} floor was the whole test. The VIX's own ATR is smaller than expected.",
							VixMinDirectionalMove));
				}
				else if (vix.RejectedNoData > 0)
				{
					int loaded = idxVix >= 0 && BarsArray != null && idxVix < BarsArray.Length && BarsArray[idxVix] != null
						? BarsArray[idxVix].Count
						: 0;

					int warmup = Math.Max(VixLookbackBars, AtrPeriod) + 1;

					Print(string.Format("      Series '{0}' at {1}-minute: {2} bars loaded, {3} reached OnBarUpdate, {4} fed the step (warm-up {5}).",
						VixSymbol, VixBarMinutes, loaded, vixBarsSeen, vixUpdatesApplied, warmup));

					if (loaded == 0)
						Print("      NOTE: the series is empty. The strategy requests its own bar period and date range,");
					else if (vixBarsSeen == 0)
						Print("      NOTE: bars loaded but none reached the strategy. Session template mismatch;");
					else
						Print(string.Format("      NOTE: only {0} bars, short of the {1} needed to warm up;", vixBarsSeen, warmup));

					Print("            so a symbol that charts fine can still arrive empty here. Open a chart at the");
					Print(string.Format("            same period ({0}-minute) and date range to confirm the data exists.", VixBarMinutes));
				}
				else if (vix.RejectedStale > 0)
				{
					Print("      NOTE: the VIX series was always stale and skipping is off, so step 5 refused");
					Print("            everything. Turn on 'VIX skip when quiet', or raise 'VIX max data age'.");
				}

				// The step standing aside more often than it speaks is worth saying out loud.
				// It is still doing its job on the bars it can reach, but it is not the filter
				// the parameter panel implies it is.
				if (vix.SkippedQuiet > totalRejectedVix + vix.Confirmed)
				{
					Print(string.Format("      NOTE: step 5 stood aside on {0} setups and judged {1}. VX prints rarely",
						vix.SkippedQuiet, totalRejectedVix + vix.Confirmed));
					Print("            outside the cash session, so overnight it mostly has nothing to say.");
				}
			}
			// Relative strength reports on its own terms. Reusing the leader wording - how
			// many of seven agreed - would describe a test that is not running.
			if (relStrength != null)
			{
				Print(string.Format("  Step 6 (rel strength): {0}{1}", totalRejectedBreadth,
					relStrength.SkippedQuiet > 0
						? string.Format("   ({0} skipped, comparison quiet)", relStrength.SkippedQuiet)
						: string.Empty));

				Print(string.Format("      {0} vs {1} over {2} bars: no data {3}, direction {4}, confirmed {5}",
					Instrument != null ? Instrument.MasterInstrument.Name : "primary",
					RelativeStrengthSymbol, RelativeStrengthLookback,
					relStrength.RejectedNoData, relStrength.RejectedDirection, relStrength.Confirmed));

				if (relStrength.Samples > 0)
				{
					Print(string.Format("      |spread| : min {0:N3}%, mean {1:N3}%, max {2:N3}%",
						relStrength.SpreadAbsMin, relStrength.SpreadAbsMean, relStrength.SpreadAbsMax));
					Print(string.Format("      threshold: min {0:N3}%, mean {1:N3}%, max {2:N3}%",
						relStrength.ThresholdMin, relStrength.ThresholdMean, relStrength.ThresholdMax));

					// The two ways a self-scaling threshold goes wrong, and neither is
					// visible from a rejection count alone.
					if (relStrength.SpreadAbsMax < relStrength.ThresholdMin)
						Print("      NOTE: the threshold is above every spread measured. Lower 'Min spread (multiple)'.");
					else if (relStrength.ThresholdMax <= RelativeStrengthMinSpread)
						Print(string.Format("      NOTE: the scaling term never bound - the {0:N3}% floor was the whole test.",
							RelativeStrengthMinSpread));
				}
				else if (relStrength.RejectedNoData > 0)
				{
					Print(string.Format("      NOTE: '{0}' produced no usable bars. Open it on a chart at this period.",
						RelativeStrengthSymbol));
				}
			}
			else
			{
				Print(string.Format("  Step 6 (leaders)     : {0}{1}", totalRejectedBreadth,
					breadthSkippedClosed > 0 ? string.Format("   ({0} skipped, leaders closed)", breadthSkippedClosed) : string.Empty));

				// Deliberately not gated on Evaluations: a step that never got as far as counting
				// leaders reports zero evaluations, which is exactly the case worth printing.
				// Gating on it hid the no-data run behind silence for two rounds.
				if (breadth != null && (breadth.Evaluations > 0 || breadth.RejectedNoData > 0 || breadthSkippedClosed > 0))
				{
					Print(string.Format("      evaluated {0}, confirmed {1}, not aligned {2}, no data {3}",
						breadth.Evaluations, breadth.Confirmed, breadth.RejectedNotAligned, breadth.RejectedNoData));

					if (breadth.Evaluations > 0)
					{
						Print(string.Format("      leaders agreeing: mean {0:N1} of {1:N1} available, needed {2:N1}",
							breadth.MeanAligned, breadth.MeanAvailable, breadth.MeanRequired));

						// A gate that never passes anything it looks at is set beyond what the
						// data does, not a selective one.
						if (breadth.Confirmed == 0)
							Print("      NOTE: nothing it evaluated ever passed. Lower 'Min leaders aligned' before reading anything into this.");
					}

					if (breadth.RejectedNoData > 0)
					{
						Print(string.Format("      NOTE: {0} setups were refused because not one leader had produced a bar.", breadth.RejectedNoData));
						LogBreadthSeriesCounts();
					}
				}
			}

			Print(string.Format("  Stop band            : {0}", totalRejectedStop));
			Print(string.Format("  Sizing               : {0}", totalRejectedSizing));
			Print(string.Format("  Daily risk budget    : {0}", totalRejectedRiskBudget));

			LogStopDistribution();
			LogPerformance();

			if (totalSweeps == 0)
				Print("  No sweeps at all. Loosen 'Min penetration' or check that levels are being built.");
			else if (totalShifts == 0 && !EnableReversals)
				Print("  No structure shifts, which is correct with reversals off: a continuation treats the break "
					+ "itself as the structural event and goes straight to the retest. 'Min displacement (ATR)', "
					+ "'Max bars sweep to shift' and 'Max structure distance (ATR)' are inert in this configuration.");
			else if (totalShifts == 0)
				Print("  Sweeps but no structure shifts. Lower 'Min displacement (ATR)' or raise 'Max bars sweep to shift'.");
			else if (totalZoneTouches == 0)
				Print("  Shifts but no retests. Widen 'Retest zone width (ATR)' or raise 'Max bars shift to retest'.");
			else if (totalEntries == 0)
				Print("  Retests reached but nothing entered. The counts above say which gate is doing it.");

			Print("===================================================================");
		}

		/// <summary>
		/// The stop distance every completed setup asked for, against the band that admits
		/// them. MinStopTicks and MaxStopTicks can be read straight off this.
		/// </summary>
		/// <summary>
		/// Bar count per leader. Which symbols arrived and which did not is the whole question
		/// when step 6 refuses everything for want of data, and it is not answerable from a
		/// count of refusals.
		/// </summary>
		private void LogBreadthSeriesCounts()
		{
			if (idxBreadthStart < 0 || BarsArray == null)
				return;

			for (int i = 0; i < breadthSymbols.Length; i++)
			{
				int series = idxBreadthStart + i;

				if (series >= BarsArray.Length)
					break;

				int count = BarsArray[series] != null ? BarsArray[series].Count : 0;

				Print(string.Format("            {0,-8} {1,7} bars{2}", breadthSymbols[i], count,
					count == 0 ? "   <-- nothing loaded" : string.Empty));
			}
		}

		private void LogStopDistribution()
		{
			if (stopSamples == 0)
				return;

			Print(string.Format("  --- stop distance the structure implied, {0} completed setups (ticks) ---", stopSamples));
			Print(string.Format("  Min {0:N0}, mean {1:N0}, max {2:N0}. Using {3}.",
				stopTicksMin, stopTicksSum / stopSamples, stopTicksMax, DescribeStop()));

			int running = 0;

			for (int i = 0; i < StopBucketCount; i++)
			{
				if (stopTickBuckets[i] == 0)
					continue;

				running += stopTickBuckets[i];

				string label = i == StopBucketCount - 1
					? string.Format("{0,4}+     ", i * StopBucketTicks)
					: string.Format("{0,4}-{1,-4}", i * StopBucketTicks, ((i + 1) * StopBucketTicks) - 1);

				Print(string.Format("    {0} {1,4}  ({2,3:N0}% at or below)", label, stopTickBuckets[i],
					(running * 100.0) / stopSamples));
			}

			if (setup != null && setup.StopsFromRetest + setup.StopsFromSwing + setup.StopsFromSweepExtreme > 0)
			{
				Print(string.Format("  Anchored to the retest low/high: {0}. Confirmed swing: {1}. Swept extreme: {2}.",
					setup.StopsFromRetest, setup.StopsFromSwing, setup.StopsFromSweepExtreme));
			}

			int wouldPass = 0;

			for (int i = 0; i < StopBucketCount; i++)
			{
				int bucketLow = i * StopBucketTicks;

				if (bucketLow >= MinStopTicks && bucketLow < MaxStopTicks)
					wouldPass += stopTickBuckets[i];
			}

			if (wouldPass == 0)
			{
				Print("  NOTE: the band does not overlap the distribution at all - no setup can ever pass it.");
				Print(string.Format("        Widening the band to admit these means risking {0:C} a contract at the top end.",
					stopTicksMax * TickValueDollars));
				Print("        If that is more than the trade is worth, the setups are wrong, not the band -");
				Print("        check 'Max setup risk (ATR)' and 'Use post-sweep swing'.");
			}
		}

		#endregion

		#region Properties

		// Minimum 1, not 0. At zero the sizer returns no contracts, every setup is refused as
		// a sizing failure, and the strategy looks broken rather than misconfigured.
		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "Fixed contracts", GroupName = "1. Position Sizing", Order = 0)]
		public int FixedContracts { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "Max contracts", Description = "Hard ceiling on size. The last line of defence against a sizing bug.", GroupName = "1. Position Sizing", Order = 2)]
		public int MaxContracts { get; set; }

		[NinjaScriptProperty]
		[Range(0.01, double.MaxValue)]
		[Display(Name = "Tick value ($)", Description = "NQ = 5.00, MNQ = 0.50.", GroupName = "1. Position Sizing", Order = 3)]
		public double TickValueDollars { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Scale size by confirmation strength", Description = "Reduce size when the VIX or leaders agree only weakly. Only bites when Fixed contracts is above 1.", GroupName = "1. Position Sizing", Order = 4)]
		public bool UseConfidenceSizing { get; set; }

		[NinjaScriptProperty]
		[Range(0, 60)]
		[Display(Name = "Fill resolution (minutes)", Description = "Entries are submitted against a series this fine, so their fills and their stop and target resolve on smaller bars than the chart's. NinjaTrader's own High resolution setting cannot be used here - it is single-series only. 0 disables it and accepts the primary bar size. Needs history at this period.", GroupName = "9. Diagnostics", Order = 3)]
		public int FillResolutionMinutes { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Trading hours", Description = "Regular = 09:45-15:45 ET. Extended = 18:00-16:45 ET, the full Globex session. Custom uses the three times below; they are ignored otherwise.", GroupName = "2. Risk", Order = 0)]
		public TradingHoursMode TradingHours { get; set; }

		[NinjaScriptProperty]
		[Range(0, 235959)]
		[Display(Name = "Session start (HHmmss)", Description = "Custom only. May be later than the end time, for a window that wraps midnight.", GroupName = "2. Risk", Order = 1)]
		public int SessionStartTime { get; set; }

		[NinjaScriptProperty]
		[Range(0, 235959)]
		[Display(Name = "Session end (HHmmss)", Description = "Custom only.", GroupName = "2. Risk", Order = 2)]
		public int SessionEndTime { get; set; }

		[NinjaScriptProperty]
		[Range(0, 235959)]
		[Display(Name = "Flatten time (HHmmss)", Description = "Custom only. Positions are closed from here until the next session start.", GroupName = "2. Risk", Order = 3)]
		public int FlattenTime { get; set; }

		[NinjaScriptProperty]
		[Range(0, double.MaxValue)]
		[Display(Name = "Max daily loss ($)", Description = "0 disables it. When set, it halts the day once reached and refuses any entry risking more than the day has left - so it must be larger than a single stop-out or it turns away most setups.", GroupName = "2. Risk", Order = 4)]
		public double MaxDailyLossDollars { get; set; }

		[NinjaScriptProperty]
		[Range(0, double.MaxValue)]
		[Display(Name = "Daily profit target ($)", GroupName = "2. Risk", Order = 5)]
		public double DailyProfitTargetDollars { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Stop for day on profit target", GroupName = "2. Risk", Order = 6)]
		public bool StopForDayOnProfitTarget { get; set; }

		[NinjaScriptProperty]
		[Range(0, int.MaxValue)]
		[Display(Name = "Max trades per day", Description = "0 disables.", GroupName = "2. Risk", Order = 7)]
		public int MaxTradesPerDay { get; set; }

		[NinjaScriptProperty]
		[Range(0, int.MaxValue)]
		[Display(Name = "Max consecutive losses", Description = "0 disables.", GroupName = "2. Risk", Order = 8)]
		public int MaxConsecutiveLosses { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Carry replay risk state", Description = "Off by default. Enabling a strategy replays every loaded bar and fills trades against them; those fills are simulated but still count towards the day's trade cap and still arm the consecutive-loss halt, so a strategy enabled on a morning the replay scores as two losses will refuse every real setup until the session rolls, silently. Off discards that state at the switch to live data and logs what it discarded. Turn it on only when restarting mid-session and you want the replay's approximation of trades that really happened to keep counting.", GroupName = "2. Risk", Order = 9)]
		public bool CarryReplayRiskState { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "ATR period", GroupName = "3. Step 1 - Context", Order = 0)]
		public int AtrPeriod { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "Swing strength", Description = "Bars either side needed to confirm a swing. Higher finds only major turns.", GroupName = "3. Step 1 - Context", Order = 1)]
		public int SwingStrength { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Use 4-hour pivots", GroupName = "3. Step 1 - Context", Order = 2)]
		public bool UseFourHourPivots { get; set; }

		[NinjaScriptProperty]
		[Range(1, 120)]
		[Display(Name = "Opening range (minutes)", GroupName = "3. Step 1 - Context", Order = 3)]
		public int OpeningRangeMinutes { get; set; }

		[NinjaScriptProperty]
		[Range(0.01, 5)]
		[Display(Name = "Zone half-width (ATR)", Description = "How wide a swing level's zone is, as a multiple of ATR.", GroupName = "3. Step 1 - Context", Order = 4)]
		public double ZoneHalfWidthAtr { get; set; }

		[NinjaScriptProperty]
		[Range(0, 5)]
		[Display(Name = "Level merge distance (ATR)", Description = "Session levels closer than this are one area, not several. Raise it to thin a crowded level book. 0 keeps every level.", GroupName = "3. Step 1 - Context", Order = 10)]
		public double LevelMergeAtr { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Use order blocks", Description = "Detect supply and demand as the last opposing candle before a displacement move.", GroupName = "3. Step 1 - Context", Order = 5)]
		public bool UseOrderBlocks { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Order block needs imbalance", Description = "Require a three-bar fair value gap. Off produces many more, much weaker blocks.", GroupName = "3. Step 1 - Context", Order = 6)]
		public bool OrderBlockRequireImbalance { get; set; }

		[NinjaScriptProperty]
		[Range(0.1, 10)]
		[Display(Name = "Order block displacement (ATR)", Description = "Minimum range of the candle that moves away from the block.", GroupName = "3. Step 1 - Context", Order = 7)]
		public double OrderBlockDisplacementAtr { get; set; }

		[NinjaScriptProperty]
		[Range(1, 50)]
		[Display(Name = "Order block origin lookback", Description = "How far back to search for the last opposing candle.", GroupName = "3. Step 1 - Context", Order = 8)]
		public int OrderBlockMaxLookback { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Order block zone", Description = "FullRange uses the candle's high to low. Body uses open to close - tighter entries, more misses.", GroupName = "3. Step 1 - Context", Order = 9)]
		public OrderBlockZoneMode OrderBlockZone { get; set; }

		[NinjaScriptProperty]
		[Range(0, 5)]
		[Display(Name = "Min penetration (ATR)", Description = "How far beyond a level price must trade for a sweep to count.", GroupName = "4. Step 2 - Liquidity", Order = 0)]
		public double MinPenetrationAtr { get; set; }

		[NinjaScriptProperty]
		[Range(0, 100)]
		[Display(Name = "Min penetration (points)", GroupName = "4. Step 2 - Liquidity", Order = 1)]
		public double MinPenetrationPoints { get; set; }

		[NinjaScriptProperty]
		[Range(1, 100)]
		[Display(Name = "Max bars to reclaim", Description = "Beyond this the move is a real break, not a sweep.", GroupName = "4. Step 2 - Liquidity", Order = 2)]
		public int MaxBarsToReclaim { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Trade continuations", Description = "Also trade breaks that hold: price goes through a level, does not reclaim, then retests it from the other side and carries on. Measured at -0.08R over 147 trades in its unselective form, so it ships off.", GroupName = "4. Step 2 - Liquidity", Order = 3)]
		public bool EnableContinuations { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Trade reversals", Description = "Trade the reclaim: price pushes through a level, fails, and closes back inside. This is the original setup and the overnight session's earner - $455 a trade there, against continuations losing money. In the cash session it inverts, so turn it off to test continuations alone.", GroupName = "4. Step 2 - Liquidity", Order = 2)]
		public bool EnableReversals { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Continuations on major levels only", Description = "Restrict continuations to prior day and week levels, pivots, the overnight and opening ranges - not swings or order blocks. With every level eligible, a break fired every seven bars and meant nothing.", GroupName = "4. Step 2 - Liquidity", Order = 4)]
		public bool ContinuationsOnMajorLevelsOnly { get; set; }

		[NinjaScriptProperty]
		[Range(1, 200)]
		[Display(Name = "Max bars sweep to shift", GroupName = "5. Step 3 - Structure", Order = 0)]
		public int MaxBarsSweepToShift { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Use post-sweep swing", Description = "Break the swing formed after the sweep. Off breaks the swing that preceded it instead, which means retracing the whole prior leg and a stop that spans it.", GroupName = "5. Step 3 - Structure", Order = 1)]
		public bool UsePostSweepSwing { get; set; }

		[NinjaScriptProperty]
		[Range(0, 50)]
		[Display(Name = "Max setup risk (ATR)", Description = "Ceiling on the trade's actual risk, entry to stop, checked at entry. 0 disables.", GroupName = "5. Step 3 - Structure", Order = 3)]
		public double MaxSetupRiskAtr { get; set; }

		[NinjaScriptProperty]
		[Range(0, 50)]
		[Display(Name = "Max structure distance (ATR)", Description = "How far the broken structure may sit from the swept extreme. A coherence test on the setup, not a risk test - the stop no longer comes from that extreme. Lower it to thin the funnel, raise it for more setups. 0 disables.", GroupName = "5. Step 3 - Structure", Order = 4)]
		public double MaxStructureDistanceAtr { get; set; }

		[NinjaScriptProperty]
		[Range(0, 10)]
		[Display(Name = "Min displacement (ATR)", Description = "Range of the bar that breaks structure. This is the strong-displacement test. 0 disables.", GroupName = "5. Step 3 - Structure", Order = 2)]
		public double MinDisplacementAtr { get; set; }

		[NinjaScriptProperty]
		[Range(1, 200)]
		[Display(Name = "Max bars shift to retest", GroupName = "6. Step 4 - Retest", Order = 0)]
		public int MaxBarsShiftToRetest { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Retest zone mode", GroupName = "6. Step 4 - Retest", Order = 1)]
		public RetestZoneMode ZoneMode { get; set; }

		[NinjaScriptProperty]
		[Range(0.01, 5)]
		[Display(Name = "Retest zone width (ATR)", GroupName = "6. Step 4 - Retest", Order = 2)]
		public double RetestZoneAtr { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Require confirmation close", Description = "Wait for a bar to close in the trade's direction inside the zone.", GroupName = "6. Step 4 - Retest", Order = 3)]
		public bool RequireConfirmationClose { get; set; }

		[NinjaScriptProperty]
		[Range(0, 5)]
		[Display(Name = "Stop buffer (ATR)", Description = "How far below the previous low the stop sits, above the previous high on a short.", GroupName = "6. Step 4 - Retest", Order = 4)]
		public double StopBufferAtr { get; set; }

		[NinjaScriptProperty]
		[Range(0, 200)]
		[Display(Name = "Target buffer (ticks)", Description = "How far short of the previous high the target sits, above the previous low on a short. The last ticks into a level are where it reverses.", GroupName = "6. Step 4 - Retest", Order = 5)]
		public int TargetBufferTicks { get; set; }

		[NinjaScriptProperty]
		[Range(0, 20)]
		[Display(Name = "Min reward:risk", Description = "Skip setups whose previous high does not pay for the stop. Also sets how far back to look: nearer swings are passed over until one is this many times the risk away. 0 disables.", GroupName = "6. Step 4 - Retest", Order = 6)]
		public double MinRewardRisk { get; set; }

		[NinjaScriptProperty]
		[Range(0, 20)]
		[Display(Name = "Target (R multiple) fallback", Description = "Used only when no previous swing is far enough away to target. 0 falls back to the next opposing liquidity level instead.", GroupName = "6. Step 4 - Retest", Order = 7)]
		public double TargetRMultiple { get; set; }

		[NinjaScriptProperty]
		[Range(1, 1000)]
		[Display(Name = "Min stop (ticks)", Description = "Setups with a tighter stop are skipped as noise.", GroupName = "6. Step 4 - Retest", Order = 8)]
		public int MinStopTicks { get; set; }

		[NinjaScriptProperty]
		[Range(1, 2000)]
		[Display(Name = "Max stop (ticks)", Description = "Backstop only - 'Max setup risk (ATR)' is the real ceiling and works in the units the market moves in. The banner warns if this contradicts the daily loss limit.", GroupName = "6. Step 4 - Retest", Order = 9)]
		public int MaxStopTicks { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "VIX mode", Description = "Off, Directional (VIX must move inversely), or Strict (must also react from a key level).", GroupName = "7. Step 5 - VIX", Order = 0)]
		public ConfirmationMode VixMode { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "VIX symbol", Description = "Needs a contract, not a bare root: 'VX' alone is not an instrument name and fails at startup with Unknown instrument. 'VX ##-##' is the platform's placeholder for the front contract and is right for live. A backtest wants the contract that was front during the window being tested - VX 08-26 for June to August - because the front contract now barely existed then. ^VIX is the index, published only around the cash session, so it is dark for most of a Globex night. Set a merge policy on the instrument for continuity across rolls.", GroupName = "7. Step 5 - VIX", Order = 1)]
		public string VixSymbol { get; set; }

		[NinjaScriptProperty]
		[Range(1, 1440)]
		[Display(Name = "VIX bar minutes", GroupName = "7. Step 5 - VIX", Order = 2)]
		public int VixBarMinutes { get; set; }

		[NinjaScriptProperty]
		[Range(1, 200)]
		[Display(Name = "VIX lookback bars", Description = "How far back to measure whether the VIX is rising or falling.", GroupName = "7. Step 5 - VIX", Order = 3)]
		public int VixLookbackBars { get; set; }

		[NinjaScriptProperty]
		[Range(0, 20)]
		[Display(Name = "VIX min move (floor)", Description = "Absolute floor in VIX points. Kept low - it only rejects a dead-flat reading.", GroupName = "7. Step 5 - VIX", Order = 4)]
		public double VixMinDirectionalMove { get; set; }

		[NinjaScriptProperty]
		[Range(0, 10)]
		[Display(Name = "VIX min move (ATR)", Description = "The real threshold: the move must be this fraction of the VIX's own ATR. Scales with volatility, so the same test works at 3am and at midday. 0 leaves only the floor.", GroupName = "7. Step 5 - VIX", Order = 5)]
		public double VixMinDirectionalMoveAtr { get; set; }

		[NinjaScriptProperty]
		[Range(0.01, 10)]
		[Display(Name = "VIX key level tolerance", Description = "How close the VIX must be to one of its levels to count as reacting from it. Only used in Strict mode.", GroupName = "7. Step 5 - VIX", Order = 6)]
		public double VixKeyLevelTolerance { get; set; }

		[NinjaScriptProperty]
		[Range(0, 1440)]
		[Display(Name = "VIX max data age (minutes)", Description = "How old the last VIX bar may be and still confirm. 0 derives it from the bar period, which suits the ^VIX index - it either publishes or is shut. VX futures are different: the contract is open nearly 23 hours but a bar only forms when someone trades, and overnight VX can go well over an hour without a print. 90 is sized for that.", GroupName = "7. Step 5 - VIX", Order = 7)]
		public int VixMaxDataAgeMinutes { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "VIX skip when quiet", Description = "When the VIX has traded and then gone quiet past the age limit, skip step 5 rather than refuse the setup - the same judgement step 6 makes about a shut equity market. A source with nothing to say is not evidence against a trade, and refusing on staleness turns every thin overnight hour into a blanket ban. A symbol that has never produced a bar still fails loudly: that is a feed problem, not a quiet one.", GroupName = "7. Step 5 - VIX", Order = 8)]
		public bool VixSkipWhenQuiet { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "VIX file", Description = "Full path to a CSV supplying the VIX, replacing the platform series entirely. Blank uses the platform. Rows are 'timestamp,open,high,low,close', and 'timestamp,close' also works. Timestamps may be epoch seconds, epoch milliseconds, or a date-time string - one carrying a zone is honoured, one without is read as UTC. This is how step 5 gets a history longer than the current VX contract has existed.", GroupName = "7. Step 5 - VIX", Order = 9)]
		public string VixFile { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Breadth mode", GroupName = "8. Step 6 - Leaders", Order = 0)]
		public ConfirmationMode BreadthMode { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Breadth source", Description = "Leaders counts the named symbols individually - the measured form, but it needs equity data this feed does not carry, so in practice it needs files and files are snapshots. RelativeStrength reads the spread between the traded index and a broader one instead: the Nasdaq-100 is roughly half Magnificent 7 by weight and the S&P 500 is not, so the difference in their moves is a continuous reading of whether big tech is leading. Both are futures on this feed, so it behaves identically in a backtest and live - but it is a different test and has not been measured. In a falling market they disagree: NQ down less than ES is leadership to one and no participation to the other.", GroupName = "8. Step 6 - Leaders", Order = 1)]
		public BreadthSourceMode BreadthSource { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Comparison symbol", Description = "The broader index the traded one is measured against. Only used when Breadth source is RelativeStrength. Loaded at the chart's own bar period so both series step together.", GroupName = "8. Step 6 - Leaders", Order = 9)]
		public string RelativeStrengthSymbol { get; set; }

		[NinjaScriptProperty]
		[Range(1, 500)]
		[Display(Name = "Relative strength lookback (bars)", Description = "Bars over which each index's percent move is measured before differencing them.", GroupName = "8. Step 6 - Leaders", Order = 10)]
		public int RelativeStrengthLookback { get; set; }

		[NinjaScriptProperty]
		[Range(0, 10)]
		[Display(Name = "Min spread (%, floor)", Description = "Absolute floor on the spread in percentage points. Kept low - it rejects a dead-flat reading rather than being the real test.", GroupName = "8. Step 6 - Leaders", Order = 11)]
		public double RelativeStrengthMinSpread { get; set; }

		[NinjaScriptProperty]
		[Range(0, 10)]
		[Display(Name = "Min spread (multiple)", Description = "The real threshold: this fraction of the spread's own recent average size. The two indices diverge far more in a volatile session than a quiet one, so a fixed number is reachable at midday and impossible at 3am - the failure step 5's fixed threshold had.", GroupName = "8. Step 6 - Leaders", Order = 12)]
		public double RelativeStrengthMultiple { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Leader symbols", Description = "Comma separated. CME single stock futures - SAAPL, SMSFT and so on - not the cash shares, which this feed does not carry. Each must open on a chart or the strategy will not start at all. History is contract-based and short, so a backtest with this step on truncates to the youngest of them.", GroupName = "8. Step 6 - Leaders", Order = 1)]
		public string BreadthSymbols { get; set; }

		[NinjaScriptProperty]
		[Range(1, 1440)]
		[Display(Name = "Leader bar minutes", GroupName = "8. Step 6 - Leaders", Order = 2)]
		public int BreadthBarMinutes { get; set; }

		[NinjaScriptProperty]
		[Range(1, 50)]
		[Display(Name = "Min leaders aligned", GroupName = "8. Step 6 - Leaders", Order = 3)]
		public int BreadthMinAligned { get; set; }

		[NinjaScriptProperty]
		[Range(0, 100)]
		[Display(Name = "Min leader move (%)", Description = "Below this a leader counts as flat rather than participating.", GroupName = "8. Step 6 - Leaders", Order = 4)]
		public double BreadthMinMovePercent { get; set; }

		[NinjaScriptProperty]
		[Range(0, 1440)]
		[Display(Name = "Leader max data age (minutes)", Description = "How stale a leader reading may be and still count. 0 derives it from the bar period, which suits a platform series - either live or absent. A delayed source needs its own number: a free equity feed fifteen minutes behind a fifteen-minute limit expires exactly as it arrives, and the step then never confirms without ever saying why.", GroupName = "8. Step 6 - Leaders", Order = 8)]
		public int BreadthMaxDataAgeMinutes { get; set; }

		[NinjaScriptProperty]
		[Range(0, 235959)]
		[Display(Name = "Leaders open (HHmmss)", Description = "Step 6 applies only inside this window and is skipped outside it. Ships at 0/0, meaning around the clock: the leaders are futures and run Globex hours, so the staleness guard decides when there is nothing to read rather than the clock. Set a window to narrow it - 093000 to 160000 restricts the step to the cash session.", GroupName = "8. Step 6 - Leaders", Order = 5)]
		public int BreadthActiveStart { get; set; }

		[NinjaScriptProperty]
		[Range(0, 235959)]
		[Display(Name = "Leaders close (HHmmss)", GroupName = "8. Step 6 - Leaders", Order = 6)]
		public int BreadthActiveEnd { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Leader files", Description = "Comma separated full paths, one per leader in the same order as 'Leader symbols' - position pairs the two lists. Same row format as the VIX file. All must be present or none are used: the leaders left on platform series need contiguous data-series indices, so this is all-or-nothing for the step rather than per symbol.", GroupName = "8. Step 6 - Leaders", Order = 7)]
		public string LeaderFiles { get; set; }

		[NinjaScriptProperty]
		[Range(0, 3600)]
		[Display(Name = "File re-read (seconds)", Description = "How often a file is checked for changes while live, on a background thread. It is only re-parsed when the file's modified time has actually moved, so polling costs nothing. A backtest loads once and never checks. 0 disables re-reading entirely - right for a static file, wrong for one something is appending to.", GroupName = "9. Diagnostics", Order = 5)]
		public int FileReloadSeconds { get; set; }

		[NinjaScriptProperty]
		[Range(-1440, 1440)]
		[Display(Name = "File time offset (minutes)", Description = "Correction applied to file timestamps after the UTC conversion, for a platform whose display time zone is not the machine's. Leave at 0 and read the banner: it prints each file's first row beside the chart's first bar, and the run summary reports what fraction of lookups were answered. A file an hour out loads perfectly and answers nothing.", GroupName = "9. Diagnostics", Order = 6)]
		public int FileTimeOffsetMinutes { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show chart visuals", Description = "Draw stop and target lines, entry markers and the stats panel. Skipped automatically when there is no chart, so the Strategy Analyzer is unaffected either way.", GroupName = "10. Chart", Order = 0)]
		public bool ShowChartVisuals { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show setup zones", Description = "Shade the retest zone while a setup is waiting in it.", GroupName = "10. Chart", Order = 1)]
		public bool ShowSetupZones { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show stats panel", Description = "Running totals in the top-right corner.", GroupName = "10. Chart", Order = 2)]
		public bool ShowStatsPanel { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Shadow confirmations", Description = "Steps 5 and 6 evaluate and record their verdict but refuse nothing - every trade is taken. The run summary then reports what the trades they would have refused actually did. This exists because both steps read contract-based data NinjaTrader does not keep across a roll, so neither can be backtested over more than the current contract's life; shadow mode measures them forward instead. It removes protection while on. Do not leave it on for a funded account.", GroupName = "9. Diagnostics", Order = 4)]
		public bool ShadowConfirmations { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Enable logging", GroupName = "9. Diagnostics", Order = 0)]
		public bool EnableLogging { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Verbose logging", Description = "Log every state transition. Useful for tuning, noisy in production.", GroupName = "9. Diagnostics", Order = 1)]
		public bool VerboseLogging { get; set; }

		[NinjaScriptProperty]
		[Range(0, int.MaxValue)]
		[Display(Name = "Status every N bars", Description = "Heartbeat line showing bar count, levels, setup state and the day's funnel. 0 disables.", GroupName = "9. Diagnostics", Order = 2)]
		public int StatusEveryBars { get; set; }

		#endregion
	}
}
