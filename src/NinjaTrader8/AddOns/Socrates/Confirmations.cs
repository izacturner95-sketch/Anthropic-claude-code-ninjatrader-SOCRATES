// Socrates NQ - Steps 5 and 6: external confirmation
//
// Step 5: "only take trades when the Nasdaq and VIX show inverse agreement", and
//         "the VIX itself should be reacting from an important technical level. If
//          it's in the middle of nowhere, its signal carries less weight."
//
// That last sentence describes a weight, not a gate, so it is modelled as one.
// Each confirmation returns both a pass/fail and a strength in [0, 1]; the strategy
// can require full strength, or accept a weaker signal at reduced size.
//
// Step 6: "check the Magnificent 7 - are the market leaders participating in the
//          same direction as we plan to trade."
//
// Both sources trade regular hours only. Outside 09:30-16:00 ET they cannot confirm
// anything, and the mode setting decides whether that blocks trading or is ignored.

using System;
using Socrates.Strategy;

namespace Socrates.Market
{
	public enum ConfirmationMode
	{
		/// <summary>Not consulted. Use for isolating the effect of a confirmation during testing.</summary>
		Off,

		/// <summary>Direction must agree, but no reaction from a key level is required.</summary>
		Directional,

		/// <summary>Direction must agree and the source must be reacting from a key level.</summary>
		Strict
	}

	public struct ConfirmationResult
	{
		public bool Agrees;
		public bool AtKeyLevel;

		/// <summary>0 to 1. Full strength requires both directional agreement and a reaction from a level.</summary>
		public double Strength;

		public string Detail;

		public static ConfirmationResult Pass(string detail)
		{
			ConfirmationResult r = new ConfirmationResult();
			r.Agrees = true;
			r.AtKeyLevel = true;
			r.Strength = 1.0;
			r.Detail = detail;
			return r;
		}
	}

	public sealed class VixConfirmationSettings
	{
		public ConfirmationMode Mode = ConfirmationMode.Strict;

		/// <summary>Bars within which a VIX rejection of a level still counts as current.</summary>
		public int MaxBarsSinceRejection = 10;

		/// <summary>How close the VIX must be to one of its own levels to count as "at a level", in VIX points.</summary>
		public double KeyLevelTolerance = 0.35;

		/// <summary>Minimum VIX move against the trade direction, in VIX points, for directional agreement.</summary>
		public double MinDirectionalMove = 0.10;

		/// <summary>Strength assigned when direction agrees but the VIX is not reacting from a level.</summary>
		public double WeakSignalStrength = 0.5;

		/// <summary>
		/// How old the last VIX bar may be and still be treated as a live reading.
		///
		/// The VIX trades regular hours only. Without this, its last cash-session close stays
		/// in memory all night and confirms overnight trades against a price from hours ago -
		/// which does not fail, it silently agrees, and that is worse.
		/// </summary>
		public double MaxDataAgeMinutes = 15;
	}

	public sealed class VixConfirmation
	{
		private readonly VixConfirmationSettings settings;
		private readonly MarketAnalyzer analyzer;

		private double lastClose;
		private double referenceClose;
		private int lastBarIndex;
		private DateTime lastUpdateTime = DateTime.MinValue;
		private bool hasData;

		public VixConfirmation(VixConfirmationSettings settings, MarketAnalyzer analyzer)
		{
			if (settings == null)
				throw new ArgumentNullException("settings");
			if (analyzer == null)
				throw new ArgumentNullException("analyzer");

			this.settings = settings;
			this.analyzer = analyzer;
		}

		public MarketAnalyzer Analyzer { get { return analyzer; } }
		public bool HasData { get { return hasData; } }

		/// <summary>
		/// Feed one completed VIX bar. referenceClose is the VIX close a lookback ago, or a
		/// moving average of it - whatever the strategy uses to judge "VIX is selling".
		/// </summary>
		public void Update(int barIndex, DateTime time, double open, double high, double low, double close, double referenceClose, double atr)
		{
			analyzer.Update(barIndex, time, open, high, low, close, atr);
			this.lastClose = close;
			this.referenceClose = referenceClose;
			this.lastBarIndex = barIndex;
			this.lastUpdateTime = time;
			hasData = true;
		}

