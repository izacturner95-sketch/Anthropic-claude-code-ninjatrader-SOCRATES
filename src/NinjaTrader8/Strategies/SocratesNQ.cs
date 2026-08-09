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
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
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
		private string[] breadthSymbols = new string[0];
		private double[] breadthSessionOpen = new double[0];

		// --- Session tracking ---
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

		// --- Diagnostics ---
		//
		// A strategy that prints nothing is indistinguishable from a strategy that never
		// ran, so every path that can silence it either logs once or is counted. The
		// funnel counters answer the only question that matters when there are no trades:
		// how far down the six steps did price actually get?
		private bool firstBarLogged;
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

		private int totalSweeps;
		private int totalShifts;
		private int totalZoneTouches;
		private int totalEntries;
		private int totalBlockedByRisk;
		private int totalRejectedVix;
		private int totalRejectedBreadth;
		private int totalRejectedStop;
		private int totalRejectedSizing;

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
				OrderFillResolution = OrderFillResolution.Standard;
				Slippage = 0;
				StartBehavior = StartBehavior.WaitUntilFlat;
				TimeInForce = TimeInForce.Gtc;
				TraceOrders = false;
				BarsRequiredToTrade = 30;
				RealtimeErrorHandling = RealtimeErrorHandling.StopCancelClose;
				StopTargetHandling = StopTargetHandling.PerEntryExecution;

				// --- Sizing ---
				SizingMode = PositionSizingMode.Fixed;
				FixedContracts = 1;
				RiskPerTradeDollars = 200;
				RiskPerTradePercent = 0.5;
				MaxContracts = 1;
				AllowMinimumOneContract = false;
				TickValueDollars = 5.00;
				BacktestStartingEquity = 25000;
				UseConfidenceSizing = false;

				// --- Risk ---
				SessionStartTime = 94500;
				SessionEndTime = 154500;
				FlattenTime = 155500;
				MaxDailyLossDollars = 1000;
				DailyProfitTargetDollars = 0;
				StopForDayOnProfitTarget = false;
				MaxTradesPerDay = 3;
				MaxConsecutiveLosses = 2;

				// --- Step 1: context ---
				AtrPeriod = 14;
				SwingStrength = 3;
				UseFourHourPivots = true;
				OpeningRangeMinutes = 15;
				ZoneHalfWidthAtr = 0.25;
				UseOrderBlocks = true;
				OrderBlockRequireImbalance = true;
				OrderBlockDisplacementAtr = 1.0;
				OrderBlockMaxLookback = 10;
				OrderBlockZone = OrderBlockZoneMode.FullRange;

				// --- Step 2: liquidity ---
				MinPenetrationAtr = 0.10;
				MinPenetrationPoints = 1.0;
				MaxBarsToReclaim = 6;

				// --- Step 3: structure ---
				MaxBarsSweepToShift = 12;
				UsePostSweepSwing = true;
				MinDisplacementAtr = 1.0;

				// --- Step 4: retest ---
				MaxBarsShiftToRetest = 15;
				ZoneMode = RetestZoneMode.BrokenStructure;
				RetestZoneAtr = 0.30;
				RequireConfirmationClose = true;
				StopBufferAtr = 0.25;
				TargetRMultiple = 2.0;
				MinStopTicks = 20;

				// This was 100 ticks (25 points), chosen so that two consecutive stop-outs on
				// one NQ contract came to exactly the $1,000 daily cap. The arithmetic was
				// tidy and the number was unreachable: entry is at the broken structure level
				// and the stop sits beyond the swept extreme, so the distance between them is
				// the whole displacement leg. On 5-minute NQ that leg is rarely under 25
				// points, and a run of 45 completed setups produced zero fills.
				//
				// 200 ticks is 50 points. On NQ that is $1,000 a contract, so two stop-outs
				// breach the daily cap - the startup banner says so, with the arithmetic. The
				// three ways out are a $2,000 cap, MNQ instead of NQ (same 50 points costs
				// $100), or accepting fewer trades by lowering this again. The run summary
				// now prints the distribution of stop distances the setups actually asked
				// for, so that is a decision with numbers behind it rather than a guess.
				MaxStopTicks = 200;

				// --- Step 5: VIX ---
				//
				// Off by default. Steps 5 and 6 need twelve data series between them, and a
				// futures-only feed carries none of the eight index and equity symbols. A
				// missing series is not a soft failure in NinjaTrader: the strategy refuses
				// to start, logs to the Log tab rather than the Output window, and produces
				// no prints and no trades at all. Turn these on once you have confirmed the
				// symbols load on a chart of their own.
				VixMode = ConfirmationMode.Off;
				VixSymbol = "^VIX";
				VixBarMinutes = 5;
				VixLookbackBars = 6;
				VixMinDirectionalMove = 0.10;
				VixKeyLevelTolerance = 0.35;

				// --- Step 6: breadth ---
				BreadthMode = ConfirmationMode.Off;
				BreadthSymbols = "AAPL,MSFT,NVDA,AMZN,META,GOOGL,TSLA";
				BreadthBarMinutes = 5;
				BreadthMinAligned = 5;
				BreadthMinMovePercent = 0.05;

				EnableLogging = true;
				VerboseLogging = false;
				StatusEveryBars = 120;
			}
			else if (State == State.Configure)
			{
				contract = new ContractSpec(0.25, TickValueDollars);

				sizerSettings = new PositionSizerSettings
				{
					Mode = SizingMode,
					FixedContracts = FixedContracts,
					RiskPerTradeDollars = RiskPerTradeDollars,
					RiskPerTradePercent = RiskPerTradePercent,
					MaxContracts = MaxContracts,
					AllowMinimumOneContract = AllowMinimumOneContract
				};

				riskSettings = new RiskManagerSettings
				{
					SessionStartTime = SessionStartTime,
					SessionEndTime = SessionEndTime,
					FlattenTime = FlattenTime,
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
						MaxBarsToReclaim = MaxBarsToReclaim
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
					ZoneMode = ZoneMode,
					RetestZoneAtr = RetestZoneAtr,
					RequireConfirmationClose = RequireConfirmationClose,
					StopBufferAtr = StopBufferAtr,
					TargetRMultiple = TargetRMultiple
				});

				// Series are added only when the step that needs them is enabled, so a data
				// feed without index or equity coverage can still run steps 1-4.
				idxDaily = idxWeekly = idxFourHour = idxVix = idxBreadthStart = -1;
				breadthSymbols = new string[0];
				breadthSessionOpen = new double[0];
				vix = null;
				breadth = null;

				int next = 1;

				AddDataSeries(BarsPeriodType.Day, 1);
				idxDaily = next++;

				AddDataSeries(BarsPeriodType.Week, 1);
				idxWeekly = next++;

				if (UseFourHourPivots)
				{
					AddDataSeries(BarsPeriodType.Minute, 240);
					idxFourHour = next++;
				}

				if (VixMode != ConfirmationMode.Off)
				{
					AddDataSeries(VixSymbol, BarsPeriodType.Minute, VixBarMinutes);
					idxVix = next++;

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
						KeyLevelTolerance = VixKeyLevelTolerance
					}, new MarketAnalyzer(vixSettings));
				}

				if (BreadthMode != ConfirmationMode.Off)
				{
					breadthSymbols = ParseSymbols(BreadthSymbols);

					if (breadthSymbols.Length > 0)
					{
						idxBreadthStart = next;

						for (int i = 0; i < breadthSymbols.Length; i++)
						{
							AddDataSeries(breadthSymbols[i], BarsPeriodType.Minute, BreadthBarMinutes);
							next++;
						}

						breadthSessionOpen = new double[breadthSymbols.Length];

						breadth = new BreadthConfirmation(new BreadthConfirmationSettings
						{
							Mode = BreadthMode,
							MinAligned = BreadthMinAligned,
							MinMovePercent = BreadthMinMovePercent
						}, breadthSymbols);
					}
				}

				processedTradeCount = 0;
				flattenedForDay = false;
				sessionLevelsBuilt = false;

				ResetDiagnostics();
			}
			else if (State == State.DataLoaded)
			{
				LogStartupBanner();
			}
			else if (State == State.Realtime)
			{
				Log(string.Format("Realtime. Sizing={0} max {1}, daily loss cap {2:C}, VIX={3}, breadth={4}.",
					SizingMode, MaxContracts, MaxDailyLossDollars, VixMode, BreadthMode));
			}
			else if (State == State.Terminated)
			{
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

			// Secondary series that only supply reference prices need no per-bar work.
			if (BarsInProgress != 0)
				return;

			// Proof of life on the very first bar, before any guard below can swallow it.
			if (!firstBarLogged)
			{
				firstBarLogged = true;
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

			// End-of-day flatten outranks everything.
			if (risk.ShouldFlatten(timeOfDay))
			{
				if (Position.MarketPosition != MarketPosition.Flat && !flattenedForDay)
				{
					Log(string.Format("Flatten time {0:000000} reached - closing position.", FlattenTime));
					CloseCurrentPosition();
					flattenedForDay = true;
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

			EvaluateConfirmationsAndEnter(result, atr);
		}

		/// <summary>
		/// Counts how far price got down the sequence on this bar. Without this, "no trades"
		/// has no diagnosis: a run that never produced a single sweep and a run that produced
		/// forty sweeps rejected at step 5 look identical from the outside.
		/// </summary>
		private void TrackFunnel(SetupState stateBefore, SetupResult result)
		{
			if (nq.LastUpdateSweep.IsValid)
			{
				daySweeps++;
				totalSweeps++;
			}

			if (stateBefore != SetupState.AwaitingRetest && setup.State == SetupState.AwaitingRetest)
			{
				dayShifts++;
				totalShifts++;
			}

			// An entry on the same bar the zone is first touched leaves ZoneTouched already
			// cleared by the engine's reset, so the result stands in for the touch.
			if ((setup.ZoneTouched || result.HasEntry) && !previousZoneTouched)
			{
				dayZoneTouches++;
				totalZoneTouches++;
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

			Log(string.Format(
				"Status: bar {0}, ATR {1:N2}, {2} levels, setup {3}. Today: {4} sweeps, {5} shifts, {6} retests, {7} entries. Day P/L {8:C}{9}.",
				CurrentBar, atr, nq.Levels.Count, setup.State,
				daySweeps, dayShifts, dayZoneTouches, dayEntries,
				risk.DailyRealisedPnL,
				risk.IsHaltedForDay ? ", HALTED: " + risk.HaltReason : string.Empty));
		}

		#region Steps 5 and 6

		private void EvaluateConfirmationsAndEnter(SetupResult result, double atr)
		{
			double strength = 1.0;

			// Step 5.
			if (vix != null)
			{
				ConfirmationResult vixResult = vix.Evaluate(result.Direction);

				if (!vixResult.Agrees)
				{
					dayRejectedVix++;
					totalRejectedVix++;
					Log(string.Format("Setup rejected at step 5. {0} | {1}", vixResult.Detail, result.Detail));
					return;
				}

				strength = Math.Min(strength, vixResult.Strength);

				if (VerboseLogging)
					Log("Step 5 passed: " + vixResult.Detail);
			}

			// Step 6.
			if (breadth != null)
			{
				ConfirmationResult breadthResult = breadth.Evaluate(result.Direction);

				if (!breadthResult.Agrees)
				{
					dayRejectedBreadth++;
					totalRejectedBreadth++;
					Log(string.Format("Setup rejected at step 6. {0} | {1}", breadthResult.Detail, result.Detail));
					return;
				}

				strength = Math.Min(strength, breadthResult.Strength);

				if (VerboseLogging)
					Log("Step 6 passed: " + breadthResult.Detail);
			}

			SubmitEntry(result, strength);
		}

		private void UpdateVix()
		{
			if (vix == null || CurrentBars[idxVix] < Math.Max(VixLookbackBars, AtrPeriod) + 1)
				return;

			double vixAtr = ATR(BarsArray[idxVix], AtrPeriod)[0];
			double reference = Closes[idxVix][VixLookbackBars];

			vix.Update(CurrentBars[idxVix], Times[idxVix][0], Opens[idxVix][0],
				Highs[idxVix][0], Lows[idxVix][0], Closes[idxVix][0], reference, vixAtr);
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
				breadth.SetComponent(component, Closes[series][0], breadthSessionOpen[component]);
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
			previousZoneTouched = false;

			flattenedForDay = false;
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
					? string.Format("Context built: PDH {0:N2}, PDL {1:N2}, PDC {2:N2}, {3} levels in play.",
						pdh, pdl, pdc, nq.Levels.Count)
					: string.Format("Context built without prior-day data: {0} levels in play.", nq.Levels.Count));
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
			double entryPrice = Close[0];
			double stopDistanceTicks = Math.Abs(entryPrice - result.StopPrice) / TickSize;

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
			int contracts = sizer.GetContracts(sizerSettings, stopDistanceTicks, GetAccountEquity(), out sizingReason);

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

			string label = result.Label;

			SetStopLoss(label, CalculationMode.Price, result.StopPrice, false);

			if (result.TargetPrice > 0)
				SetProfitTarget(label, CalculationMode.Price, result.TargetPrice);

			if (result.Direction == TradeDirection.Long)
				EnterLong(contracts, label);
			else
				EnterShort(contracts, label);

			risk.RecordEntry();
			dayEntries++;
			totalEntries++;

			Log(string.Format("ENTRY {0} x{1} @ ~{2:N2}. {3} {4}",
				result.Direction, contracts, entryPrice, result.Detail, sizingReason));
		}

		private void CloseCurrentPosition()
		{
			if (Position.MarketPosition == MarketPosition.Long)
				ExitLong();
			else if (Position.MarketPosition == MarketPosition.Short)
				ExitShort();
		}

		private void DrainCompletedTrades()
		{
			int total = SystemPerformance.AllTrades.Count;

			while (processedTradeCount < total)
			{
				Trade trade = SystemPerformance.AllTrades[processedTradeCount];
				processedTradeCount++;

				risk.RecordClosedTrade(trade.ProfitCurrency);

				Log(string.Format("Trade closed: {0:C}. Day P/L {1:C}, trades {2}, consecutive losses {3}.",
					trade.ProfitCurrency, risk.DailyRealisedPnL, risk.TradesToday, risk.ConsecutiveLosses));
			}

			if (risk.IsHaltedForDay && Position.MarketPosition != MarketPosition.Flat)
			{
				Log("Risk halt with open position - closing. " + risk.HaltReason);
				CloseCurrentPosition();
			}
		}

		private double GetAccountEquity()
		{
			if (State == State.Realtime && Account != null)
				return Account.Get(AccountItem.CashValue, Currency.UsDollar);

			return BacktestStartingEquity + SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit;
		}

		protected override void OnConnectionStatusUpdate(ConnectionStatusEventArgs e)
		{
			if (risk == null)
				return;

			bool down = e.Status != ConnectionStatus.Connected || e.PriceStatus != ConnectionStatus.Connected;
			risk.SetConnectionDown(down);

			Log(string.Format("Connection update: order={0}, price={1}. New entries {2}.",
				e.Status, e.PriceStatus, down ? "BLOCKED" : "allowed"));
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
			warmupLogged = false;
			dailyContextWarned = false;
			weeklyContextWarned = false;
			zeroAtrWarned = false;
			previousZoneTouched = false;
			barsProcessed = 0;
			lastStatusBar = int.MinValue;

			daySweeps = dayShifts = dayZoneTouches = dayEntries = 0;
			dayBlockedByRisk = dayRejectedVix = dayRejectedBreadth = dayRejectedStop = dayRejectedSizing = 0;

			totalSweeps = totalShifts = totalZoneTouches = totalEntries = 0;
			totalBlockedByRisk = totalRejectedVix = totalRejectedBreadth = totalRejectedStop = totalRejectedSizing = 0;

			Array.Clear(blockReasonCounts, 0, blockReasonCounts.Length);
			Array.Clear(stopTickBuckets, 0, stopTickBuckets.Length);
			stopSamples = 0;
			stopTicksMin = 0;
			stopTicksMax = 0;
			stopTicksSum = 0;
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
			Print(string.Format("  Entry window    : {0:000000}-{1:000000}, flatten {2:000000}", SessionStartTime, SessionEndTime, FlattenTime));
			Print(string.Format("  Sizing          : {0}, max {1} contract(s), daily loss cap {2:C}", SizingMode, MaxContracts, MaxDailyLossDollars));
			Print(string.Format("  Stop band       : {0}-{1} ticks", MinStopTicks, MaxStopTicks));
			Print(string.Format("  Step 5 (VIX)    : {0}{1}", VixMode, VixMode == ConfirmationMode.Off ? string.Empty : " on " + VixSymbol));
			Print(string.Format("  Step 6 (leaders): {0}{1}", BreadthMode, BreadthMode == ConfirmationMode.Off ? string.Empty : " on " + BreadthSymbols));
			Print(string.Format("  Data series     : {0}", BarsArray != null ? BarsArray.Length : 0));

			if (BarsArray != null)
			{
				for (int i = 0; i < BarsArray.Length; i++)
				{
					int count = BarsArray[i] != null ? BarsArray[i].Count : 0;

					Print(string.Format("    [{0}] {1,-24} {2,7} bars{3}", i, DescribeSeries(i), count,
						count == 0 ? "   <-- EMPTY, this series has no data" : string.Empty));
				}

				if (idxDaily >= 0 && BarsArray[idxDaily] != null && BarsArray[idxDaily].Count < 2)
					Print("  NOTE: fewer than two daily bars. Prior-day levels and daily pivots will be skipped.");

				if (idxWeekly >= 0 && BarsArray[idxWeekly] != null && BarsArray[idxWeekly].Count < 2)
					Print("  NOTE: fewer than two weekly bars. Prior-week levels and weekly pivots will be skipped.");
			}

			LogRiskConsistency();

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
			if (MaxDailyLossDollars <= 0 || MaxConsecutiveLosses <= 0)
				return;

			double worstCase = MaxStopTicks * TickValueDollars * Math.Max(1, MaxContracts) * MaxConsecutiveLosses;

			if (worstCase <= MaxDailyLossDollars)
				return;

			Print(string.Format(
				"  WARNING: {0} ticks x {1:C} x {2} contract(s) x {3} losses = {4:C}, which overshoots the {5:C} daily cap.",
				MaxStopTicks, TickValueDollars, Math.Max(1, MaxContracts), MaxConsecutiveLosses, worstCase, MaxDailyLossDollars));
			Print(string.Format(
				"           Raise the cap to {0:C}, switch to MNQ (Tick value 0.50), or lower Max stop to {1} ticks.",
				worstCase,
				(int)Math.Floor(MaxDailyLossDollars / (TickValueDollars * Math.Max(1, MaxContracts) * MaxConsecutiveLosses))));
		}

		private string DescribeSeries(int index)
		{
			if (index == 0)
				return "NQ primary";

			if (index == idxDaily)
				return "NQ daily";

			if (index == idxWeekly)
				return "NQ weekly";

			if (index == idxFourHour)
				return "NQ 4-hour";

			if (index == idxVix)
				return "VIX " + VixSymbol;

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

			int completedSetups = totalEntries + totalBlockedByRisk + totalRejectedVix
				+ totalRejectedBreadth + totalRejectedStop + totalRejectedSizing;

			Print("=== Socrates NQ - run summary =====================================");
			Print(string.Format("  Bars evaluated       : {0}", barsProcessed));
			Print(string.Format("  Sweeps (step 2)      : {0}", totalSweeps));
			Print(string.Format("  Structure shifts (3) : {0}", totalShifts));
			Print(string.Format("  Retests reached (4)  : {0}", totalZoneTouches));
			Print(string.Format("  Setups completed     : {0}", completedSetups));
			Print(string.Format("  Entries submitted    : {0}", totalEntries));
			Print("  --- of the completed setups, rejected by ---");
			Print(string.Format("  Risk gate            : {0}", totalBlockedByRisk));

			for (int i = 0; i < blockReasonCounts.Length; i++)
			{
				if (blockReasonCounts[i] > 0)
					Print(string.Format("      {0,-18} {1}", (EntryBlockReason)i, blockReasonCounts[i]));
			}

			Print(string.Format("  Step 5 (VIX)         : {0}", totalRejectedVix));
			Print(string.Format("  Step 6 (leaders)     : {0}", totalRejectedBreadth));
			Print(string.Format("  Stop band            : {0}", totalRejectedStop));
			Print(string.Format("  Sizing               : {0}", totalRejectedSizing));

			LogStopDistribution();

			if (totalSweeps == 0)
				Print("  No sweeps at all. Loosen 'Min penetration' or check that levels are being built.");
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
		private void LogStopDistribution()
		{
			if (stopSamples == 0)
				return;

			Print(string.Format("  --- stop distance asked for by {0} completed setups (ticks) ---", stopSamples));
			Print(string.Format("  Min {0:N0}, mean {1:N0}, max {2:N0}. Band admits {3}-{4}.",
				stopTicksMin, stopTicksSum / stopSamples, stopTicksMax, MinStopTicks, MaxStopTicks));

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

			int wouldPass = 0;

			for (int i = 0; i < StopBucketCount; i++)
			{
				int bucketLow = i * StopBucketTicks;

				if (bucketLow >= MinStopTicks && bucketLow < MaxStopTicks)
					wouldPass += stopTickBuckets[i];
			}

			if (wouldPass == 0)
				Print("  NOTE: the band does not overlap the distribution at all - no setup can ever pass it.");
		}

		#endregion

		#region Properties

		[NinjaScriptProperty]
		[Display(Name = "Sizing mode", GroupName = "1. Position Sizing", Order = 0)]
		public PositionSizingMode SizingMode { get; set; }

		[NinjaScriptProperty]
		[Range(0, int.MaxValue)]
		[Display(Name = "Fixed contracts", GroupName = "1. Position Sizing", Order = 1)]
		public int FixedContracts { get; set; }

		[NinjaScriptProperty]
		[Range(0, double.MaxValue)]
		[Display(Name = "Risk per trade ($)", GroupName = "1. Position Sizing", Order = 2)]
		public double RiskPerTradeDollars { get; set; }

		[NinjaScriptProperty]
		[Range(0, 100)]
		[Display(Name = "Risk per trade (%)", GroupName = "1. Position Sizing", Order = 3)]
		public double RiskPerTradePercent { get; set; }

		[NinjaScriptProperty]
		[Range(0, int.MaxValue)]
		[Display(Name = "Max contracts", Description = "Hard ceiling applied to every sizing mode.", GroupName = "1. Position Sizing", Order = 4)]
		public int MaxContracts { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Allow minimum 1 contract", GroupName = "1. Position Sizing", Order = 5)]
		public bool AllowMinimumOneContract { get; set; }

		[NinjaScriptProperty]
		[Range(0.01, double.MaxValue)]
		[Display(Name = "Tick value ($)", Description = "NQ = 5.00, MNQ = 0.50.", GroupName = "1. Position Sizing", Order = 6)]
		public double TickValueDollars { get; set; }

		[NinjaScriptProperty]
		[Range(0, double.MaxValue)]
		[Display(Name = "Backtest starting equity ($)", GroupName = "1. Position Sizing", Order = 7)]
		public double BacktestStartingEquity { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Scale size by confirmation strength", Description = "Reduce size when the VIX or leaders agree only weakly.", GroupName = "1. Position Sizing", Order = 8)]
		public bool UseConfidenceSizing { get; set; }

		[NinjaScriptProperty]
		[Range(0, 235959)]
		[Display(Name = "Session start (HHmmss)", GroupName = "2. Risk", Order = 0)]
		public int SessionStartTime { get; set; }

		[NinjaScriptProperty]
		[Range(0, 235959)]
		[Display(Name = "Session end (HHmmss)", GroupName = "2. Risk", Order = 1)]
		public int SessionEndTime { get; set; }

		[NinjaScriptProperty]
		[Range(0, 235959)]
		[Display(Name = "Flatten time (HHmmss)", GroupName = "2. Risk", Order = 2)]
		public int FlattenTime { get; set; }

		[NinjaScriptProperty]
		[Range(0, double.MaxValue)]
		[Display(Name = "Max daily loss ($)", GroupName = "2. Risk", Order = 3)]
		public double MaxDailyLossDollars { get; set; }

		[NinjaScriptProperty]
		[Range(0, double.MaxValue)]
		[Display(Name = "Daily profit target ($)", GroupName = "2. Risk", Order = 4)]
		public double DailyProfitTargetDollars { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Stop for day on profit target", GroupName = "2. Risk", Order = 5)]
		public bool StopForDayOnProfitTarget { get; set; }

		[NinjaScriptProperty]
		[Range(0, int.MaxValue)]
		[Display(Name = "Max trades per day", Description = "0 disables.", GroupName = "2. Risk", Order = 6)]
		public int MaxTradesPerDay { get; set; }

		[NinjaScriptProperty]
		[Range(0, int.MaxValue)]
		[Display(Name = "Max consecutive losses", Description = "0 disables.", GroupName = "2. Risk", Order = 7)]
		public int MaxConsecutiveLosses { get; set; }

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
		[Range(1, 200)]
		[Display(Name = "Max bars sweep to shift", GroupName = "5. Step 3 - Structure", Order = 0)]
		public int MaxBarsSweepToShift { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Use post-sweep swing", Description = "Break the swing formed after the sweep rather than the one before it. Faster, more signals.", GroupName = "5. Step 3 - Structure", Order = 1)]
		public bool UsePostSweepSwing { get; set; }

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
		[Display(Name = "Stop buffer (ATR)", Description = "Distance beyond the sweep extreme for the protective stop.", GroupName = "6. Step 4 - Retest", Order = 4)]
		public double StopBufferAtr { get; set; }

		[NinjaScriptProperty]
		[Range(0, 20)]
		[Display(Name = "Target (R multiple)", Description = "0 targets the next opposing liquidity level instead.", GroupName = "6. Step 4 - Retest", Order = 5)]
		public double TargetRMultiple { get; set; }

		[NinjaScriptProperty]
		[Range(1, 1000)]
		[Display(Name = "Min stop (ticks)", Description = "Setups with a tighter stop are skipped as noise.", GroupName = "6. Step 4 - Retest", Order = 6)]
		public int MinStopTicks { get; set; }

		[NinjaScriptProperty]
		[Range(1, 2000)]
		[Display(Name = "Max stop (ticks)", Description = "Setups needing a wider stop are skipped. Entry is at the broken structure and the stop sits beyond the swept extreme, so this has to cover a whole displacement leg. The run summary prints the distribution actually asked for; the startup banner warns if this contradicts the daily loss limit.", GroupName = "6. Step 4 - Retest", Order = 7)]
		public int MaxStopTicks { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "VIX mode", Description = "Off, Directional (VIX must move inversely), or Strict (must also react from a key level).", GroupName = "7. Step 5 - VIX", Order = 0)]
		public ConfirmationMode VixMode { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "VIX symbol", Description = "Must exist in your data feed. ^VIX on Kinetick.", GroupName = "7. Step 5 - VIX", Order = 1)]
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
		[Display(Name = "VIX min move", Description = "Minimum VIX points of movement to count as directional agreement.", GroupName = "7. Step 5 - VIX", Order = 4)]
		public double VixMinDirectionalMove { get; set; }

		[NinjaScriptProperty]
		[Range(0.01, 10)]
		[Display(Name = "VIX key level tolerance", Description = "How close the VIX must be to one of its levels to count as reacting from it.", GroupName = "7. Step 5 - VIX", Order = 5)]
		public double VixKeyLevelTolerance { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Breadth mode", GroupName = "8. Step 6 - Leaders", Order = 0)]
		public ConfirmationMode BreadthMode { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Leader symbols", Description = "Comma separated. Each needs data in your feed.", GroupName = "8. Step 6 - Leaders", Order = 1)]
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
