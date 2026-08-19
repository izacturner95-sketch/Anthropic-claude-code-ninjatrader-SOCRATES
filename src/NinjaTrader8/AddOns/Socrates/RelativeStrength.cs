// Socrates NQ - Step 6, alternative form: relative strength
//
// The Magnificent 7 breadth test measured well and cannot be traded live on this
// platform: NinjaTrader carries no US equities, so it only exists here through a file,
// and a file is a snapshot. This asks the same question with instruments the feed does
// have.
//
// The Nasdaq-100 is roughly half Magnificent 7 by weight; the S&P 500 is not. So the
// spread between them - NQ's move against ES's over the same window - is a continuous,
// market-cap-weighted reading of whether big tech is leading the market or lagging it.
// That is what "are the leaders participating" was asking, priced rather than counted.
//
// It is deliberately NOT the same test, and the difference shows up in a falling market.
// With NQ down 0.2% and ES down 0.5%, tech is outperforming and this confirms a long,
// while the leader count - none of them up - refuses it. Which is right is an empirical
// question, which is why this is a mode rather than a replacement.
//
// Both instruments are futures on the same feed with years of history and 23-hour
// sessions, so unlike the leader files this behaves identically in a backtest and live.

using System;
using System.Collections.Generic;
using Socrates.Strategy;

namespace Socrates.Market
{
	public sealed class RelativeStrengthSettings
	{
		public ConfirmationMode Mode = ConfirmationMode.Off;

		/// <summary>Bars over which each instrument's percent move is measured.</summary>
		public int LookbackBars = 12;

		/// <summary>
		/// Absolute floor on the spread, in percentage points. Kept low - it exists to
		/// reject a dead-flat reading, not to be the real test.
		/// </summary>
		public double MinSpreadPercent = 0.02;

		/// <summary>
		/// The real threshold: a fraction of the spread's own recent average size.
		///
		/// A fixed number does not travel. The two indices diverge far more in a volatile
		/// session than a quiet one, so a threshold reasonable at midday is unreachable at
		/// 3am - the same failure step 5's fixed threshold had. Scaling to what the spread
		/// has actually been doing asks the same question at any hour.
		/// </summary>
		public double MinSpreadMultiple = 0.75;

		/// <summary>Bars of spread history used to compute that average.</summary>
		public int VolatilityWindow = 200;

		public double MaxDataAgeMinutes = 15;

		/// <summary>When the comparison series has gone quiet, skip rather than refuse - as steps 5 and 6 both do.</summary>
		public bool SkipWhenQuiet = true;
	}

	/// <summary>
	/// Step 6 as a spread between the traded index and a broader one.
	///
	/// Reads the change in the spread, not its level. The level drifts with valuation and
	/// says nothing; the change over a lookback is the rotation.
	/// </summary>
	public sealed class RelativeStrengthConfirmation
	{
		private readonly RelativeStrengthSettings settings;
		private readonly Queue<double> recent = new Queue<double>();

		private double spread;
		private double leadPercent;
		private double basePercent;
		private double absSum;
		private DateTime lastUpdateTime = DateTime.MinValue;
		private bool hasData;

		private int rejectedNoData;
		private int rejectedStale;
		private int skippedQuiet;
		private int rejectedDirection;
		private int confirmed;

		private int samples;
		private double spreadAbsMin = double.MaxValue;
		private double spreadAbsMax;
		private double spreadAbsSum;
		private double thresholdMin = double.MaxValue;
		private double thresholdMax;
		private double thresholdSum;

		public RelativeStrengthConfirmation(RelativeStrengthSettings settings)
		{
			if (settings == null)
				throw new ArgumentNullException("settings");

			this.settings = settings;
		}

		public bool HasData { get { return hasData; } }
		public int RejectedNoData { get { return rejectedNoData; } }
		public int RejectedStale { get { return rejectedStale; } }
		public int SkippedQuiet { get { return skippedQuiet; } }
		public int RejectedDirection { get { return rejectedDirection; } }
		public int Confirmed { get { return confirmed; } }
		public int Samples { get { return samples; } }