		/// <summary>
		/// The VIX moves inversely to the Nasdaq, so a long NQ trade wants the VIX falling
		/// and rejecting resistance, and a short wants the VIX rising and holding support.
		/// </summary>
		public ConfirmationResult Evaluate(TradeDirection direction, DateTime now)
		{
			if (settings.Mode == ConfirmationMode.Off)
				return ConfirmationResult.Pass("VIX confirmation disabled.");

			ConfirmationResult result = new ConfirmationResult();

			if (!hasData)
			{
				result.Detail = "No VIX data available for this bar.";
				return result;
			}

			double ageMinutes = (now - lastUpdateTime).TotalMinutes;

			if (settings.MaxDataAgeMinutes > 0 && ageMinutes > settings.MaxDataAgeMinutes)
			{
				result.Detail = string.Format("VIX data is {0:N0} minutes old - the index is closed, so it cannot confirm.", ageMinutes);
				return result;
			}

			double move = lastClose - referenceClose;

			bool directionAgrees = direction == TradeDirection.Long
				? move <= -settings.MinDirectionalMove
				: move >= settings.MinDirectionalMove;

			if (!directionAgrees)
			{
				result.Detail = string.Format("VIX move {0:+0.00;-0.00} does not confirm {1}.", move, direction);
				return result;
			}

			result.Agrees = true;

			// A long wants the VIX to have swept its own highs and failed - a rejection of
			// resistance. A short wants the mirror image at support.
			SweepSide wanted = direction == TradeDirection.Long ? SweepSide.BuySide : SweepSide.SellSide;
			SweepEvent lastSweep = analyzer.Sweeps.LastSweep;

			bool recentRejection = lastSweep.IsValid
				&& lastSweep.Side == wanted
				&& (lastBarIndex - lastSweep.ConfirmBarIndex) <= settings.MaxBarsSinceRejection;

			Level nearest;
			double distance;
			bool nearLevel = analyzer.Levels.TryGetNearest(lastClose, settings.KeyLevelTolerance, 0, out nearest, out distance);

			result.AtKeyLevel = recentRejection || nearLevel;

			if (result.AtKeyLevel)
			{
				result.Strength = 1.0;
				result.Detail = string.Format("VIX {0:+0.00;-0.00} confirms {1}, reacting from {2}.",
					move, direction, recentRejection ? lastSweep.Level.ToString() : nearest.ToString());
			}
			else
			{
				result.Strength = settings.WeakSignalStrength;
				result.Detail = string.Format("VIX {0:+0.00;-0.00} confirms {1}, but is not at a key level - weak signal.",
					move, direction);

				if (settings.Mode == ConfirmationMode.Strict)
					result.Agrees = false;
			}

			return result;
		}
	}

	public sealed class BreadthConfirmationSettings
	{
		public ConfirmationMode Mode = ConfirmationMode.Directional;

		/// <summary>How many of the components must agree with the trade direction.</summary>
		public int MinAligned = 5;

		/// <summary>Components must move at least this percent to count as participating, filtering out flat names.</summary>
		public double MinMovePercent = 0.0;

		/// <summary>Strength assigned when the count clears MinAligned but not by much.</summary>
		public double WeakSignalStrength = 0.6;

		/// <summary>
		/// How old a component's last bar may be and still count as participating. The
		/// leaders trade regular hours, so overnight their closing prices would otherwise
		/// keep voting on trades taken hours later.
		/// </summary>
		public double MaxDataAgeMinutes = 15;

		/// <summary>
		/// When every leader that has traded today has gone stale, treat the step as not
		/// applicable rather than as disagreement. The equity market being shut is not
		/// evidence against a trade.
		///
		/// This deliberately does not cover leaders that have never produced a bar at all.
		/// That is a data problem - a bad symbol, a feed without equity coverage - and
		/// skipping the step silently would hide it. Those still fail.
		/// </summary>
		public bool SkipWhenClosed = true;
	}

