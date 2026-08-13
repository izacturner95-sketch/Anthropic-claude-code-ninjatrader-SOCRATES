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
		/// Ceiling on the trade's actual risk - entry to stop - in ATRs, applied at entry.
		/// Zero disables.
		/// </summary>
		public double MaxSetupRiskAtr = 2.5;

		/// <summary>
		/// Ceiling on how far the broken structure may sit from the swept extreme, applied at
		/// the shift. Zero disables.
		///
		/// This used to be MaxSetupRiskAtr, back when the stop was pinned beyond the swept
		/// extreme and that distance was the risk. The stop now comes from the retest
		/// pullback, which is far nearer, so the two measure different things - and the risk
		/// ceiling was still being applied to a distance that no longer sets the risk. It was
		/// discarding 573 of 819 structure breaks on that basis. Kept as a coherence test on
		/// the setup, set loose enough to be one.
		/// </summary>
		public double MaxStructureDistanceAtr = 5.0;

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

		/// <summary>Buffer beyond the swing the stop is anchored to, as a multiple of ATR.</summary>
		public double StopBufferAtr = 0.25;

		/// <summary>
		/// Fallback reward-to-risk multiple, used only when no previous swing far enough away
		/// exists to target. Zero falls back to the opposing liquidity level instead.
		/// </summary>
		public double TargetRMultiple = 2.0;

		/// <summary>
		/// How far short of the previous swing to place the target, in points. "At or just
		/// below the previous high" - the last ticks into a level are where the reversal
		/// happens, so the exit sits in front of it rather than on it.
		/// </summary>
		public double TargetBufferPoints = 1.0;

		/// <summary>
		/// Minimum reward-to-risk for a setup to be taken. With both the stop and the target
		/// read off structure, the ratio is whatever the chart happens to offer, and some of
		/// what it offers is not worth trading. Zero disables.
		/// </summary>
		public double MinRewardRisk = 1.0;
	}

	public struct SetupResult
	{
		public bool HasEntry;

		/// <summary>True when this came from a break that held rather than a sweep that reversed.</summary>
		public bool IsContinuation;
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
		private double retestExtreme;
		private bool zoneTouched;
		private bool isContinuation;
		private int discardedTooWide;
		private int discardedPoorReward;
		private int sweepsAdopted;
		private int sweepsIgnored;
		private int stopsFromRetest;
		private int stopsFromSwing;
		private int stopsFromSweepExtreme;
		private int targetsFromSwing;
		private int targetsFromRMultiple;
		private int targetsFromLiquidity;
		private int reversalEntries;
		private int continuationEntries;

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

		/// <summary>Whether the setup in progress is a continuation. A continuation never passes through a structure shift, so the funnel must not count it as one.</summary>
		public bool ActiveIsContinuation { get { return isContinuation; } }

		/// <summary>Centre of the retest zone in progress. Exposed for drawing it on a chart.</summary>
		public double ZoneCenter { get { return zoneCenter; } }

		public double ZoneHalfWidth { get { return zoneHalfWidth; } }

		/// <summary>Running count of setups abandoned because the structure was too far from the swept extreme to trade against.</summary>
		public int DiscardedTooWide { get { return discardedTooWide; } }

		/// <summary>Sweeps that started or replaced a setup.</summary>
		public int SweepsAdopted { get { return sweepsAdopted; } }

		/// <summary>Sweeps left alone because a setup was already developing. A high ratio here means the level book is generating noise.</summary>
		public int SweepsIgnored { get { return sweepsIgnored; } }

		/// <summary>Setups abandoned because the structural target did not pay for the structural stop.</summary>
		public int DiscardedPoorReward { get { return discardedPoorReward; } }

		/// <summary>Stops anchored to the pullback extreme of the retest - the intended source.</summary>
		public int StopsFromRetest { get { return stopsFromRetest; } }

		/// <summary>Stops that fell back to the last confirmed swing.</summary>
		public int StopsFromSwing { get { return stopsFromSwing; } }

		public int StopsFromSweepExtreme { get { return stopsFromSweepExtreme; } }

		/// <summary>Targets taken from a previous swing, versus the R-multiple or liquidity fallbacks.</summary>
		public int TargetsFromSwing { get { return targetsFromSwing; } }

		public int TargetsFromRMultiple { get { return targetsFromRMultiple; } }

		public int TargetsFromLiquidity { get { return targetsFromLiquidity; } }

		/// <summary>Completed setups by kind, so the two can be judged separately rather than as one blended number.</summary>
		public int ReversalEntries { get { return reversalEntries; } }

		public int ContinuationEntries { get { return continuationEntries; } }

		public string LastTransition { get; private set; }

		public void Reset(string reason)
		{
			state = SetupState.Idle;
			sweep = default(SweepEvent);
			structureReference = default(SwingPoint);
			zoneTouched = false;
			isContinuation = false;
			LastTransition = reason;
		}

		/// <summary>
		/// A reversal trades against the move that took the level: lows swept, so buy. A
		/// continuation trades with it: the level broke and held, so the break direction is
		/// the trade direction. Same event type, opposite mapping, which is worth having in
		/// one place rather than inline at each use.
		/// </summary>
		private static bool IsBullish(SweepEvent s)
		{
			return s.IsContinuation
				? s.Side == SweepSide.BuySide
				: s.Side == SweepSide.SellSide;
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
					zoneTouched = false;
					isContinuation = fresh.IsContinuation;
					structureReference = default(SwingPoint);

					if (isContinuation)
					{
						// No structure shift to wait for: the break through the level is the
						// structural event. Straight to the retest, with the broken level as
						// the zone it is expected to hold from the other side.
						bool up = IsBullish(sweep);

						shiftBarIndex = barIndex;
						displacementExtreme = sweep.ExtremePrice;
						zoneCenter = sweep.Level.Price;
						zoneHalfWidth = Math.Max(atr * settings.RetestZoneAtr, 0.25);
						retestExtreme = up ? low : high;
						state = SetupState.AwaitingRetest;

						LastTransition = string.Format("Break {0} of {1} ({2:N2} beyond, no reclaim). Awaiting retest of {3:N2}.",
							up ? "above" : "below", sweep.Level, sweep.PenetrationPoints, zoneCenter);

						return result;
					}

					state = SetupState.AwaitingStructureShift;

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
				double structureDistance = Math.Abs(structureReference.Price - sweep.ExtremePrice);

				if (settings.MaxStructureDistanceAtr > 0 && structureDistance > atr * settings.MaxStructureDistanceAtr)
				{
					discardedTooWide++;
					Reset(string.Format(
						"Structure at {0:N2} is {1:N2} pts from the swept extreme, over the {2:N2} allowed ({3:N1} ATR). Setup discarded.",
						structureReference.Price, structureDistance, atr * settings.MaxStructureDistanceAtr, settings.MaxStructureDistanceAtr));
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
				retestExtreme = IsBullish(sweep) ? low : high;
				BuildRetestZone(atr);
				zoneTouched = false;
				state = SetupState.AwaitingRetest;

				LastTransition = string.Format("Structure shift {0} {1:N2}. Retest zone {2:N2} +/- {3:N2}.",
					sweep.Side == SweepSide.SellSide ? "above" : "below",
					structureReference.Price, zoneCenter, zoneHalfWidth);

				return result;
			}

			// AwaitingRetest
			//
			// The low of the pullback into the retest, tracked live. It is the level the
			// trade is betting holds, and unlike a swing point it needs no confirmation lag -
			// which matters, because a swing needs (2 x strength) + 1 bars and the pullback
			// low is usually one or two bars old when entry triggers.
			// IsBullish, not the raw side. A continuation runs with the break, so BuySide is
			// bullish there and bearish for a reversal. Reading the side directly tracked the
			// high on a bullish continuation - the opposite extreme - which made the anchor
			// fail its own validity test and fall through to the confirmed-swing fallback on
			// 236 of 247 setups, quietly undoing the retest stop entirely.
			retestExtreme = IsBullish(sweep)
				? Math.Min(retestExtreme, low)
				: Math.Max(retestExtreme, high);

			if (barIndex - shiftBarIndex > settings.MaxBarsShiftToRetest)
			{
				Reset(string.Format("No retest within {0} bars of the structure shift.", settings.MaxBarsShiftToRetest));
				return result;
			}

			bool bullish = IsBullish(sweep);

			if (isContinuation)
			{
				// The swept extreme sits in the trade's favour on a continuation, so the
				// reversal test would fire on the first bar. What invalidates a break is
				// price closing back through the level it broke.
				bool brokeBack = bullish
					? close < sweep.Level.Lower
					: close > sweep.Level.Upper;

				if (brokeBack)
				{
					Reset("Price closed back through the broken level; the break failed.");
					return result;
				}
			}
			else if ((bullish && low < sweep.ExtremePrice) || (!bullish && high > sweep.ExtremePrice))
			{
				// Invalidation: price back through the sweep extreme means the level did not hold.
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

			// Stop below the previous low, or above the previous high on a short.
			//
			// "The previous low" is the low of the pullback into this retest, not the last
			// confirmed swing low. Asking the swing detector was the obvious reading and it
			// was wrong in practice: a swing needs (2 x strength) + 1 bars to confirm, the
			// pullback low is one or two bars old when entry triggers, so the nearest
			// confirmed low below entry was almost always the swept extreme itself. The stop
			// was landing exactly where it had before - 169 to 723 ticks - while the log
			// reported it as anchored to a swing.
			//
			// The tracked pullback extreme has no confirmation lag and is the level the trade
			// is actually betting holds.
			double stopAnchor;
			string stopSource;

			bool retestValid = retestExtreme > 0
				&& (bullish ? retestExtreme < close : retestExtreme > close);

			if (retestValid)
			{
				stopAnchor = retestExtreme;
				stopSource = bullish ? "retest low" : "retest high";
				stopsFromRetest++;
			}
			else
			{
				// Degenerate case: the confirming bar is itself the extreme. Fall back to the
				// last confirmed swing, then to the swept extreme.
				SwingPoint stopSwing = bullish
					? analyzer.Swings.MostRecentLowBelow(close, 0)
					: analyzer.Swings.MostRecentHighAbove(close, 0);

				if (stopSwing.IsValid)
				{
					stopAnchor = stopSwing.Price;
					stopSource = bullish ? "previous swing low" : "previous swing high";
					stopsFromSwing++;
				}
				else
				{
					stopAnchor = sweep.ExtremePrice;
					stopSource = "swept extreme";
					stopsFromSweepExtreme++;
				}
			}

			double stopPrice = bullish ? stopAnchor - stopBuffer : stopAnchor + stopBuffer;
			double risk = Math.Abs(close - stopPrice);

			if (risk <= 0)
			{
				Reset("Degenerate stop distance; setup discarded.");
				return result;
			}

			// The same ceiling as at the shift, applied again to the distance that actually
			// gets traded. Checking it once at the shift bounds structure-to-extreme, but
			// entry is the confirmation bar's close, and the only constraint on that close is
			// that it finished in the trade's direction - it can be a long way past the zone
			// by then. Without this the cap held on paper while entries ran well beyond it.
			if (settings.MaxSetupRiskAtr > 0 && risk > atr * settings.MaxSetupRiskAtr)
			{
				discardedTooWide++;
				Reset(string.Format(
					"Entry at {0:N2} is {1:N2} pts from the stop, over the {2:N2} allowed ({3:N1} ATR). Setup discarded.",
					close, risk, atr * settings.MaxSetupRiskAtr, settings.MaxSetupRiskAtr));
				return result;
			}

			// Target at, or just short of, the previous high - the previous low on a short.
			// Searching from a minimum distance rather than taking the nearest one skips the
			// swings too close to be worth aiming at and walks back to one that pays for the
			// risk being taken.
			double minTargetDistance = settings.TargetBufferPoints
				+ (settings.MinRewardRisk > 0 ? risk * settings.MinRewardRisk : 0);

			SwingPoint targetSwing = bullish
				? analyzer.Swings.MostRecentHighAbove(close, minTargetDistance)
				: analyzer.Swings.MostRecentLowBelow(close, minTargetDistance);

			double targetPrice;
			string targetSource;

			if (targetSwing.IsValid)
			{
				targetPrice = bullish
					? targetSwing.Price - settings.TargetBufferPoints
					: targetSwing.Price + settings.TargetBufferPoints;

				targetSource = string.Format("previous {0} {1:N2}", bullish ? "high" : "low", targetSwing.Price);
				targetsFromSwing++;
			}
			else if (settings.TargetRMultiple > 0)
			{
				targetPrice = bullish
					? close + (risk * settings.TargetRMultiple)
					: close - (risk * settings.TargetRMultiple);

				targetSource = string.Format("{0:N1}R fallback, no previous swing far enough", settings.TargetRMultiple);
				targetsFromRMultiple++;
			}
			else
			{
				targetPrice = FindOpposingLiquidity(analyzer, close, bullish, risk);
				targetSource = "opposing liquidity";
				targetsFromLiquidity++;
			}

			double reward = Math.Abs(targetPrice - close);

			if (settings.MinRewardRisk > 0 && reward < risk * settings.MinRewardRisk)
			{
				discardedPoorReward++;
				Reset(string.Format("Reward {0:N2} against risk {1:N2} is {2:N2}R, under the {3:N2}R minimum. Setup discarded.",
					reward, risk, reward / risk, settings.MinRewardRisk));
				return result;
			}

			result.HasEntry = true;
			result.IsContinuation = isContinuation;
			result.Direction = bullish ? TradeDirection.Long : TradeDirection.Short;
			result.StopPrice = stopPrice;
			result.TargetPrice = targetPrice;
			// Separate labels so the two setup types are told apart in the trade list, and so
			// their stop and target orders never share a signal name.
			result.Label = isContinuation
				? (bullish ? "contLong" : "contShort")
				: (bullish ? "sweepLong" : "sweepShort");

			if (isContinuation)
			{
				continuationEntries++;
				result.Detail = string.Format(
					"{0}: broke {1}, retest {2:N2}. Stop {3:N2} ({4}), target {5:N2} ({6}). Risk {7:N2} pts, {8:N2}R.",
					result.Label, sweep.Level, zoneCenter,
					stopPrice, string.Format("{0} {1:N2}", stopSource, stopAnchor),
					targetPrice, targetSource, risk, reward / risk);
			}
			else
			{
				reversalEntries++;
				result.Detail = string.Format(
					"{0}: swept {1}, shift at {2:N2}, retest {3:N2}. Stop {4:N2} ({5}), target {6:N2} ({7}). Risk {8:N2} pts, {9:N2}R.",
					result.Label, sweep.Level, structureReference.Price, zoneCenter,
					stopPrice, string.Format("{0} {1:N2}", stopSource, stopAnchor),
					targetPrice, targetSource, risk, reward / risk);
			}

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
