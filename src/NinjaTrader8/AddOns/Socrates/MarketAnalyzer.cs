// Socrates NQ - Per-instrument analysis
//
// Bundles swing detection, the level book and sweep detection for a single price
// series. The NQ gets one of these; the VIX gets its own, which is what allows the
// same "reacting from an important technical level" test to be applied to both.

using System;

namespace Socrates.Market
{
	public sealed class MarketAnalyzerSettings
	{
		/// <summary>Bars either side required to confirm a swing point.</summary>
		public int SwingStrength = 3;

		/// <summary>Half-width of a swing-derived zone, as a multiple of ATR.</summary>
		public double ZoneHalfWidthAtr = 0.25;

		/// <summary>Swings closer together than this (as a multiple of ATR) are merged into one zone.</summary>
		public double ZoneMergeAtr = 0.5;

		/// <summary>Structural zones further than this many ATRs from price are discarded.</summary>
		public double PruneDistanceAtr = 12.0;

		public int MaxStructuralLevels = 24;

		public SweepSettings Sweep = new SweepSettings();
	}

	public sealed class MarketAnalyzer
	{
		private readonly MarketAnalyzerSettings settings;

		public MarketAnalyzer(MarketAnalyzerSettings settings)
		{
			if (settings == null)
				throw new ArgumentNullException("settings");

			this.settings = settings;
			Swings = new SwingDetector(settings.SwingStrength, 100);
			Levels = new LevelBook(settings.MaxStructuralLevels);
			Sweeps = new SweepDetector(settings.Sweep);
		}

		public SwingDetector Swings { get; private set; }
		public LevelBook Levels { get; private set; }
		public SweepDetector Sweeps { get; private set; }

		/// <summary>Sweep confirmed on the most recent Update, if any.</summary>
		public SweepEvent LastUpdateSweep { get; private set; }

		public double LastAtr { get; private set; }

		/// <summary>
		/// Feed one completed bar. Session levels must already have been refreshed for the
		/// day by the caller; this handles the structural levels that emerge from price action.
		/// </summary>
		public void Update(int barIndex, DateTime time, double high, double low, double close, double atr)
		{
			LastAtr = atr > 0 ? atr : LastAtr;
			double effectiveAtr = LastAtr > 0 ? LastAtr : Math.Max(0.25, (high - low));

			Swings.Update(barIndex, time, high, low);

			double halfWidth = effectiveAtr * settings.ZoneHalfWidthAtr;
			double mergeDistance = effectiveAtr * settings.ZoneMergeAtr;

			if (Swings.NewHighConfirmed)
				Levels.AddOrReinforceStructural(LevelKind.SwingHigh, Swings.LastHigh.Price, halfWidth, time, mergeDistance);

			if (Swings.NewLowConfirmed)
				Levels.AddOrReinforceStructural(LevelKind.SwingLow, Swings.LastLow.Price, halfWidth, time, mergeDistance);

			Levels.PruneStructural(close, effectiveAtr * settings.PruneDistanceAtr);

			LastUpdateSweep = Sweeps.Update(barIndex, time, high, low, close, effectiveAtr, Levels);
		}

		public void ResetForNewSession()
		{
			Levels.ClearSessionLevels();
			Sweeps.Reset();
		}
	}
}