	/// <summary>
	/// Step 6. Tracks the market leaders and reports whether they are moving with the
	/// intended trade. Component count is whatever the strategy registers, so the list
	/// is not fixed at seven.
	/// </summary>
	public sealed class BreadthConfirmation
	{
		private readonly BreadthConfirmationSettings settings;
		private readonly string[] names;
		private readonly double[] last;
		private readonly double[] reference;
		private readonly bool[] hasData;
		private readonly DateTime[] updated;

		public BreadthConfirmation(BreadthConfirmationSettings settings, string[] componentNames)
		{
			if (settings == null)
				throw new ArgumentNullException("settings");
			if (componentNames == null || componentNames.Length == 0)
				throw new ArgumentException("At least one component is required.", "componentNames");

			this.settings = settings;
			this.names = componentNames;
			this.last = new double[componentNames.Length];
			this.reference = new double[componentNames.Length];
			this.hasData = new bool[componentNames.Length];
			this.updated = new DateTime[componentNames.Length];
		}

		public int ComponentCount { get { return names.Length; } }

		/// <summary>
		/// Update one component. referenceValue is its session open or its price a lookback
		/// ago, depending on the configured measure.
		/// </summary>
		public void SetComponent(int index, double lastPrice, double referenceValue, DateTime time)
		{
			if (index < 0 || index >= names.Length)
				return;

			if (lastPrice <= 0 || referenceValue <= 0)
				return;

			last[index] = lastPrice;
			reference[index] = referenceValue;
			updated[index] = time;
			hasData[index] = true;
		}

		public void ResetSession()
		{
			for (int i = 0; i < hasData.Length; i++)
				hasData[i] = false;
		}

		public ConfirmationResult Evaluate(TradeDirection direction, DateTime now)
		{
			if (settings.Mode == ConfirmationMode.Off)
				return ConfirmationResult.Pass("Breadth confirmation disabled.");

			ConfirmationResult result = new ConfirmationResult();

			int available = 0;
			int aligned = 0;
			int stale = 0;
			double netPercent = 0;

			for (int i = 0; i < names.Length; i++)
			{
				if (!hasData[i])
					continue;

				if (settings.MaxDataAgeMinutes > 0
					&& (now - updated[i]).TotalMinutes > settings.MaxDataAgeMinutes)
				{
					stale++;
					continue;
				}

				available++;
				double changePercent = ((last[i] - reference[i]) / reference[i]) * 100.0;
				netPercent += changePercent;

				if (Math.Abs(changePercent) < settings.MinMovePercent)
					continue;

				if (direction == TradeDirection.Long && changePercent > 0)
					aligned++;
				else if (direction == TradeDirection.Short && changePercent < 0)
					aligned++;
			}

			if (available == 0)
			{
				// Stale means they traded and then stopped: the market closed. Never having
				// had data means something is wrong with the symbols, and that must not pass.
				if (stale > 0 && settings.SkipWhenClosed)
					return ConfirmationResult.Pass(string.Format("Leaders closed ({0} stale) - step 6 not applicable.", stale));

				result.Detail = stale > 0
					? string.Format("All {0} leaders are stale and skipping is off.", stale)
					: "No leader data available - check that the symbols exist in your feed.";

				return result;
			}

			// Scale the requirement when data for some components is missing, so a single
			// unavailable symbol does not silently block every trade.
			int required = (int)Math.Ceiling(settings.MinAligned * (available / (double)names.Length));
			if (required < 1)
				required = 1;

			double average = netPercent / available;

			if (aligned < required)
			{
				result.Detail = string.Format("Leaders {0}/{1} aligned with {2}, need {3}. Average {4:+0.00;-0.00}%.",
					aligned, available, direction, required, average);
				return result;
			}

			result.Agrees = true;
			result.AtKeyLevel = true;

			// Unanimity is a stronger signal than a bare majority.
			result.Strength = aligned >= available ? 1.0 : settings.WeakSignalStrength;
			result.Detail = string.Format("Leaders {0}/{1} aligned with {2}. Average {3:+0.00;-0.00}%.",
				aligned, available, direction, average);

			return result;
		}
	}
}
