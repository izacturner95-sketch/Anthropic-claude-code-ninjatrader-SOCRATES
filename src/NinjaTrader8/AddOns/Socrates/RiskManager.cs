// Socrates NQ - Risk and session gatekeeping
//
// Plain C# with no NinjaTrader dependencies. The strategy feeds it the current
// trading day, the clock, and the outcome of each closed trade; it answers one
// question: are we allowed to open a new position right now?
//
// Times are integers in HHmmss form to match NinjaScript's ToTime() helper,
// e.g. 9:30:00am is 93000 and 4:00:00pm is 160000.

using System;

namespace Socrates.Risk
{
	/// <summary>
	/// Which part of the trading day entries are allowed in. The presets exist because a
	/// window that wraps midnight is easy to enter wrongly by hand, and getting it wrong
	/// fails silently - every setup outside it is simply refused.
	/// </summary>
	public enum TradingHoursMode
	{
		/// <summary>09:45 to 15:45 ET, flat by 15:55. The cash session only.</summary>
		RegularHours,

		/// <summary>18:00 to 16:45 ET, flat by 16:55. The full Globex session, wrapping midnight.</summary>
		ExtendedHours,

		/// <summary>Use the session start, end and flatten times exactly as entered.</summary>
		Custom
	}

	public sealed class RiskManagerSettings
	{
		/// <summary>Earliest time of day a new entry may be opened.</summary>
		public int SessionStartTime = 93000;

		/// <summary>Latest time of day a new entry may be opened. No new entries after this.</summary>
		public int SessionEndTime = 155000;

		/// <summary>Any open position is closed at this time regardless of strategy logic. Set equal to SessionEndTime to disable the separate flatten window.</summary>
		public int FlattenTime = 155500;

		/// <summary>Realised loss for the trading day, as a positive dollar amount, at which trading halts. Zero disables.</summary>
		public double MaxDailyLossDollars = 1000.0;

		/// <summary>Realised profit for the trading day at which trading halts, if StopForDayOnProfitTarget is true. Zero disables.</summary>
		public double DailyProfitTargetDollars = 0.0;

		public bool StopForDayOnProfitTarget = false;

		/// <summary>Maximum entries per trading day. Zero disables.</summary>
		public int MaxTradesPerDay = 0;

		/// <summary>Consecutive losing trades that trigger a halt for the day. Zero disables.</summary>
		public int MaxConsecutiveLosses = 0;
	}

	/// <summary>
	/// Why entry is currently blocked. Distinguishing these makes the log readable
	/// and lets the strategy react differently to a soft block (outside session)
	/// versus a hard halt (daily loss limit hit).
	/// </summary>
	public enum EntryBlockReason
	{
		None,
		OutsideSession,
		HaltedForDay,
		MaxTradesReached,
		ConnectionDown
	}

	public sealed class RiskManager
	{
		private readonly RiskManagerSettings settings;

		private DateTime currentTradingDay = DateTime.MinValue;
		private double dailyRealisedPnL;
		private int tradesToday;
		private int consecutiveLosses;
		private bool haltedForDay;
		private string haltReason = string.Empty;
		private bool connectionDown;

		public RiskManager(RiskManagerSettings settings)
		{
			if (settings == null)
				throw new ArgumentNullException("settings");

			this.settings = settings;
		}

		public double DailyRealisedPnL { get { return dailyRealisedPnL; } }
		public int TradesToday { get { return tradesToday; } }
		public int ConsecutiveLosses { get { return consecutiveLosses; } }
		public bool IsHaltedForDay { get { return haltedForDay; } }
		public string HaltReason { get { return haltReason; } }
		public DateTime CurrentTradingDay { get { return currentTradingDay; } }

		/// <summary>
		/// Called on every bar with an identifier for the current session. It only has to be
		/// unique per session and change exactly once when the session rolls - the caller
		/// decides what marks a session boundary. Resets the daily counters on a roll and
		/// returns true when it did.
		/// </summary>
		public bool SyncTradingDay(DateTime tradingDay)
		{
			if (tradingDay.Date == currentTradingDay.Date)
				return false;

			currentTradingDay = tradingDay.Date;
			dailyRealisedPnL = 0;
			tradesToday = 0;
			consecutiveLosses = 0;
			haltedForDay = false;
			haltReason = string.Empty;
			return true;
		}

