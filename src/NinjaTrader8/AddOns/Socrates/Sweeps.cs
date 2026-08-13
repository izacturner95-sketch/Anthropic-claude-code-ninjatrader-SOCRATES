// Socrates NQ - Step 2: liquidity
//
// "Don't chase breakouts. Wait for price to move beyond the nearest pivot zone."
//
// A sweep is modelled in two parts, which is what separates it from a breakout:
//   1. Penetration - price trades beyond a level by at least a minimum distance.
//      A one-tick poke is noise, not a raid on liquidity.
//   2. Reclaim - price closes back on the original side of that level within a
//      limited number of bars.
//
// Part 2 is the whole point. Until price comes back, a move beyond a level is just
// a breakout, and the spec explicitly says not to chase those. If the reclaim never
// arrives within the window, the candidate is discarded as a genuine break.

using System;

namespace Socrates.Market
{
	public enum SweepSide
	{
		/// <summary>Highs were taken out and then reclaimed. Liquidity above the market was consumed, so the resulting setup is bearish.</summary>
		BuySide,

		/// <summary>Lows were taken out and then reclaimed. Liquidity below the market was consumed, so the resulting setup is bullish.</summary>
		SellSide
	}

	public struct SweepEvent
	{
		public bool IsValid;
		public SweepSide Side;

		/// <summary>The level whose liquidity was taken.</summary>
		public Level Level;

		/// <summary>Extreme price reached beyond the level - the high of a buy-side sweep, the low of a sell-side sweep. This is where the protective stop belongs.</summary>
		public double ExtremePrice;

		public int ExtremeBarIndex;

		/// <summary>Bar on which price closed back inside, confirming the sweep.</summary>
		public int ConfirmBarIndex;

		public DateTime ConfirmTime;

		/// <summary>How far beyond the level price reached, in points.</summary>
		public double PenetrationPoints;

		/// <summary>
		/// True when price went through the level and stayed through it, rather than
		/// reclaiming. The level is expected to flip role and hold on the retest, so the
		/// trade runs with the break instead of against it.
		/// </summary>
		public bool IsContinuation;
	}

	public sealed class SweepSettings
	{
		/// <summary>Minimum distance beyond a level, as a multiple of ATR, for a penetration to count. Guards against noise pokes.</summary>
		public double MinPenetrationAtr = 0.10;

		/// <summary>Absolute floor on penetration distance, in points, applied alongside the ATR test.</summary>
		public double MinPenetrationPoints = 1.0;

		/// <summary>Maximum bars between penetration and reclaim. Beyond this the move is treated as a real break, not a sweep.</summary>
		public int MaxBarsToReclaim = 6;

		/// <summary>How far from price to consider levels, as a multiple of ATR.</summary>
		public double LevelSearchAtr = 3.0;

		/// <summary>
		/// Report a candidate that never reclaimed as a continuation break rather than
		/// discarding it.
		///
		/// These were already being detected and thrown away - "never came back, this was a
		/// genuine break, so stand aside". That is the correct call for a reversal strategy
		/// and it also means every break through a level was going in the bin unexamined.
		/// </summary>
		public bool EmitContinuations = false;

		/// <summary>
		/// Only report a continuation when the level broken is a major reference - prior day
		/// or week, a pivot, the overnight or opening range - rather than any swing or order
		/// block in the book.
		///
		/// Without it, 3,011 breaks were reported over 21,590 bars, one every seven, because
		/// with levels every few points something is always breaking. A break has to be an
		/// event for the retest of it to mean anything.
		/// </summary>
		public bool ContinuationsOnMajorLevelsOnly = true;
	}

	public sealed class SweepDetector
	{
		private struct Candidate
		{
			public bool Active;
			public Level Level;
			public double Extreme;
			public int ExtremeBarIndex;
			public int StartBarIndex;
		}

		private readonly SweepSettings settings;
		private Candidate buySide;
		private Candidate sellSide;
		private int ambiguousBars;
		private int continuationsFound;

		public SweepDetector(SweepSettings settings)
		{
			if (settings == null)
				throw new ArgumentNullException("settings");

			this.settings = settings;
		}

		public SweepEvent LastSweep { get; private set; }

		/// <summary>Bars where both sides reclaimed at once and one had to be chosen over the other.</summary>
		public int AmbiguousBars { get { return ambiguousBars; } }

		/// <summary>Breaks reported as continuations rather than discarded.</summary>
		public int ContinuationsFound { get { return continuationsFound; } }

		public void Reset()
		{
			buySide = default(Candidate);
			sellSide = default(Candidate);
			LastSweep = default(SweepEvent);
		}

