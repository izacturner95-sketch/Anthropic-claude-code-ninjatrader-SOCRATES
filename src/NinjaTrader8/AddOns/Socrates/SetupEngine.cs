// Socrates NQ - Steps 3 and 4: structure shift, then retest
//
// Step 3: "after liquidity is taken, we need proof the move is failing. Proof comes
//          from a shift in market structure. Without a structure shift - no trade."
// Step 4: "trade the retest, do not trade during the sweep."
//
// Both are enforced by sequence, not by scoring. A setup advances Idle -> AwaitingShift
// -> AwaitingRetest -> entry, and any stage that is not reached within its bar budget
// resets the whole setup. A structure shift with no preceding sweep is ignored, and a
// retest with no preceding structure shift is ignored. That is what makes "everything
// must align" a property of the code rather than a hope.

using System;
using Socrates.Strategy;

namespace Socrates.Market
{
	public enum SetupState
	{
		Idle,
		AwaitingStructureShift,
		AwaitingRetest
	}

	public enum RetestZoneMode
	{
		/// <summary>The structure level broken by the shift, now expected to flip roles.</summary>
		BrokenStructure,

		/// <summary>A retracement of the leg from the sweep extreme to the structure break.</summary>
		FibRetrace,

		/// <summary>The level whose liquidity was originally swept.</summary>
		SweptLevel
	}

	public sealed class SetupEngineSettings
	{
		/// <summary>Bars allowed between a confirmed sweep and a structure shift before the setup is abandoned.</summary>
		public int MaxBarsSweepToShift = 12;

		/// <summary>Bars allowed between a structure shift and the retest entry before the setup is abandoned.</summary>
		public int MaxBarsShiftToRetest = 15;

		/// <summary>
		/// Anchor the structure reference to the first swing formed after the sweep. When
		/// false, the shift must break the swing that preceded the sweep instead.
		///
		/// These are not two speeds of the same test, which is how this was originally
		/// written. The pre-sweep swing is the origin of the entire leg that ended in the
		/// sweep, so breaking it means a full retracement and the stop - pinned beyond the
		/// swept extreme - spans that leg end to end. The post-sweep swing is a local high
		/// or low made while price is turning, a few bars and a fraction of the range away.
		///
		/// So when this is on, a setup waits for the near swing rather than falling back to
		/// the far one. Falling back was silently converting every setup into the strictest,
		/// widest-stop version of itself.
		/// </summary>
		public bool UsePostSweepSwing = true;

		/// <summary>
		/// Reject a setup whose structure reference sits further than this many ATRs from the
		/// swept extreme. That distance is the trade's risk, so this is a ceiling on risk
		/// expressed in the units that produced it. Zero disables.
		/// </summary>
		public double MaxSetupRiskAtr = 2.5;

		/// <summary>Minimum range of the bar that breaks structure, as a multiple of ATR. This is the "strong displacement" test. Zero disables it.</summary>
		public double MinDisplacementAtr = 1.0;

		public RetestZoneMode ZoneMode = RetestZoneMode.BrokenStructure;

		/// <summary>Half-width of the retest zone, as a multiple of ATR.</summary>
		public double RetestZoneAtr = 0.30;

		/// <summary>Retracement fraction of the displacement leg, used when ZoneMode is FibRetrace.</summary>
		public double FibRetracePercent = 0.5;

		/// <summary>
		/// Require a bar to close in the trade direction after touching the retest zone.
		/// Without it, entry is a limit order into the zone: better fills, but no evidence
		/// the zone is holding.
		/// </summary>
		public bool RequireConfirmationClose = true;

		/// <summary>Buffer beyond the sweep extreme for the protective stop, as a multiple of ATR.</summary>
		public double StopBufferAtr = 0.25;

		/// <summary>Reward-to-risk multiple for the profit target. Zero means target the opposing liquidity level instead.</summary>
		public double TargetRMultiple = 2.0;
	}

	public struct SetupResult
	{
		public bool HasEntry;
		public TradeDirection Direction;
		public double StopPrice;
		public double TargetPrice;
		public string Label;
		public string Detail;
	}

	public sealed class SetupEngine
	{
		private readonly SetupEngineSettings settings;

		private SetupState state = SetupState.Idle;
		private SweepEvent sweep;
		private SwingPoint structureReference;
		private int shiftBarIndex;
		private double displacementExtreme;
		private double zoneCenter;
		private double zoneHalfWidth;
		private bool zoneTouched;
		private int discardedTooWide;
		private int sweepsAdopted;
		private int sweepsIgnored;

		public SetupEngine(SetupEngineSettings settings)
		{
			if (settings == null)
				throw new ArgumentNullException("settings");

			this.settings = settings;
		}

		public SetupState State { get { return state; } }
		public SweepEvent ActiveSweep { get { return sweep; } }

		/// <summary>True once price has traded into the retest zone of the current setup. Exposed so the strategy can count how far setups get.</summary>
		public bool ZoneTouched { get { return zoneTouched; } }

		/// <summary>Running count of setups abandoned because the structure was too far from the swept extreme to trade against.</summary>
		public int DiscardedTooWide { get { return discardedTooWide; } }

