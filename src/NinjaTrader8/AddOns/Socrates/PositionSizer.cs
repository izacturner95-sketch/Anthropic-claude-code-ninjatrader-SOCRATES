// Socrates NQ - Position sizing
//
// Plain C# with no NinjaTrader dependencies so the maths can be reasoned about
// (and eventually tested) in isolation from the platform.

using System;

namespace Socrates.Risk
{
	/// <summary>
	/// Contract specification. Defaults are the CME E-mini Nasdaq-100 (NQ):
	/// 0.25 index points per tick, $5.00 per tick, therefore $20.00 per index point.
	/// For the Micro (MNQ) use tickValue 0.50.
	/// </summary>
	public sealed class ContractSpec
	{
		public double TickSize { get; private set; }
		public double TickValue { get; private set; }

		public ContractSpec(double tickSize, double tickValue)
		{
			if (tickSize <= 0)
				throw new ArgumentOutOfRangeException("tickSize", "Tick size must be positive.");
			if (tickValue <= 0)
				throw new ArgumentOutOfRangeException("tickValue", "Tick value must be positive.");

			TickSize = tickSize;
			TickValue = tickValue;
		}

		public static ContractSpec NQ { get { return new ContractSpec(0.25, 5.00); } }
		public static ContractSpec MNQ { get { return new ContractSpec(0.25, 0.50); } }

		public double PointValue { get { return TickValue / TickSize; } }

		public double TicksToDollars(double ticks, int contracts)
		{
			return ticks * TickValue * contracts;
		}
	}

	public sealed class PositionSizerSettings
	{
		/// <summary>Contracts per trade.</summary>
		public int FixedContracts = 1;

		/// <summary>Hard ceiling on contracts. This is the last line of defence against a sizing bug.</summary>
		public int MaxContracts = 1;
	}

	/// <summary>
	/// Size is a fixed contract count under a ceiling.
	///
	/// Risk per trade is set by the stop, not here: the strategy places its stop a fixed
	/// number of ticks from entry, so the dollars at risk are that tick count x tick value
	/// x contracts, known before the order goes out. Dividing a dollar budget by a stop
	/// distance - the usual risk-based sizing - would be deriving the same number twice.
	/// </summary>
	public sealed class PositionSizer
	{
		private readonly ContractSpec spec;

		public PositionSizer(ContractSpec spec)
		{
			if (spec == null)
				throw new ArgumentNullException("spec");

			this.spec = spec;
		}

		/// <summary>
		/// Returns the number of contracts to trade, or 0 if the trade should be skipped.
		/// </summary>
		/// <param name="settings">Sizing configuration.</param>
		/// <param name="stopDistanceTicks">Distance from entry to the protective stop, in ticks. Used only to report the dollars at risk.</param>
		/// <param name="reason">Human-readable explanation, intended for the strategy log.</param>
		public int GetContracts(PositionSizerSettings settings, double stopDistanceTicks, out string reason)
		{
			if (settings == null)
				throw new ArgumentNullException("settings");

			int maxContracts = Math.Max(0, settings.MaxContracts);

			if (maxContracts == 0)
			{
				reason = "MaxContracts is 0 - sizing disabled.";
				return 0;
			}

			int size = Clamp(settings.FixedContracts, 0, maxContracts);

			if (size == 0)
			{
				reason = "Fixed contracts is 0 - no size to trade.";
				return 0;
			}

			reason = string.Format("Size {0} contract(s){1}, risking {2:C} at a {3:N0} tick stop.",
				size,
				size < settings.FixedContracts ? string.Format(" (capped from {0})", settings.FixedContracts) : string.Empty,
				stopDistanceTicks * spec.TickValue * size,
				stopDistanceTicks);

			return size;
		}

		private static int Clamp(int value, int min, int max)
		{
			if (value < min) return min;
			if (value > max) return max;
			return value;
		}
	}
}
