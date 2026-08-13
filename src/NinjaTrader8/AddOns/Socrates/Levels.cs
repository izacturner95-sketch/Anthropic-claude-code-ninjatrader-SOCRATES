// Socrates NQ - Step 1: market context
//
// "Is price approaching an important area?" becomes: maintain a book of computable
// reference levels, and answer proximity questions against it.
//
// Supply and demand zones are the one item from the spec that has no single accepted
// formula. They are modelled here as swing-derived zones: a confirmed swing extreme
// carries a band around it whose width scales with ATR. A zone touched repeatedly
// accumulates strength, on the reasoning that a level price keeps respecting is the
// one worth trading.

using System;
using System.Collections.Generic;

namespace Socrates.Market
{
	public enum LevelKind
	{
		PriorDayHigh,
		PriorDayLow,
		PriorDayClose,
		PriorWeekHigh,
		PriorWeekLow,
		OvernightHigh,
		OvernightLow,
		OpeningRangeHigh,
		OpeningRangeLow,
		Pivot,
		R1,
		R2,
		R3,
		S1,
		S2,
		S3,
		SwingHigh,
		SwingLow,

		/// <summary>Bearish order block - the last up candle before a down displacement.</summary>
		Supply,

		/// <summary>Bullish order block - the last down candle before an up displacement.</summary>
		Demand
	}

	public enum LevelTimeframe
	{
		Intraday,
		FourHour,
		Daily,
		Weekly
	}

	public sealed class Level
	{
		public LevelKind Kind;
		public LevelTimeframe Timeframe;
		public double Price;

		/// <summary>Half-width of the zone around Price, in points. Zero for a pure line level.</summary>
		public double HalfWidth;

		/// <summary>How many times price has interacted with this level. Higher implies a more meaningful area.</summary>
		public int Touches;

		public DateTime Created;

		public double Upper { get { return Price + HalfWidth; } }
		public double Lower { get { return Price - HalfWidth; } }

		public bool Contains(double price)
		{
			return price >= Lower && price <= Upper;
		}

		public string Name
		{
			get { return string.Format("{0} {1}", Timeframe, Kind); }
		}

		/// <summary>
		/// A reference level the whole market can see - prior day and week extremes, pivots,
		/// the overnight range, the opening range - as opposed to a swing or an order block
		/// the price action happened to leave behind.
		///
		/// The distinction matters for continuations. A break of the prior day's high is an
		/// event; a break of the fourteenth swing high in the book is not, and with a level
		/// every few points something is always being broken.
		/// </summary>
		public bool IsMajor
		{
			get
			{
				return Kind != LevelKind.SwingHigh
					&& Kind != LevelKind.SwingLow
					&& Kind != LevelKind.Supply
					&& Kind != LevelKind.Demand;
			}
		}

		public override string ToString()
		{
			return string.Format("{0} @ {1:N2}", Name, Price);
		}
	}

	/// <summary>
	/// Classic floor-trader pivots, derived from a completed period's high, low and close.
	///
	/// Named FloorPivots rather than Pivots because NinjaTrader's Strategy base class
	/// already exposes a Pivots() indicator method, which would shadow this type
	/// everywhere inside a strategy.
	/// </summary>
	public static class FloorPivots
	{
		public static void Classic(double high, double low, double close,
			out double p, out double r1, out double r2, out double r3,
			out double s1, out double s2, out double s3)
		{
			double range = high - low;
			p = (high + low + close) / 3.0;
			r1 = (2.0 * p) - low;
			s1 = (2.0 * p) - high;
			r2 = p + range;
			s2 = p - range;
			r3 = high + (2.0 * (p - low));
			s3 = low - (2.0 * (high - p));
		}
	}

	/// <summary>
	/// The set of levels currently in play. Session levels are rebuilt when the trading
	/// day rolls; structural (swing) levels accumulate and are pruned by distance and age.
	/// </summary>
	public sealed class LevelBook
	{
		private readonly List<Level> sessionLevels = new List<Level>();
		private readonly List<Level> structuralLevels = new List<Level>();

		// Zones owned elsewhere - currently the order block detector - and re-registered
		// each bar. The book does not manage their lifetime.
		private readonly List<Level> dynamicLevels = new List<Level>();

		private readonly int maxStructuralLevels;

		public LevelBook(int maxStructuralLevels)
		{
			this.maxStructuralLevels = Math.Max(4, maxStructuralLevels);
		}

		/// <summary>
		/// Session levels closer together than this are treated as one area rather than
		/// stacked. Three pivot formulas landing within a point of each other describe one
		/// place on the chart, but as three separate levels they widen the band price has to
		/// be inside to count as sweeping something, and each one can trip the sweep detector
		/// on its own. Set per rebuild, in price units. Zero keeps every level.
		/// </summary>
		public double SessionLevelMinSeparation { get; set; }

		/// <summary>Session levels discarded as duplicates of an area already in the book, since the last clear.</summary>
		public int SessionLevelsMerged { get; private set; }

		public IEnumerable<Level> All
		{
			get
			{
				for (int i = 0; i < sessionLevels.Count; i++)
					yield return sessionLevels[i];

				for (int i = 0; i < structuralLevels.Count; i++)
					yield return structuralLevels[i];

				for (int i = 0; i < dynamicLevels.Count; i++)
					yield return dynamicLevels[i];
			}
		}

