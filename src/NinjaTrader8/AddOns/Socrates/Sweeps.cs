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

		public SweepDetector(SweepSettings settings)
		{
			if (settings == null)
				throw new ArgumentNullException("settings");

			this.settings = settings;
		}

		public SweepEvent LastSweep { get; private set; }

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
			SweepEvent result = default(SweepEvent);

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
					result.IsValid = true;
					result.Side = SweepSide.BuySide;
					result.Level = buySide.Level;
					result.ExtremePrice = buySide.Extreme;
					result.ExtremeBarIndex = buySide.ExtremeBarIndex;
					result.ConfirmBarIndex = barIndex;
					result.ConfirmTime = time;
					result.PenetrationPoints = buySide.Extreme - buySide.Level.Price;

					buySide.Level.Touches++;
					buySide = default(Candidate);
				}
				else if (barIndex - buySide.StartBarIndex >= settings.MaxBarsToReclaim)
				{
					// Never came back. This was a genuine break, so stand aside.
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
					// A bar that reclaims both sides is ambiguous; the buy-side result wins
					// only if nothing has been recorded yet, otherwise this one is skipped.
					if (!result.IsValid)
					{
						result.IsValid = true;
						result.Side = SweepSide.SellSide;
						result.Level = sellSide.Level;
						result.ExtremePrice = sellSide.Extreme;
						result.ExtremeBarIndex = sellSide.ExtremeBarIndex;
						result.ConfirmBarIndex = barIndex;
						result.ConfirmTime = time;
						result.PenetrationPoints = sellSide.Level.Price - sellSide.Extreme;

						sellSide.Level.Touches++;
					}

					sellSide = default(Candidate);
				}
				else if (barIndex - sellSide.StartBarIndex >= settings.MaxBarsToReclaim)
				{
					sellSide = default(Candidate);
				}
			}

			if (result.IsValid)
				LastSweep = result;

			return result;
		}
	}
}
