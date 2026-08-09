// Socrates NQ - Swing point detection
//
// Market structure is expressed in swing highs and lows, so everything downstream
// (structure shifts, structural levels, retest zones) depends on this file.
//
// A swing high of strength N is a bar whose high exceeds the highs of the N bars
// on either side of it. It therefore cannot be confirmed until N bars have closed
// after it - this lag is real and is not a bug. Smaller N reacts faster and produces
// more noise; larger N finds only major turns.

using System;
using System.Collections.Generic;

namespace Socrates.Market
{
	public enum SwingType
	{
		High,
		Low
	}

	public struct SwingPoint
	{
		public bool IsValid;
		public SwingType Type;
		public double Price;

		/// <summary>Bar index at which the swing actually formed, not the bar that confirmed it.</summary>
		public int BarIndex;

		public DateTime Time;

		public override string ToString()
		{
			return string.Format("{0} {1:N2} @ bar {2}", Type, Price, BarIndex);
		}
	}

	public sealed class SwingDetector
	{
		private struct BarSnapshot
		{
			public double High;
			public double Low;
			public int Index;
			public DateTime Time;
		}

		private readonly int strength;
		private readonly int historyLimit;
		private readonly List<BarSnapshot> window = new List<BarSnapshot>();
		private readonly List<SwingPoint> highs = new List<SwingPoint>();
		private readonly List<SwingPoint> lows = new List<SwingPoint>();

		public SwingDetector(int strength, int historyLimit)
		{
			if (strength < 1)
				throw new ArgumentOutOfRangeException("strength", "Swing strength must be at least 1.");

			this.strength = strength;
			this.historyLimit = Math.Max(10, historyLimit);
		}

		/// <summary>True if the most recent Update confirmed a new swing high.</summary>
		public bool NewHighConfirmed { get; private set; }

		/// <summary>True if the most recent Update confirmed a new swing low.</summary>
		public bool NewLowConfirmed { get; private set; }

		public IList<SwingPoint> Highs { get { return highs; } }
		public IList<SwingPoint> Lows { get { return lows; } }

		public SwingPoint LastHigh
		{
			get { return highs.Count > 0 ? highs[highs.Count - 1] : default(SwingPoint); }
		}

		public SwingPoint LastLow
		{
			get { return lows.Count > 0 ? lows[lows.Count - 1] : default(SwingPoint); }
		}

		public void Reset()
		{
			window.Clear();
			highs.Clear();
			lows.Clear();
			NewHighConfirmed = false;
			NewLowConfirmed = false;
		}

		/// <summary>Feed one completed bar. Call once per bar, in chronological order.</summary>
		public void Update(int barIndex, DateTime time, double high, double low)
		{
			NewHighConfirmed = false;
			NewLowConfirmed = false;

			BarSnapshot snapshot;
			snapshot.High = high;
			snapshot.Low = low;
			snapshot.Index = barIndex;
			snapshot.Time = time;
			window.Add(snapshot);

			int required = (2 * strength) + 1;
			if (window.Count < required)
				return;

			if (window.Count > required)
				window.RemoveAt(0);

			BarSnapshot candidate = window[strength];
			bool isHigh = true;
			bool isLow = true;

			for (int i = 0; i < window.Count; i++)
			{
				if (i == strength)
					continue;

				if (window[i].High >= candidate.High)
					isHigh = false;

				if (window[i].Low <= candidate.Low)
					isLow = false;
			}

			if (isHigh)
			{
				SwingPoint point;
				point.IsValid = true;
				point.Type = SwingType.High;
				point.Price = candidate.High;
				point.BarIndex = candidate.Index;
				point.Time = candidate.Time;
				Append(highs, point);
				NewHighConfirmed = true;
			}

			if (isLow)
			{
				SwingPoint point;
				point.IsValid = true;
				point.Type = SwingType.Low;
				point.Price = candidate.Low;
				point.BarIndex = candidate.Index;
				point.Time = candidate.Time;
				Append(lows, point);
				NewLowConfirmed = true;
			}
		}

		/// <summary>
		/// Most recent confirmed swing high that formed at or before the given bar index.
		/// Used to find the structure a move must break to count as a shift.
		/// </summary>
		public SwingPoint MostRecentHighAtOrBefore(int barIndex)
		{
			for (int i = highs.Count - 1; i >= 0; i--)
			{
				if (highs[i].BarIndex <= barIndex)
					return highs[i];
			}

			return default(SwingPoint);
		}

		public SwingPoint MostRecentLowAtOrBefore(int barIndex)
		{
			for (int i = lows.Count - 1; i >= 0; i--)
			{
				if (lows[i].BarIndex <= barIndex)
					return lows[i];
			}

			return default(SwingPoint);
		}

		/// <summary>
		/// Most recent confirmed swing high that formed strictly after the given bar index.
		/// Used to re-anchor a structure shift to a nearer high once one forms after a sweep.
		/// </summary>
		public SwingPoint MostRecentHighAfter(int barIndex)
		{
			SwingPoint result = default(SwingPoint);

			for (int i = highs.Count - 1; i >= 0; i--)
			{
				if (highs[i].BarIndex > barIndex)
					result = highs[i];
				else
					break;
			}

			return result;
		}

		public SwingPoint MostRecentLowAfter(int barIndex)
		{
			SwingPoint result = default(SwingPoint);

			for (int i = lows.Count - 1; i >= 0; i--)
			{
				if (lows[i].BarIndex > barIndex)
					result = lows[i];
				else
					break;
			}

			return result;
		}

		/// <summary>
		/// Most recent confirmed swing high at least minDistance above the given price.
		///
		/// Scanning newest first means "the previous high" is the one price last turned at,
		/// not the largest in memory. minDistance walks past highs too close to be worth
		/// aiming at - a target two points away is not a target.
		/// </summary>
		public SwingPoint MostRecentHighAbove(double price, double minDistance)
		{
			double threshold = price + Math.Max(0, minDistance);

			for (int i = highs.Count - 1; i >= 0; i--)
			{
				if (highs[i].Price >= threshold)
					return highs[i];
			}

			return default(SwingPoint);
		}

		/// <summary>Most recent confirmed swing low at least minDistance below the given price.</summary>
		public SwingPoint MostRecentLowBelow(double price, double minDistance)
		{
			double threshold = price - Math.Max(0, minDistance);

			for (int i = lows.Count - 1; i >= 0; i--)
			{
				if (lows[i].Price <= threshold)
					return lows[i];
			}

			return default(SwingPoint);
		}

		private void Append(List<SwingPoint> target, SwingPoint point)
		{
			target.Add(point);

			if (target.Count > historyLimit)
				target.RemoveAt(0);
		}
	}
}