		public int Count { get { return sessionLevels.Count + structuralLevels.Count + dynamicLevels.Count; } }

		public void ClearSessionLevels()
		{
			sessionLevels.Clear();
			SessionLevelsMerged = 0;
		}

		public void ClearDynamicLevels()
		{
			dynamicLevels.Clear();
		}

		public void AddDynamicLevel(Level level)
		{
			if (level == null || level.Price <= 0)
				return;

			dynamicLevels.Add(level);
		}

		/// <summary>
		/// Adds a session level. Non-finite or non-positive prices are ignored so that
		/// missing data (an absent prior week, an unformed opening range) simply produces
		/// no level rather than a bogus one at zero.
		/// </summary>
		public void AddSessionLevel(LevelKind kind, LevelTimeframe timeframe, double price, double halfWidth, DateTime time)
		{
			if (double.IsNaN(price) || double.IsInfinity(price) || price <= 0)
				return;

			// Callers add in order of significance - prior day and week before the pivots
			// derived from them - so the level already in the book is the one worth keeping.
			if (SessionLevelMinSeparation > 0)
			{
				for (int i = 0; i < sessionLevels.Count; i++)
				{
					if (Math.Abs(sessionLevels[i].Price - price) < SessionLevelMinSeparation)
					{
						sessionLevels[i].HalfWidth = Math.Max(sessionLevels[i].HalfWidth, halfWidth);
						SessionLevelsMerged++;
						return;
					}
				}
			}

			Level level = new Level();
			level.Kind = kind;
			level.Timeframe = timeframe;
			level.Price = price;
			level.HalfWidth = Math.Max(0, halfWidth);
			level.Created = time;
			level.Touches = 0;
			sessionLevels.Add(level);
		}

		/// <summary>
		/// Adds or reinforces a swing-derived zone. A new swing close to an existing zone
		/// counts as another touch of that zone rather than a separate level.
		/// </summary>
		public void AddOrReinforceStructural(LevelKind kind, double price, double halfWidth, DateTime time, double mergeDistance)
		{
			if (double.IsNaN(price) || double.IsInfinity(price) || price <= 0)
				return;

			for (int i = 0; i < structuralLevels.Count; i++)
			{
				Level existing = structuralLevels[i];

				if (existing.Kind == kind && Math.Abs(existing.Price - price) <= mergeDistance)
				{
					// Drift the zone toward the new extreme and count the interaction.
					existing.Price = (existing.Price + price) / 2.0;
					existing.HalfWidth = Math.Max(existing.HalfWidth, halfWidth);
					existing.Touches++;
					existing.Created = time;
					return;
				}
			}

			Level level = new Level();
			level.Kind = kind;
			level.Timeframe = LevelTimeframe.Intraday;
			level.Price = price;
			level.HalfWidth = Math.Max(0, halfWidth);
			level.Created = time;
			level.Touches = 1;
			structuralLevels.Add(level);

			if (structuralLevels.Count > maxStructuralLevels)
				structuralLevels.RemoveAt(0);
		}

		/// <summary>
		/// Discards structural zones far from current price. Without this the book fills
		/// with levels from prices the market has long since left behind.
		/// </summary>
		public void PruneStructural(double currentPrice, double maxDistance)
		{
			for (int i = structuralLevels.Count - 1; i >= 0; i--)
			{
				if (Math.Abs(structuralLevels[i].Price - currentPrice) > maxDistance)
					structuralLevels.RemoveAt(i);
			}
		}

		/// <summary>
		/// Nearest level to a price within maxDistance. Returns false when price is not
		/// near anything meaningful, which is the Step 1 "no context, no trade" case.
		/// </summary>
		public bool TryGetNearest(double price, double maxDistance, int minTouches, out Level nearest, out double distance)
		{
			nearest = null;
			distance = double.MaxValue;

			foreach (Level level in All)
			{
				if (level.Touches > 0 && level.Touches < minTouches)
					continue;

				double gap = Math.Abs(level.Price - price);

				if (gap < distance && gap <= maxDistance)
				{
					distance = gap;
					nearest = level;
				}
			}

			return nearest != null;
		}

		/// <summary>Levels sitting above the given price, nearest first. These are the buy-side liquidity targets.</summary>
		public List<Level> Above(double price, double maxDistance)
		{
			List<Level> result = new List<Level>();

			foreach (Level level in All)
			{
				if (level.Price > price && (level.Price - price) <= maxDistance)
					result.Add(level);
			}

			result.Sort(delegate(Level a, Level b) { return a.Price.CompareTo(b.Price); });
			return result;
		}

		/// <summary>Levels sitting below the given price, nearest first. These are the sell-side liquidity targets.</summary>
		public List<Level> Below(double price, double maxDistance)
		{
			List<Level> result = new List<Level>();

			foreach (Level level in All)
			{
				if (level.Price < price && (price - level.Price) <= maxDistance)
					result.Add(level);
			}

			result.Sort(delegate(Level a, Level b) { return b.Price.CompareTo(a.Price); });
			return result;
		}
	}
}