		/// <summary>Sweeps that started or replaced a setup.</summary>
		public int SweepsAdopted { get { return sweepsAdopted; } }

		/// <summary>Sweeps left alone because a setup was already developing. A high ratio here means the level book is generating noise.</summary>
		public int SweepsIgnored { get { return sweepsIgnored; } }

		public string LastTransition { get; private set; }

		public void Reset(string reason)
		{
			state = SetupState.Idle;
			sweep = default(SweepEvent);
			structureReference = default(SwingPoint);
			zoneTouched = false;
			LastTransition = reason;
		}

		/// <summary>
		/// Advance the state machine by one completed bar. Returns a result whose HasEntry
		/// is true only on the bar where a full sequence completes.
		/// </summary>
		public SetupResult Update(MarketAnalyzer analyzer, int barIndex, DateTime time,
			double open, double high, double low, double close, double atr)
		{
			SetupResult result = default(SetupResult);
			LastTransition = null;

			SweepEvent fresh = analyzer.LastUpdateSweep;

			if (fresh.IsValid)
			{
				if (ShouldAdopt(fresh))
				{
					sweepsAdopted++;
					sweep = fresh;
					state = SetupState.AwaitingStructureShift;
					zoneTouched = false;

					structureReference = sweep.Side == SweepSide.SellSide
						? analyzer.Swings.MostRecentHighAtOrBefore(sweep.ConfirmBarIndex)
						: analyzer.Swings.MostRecentLowAtOrBefore(sweep.ConfirmBarIndex);

					LastTransition = string.Format("Sweep {0} of {1} ({2:N2} penetration). Awaiting structure shift; reference {3}.",
						sweep.Side, sweep.Level, sweep.PenetrationPoints,
						structureReference.IsValid ? structureReference.ToString() : "none yet");

					return result;
				}

				sweepsIgnored++;
			}

			if (state == SetupState.Idle)
				return result;

			if (state == SetupState.AwaitingStructureShift)
			{
				if (barIndex - sweep.ConfirmBarIndex > settings.MaxBarsSweepToShift)
				{
					Reset(string.Format("No structure shift within {0} bars of the sweep.", settings.MaxBarsSweepToShift));
					return result;
				}

				// Use the swing formed since the sweep - it is nearer, gives an earlier signal,
				// and keeps the resulting stop proportional to the move being traded. If none
				// has formed yet, wait for one. The pre-sweep swing is not a substitute; see
				// the note on UsePostSweepSwing.
				if (settings.UsePostSweepSwing)
				{
					SwingPoint post = sweep.Side == SweepSide.SellSide
						? analyzer.Swings.MostRecentHighAfter(sweep.ExtremeBarIndex)
						: analyzer.Swings.MostRecentLowAfter(sweep.ExtremeBarIndex);

					if (!post.IsValid)
						return result;

					structureReference = post;
				}

				if (!structureReference.IsValid)
					return result;

				bool broke = sweep.Side == SweepSide.SellSide
					? close > structureReference.Price
					: close < structureReference.Price;

				if (!broke)
					return result;

				// The gap between the structure being broken and the swept extreme is the
				// trade's risk, fixed before entry is even considered. Checked here so a
				// hopeless setup is abandoned at the shift rather than carried to the retest
				// and rejected on stop size, which reads as a sizing problem and is not one.
				double setupRisk = Math.Abs(structureReference.Price - sweep.ExtremePrice);

				if (settings.MaxSetupRiskAtr > 0 && setupRisk > atr * settings.MaxSetupRiskAtr)
				{
					discardedTooWide++;
					Reset(string.Format(
						"Structure at {0:N2} is {1:N2} pts from the swept extreme, over the {2:N2} allowed ({3:N1} ATR). Setup discarded.",
						structureReference.Price, setupRisk, atr * settings.MaxSetupRiskAtr, settings.MaxSetupRiskAtr));
					return result;
				}

				if (settings.MinDisplacementAtr > 0 && (high - low) < atr * settings.MinDisplacementAtr)
				{
					// Structure gave way but without conviction. Wait for a better break
					// rather than abandoning the setup outright.
					LastTransition = string.Format("Structure broken at {0:N2} but displacement {1:N2} < {2:N2} required. Holding.",
						structureReference.Price, high - low, atr * settings.MinDisplacementAtr);
					return result;
				}

				shiftBarIndex = barIndex;
				displacementExtreme = sweep.Side == SweepSide.SellSide ? high : low;
				BuildRetestZone(atr);
				zoneTouched = false;
				state = SetupState.AwaitingRetest;

				LastTransition = string.Format("Structure shift {0} {1:N2}. Retest zone {2:N2} +/- {3:N2}.",
					sweep.Side == SweepSide.SellSide ? "above" : "below",
					structureReference.Price, zoneCenter, zoneHalfWidth);

				return result;
			}

			// AwaitingRetest
			if (barIndex - shiftBarIndex > settings.MaxBarsShiftToRetest)
			{
				Reset(string.Format("No retest within {0} bars of the structure shift.", settings.MaxBarsShiftToRetest));
				return result;
			}

			bool bullish = sweep.Side == SweepSide.SellSide;

			// Invalidation: price back through the sweep extreme means the level did not hold.
			if ((bullish && low < sweep.ExtremePrice) || (!bullish && high > sweep.ExtremePrice))
			{
				Reset("Price traded back through the sweep extreme; setup invalidated.");
				return result;
			}

			double zoneUpper = zoneCenter + zoneHalfWidth;
			double zoneLower = zoneCenter - zoneHalfWidth;

			if (!zoneTouched && low <= zoneUpper && high >= zoneLower)
			{
				zoneTouched = true;
				LastTransition = string.Format("Retest zone touched at {0:N2}.", zoneCenter);

				if (settings.RequireConfirmationClose)
					return result;
			}

			if (!zoneTouched)
				return result;

			if (settings.RequireConfirmationClose)
			{
				bool confirms = bullish ? close > open && close > zoneLower : close < open && close < zoneUpper;

				if (!confirms)
					return result;
			}

			double stopBuffer = atr * settings.StopBufferAtr;
			double stopPrice = bullish ? sweep.ExtremePrice - stopBuffer : sweep.ExtremePrice + stopBuffer;
			double risk = Math.Abs(close - stopPrice);

			if (risk <= 0)
			{
				Reset("Degenerate stop distance; setup discarded.");
				return result;
			}

			double targetPrice;
			if (settings.TargetRMultiple > 0)
			{
				targetPrice = bullish
					? close + (risk * settings.TargetRMultiple)
					: close - (risk * settings.TargetRMultiple);
			}
			else
			{
				targetPrice = FindOpposingLiquidity(analyzer, close, bullish, risk);
			}

			result.HasEntry = true;
			result.Direction = bullish ? TradeDirection.Long : TradeDirection.Short;
			result.StopPrice = stopPrice;
			result.TargetPrice = targetPrice;
			result.Label = bullish ? "sweepLong" : "sweepShort";
			result.Detail = string.Format(
				"{0}: swept {1}, structure shift at {2:N2}, retest {3:N2}. Stop {4:N2}, target {5:N2}, risk {6:N2} pts.",
				result.Label, sweep.Level, structureReference.Price, zoneCenter, stopPrice, targetPrice, risk);

			Reset("Entry taken.");
			return result;
		}

