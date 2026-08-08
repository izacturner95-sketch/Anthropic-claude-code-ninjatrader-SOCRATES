// Socrates NQ - automated strategy shell for the CME E-mini Nasdaq-100.
//
// The trade logic in EvaluateEntry() is a PLACEHOLDER (a plain EMA cross). It exists
// so the order handling, sizing and risk plumbing can be verified end to end before
// the real strategy is written. Replace it; do not trade it.
//
// Everything outside the "STRATEGY LOGIC" region is infrastructure and should not need
// to change when the signal logic does.

#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
using Socrates.Risk;
using Socrates.Strategy;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
	public class SocratesNQ : Strategy
	{
		private RiskManager risk;
		private PositionSizer sizer;
		private RiskManagerSettings riskSettings;
		private PositionSizerSettings sizerSettings;
		private ContractSpec contract;

		// Index of the next trade in SystemPerformance.AllTrades we have not yet accounted for.
		private int processedTradeCount;

		// Set when the flatten deadline passes, cleared on the next trading day.
		private bool flattenedForDay;

		// Placeholder indicators - remove along with the placeholder logic.
		private EMA emaFast;
		private EMA emaSlow;

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description = "Socrates automated strategy for NQ futures.";
				Name = "SocratesNQ";

				// OnBarClose is the honest default: signals are evaluated on completed bars only,
				// so backtest and live behaviour agree. Intrabar evaluation is a deliberate change,
				// not a default.
				Calculate = Calculate.OnBarClose;

				EntriesPerDirection = 1;
				EntryHandling = EntryHandling.AllEntries;
				IsExitOnSessionCloseStrategy = true;
				ExitOnSessionCloseSeconds = 30;
				IsFillLimitOnTouch = false;
				MaximumBarsLookBack = MaximumBarsLookBack.TwoHundredFiftySix;
				OrderFillResolution = OrderFillResolution.Standard;
				Slippage = 0;

				// Do not adopt a position that already exists when the strategy is enabled.
				StartBehavior = StartBehavior.WaitUntilFlat;

				TimeInForce = TimeInForce.Gtc;
				TraceOrders = false;
				BarsRequiredToTrade = 20;

				// On an unhandled realtime order error, cancel working orders and close the
				// position rather than continuing in an unknown state.
				RealtimeErrorHandling = RealtimeErrorHandling.StopCancelClose;
				StopTargetHandling = StopTargetHandling.PerEntryExecution;

				// --- Position sizing defaults ---
				SizingMode = PositionSizingMode.Fixed;
				FixedContracts = 1;
				RiskPerTradeDollars = 200;
				RiskPerTradePercent = 0.5;
				MaxContracts = 1;
				AllowMinimumOneContract = false;

				// --- Contract defaults (NQ) ---
				TickValueDollars = 5.00;
				BacktestStartingEquity = 25000;

				// --- Risk defaults ---
				SessionStartTime = 93000;
				SessionEndTime = 155000;
				FlattenTime = 155500;
				MaxDailyLossDollars = 1000;
				DailyProfitTargetDollars = 0;
				StopForDayOnProfitTarget = false;
				MaxTradesPerDay = 0;
				MaxConsecutiveLosses = 0;

				// --- Placeholder signal defaults ---
				FastPeriod = 9;
				SlowPeriod = 21;
				StopLossTicks = 60;
				ProfitTargetTicks = 80;

				EnableLogging = true;
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
				processedTradeCount = 0;
				flattenedForDay = false;
			}
			else if (State == State.DataLoaded)
			{
				emaFast = EMA(Close, FastPeriod);
				emaSlow = EMA(Close, SlowPeriod);
			}
			else if (State == State.Realtime)
			{
				Log(string.Format("Entering realtime. Sizing={0}, MaxContracts={1}, MaxDailyLoss={2:C}.",
					SizingMode, MaxContracts, MaxDailyLossDollars));
			}
		}

		protected override void OnBarUpdate()
		{
			if (CurrentBar < BarsRequiredToTrade)
				return;

			// Account for any trades that closed since the last bar before making decisions.
			DrainCompletedTrades();

			if (risk.SyncTradingDay(Bars.GetTradingDayFromLocal(Time[0])))
			{
				flattenedForDay = false;
				Log("New trading day - daily counters reset.");
			}

			int timeOfDay = ToTime(Time[0]);

			// End-of-day flatten takes precedence over everything else.
			if (risk.ShouldFlatten(timeOfDay))
			{
				if (Position.MarketPosition != MarketPosition.Flat && !flattenedForDay)
				{
					Log(string.Format("Flatten time {0:000000} reached - closing position.", FlattenTime));
					CloseCurrentPosition("flatten");
					flattenedForDay = true;
				}
				return;
			}

			// Managing an existing position is the exit layer's job.
			if (Position.MarketPosition != MarketPosition.Flat)
			{
				ManageOpenPosition();
				return;
			}

			string blockDetail;
			EntryBlockReason block = risk.CanEnter(timeOfDay, out blockDetail);
			if (block != EntryBlockReason.None)
			{
				// Session-window blocks happen on most bars; logging them would flood the output.
				if (block != EntryBlockReason.OutsideSession)
					Log(string.Format("Entry blocked ({0}): {1}", block, blockDetail));
				return;
			}

			Signal signal = EvaluateEntry();
			if (!signal.IsEntry)
				return;

			SubmitEntry(signal);
		}

		#region Order handling

		private void SubmitEntry(Signal signal)
		{
			string sizingReason;
			int contracts = sizer.GetContracts(sizerSettings, signal.StopTicks, GetAccountEquity(), out sizingReason);

			if (contracts <= 0)
			{
				Log("Entry skipped. " + sizingReason);
				return;
			}

			string label = string.IsNullOrEmpty(signal.Label) ? "socrates" : signal.Label;

			// Stop and target must be registered before the entry order they protect.
			if (signal.StopTicks > 0)
				SetStopLoss(label, CalculationMode.Ticks, signal.StopTicks, false);

			if (signal.TargetTicks > 0)
				SetProfitTarget(label, CalculationMode.Ticks, signal.TargetTicks);

			if (signal.Direction == TradeDirection.Long)
				EnterLong(contracts, label);
			else
				EnterShort(contracts, label);

			risk.RecordEntry();

			Log(string.Format("{0} {1} @ market [{2}] stop={3:N0}t target={4:N0}t. {5}",
				signal.Direction, contracts, label, signal.StopTicks, signal.TargetTicks, sizingReason));
		}

		private void CloseCurrentPosition(string reason)
		{
			if (Position.MarketPosition == MarketPosition.Long)
				ExitLong();
			else if (Position.MarketPosition == MarketPosition.Short)
				ExitShort();
		}

		/// <summary>
		/// Pulls newly closed trades out of SystemPerformance and reports them to the risk
		/// manager. Driven from OnBarUpdate rather than OnPositionUpdate so that historical
		/// and realtime processing follow exactly the same path.
		/// </summary>
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
				CloseCurrentPosition("risk-halt");
			}
		}

		private double GetAccountEquity()
		{
			// Account values are only meaningful on a live or sim connection. In a backtest
			// there is no account to query, so equity is modelled as the configured starting
			// balance plus realised profit to date.
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

		#region STRATEGY LOGIC - replace this region

		/// <summary>
		/// Decides whether to open a position on the current bar. Called only when flat,
		/// inside the session window, and with all risk gates passed.
		///
		/// PLACEHOLDER: EMA cross. Replace with the real strategy.
		/// </summary>
		private Signal EvaluateEntry()
		{
			if (CrossAbove(emaFast, emaSlow, 1))
			{
				return new Signal
				{
					Direction = TradeDirection.Long,
					StopTicks = StopLossTicks,
					TargetTicks = ProfitTargetTicks,
					Label = "emaCrossLong"
				};
			}

			if (CrossBelow(emaFast, emaSlow, 1))
			{
				return new Signal
				{
					Direction = TradeDirection.Short,
					StopTicks = StopLossTicks,
					TargetTicks = ProfitTargetTicks,
					Label = "emaCrossShort"
				};
			}

			return Signal.None;
		}

		/// <summary>
		/// Called on every bar while a position is open, after the flatten check.
		/// The fixed stop and target attached at entry are already working; this is where
		/// discretionary exits, trailing logic or breakeven moves belong.
		///
		/// PLACEHOLDER: no active management.
		/// </summary>
		private void ManageOpenPosition()
		{
		}

		#endregion

		private void Log(string message)
		{
			if (!EnableLogging)
				return;

			Print(string.Format("[{0:yyyy-MM-dd HH:mm:ss}] {1}", Time[0], message));
		}

		#region Properties

		[NinjaScriptProperty]
		[Display(Name = "Sizing mode", GroupName = "1. Position Sizing", Order = 0)]
		public PositionSizingMode SizingMode { get; set; }

		[NinjaScriptProperty]
		[Range(0, int.MaxValue)]
		[Display(Name = "Fixed contracts", Description = "Contracts per trade when sizing mode is Fixed.", GroupName = "1. Position Sizing", Order = 1)]
		public int FixedContracts { get; set; }

		[NinjaScriptProperty]
		[Range(0, double.MaxValue)]
		[Display(Name = "Risk per trade ($)", Description = "Dollar risk per trade when sizing mode is FixedRiskPerTrade.", GroupName = "1. Position Sizing", Order = 2)]
		public double RiskPerTradeDollars { get; set; }

		[NinjaScriptProperty]
		[Range(0, 100)]
		[Display(Name = "Risk per trade (%)", Description = "Percent of equity risked per trade when sizing mode is PercentOfEquity.", GroupName = "1. Position Sizing", Order = 3)]
		public double RiskPerTradePercent { get; set; }

		[NinjaScriptProperty]
		[Range(0, int.MaxValue)]
		[Display(Name = "Max contracts", Description = "Hard ceiling applied to every sizing mode.", GroupName = "1. Position Sizing", Order = 4)]
		public int MaxContracts { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Allow minimum 1 contract", Description = "If risk sizing rounds to zero, trade 1 contract anyway instead of skipping.", GroupName = "1. Position Sizing", Order = 5)]
		public bool AllowMinimumOneContract { get; set; }

		[NinjaScriptProperty]
		[Range(0.01, double.MaxValue)]
		[Display(Name = "Tick value ($)", Description = "Dollars per tick. NQ = 5.00, MNQ = 0.50.", GroupName = "1. Position Sizing", Order = 6)]
		public double TickValueDollars { get; set; }

		[NinjaScriptProperty]
		[Range(0, double.MaxValue)]
		[Display(Name = "Backtest starting equity ($)", Description = "Only used by percent-of-equity sizing during backtests, where no live account balance exists. Match this to the Strategy Analyzer's account size.", GroupName = "1. Position Sizing", Order = 7)]
		public double BacktestStartingEquity { get; set; }

		[NinjaScriptProperty]
		[Range(0, 235959)]
		[Display(Name = "Session start (HHmmss)", GroupName = "2. Risk", Order = 0)]
		public int SessionStartTime { get; set; }

		[NinjaScriptProperty]
		[Range(0, 235959)]
		[Display(Name = "Session end (HHmmss)", Description = "No new entries after this time.", GroupName = "2. Risk", Order = 1)]
		public int SessionEndTime { get; set; }

		[NinjaScriptProperty]
		[Range(0, 235959)]
		[Display(Name = "Flatten time (HHmmss)", Description = "Any open position is closed at this time.", GroupName = "2. Risk", Order = 2)]
		public int FlattenTime { get; set; }

		[NinjaScriptProperty]
		[Range(0, double.MaxValue)]
		[Display(Name = "Max daily loss ($)", Description = "Realised loss that halts trading for the day. 0 disables.", GroupName = "2. Risk", Order = 3)]
		public double MaxDailyLossDollars { get; set; }

		[NinjaScriptProperty]
		[Range(0, double.MaxValue)]
		[Display(Name = "Daily profit target ($)", Description = "Realised profit that halts trading, if the stop-for-day switch is on. 0 disables.", GroupName = "2. Risk", Order = 4)]
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
		[Display(Name = "Fast EMA period", GroupName = "3. Placeholder Signal", Order = 0)]
		public int FastPeriod { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "Slow EMA period", GroupName = "3. Placeholder Signal", Order = 1)]
		public int SlowPeriod { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "Stop loss (ticks)", GroupName = "3. Placeholder Signal", Order = 2)]
		public int StopLossTicks { get; set; }

		[NinjaScriptProperty]
		[Range(0, int.MaxValue)]
		[Display(Name = "Profit target (ticks)", Description = "0 means no fixed target.", GroupName = "3. Placeholder Signal", Order = 3)]
		public int ProfitTargetTicks { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Enable logging", Description = "Writes decisions to the NinjaScript Output window.", GroupName = "4. Diagnostics", Order = 0)]
		public bool EnableLogging { get; set; }

		#endregion
	}
}
