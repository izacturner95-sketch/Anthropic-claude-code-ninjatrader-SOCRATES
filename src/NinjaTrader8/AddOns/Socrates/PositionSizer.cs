// Socrates NQ - Position sizing
//
// Plain C# with no NinjaTrader dependencies so the maths can be reasoned about
// (and eventually tested) in isolation from the platform.

using System;

namespace Socrates.Risk
{
	public enum PositionSizingMode
	{
		/// <summary>Always trade the same number of contracts.</summary>
		Fixed,

		/// <summary>Size so that a stop-out costs approximately a fixed dollar amount.</summary>
		FixedRiskPerTrade,

		/// <summary>Size so that a stop-out costs approximately a fixed percentage of equity.</summary>
		PercentOfEquity
	}

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
		public PositionSizingMode Mode = PositionSizingMode.Fixed;

		/// <summary>Contract count used when Mode is Fixed. Also the fallback if a risk-based calculation cannot be performed.</summary>
		public int FixedContracts = 1;

		/// <summary>Dollar risk per trade when Mode is FixedRiskPerTrade.</summary>
		public double RiskPerTradeDollars = 200.0;

		/// <summary>Percent of equity risked per trade when Mode is PercentOfEquity. Expressed as a percent, e.g. 0.5 means 0.5%.</summary>
		public double RiskPerTradePercent = 0.5;

		/// <summary>Hard ceiling on contracts, applied to every mode. This is the last line of defence against a sizing bug.</summary>
		public int MaxContracts = 1;

		/// <summary>
		/// When a risk-based size rounds down to zero, trade one contract anyway if true, or skip the trade if false.
		/// False is the safer default: it means the account is too small for the stop being asked for.
		/// </summary>
		public bool AllowMinimumOneContract = false;
	}

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
		/// <param name="stopDistanceTicks">Distance from entry to the protective stop, in ticks. Must be positive for risk-based modes.</param>
		/// <param name="accountEquity">Account equity in dollars. Only consulted for PercentOfEquity.</param>
		/// <param name="reason">Human-readable explanation, intended for the strategy log.</param>
		public int GetContracts(PositionSizerSettings settings, double stopDistanceTicks, double accountEquity, out string reason)
		{
			if (settings == null)
				throw new ArgumentNullException("settings");

			int maxContracts = Math.Max(0, settings.MaxContracts);
			if (maxContracts == 0)
			{
				reason = "MaxContracts is 0 - sizing disabled.";
				return 0;
			}

			if (settings.Mode == PositionSizingMode.Fixed)
			{
				int fixedSize = Clamp(settings.FixedContracts, 0, maxContracts);
				reason = string.Format("Fixed sizing: {0} contract(s).", fixedSize);
				return fixedSize;
			}

			// Both risk-based modes need a real stop distance to divide by.
			if (stopDistanceTicks <= 0)
			{
				reason = "Risk-based sizing requires a positive stop distance; no size calculated.";
				return 0;
			}

			double riskBudget;
			if (settings.Mode == PositionSizingMode.FixedRiskPerTrade)
			{
				riskBudget = settings.RiskPerTradeDollars;
			}
			else
			{
				if (accountEquity <= 0)
				{
					reason = "Percent-of-equity sizing requires a positive account equity; no size calculated.";
					return 0;
				}

				riskBudget = accountEquity * (settings.RiskPerTradePercent / 100.0);
			}

			if (riskBudget <= 0)
			{
				reason = "Risk budget is zero or negative; no size calculated.";
				return 0;
			}

			double riskPerContract = stopDistanceTicks * spec.TickValue;
			int size = (int)Math.Floor(riskBudget / riskPerContract);

			if (size < 1)
			{
				if (!settings.AllowMinimumOneContract)
				{
					reason = string.Format(
						"Risk budget ${0:N2} will not cover one contract at a {1:N0} tick stop (${2:N2} risk). Trade skipped.",
						riskBudget, stopDistanceTicks, riskPerContract);
					return 0;
				}

				size = 1;
			}

			int finalSize = Clamp(size, 0, maxContracts);
			reason = string.Format(
				"Risk sizing: budget ${0:N2} / ${1:N2} per contract ({2:N0} tick stop) = {3} contract(s){4}.",
				riskBudget, riskPerContract, stopDistanceTicks, finalSize,
				finalSize < size ? string.Format(", capped from {0} by MaxContracts", size) : string.Empty);

			return finalSize;
		}

		private static int Clamp(int value, int min, int max)
		{
			if (value < min) return min;
			if (value > max) return max;
			return value;
		}
	}
}
