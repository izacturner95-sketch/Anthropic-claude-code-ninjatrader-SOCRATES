// Socrates NQ - Order blocks (supply and demand)
//
// An order block is the last opposing candle before a displacement move: the last
// down candle before a sharp rally is where buyers absorbed supply, and price
// returning there is expected to find them again.
//
// Detection is deliberately strict, because "last opposing candle" on its own matches
// something on nearly every bar. Two conditions qualify a move as displacement:
//
//   1. The moving candle's range is at least MinDisplacementAtr x ATR.
//   2. It leaves an imbalance - a three-bar fair value gap where the bar before and
//      the bar after do not overlap. Price moved fast enough to skip a whole range,
//      which is the actual footprint of size going through.
//
// Condition 2 can be relaxed, but leaving it on is what separates order blocks from
// ordinary pullback candles.

using System;
using System.Collections.Generic;

namespace Socrates.Market
{
	public enum OrderBlockKind
	{
		/// <summary>Last down candle before an up move. A demand zone.</summary>
		Bullish,

		/// <summary>Last up candle before a down move. A supply zone.</summary>
		Bearish
	}

	public enum OrderBlockZoneMode
	{
		/// <summary>Zone spans the origin candle's full high to low.</summary>
		FullRange,

		/// <summary>Zone spans the origin candle's open to close only. Tighter, so entries are more precise and misses more frequent.</summary>
		Body
	}

	public sealed class OrderBlock
	{
		public OrderBlockKind Kind;
		public double High;
		public double Low;
		public int FormedBarIndex;
		public DateTime FormedTime;
		public bool HasImbalance;
		public bool IsMitigated;

		/// <summary>
		/// The level object handed to the level book. Held here so that touch counts
		/// accumulated by the sweep detector survive across bars.
		/// </summary>
		public Level CachedLevel;

		public double Mid { get { return (High + Low) / 2.0; } }

		public bool Overlaps(OrderBlock other)
		{
			return Kind == other.Kind && High >= other.Low && Low <= other.High;
		}
	}

	public sealed class OrderBlockSettings
	{
		public bool Enabled = true;

		/// <summary>Require a three-bar fair value gap. Turning this off produces far more, far weaker blocks.</summary>
		public bool RequireImbalance = true;

		/// <summary>Minimum range of the displacement candle, as a multiple of ATR.</summary>
		public double MinDisplacementAtr = 1.0;

		/// <summary>How far back to search for the last opposing candle before giving up.</summary>
		public int MaxLookbackForOrigin = 10;

		public OrderBlockZoneMode ZoneMode = OrderBlockZoneMode.FullRange;

		/// <summary>Retire a block once price closes clean through it. Until then it stays live however many times it is touched.</summary>
		public bool InvalidateOnCloseThrough = true;

		public int MaxActive = 12;
	}

	public sealed class OrderBlockDetector
	{
		private struct BarSnapshot
		{
			public double Open;
			public double High;
			public double Low;
			public double Close;
			public int Index;
			public DateTime Time;

			public bool IsUp { get { return Close > Open; } }
			public bool IsDown { get { return Close < Open; } }
		}

		private readonly OrderBlockSettings settings;
		private readonly List<BarSnapshot> window = new List<BarSnapshot>();
		private readonly List<OrderBlock> active = new List<OrderBlock>();

		public OrderBlockDetector(OrderBlockSettings settings)
		{
			if (settings == null)
				throw new ArgumentNullException("settings");

			this.settings = settings;
		}

		public IList<OrderBlock> Active { get { return active; } }

		/// <summary>Blocks created by the most recent Update.</summary>
		public int LastFormedCount { get; private set; }

		public void Reset()
		{
			window.Clear();
			active.Clear();
			LastFormedCount = 0;
		}