		/// <summary>
		/// Whether a newly confirmed sweep should replace the setup in progress.
		///
		/// This used to be unconditional, on the reasoning that the newer liquidity event is
		/// the more relevant one. With a dense level book that is wrong: sweeps confirm every
		/// few bars, so every setup was demolished and restarted long before it could develop.
		/// A post-sweep swing alone needs (2 x strength) + 1 bars to confirm, which it never
		/// got. Nothing downstream could work, and the counters blamed the steps that never
		/// ran rather than the restart that stopped them.
		///
		/// The anchor now holds unless the new sweep is genuinely more relevant: the market
		/// turning the other way, or price reaching further into the same liquidity, which
		/// moves where the protective stop belongs.
		/// </summary>
		private bool ShouldAdopt(SweepEvent fresh)
		{
			if (state == SetupState.Idle || !sweep.IsValid)
				return true;

			// The other side taking liquidity supersedes whatever we were watching.
			if (fresh.Side != sweep.Side)
				return true;

			// A deeper extreme on the same side is the same setup with a better stop.
			bool deeper = fresh.Side == SweepSide.SellSide
				? fresh.ExtremePrice < sweep.ExtremePrice
				: fresh.ExtremePrice > sweep.ExtremePrice;

			if (deeper)
				return true;

			// Anything else is a shallower poke at a neighbouring level while the setup we
			// already have is still developing. Let it develop.
			return false;
		}

		private void BuildRetestZone(double atr)
		{
			zoneHalfWidth = Math.Max(atr * settings.RetestZoneAtr, 0.25);

			switch (settings.ZoneMode)
			{
				case RetestZoneMode.SweptLevel:
					zoneCenter = sweep.Level.Price;
					break;

				case RetestZoneMode.FibRetrace:
					zoneCenter = displacementExtreme
						+ ((sweep.ExtremePrice - displacementExtreme) * settings.FibRetracePercent);
					break;

				default:
					zoneCenter = structureReference.Price;
					break;
			}
		}

		/// <summary>
		/// Target the next pool of liquidity in the trade's direction. Falls back to a 2R
		/// target when nothing suitable is in range, so a trade is never left without one.
		/// </summary>
		private double FindOpposingLiquidity(MarketAnalyzer analyzer, double entryPrice, bool bullish, double risk)
		{
			double searchDistance = risk * 10.0;
			double minDistance = risk * 1.0;

			if (bullish)
			{
				foreach (Level level in analyzer.Levels.Above(entryPrice, searchDistance))
				{
					if (level.Price - entryPrice >= minDistance)
						return level.Price;
				}

				return entryPrice + (risk * 2.0);
			}

			foreach (Level level in analyzer.Levels.Below(entryPrice, searchDistance))
			{
				if (entryPrice - level.Price >= minDistance)
					return level.Price;
			}

			return entryPrice - (risk * 2.0);
		}
	}
}