		public void SetConnectionDown(bool isDown)
		{
			connectionDown = isDown;
		}

		/// <summary>Records a completed round-turn trade and applies the daily limits.</summary>
		public void RecordClosedTrade(double profitDollars)
		{
			dailyRealisedPnL += profitDollars;

			if (profitDollars < 0)
				consecutiveLosses++;
			else if (profitDollars > 0)
				consecutiveLosses = 0;

			if (settings.MaxDailyLossDollars > 0 && dailyRealisedPnL <= -Math.Abs(settings.MaxDailyLossDollars))
			{
				Halt(string.Format("Daily loss limit reached ({0:C} <= {1:C}).",
					dailyRealisedPnL, -Math.Abs(settings.MaxDailyLossDollars)));
				return;
			}

			if (settings.StopForDayOnProfitTarget
				&& settings.DailyProfitTargetDollars > 0
				&& dailyRealisedPnL >= settings.DailyProfitTargetDollars)
			{
				Halt(string.Format("Daily profit target reached ({0:C} >= {1:C}).",
					dailyRealisedPnL, settings.DailyProfitTargetDollars));
				return;
			}

			if (settings.MaxConsecutiveLosses > 0 && consecutiveLosses >= settings.MaxConsecutiveLosses)
			{
				Halt(string.Format("{0} consecutive losing trades.", consecutiveLosses));
			}
		}

		/// <summary>Records that an entry was submitted, for the per-day trade cap.</summary>
		public void RecordEntry()
		{
			tradesToday++;
		}

		public void Halt(string reason)
		{
			haltedForDay = true;
			haltReason = reason;
		}

		/// <summary>
		/// The single gate every entry must pass. Returns None when entry is permitted.
		/// </summary>
		public EntryBlockReason CanEnter(int timeOfDay, out string detail)
		{
			if (connectionDown)
			{
				detail = "Data or order connection is down.";
				return EntryBlockReason.ConnectionDown;
			}

			if (haltedForDay)
			{
				detail = haltReason;
				return EntryBlockReason.HaltedForDay;
			}

			if (!IsWithinSession(timeOfDay))
			{
				detail = string.Format("Outside entry window {0:000000}-{1:000000} (now {2:000000}).",
					settings.SessionStartTime, settings.SessionEndTime, timeOfDay);
				return EntryBlockReason.OutsideSession;
			}

			if (settings.MaxTradesPerDay > 0 && tradesToday >= settings.MaxTradesPerDay)
			{
				detail = string.Format("Trade cap reached ({0} of {1}).", tradesToday, settings.MaxTradesPerDay);
				return EntryBlockReason.MaxTradesReached;
			}

			detail = string.Empty;
			return EntryBlockReason.None;
		}

		/// <summary>
		/// True while the strategy should hold no position: from the flatten time until the
		/// next session start.
		///
		/// This was a bare `timeOfDay >= FlattenTime`, which is only right for a window that
		/// ends in the evening and does not reopen. On a session running 18:00 to 16:45 it
		/// was true from 16:55 through to midnight and past it, so the overnight half of the
		/// session was permanently in its own flatten period and could never trade.
		/// </summary>
		public bool ShouldFlatten(int timeOfDay)
		{
			if (settings.FlattenTime == settings.SessionStartTime)
				return false;

			return IsInWindow(timeOfDay, settings.FlattenTime, settings.SessionStartTime);
		}

		/// <summary>
		/// Handles both same-day windows (09:30 to 15:50) and windows that wrap
		/// across midnight (e.g. 18:00 to 16:45 for the full Globex session).
		/// </summary>
		private bool IsWithinSession(int timeOfDay)
		{
			int start = settings.SessionStartTime;
			int end = settings.SessionEndTime;

			if (start == end)
				return true; // No restriction configured.

			if (start < end)
				return timeOfDay >= start && timeOfDay <= end;

			return timeOfDay >= start || timeOfDay <= end;
		}

		/// <summary>Half-open [start, end), wrapping midnight when start is after end.</summary>
		private static bool IsInWindow(int timeOfDay, int start, int end)
		{
			if (start == end)
				return false;

			if (start < end)
				return timeOfDay >= start && timeOfDay < end;

			return timeOfDay >= start || timeOfDay < end;
		}
	}
}