		public void Update(int barIndex, DateTime time, double open, double high, double low, double close, double atr, double zonePadding)
		{
			LastFormedCount = 0;

			if (!settings.Enabled)
				return;

			BarSnapshot bar;
			bar.Open = open;
			bar.High = high;
			bar.Low = low;
			bar.Close = close;
			bar.Index = barIndex;
			bar.Time = time;
			window.Add(bar);

			int capacity = settings.MaxLookbackForOrigin + 4;
			while (window.Count > capacity)
				window.RemoveAt(0);

			RetireMitigated(close);

			if (window.Count < 3)
				return;

			// The candle two back is the displacement candidate; the current bar and the
			// one three back form the outer edges of the potential imbalance.
			int displacementIdx = window.Count - 2;
			BarSnapshot displacement = window[displacementIdx];
			BarSnapshot before = window[window.Count - 3];
			BarSnapshot after = window[window.Count - 1];

			if (displacement.High - displacement.Low < atr * settings.MinDisplacementAtr)
				return;

			bool bullishGap = after.Low > before.High;
			bool bearishGap = after.High < before.Low;

			if (displacement.IsUp && (bullishGap || !settings.RequireImbalance))
				TryCreate(OrderBlockKind.Bullish, displacementIdx, bullishGap, zonePadding, time);
			else if (displacement.IsDown && (bearishGap || !settings.RequireImbalance))
				TryCreate(OrderBlockKind.Bearish, displacementIdx, bearishGap, zonePadding, time);
		}

		/// <summary>
		/// Walks back from the displacement candle to the last candle moving the other way,
		/// which is the block itself.
		/// </summary>
		private void TryCreate(OrderBlockKind kind, int displacementIdx, bool hasImbalance, double zonePadding, DateTime time)
		{
			int limit = Math.Max(0, displacementIdx - settings.MaxLookbackForOrigin);

			for (int i = displacementIdx - 1; i >= limit; i--)
			{
				BarSnapshot candidate = window[i];

				bool isOrigin = kind == OrderBlockKind.Bullish ? candidate.IsDown : candidate.IsUp;
				if (!isOrigin)
					continue;

				double zoneHigh;
				double zoneLow;

				if (settings.ZoneMode == OrderBlockZoneMode.Body)
				{
					zoneHigh = Math.Max(candidate.Open, candidate.Close);
					zoneLow = Math.Min(candidate.Open, candidate.Close);
				}
				else
				{
					zoneHigh = candidate.High;
					zoneLow = candidate.Low;
				}

				zoneHigh += zonePadding;
				zoneLow -= zonePadding;

				OrderBlock block = new OrderBlock();
				block.Kind = kind;
				block.High = zoneHigh;
				block.Low = zoneLow;
				block.FormedBarIndex = candidate.Index;
				block.FormedTime = candidate.Time;
				block.HasImbalance = hasImbalance;

				// A new block covering ground an existing one already holds is the same
				// area rediscovered, so widen the original rather than stacking duplicates.
				for (int j = 0; j < active.Count; j++)
				{
					if (active[j].Overlaps(block))
					{
						active[j].High = Math.Max(active[j].High, block.High);
						active[j].Low = Math.Min(active[j].Low, block.Low);
						active[j].FormedTime = time;
						SyncLevel(active[j]);
						return;
					}
				}

				block.CachedLevel = new Level();
				block.CachedLevel.Kind = kind == OrderBlockKind.Bullish ? LevelKind.Demand : LevelKind.Supply;
				block.CachedLevel.Timeframe = LevelTimeframe.Intraday;
				block.CachedLevel.Created = time;
				block.CachedLevel.Touches = 1;
				SyncLevel(block);

				active.Add(block);
				LastFormedCount++;

				if (active.Count > settings.MaxActive)
					active.RemoveAt(0);

				return;
			}
		}

		private static void SyncLevel(OrderBlock block)
		{
			block.CachedLevel.Price = block.Mid;
			block.CachedLevel.HalfWidth = Math.Max(0, (block.High - block.Low) / 2.0);
		}

		private void RetireMitigated(double close)
		{
			if (!settings.InvalidateOnCloseThrough)
				return;

			for (int i = active.Count - 1; i >= 0; i--)
			{
				OrderBlock block = active[i];

				bool broken = block.Kind == OrderBlockKind.Bullish
					? close < block.Low
					: close > block.High;

				if (broken)
				{
					block.IsMitigated = true;
					active.RemoveAt(i);
				}
			}
		}
	}
}