		/// <summary>
		/// Feed one completed bar. Returns a confirmed sweep, or an event with IsValid false.
		/// </summary>
		public SweepEvent Update(int barIndex, DateTime time, double high, double low, double close, double atr, LevelBook levels)
		{
			SweepEvent buyResult = default(SweepEvent);
			SweepEvent sellResult = default(SweepEvent);
			SweepEvent buyBreak = default(SweepEvent);
			SweepEvent sellBreak = default(SweepEvent);

			double minPenetration = Math.Max(settings.MinPenetrationPoints, atr * settings.MinPenetrationAtr);
			double searchDistance = Math.Max(atr * settings.LevelSearchAtr, minPenetration * 4.0);

			// --- Buy-side: price pushed above a level ---
			if (!buySide.Active)
			{
				// When several levels were exceeded on one bar, the highest of them is the
				// meaningful one: it is the last pool of liquidity price reached for.
				Level taken = null;

				foreach (Level level in levels.All)
				{
					if (high <= level.Upper + minPenetration)
						continue;

					if (high - level.Price > searchDistance)
						continue;

					if (taken == null || level.Price > taken.Price)
						taken = level;
				}

				if (taken != null)
				{
					buySide.Active = true;
					buySide.Level = taken;
					buySide.Extreme = high;
					buySide.ExtremeBarIndex = barIndex;
					buySide.StartBarIndex = barIndex;
				}
			}
			else
			{
				if (high > buySide.Extreme)
				{
					buySide.Extreme = high;
					buySide.ExtremeBarIndex = barIndex;
				}

				if (close < buySide.Level.Price)
				{
					buyResult.IsValid = true;
					buyResult.Side = SweepSide.BuySide;
					buyResult.Level = buySide.Level;
					buyResult.ExtremePrice = buySide.Extreme;
					buyResult.ExtremeBarIndex = buySide.ExtremeBarIndex;
					buyResult.ConfirmBarIndex = barIndex;
					buyResult.ConfirmTime = time;
					buyResult.PenetrationPoints = buySide.Extreme - buySide.Level.Price;

					buySide = default(Candidate);
				}
				else if (barIndex - buySide.StartBarIndex >= settings.MaxBarsToReclaim)
				{
					// Never came back: a genuine break upward. The close test makes sure price
					// is still holding above the level rather than drifting back into it.
					if (settings.EmitContinuations && close > buySide.Level.Price
						&& (!settings.ContinuationsOnMajorLevelsOnly || buySide.Level.IsMajor))
					{
						buyBreak.IsValid = true;
						buyBreak.IsContinuation = true;
						buyBreak.Side = SweepSide.BuySide;
						buyBreak.Level = buySide.Level;
						buyBreak.ExtremePrice = buySide.Extreme;
						buyBreak.ExtremeBarIndex = buySide.ExtremeBarIndex;
						buyBreak.ConfirmBarIndex = barIndex;
						buyBreak.ConfirmTime = time;
						buyBreak.PenetrationPoints = buySide.Extreme - buySide.Level.Price;
					}

					buySide = default(Candidate);
				}
			}

			// --- Sell-side: price pushed below a level ---
			if (!sellSide.Active)
			{
				Level taken = null;

				foreach (Level level in levels.All)
				{
					if (low >= level.Lower - minPenetration)
						continue;

					if (level.Price - low > searchDistance)
						continue;

					if (taken == null || level.Price < taken.Price)
						taken = level;
				}

				if (taken != null)
				{
					sellSide.Active = true;
					sellSide.Level = taken;
					sellSide.Extreme = low;
					sellSide.ExtremeBarIndex = barIndex;
					sellSide.StartBarIndex = barIndex;
				}
			}
			else
			{
				if (low < sellSide.Extreme)
				{
					sellSide.Extreme = low;
					sellSide.ExtremeBarIndex = barIndex;
				}

				if (close > sellSide.Level.Price)
				{
					sellResult.IsValid = true;
					sellResult.Side = SweepSide.SellSide;
					sellResult.Level = sellSide.Level;
					sellResult.ExtremePrice = sellSide.Extreme;
					sellResult.ExtremeBarIndex = sellSide.ExtremeBarIndex;
					sellResult.ConfirmBarIndex = barIndex;
					sellResult.ConfirmTime = time;
					sellResult.PenetrationPoints = sellSide.Level.Price - sellSide.Extreme;

					sellSide = default(Candidate);
				}
				else if (barIndex - sellSide.StartBarIndex >= settings.MaxBarsToReclaim)
				{
					if (settings.EmitContinuations && close < sellSide.Level.Price
						&& (!settings.ContinuationsOnMajorLevelsOnly || sellSide.Level.IsMajor))
					{
						sellBreak.IsValid = true;
						sellBreak.IsContinuation = true;
						sellBreak.Side = SweepSide.SellSide;
						sellBreak.Level = sellSide.Level;
						sellBreak.ExtremePrice = sellSide.Extreme;
						sellBreak.ExtremeBarIndex = sellSide.ExtremeBarIndex;
						sellBreak.ConfirmBarIndex = barIndex;
						sellBreak.ConfirmTime = time;
						sellBreak.PenetrationPoints = sellSide.Level.Price - sellSide.Extreme;
					}

					sellSide = default(Candidate);
				}
			}

			// A bar that reclaims both sides is genuinely ambiguous. This used to resolve by
			// evaluation order - buy-side was written first and sell-side only filled in if
			// nothing was there - which is a standing bias toward short setups for no reason
			// anyone chose. The deeper raid is the one that took more liquidity, so it wins.
			SweepEvent result;

			if (buyResult.IsValid && sellResult.IsValid)
			{
				result = buyResult.PenetrationPoints >= sellResult.PenetrationPoints ? buyResult : sellResult;
				ambiguousBars++;
			}
			else if (buyResult.IsValid || sellResult.IsValid)
			{
				result = buyResult.IsValid ? buyResult : sellResult;
			}
			else
			{
				// Continuations only get a look in when no reclaim happened anywhere on this
				// bar. A reversal is the more specific event and keeps priority.
				if (buyBreak.IsValid && sellBreak.IsValid)
					result = buyBreak.PenetrationPoints >= sellBreak.PenetrationPoints ? buyBreak : sellBreak;
				else
					result = buyBreak.IsValid ? buyBreak : sellBreak;

				if (result.IsValid)
					continuationsFound++;
			}

			if (result.IsValid)
			{
				result.Level.Touches++;
				LastSweep = result;
			}

			return result;
		}
	}
}