		public double SpreadAbsMin { get { return samples > 0 ? spreadAbsMin : 0; } }
		public double SpreadAbsMax { get { return spreadAbsMax; } }
		public double SpreadAbsMean { get { return samples > 0 ? spreadAbsSum / samples : 0; } }

		public double ThresholdMin { get { return samples > 0 ? thresholdMin : 0; } }
		public double ThresholdMax { get { return thresholdMax; } }
		public double ThresholdMean { get { return samples > 0 ? thresholdSum / samples : 0; } }

		/// <summary>
		/// Feed one bar. Both percentages are the instrument's own move over the same
		/// lookback, so the spread is in percentage points and is comparable across price
		/// levels - which matters, because NQ trades near 23,000 and ES near 6,400 and a
		/// point-based spread would be almost entirely NQ.
		/// </summary>
		public void Update(DateTime time, double leadPercent, double basePercent)
		{
			this.leadPercent = leadPercent;
			this.basePercent = basePercent;
			this.spread = leadPercent - basePercent;
			this.lastUpdateTime = time;
			hasData = true;

			double magnitude = Math.Abs(spread);
			recent.Enqueue(magnitude);
			absSum += magnitude;

			while (recent.Count > Math.Max(10, settings.VolatilityWindow))
				absSum -= recent.Dequeue();
		}

		/// <summary>The threshold currently in force, for reporting as well as testing.</summary>
		private double CurrentThreshold()
		{
			double average = recent.Count > 0 ? absSum / recent.Count : 0;
			return Math.Max(settings.MinSpreadPercent, average * settings.MinSpreadMultiple);
		}

		public ConfirmationResult Evaluate(TradeDirection direction, DateTime now)
		{
			if (settings.Mode == ConfirmationMode.Off)
				return ConfirmationResult.Pass("Relative strength disabled.");

			ConfirmationResult result = new ConfirmationResult();

			if (!hasData)
			{
				rejectedNoData++;
				result.Detail = "No comparison data - check the symbol exists in your feed.";
				return result;
			}

			double ageMinutes = (now - lastUpdateTime).TotalMinutes;

			if (settings.MaxDataAgeMinutes > 0 && ageMinutes > settings.MaxDataAgeMinutes)
			{
				if (settings.SkipWhenQuiet)
				{
					skippedQuiet++;
					return ConfirmationResult.Pass(string.Format(
						"Comparison quiet for {0:N0} minutes - step 6 not applicable.", ageMinutes));
				}

				rejectedStale++;
				result.Detail = string.Format("Comparison data is {0:N0} minutes old.", ageMinutes);
				return result;
			}

			double threshold = CurrentThreshold();

			samples++;
			double magnitude = Math.Abs(spread);
			spreadAbsSum += magnitude;
			thresholdSum += threshold;

			if (magnitude < spreadAbsMin) spreadAbsMin = magnitude;
			if (magnitude > spreadAbsMax) spreadAbsMax = magnitude;
			if (threshold < thresholdMin) thresholdMin = threshold;
			if (threshold > thresholdMax) thresholdMax = threshold;

			// Leadership runs with the trade, not against it: a long wants the index we are
			// trading to be outrunning the broad market, a short wants it lagging. The
			// opposite of step 5's inverse test, and the sign is the whole thing - getting
			// it backwards would produce a filter that confirms precisely the wrong setups
			// and still looks like it is working.
			bool agrees = direction == TradeDirection.Long
				? spread >= threshold
				: spread <= -threshold;

			if (!agrees)
			{
				rejectedDirection++;
				result.Detail = string.Format(
					"Spread {0:+0.000;-0.000}% does not confirm {1} against a {2:N3}% threshold (lead {3:+0.00;-0.00}%, base {4:+0.00;-0.00}%).",
					spread, direction, threshold, leadPercent, basePercent);
				return result;
			}

			confirmed++;
			result.Agrees = true;
			result.AtKeyLevel = true;
			result.Strength = 1.0;
			result.Detail = string.Format(
				"Spread {0:+0.000;-0.000}% confirms {1} (lead {2:+0.00;-0.00}%, base {3:+0.00;-0.00}%, threshold {4:N3}%).",
				spread, direction, leadPercent, basePercent, threshold);

			return result;
		}
	}
}
