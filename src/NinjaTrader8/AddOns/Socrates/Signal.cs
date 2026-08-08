// Socrates NQ - Signal contract
//
// The seam between "what the market is doing" and "what we do about it".
// Strategy logic produces a Signal; the execution and risk layers consume it
// without knowing anything about how it was derived.

namespace Socrates.Strategy
{
	public enum TradeDirection
	{
		None,
		Long,
		Short
	}

	public struct Signal
	{
		public TradeDirection Direction;

		/// <summary>Protective stop distance from entry, in ticks. Drives both the stop order and risk-based position sizing.</summary>
		public double StopTicks;

		/// <summary>Profit target distance from entry, in ticks. Zero means no fixed target - the exit is managed by strategy logic instead.</summary>
		public double TargetTicks;

		/// <summary>Short label recorded on the entry order, so fills can be traced back to the setup that produced them.</summary>
		public string Label;

		public static Signal None
		{
			get
			{
				Signal s = new Signal();
				s.Direction = TradeDirection.None;
				return s;
			}
		}

		public bool IsEntry
		{
			get { return Direction == TradeDirection.Long || Direction == TradeDirection.Short; }
		}
	}
}
