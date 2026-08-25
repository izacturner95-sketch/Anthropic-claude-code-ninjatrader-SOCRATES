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

		/// <summary>
		/// Absolute floor on the VIX move needed for directional agreement, in VIX points.
		/// Kept low - it exists to reject a dead-flat reading, not to be the real test.
		/// </summary>
		public double MinDirectionalMove = 0.02;

		/// <summary>
		/// The real threshold: the move must be at least this fraction of the VIX's own ATR.
		///
		/// A fixed 0.10 points was the original test and it does not travel. The VIX moves
		/// very differently at 12 than at 30, and on a Globex chart it barely moves at all
		/// overnight - so a fixed number that is reasonable during the cash session silently
		/// becomes an impossible one at 3am, and the step refuses everything. Scaling to the
		/// source's own volatility asks the same question at any hour.
		/// </summary>
		public double MinDirectionalMoveAtr = 0.5;

		/// <summary>
		/// Alternative scaling: the move must be at least this percent of the VIX's own
		/// level. 0 disables it. Where the ATR term scales to how much the source has been
		/// moving bar to bar - and collapses when the bars are quiet - this scales to where
		/// the index is: a VIX at 30 moves more in points than a VIX at 12 for the same
		/// amount of fear, so half a percent means the same thing in both regimes. On short
		/// bars, where the ATR term degenerates to the floor, this is the term that still
		/// means something.
		/// </summary>
		public double MinDirectionalMovePercent = 0;

		/// <summary>Strength assigned when direction agrees but the VIX is not reacting from a level.</summary>
		public double WeakSignalStrength = 0.5;

		/// <summary>
		/// How old the last VIX bar may be and still be treated as a live reading.
		///
		/// Without this, the last close stays in memory and confirms trades against a price
		/// from hours ago - which does not fail, it silently agrees, and that is worse.
		///
		/// Sized for the source. Three bar periods is right for the ^VIX index, which either
		/// publishes continuously or is shut. It is far too tight for VX futures overnight:
		/// the contract is open from 17:00 to 16:00 CT but a bar only forms when someone
		/// trades, and at 1am VX can go well over an hour without a print. Open and quiet is
		/// not the same as closed, and this number cannot tell them apart.
		/// </summary>
		public double MaxDataAgeMinutes = 15;

		/// <summary>
		/// When the source has gone quiet, skip the step rather than fail the setup.
		///
		/// The same judgement step 6 makes about a shut equity market, for the same reason: a
		/// source with nothing to say is not evidence against a trade. Rejecting on staleness
		/// turns every thin overnight hour into a blanket refusal, which is not a filter - it
		/// is the step deciding the strategy may not trade at night.
		///
		/// Deliberately narrow, as it is for step 6. It applies once the VIX has produced a
		/// bar at some point and then stopped. A source that has never produced one is a feed
		/// or symbol problem and still fails loudly, because quietly disabling a confirmation
		/// over a bad symbol is exactly the silent failure worth avoiding.
		/// </summary>
		public bool SkipWhenQuiet = true;
	}

	public sealed class VixConfirmation
	{
		private readonly VixConfirmationSettings settings;
		private readonly MarketAnalyzer analyzer;

		private double lastClose;
		private double referenceClose;
		private double lastAtr;
		private int lastBarIndex;
		private DateTime lastUpdateTime = DateTime.MinValue;
		private bool hasData;

		// Why the step said no. Four very different problems - a series that never loaded, a
		// source that has closed for the night, a move that was real but too small, and a
		// move that was big enough but not from a level - all previously arrived as one
		// number in the summary, which is not enough to act on.
		private int rejectedNoData;
		private int rejectedStale;
		private int skippedQuiet;
		private int rejectedDirection;
		private int rejectedNotAtLevel;
		private int confirmed;

		private int moveSamples;
		private double moveAbsMin = double.MaxValue;
		private double moveAbsMax;
		private double moveAbsSum;

		// The threshold varies bar to bar once it is scaled to ATR, so reporting only the
		// last one says nothing about the run. Its spread against the move spread is what
		// shows whether the gate is filtering or just waved through.
		private double thresholdMin = double.MaxValue;
		private double thresholdMax;
		private double thresholdSum;

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
			this.lastAtr = atr;
			this.lastBarIndex = barIndex;
			this.lastUpdateTime = time;
			hasData = true;
		}

		public int RejectedNoData { get { return rejectedNoData; } }
		public int RejectedStale { get { return rejectedStale; } }

		/// <summary>Setups the step stood aside on because the source had gone quiet, rather than refusing them.</summary>
		public int SkippedQuiet { get { return skippedQuiet; } }
		public int RejectedDirection { get { return rejectedDirection; } }
		public int RejectedNotAtLevel { get { return rejectedNotAtLevel; } }
		public int Confirmed { get { return confirmed; } }

		/// <summary>Evaluations where a move was actually measured, i.e. data was present and current.</summary>
		public int MoveSamples { get { return moveSamples; } }

		public double MoveAbsMin { get { return moveSamples > 0 ? moveAbsMin : 0; } }
		public double MoveAbsMax { get { return moveAbsMax; } }
		public double MoveAbsMean { get { return moveSamples > 0 ? moveAbsSum / moveSamples : 0; } }

		public double ThresholdMin { get { return moveSamples > 0 ? thresholdMin : 0; } }
		public double ThresholdMax { get { return thresholdMax; } }
		public double ThresholdMean { get { return moveSamples > 0 ? thresholdSum / moveSamples : 0; } }

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
				rejectedNoData++;
				result.Detail = "No VIX data available for this bar.";
				return result;
			}

			double ageMinutes = (now - lastUpdateTime).TotalMinutes;

			if (settings.MaxDataAgeMinutes > 0 && ageMinutes > settings.MaxDataAgeMinutes)
			{
				// hasData is already true here, so the source did trade at some point and has
				// since gone quiet. That is a source with nothing to say, not a source
				// disagreeing, and the two deserve different answers.
				if (settings.SkipWhenQuiet)
				{
					skippedQuiet++;
					return ConfirmationResult.Pass(string.Format(
						"VIX quiet for {0:N0} minutes - step 5 not applicable.", ageMinutes));
				}

				rejectedStale++;
				result.Detail = string.Format(
					"VIX data is {0:N0} minutes old, over the {1:N0} minute limit, and skipping is off.",
					ageMinutes, settings.MaxDataAgeMinutes);
				return result;
			}

			double move = lastClose - referenceClose;

			// Scaled to the VIX's own volatility, with an absolute floor. Overnight the VIX
			// hardly moves, and a threshold set for the cash session refuses everything.
			double threshold = Math.Max(settings.MinDirectionalMove, lastAtr * settings.MinDirectionalMoveAtr);

			// The level-scaled term, where it is in use. All three compose as a max, so each
			// can be zeroed independently and the strictest active one decides.
			if (settings.MinDirectionalMovePercent > 0)
				threshold = Math.Max(threshold, lastClose * settings.MinDirectionalMovePercent / 100.0);

			moveSamples++;
			double moveAbs = Math.Abs(move);
			moveAbsSum += moveAbs;
			thresholdSum += threshold;

			if (moveAbs < moveAbsMin)
				moveAbsMin = moveAbs;

			if (moveAbs > moveAbsMax)
				moveAbsMax = moveAbs;

			if (threshold < thresholdMin)
				thresholdMin = threshold;

			if (threshold > thresholdMax)
				thresholdMax = threshold;

			bool directionAgrees = direction == TradeDirection.Long
				? move <= -threshold
				: move >= threshold;

			if (!directionAgrees)
			{
				rejectedDirection++;
				result.Detail = string.Format("VIX move {0:+0.00;-0.00} does not confirm {1} against a {2:N2} threshold.",
					move, direction, threshold);
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
				{
					rejectedNotAtLevel++;
					result.Agrees = false;
					return result;
				}
			}

			confirmed++;
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

		// Same reasoning as the VIX counters: a step that turns down everything it looks at
		// is either genuinely selective or mis-calibrated, and one count cannot tell those
		// apart. How many leaders were available, how many agreed, and how many were needed
		// is what separates them.
		private int evaluations;
		private int confirmedCount;
		private int rejectedNotAligned;
		private int rejectedNoDataCount;
		private int alignedSum;
		private int availableSum;
		private int requiredSum;

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

		/// <summary>Setups where the leaders were open and the step actually ran.</summary>
		public int Evaluations { get { return evaluations; } }

		public int Confirmed { get { return confirmedCount; } }
		public int RejectedNotAligned { get { return rejectedNotAligned; } }
		public int RejectedNoData { get { return rejectedNoDataCount; } }

		public double MeanAligned { get { return evaluations > 0 ? alignedSum / (double)evaluations : 0; } }
		public double MeanAvailable { get { return evaluations > 0 ? availableSum / (double)evaluations : 0; } }
		public double MeanRequired { get { return evaluations > 0 ? requiredSum / (double)evaluations : 0; } }

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

				rejectedNoDataCount++;

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

			evaluations++;
			alignedSum += aligned;
			availableSum += available;
			requiredSum += required;

			if (aligned < required)
			{
				rejectedNotAligned++;

				result.Detail = string.Format("Leaders {0}/{1} aligned with {2}, need {3}. Average {4:+0.00;-0.00}%.",
					aligned, available, direction, required, average);
				return result;
			}

			result.Agrees = true;
			result.AtKeyLevel = true;
			confirmedCount++;

			// Unanimity is a stronger signal than a bare majority.
			result.Strength = aligned >= available ? 1.0 : settings.WeakSignalStrength;
			result.Detail = string.Format("Leaders {0}/{1} aligned with {2}. Average {3:+0.00;-0.00}%.",
				aligned, available, direction, average);

			return result;
		}
	}
}
