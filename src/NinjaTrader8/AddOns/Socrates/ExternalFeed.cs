// Socrates NQ - External data feed
//
// Bars fetched from somewhere that is not NinjaTrader: an HTTP endpoint on your own
// server, or a file on disk. Steps 5 and 6 exist to read a VIX and a handful of equity
// leaders, and NinjaTrader's feed cannot supply either over a useful range - it carries
// no US equities at all, and the futures substitutes are contract-based with no history
// across a roll. That is not a tuning problem, it is a data problem, and this is the
// route around it.
//
// Deliberately NOT an AddDataSeries. Going through NinjaTrader's data layer for this
// means fighting instrument definitions, contract rolls and merge policies to obtain
// what is fundamentally a lookup table of one number per timestamp. It also drags the
// whole backtest range down to the youngest series, which is what has been truncating
// every step 5 and step 6 run to a fortnight. A feed held here has none of those
// properties: the primary series decides the range, and this answers questions about
// timestamps within it.
//
// Plain C# with no NinjaTrader dependencies, like the rest of the engine.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Threading;

namespace Socrates.Data
{
	public struct ExternalBar
	{
		public bool IsValid;
		public DateTime Time;
		public double Open;
		public double High;
		public double Low;
		public double Close;
	}

	public sealed class ExternalFeedSettings
	{
		/// <summary>Label used in logs. The symbol, normally.</summary>
		public string Name = "feed";

		/// <summary>An http(s) URL, or a path to a file on disk. Anything else is refused at startup rather than at the first bar.</summary>
		public string Source = string.Empty;

		/// <summary>How often to re-fetch while live. Ignored in a backtest, which fetches once.</summary>
		public int PollSeconds = 30;

		public int TimeoutSeconds = 10;

		/// <summary>Period for the ATR computed over the fetched bars. Step 5 scales its threshold by it.</summary>
		public int AtrPeriod = 14;

		/// <summary>
		/// Correction applied after the UTC conversion, for a platform whose display time
		/// zone is not the machine's. Positive moves feed timestamps later.
		/// </summary>
		public int TimestampOffsetMinutes = 0;
	}

	/// <summary>
	/// Bars from an external source, answering "what was this instrument doing at time T".
	///
	/// Fetching happens on a background thread and never on the bar thread: a blocking web
	/// request inside OnBarUpdate stalls the strategy on every bar and eventually gets it
	/// disabled by the platform. Reads take a lock and copy, so a fetch landing mid-bar
	/// cannot hand out a half-built list.
	/// </summary>
	public sealed class ExternalFeed
	{
		private readonly ExternalFeedSettings settings;
		private readonly object gate = new object();

		private List<ExternalBar> bars = new List<ExternalBar>();
		private List<double> atr = new List<double>();

		private Thread poller;
		private volatile bool running;

		private int fetches;
		private int fetchFailures;
		private int parseFailures;
		private string lastError = string.Empty;
		private DateTime lastFetchUtc = DateTime.MinValue;

		public ExternalFeed(ExternalFeedSettings settings)
		{
			if (settings == null)
				throw new ArgumentNullException("settings");

			this.settings = settings;
		}

		public string Name { get { return settings.Name; } }
		public int Fetches { get { return fetches; } }
		public int FetchFailures { get { return fetchFailures; } }
		public int ParseFailures { get { return parseFailures; } }
		public string LastError { get { return lastError; } }

		public int BarCount
		{
			get { lock (gate) { return bars.Count; } }
		}

		public DateTime FirstBarTime
		{
			get { lock (gate) { return bars.Count > 0 ? bars[0].Time : DateTime.MinValue; } }
		}

		public DateTime LastBarTime
		{
			get { lock (gate) { return bars.Count > 0 ? bars[bars.Count - 1].Time : DateTime.MinValue; } }
		}

		/// <summary>True when the source string looks like something this can actually fetch.</summary>
		public static bool LooksUsable(string source)
		{
			if (string.IsNullOrEmpty(source))
				return false;

			string s = source.Trim();

			return s.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
				|| s.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
				|| File.Exists(s);
		}

		/// <summary>
		/// One blocking fetch. Called from State.DataLoaded, where blocking is fine and a
		/// failure can still be reported before any bar is processed.
		/// </summary>
		public bool FetchNow(out string error)
		{
			error = null;

			try
			{
				string text = Read();
				int parsed = Ingest(text);

				fetches++;
				lastFetchUtc = DateTime.UtcNow;

				if (parsed == 0)
				{
					error = "fetched but no usable rows - check the format";
					lastError = error;
					return false;
				}

				return true;
			}
			catch (Exception ex)
			{
				fetchFailures++;
				error = ex.Message;
				lastError = ex.Message;
				return false;
			}
		}

		/// <summary>Begin background polling. Live only; a backtest has nothing to poll for.</summary>
		public void StartPolling()
		{
			if (running || settings.PollSeconds <= 0)
				return;

			running = true;

			poller = new Thread(PollLoop);
			poller.IsBackground = true;
			poller.Name = "SocratesFeed-" + settings.Name;
			poller.Start();
		}

		public void Stop()
		{
			running = false;

			Thread t = poller;
			poller = null;

			if (t != null)
			{
				try
				{
					t.Join(500);
				}
				catch (Exception)
				{
					// Terminated is not a place to throw from.
				}
			}
		}

		private void PollLoop()
		{
			while (running)
			{
				// Slept in slices so Stop() is honoured promptly rather than after a full
				// poll interval, which on a 60 second setting would hang shutdown.
				for (int i = 0; i < settings.PollSeconds && running; i++)
					Thread.Sleep(1000);

				if (!running)
					return;

				string error;
				FetchNow(out error);
			}
		}

		private string Read()
		{
			string source = settings.Source.Trim();

			if (source.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
				|| source.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
			{
				using (WebClient client = new WebClient())
				{
					client.Headers.Add("User-Agent", "SocratesNQ");
					return client.DownloadString(source);
				}
			}

			return File.ReadAllText(source);
		}

		/// <summary>
		/// Parse and merge. Rows are keyed by timestamp, so re-fetching the same window is
		/// harmless - a poll that overlaps what is already held updates those bars rather
		/// than duplicating them, which matters because the most recent bar is usually
		/// still forming when it is first seen.
		/// </summary>
		private int Ingest(string text)
		{
			if (string.IsNullOrEmpty(text))
				return 0;

			Dictionary<DateTime, ExternalBar> merged = new Dictionary<DateTime, ExternalBar>();

			lock (gate)
			{
				for (int i = 0; i < bars.Count; i++)
					merged[bars[i].Time] = bars[i];
			}

			string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
			int accepted = 0;

			for (int i = 0; i < lines.Length; i++)
			{
				string line = lines[i].Trim();

				if (line.Length == 0 || line[0] == '#')
					continue;

				ExternalBar bar;

				if (!TryParseRow(line, out bar))
				{
					parseFailures++;
					continue;
				}

				merged[bar.Time] = bar;
				accepted++;
			}

			if (accepted == 0)
				return 0;

			List<ExternalBar> ordered = new List<ExternalBar>(merged.Values);
			ordered.Sort(CompareByTime);

			List<double> computed = ComputeAtr(ordered, settings.AtrPeriod);

			lock (gate)
			{
				bars = ordered;
				atr = computed;
			}

			return accepted;
		}

		private static int CompareByTime(ExternalBar a, ExternalBar b)
		{
			return a.Time.CompareTo(b.Time);
		}

		/// <summary>
		/// One bar per line: timestamp,open,high,low,close - and a bare timestamp,close is
		/// accepted too, for a source that only publishes a level. Volume is ignored if
		/// present.
		///
		/// The timestamp is epoch seconds, epoch milliseconds, or ISO-8601 carrying an
		/// explicit zone. A local-looking timestamp with no zone is refused rather than
		/// guessed at: silently reading a feed an hour out is exactly the failure that
		/// looks like a strategy problem for a week.
		/// </summary>
		private bool TryParseRow(string line, out ExternalBar bar)
		{
			bar = default(ExternalBar);

			string[] parts = line.Split(',');

			if (parts.Length < 2)
				return false;

			DateTime stamp;

			if (!TryParseTimestamp(parts[0].Trim(), out stamp))
				return false;

			double o, h, l, c;

			if (parts.Length >= 5)
			{
				if (!TryParseNumber(parts[1], out o)) return false;
				if (!TryParseNumber(parts[2], out h)) return false;
				if (!TryParseNumber(parts[3], out l)) return false;
				if (!TryParseNumber(parts[4], out c)) return false;
			}
			else
			{
				if (!TryParseNumber(parts[1], out c)) return false;
				o = h = l = c;
			}

			if (c <= 0 || h < l)
				return false;

			bar.IsValid = true;
			bar.Time = stamp;
			bar.Open = o;
			bar.High = h;
			bar.Low = l;
			bar.Close = c;
			return true;
		}

		private bool TryParseTimestamp(string raw, out DateTime local)
		{
			local = DateTime.MinValue;

			if (raw.Length == 0)
				return false;

			double epoch;

			if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out epoch))
			{
				// Milliseconds past a threshold no plausible second-based stamp reaches.
				DateTime utc = epoch > 100000000000.0
					? new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(epoch)
					: new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(epoch);

				local = ToPlatformTime(utc);
				return true;
			}

			DateTimeOffset parsed;

			if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture,
				DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out parsed))
			{
				// AssumeUniversal means a zoneless string is read as UTC. That is a stated
				// contract rather than a guess, and the banner prints the first bar's time
				// beside the chart's so a mismatch shows up immediately.
				local = ToPlatformTime(parsed.UtcDateTime);
				return true;
			}

			return false;
		}

		private DateTime ToPlatformTime(DateTime utc)
		{
			DateTime converted = utc.ToLocalTime();

			if (settings.TimestampOffsetMinutes != 0)
				converted = converted.AddMinutes(settings.TimestampOffsetMinutes);

			return converted;
		}

		private static bool TryParseNumber(string raw, out double value)
		{
			return double.TryParse(raw.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
		}

		/// <summary>Wilder's ATR over the fetched bars, so step 5's threshold can scale to the source's own volatility exactly as it does on a platform series.</summary>
		private static List<double> ComputeAtr(List<ExternalBar> series, int period)
		{
			List<double> result = new List<double>(series.Count);

			if (period < 1)
				period = 1;

			double sum = 0;
			double current = 0;

			for (int i = 0; i < series.Count; i++)
			{
				double trueRange;

				if (i == 0)
				{
					trueRange = series[i].High - series[i].Low;
				}
				else
				{
					double prevClose = series[i - 1].Close;
					double a = series[i].High - series[i].Low;
					double b = Math.Abs(series[i].High - prevClose);
					double c = Math.Abs(series[i].Low - prevClose);
					trueRange = Math.Max(a, Math.Max(b, c));
				}

				if (i < period)
				{
					sum += trueRange;
					current = sum / (i + 1);
				}
				else
				{
					current = ((current * (period - 1)) + trueRange) / period;
				}

				result.Add(current);
			}

			return result;
		}

		/// <summary>
		/// The most recent bar at or before a time, with the ATR as at that bar and the bar
		/// a lookback earlier. Returns false when the feed holds nothing at or before it,
		/// which is the honest answer for a backtest that starts before the feed's history.
		/// </summary>
		public bool TryGetAt(DateTime when, int lookbackBars,
			out ExternalBar bar, out double barAtr, out double referenceClose)
		{
			bar = default(ExternalBar);
			barAtr = 0;
			referenceClose = 0;

			lock (gate)
			{
				if (bars.Count == 0)
					return false;

				int index = IndexAtOrBefore(when);

				if (index < 0)
					return false;

				bar = bars[index];
				barAtr = index < atr.Count ? atr[index] : 0;

				int refIndex = index - Math.Max(1, lookbackBars);
				referenceClose = refIndex >= 0 ? bars[refIndex].Close : bars[0].Close;
				return true;
			}
		}

		/// <summary>The first bar of the session containing a time, used as the reference a leader's percent move is measured from.</summary>
		public bool TryGetSessionOpen(DateTime when, out double sessionOpen)
		{
			sessionOpen = 0;

			lock (gate)
			{
				if (bars.Count == 0)
					return false;

				int index = IndexAtOrBefore(when);

				if (index < 0)
					return false;

				DateTime day = bars[index].Time.Date;
				int first = index;

				while (first > 0 && bars[first - 1].Time.Date == day)
					first--;

				sessionOpen = bars[first].Open;
				return sessionOpen > 0;
			}
		}

		/// <summary>Caller holds the lock.</summary>
		private int IndexAtOrBefore(DateTime when)
		{
			int lo = 0;
			int hi = bars.Count - 1;
			int found = -1;

			while (lo <= hi)
			{
				int mid = lo + ((hi - lo) / 2);

				if (bars[mid].Time <= when)
				{
					found = mid;
					lo = mid + 1;
				}
				else
				{
					hi = mid - 1;
				}
			}

			return found;
		}
	}
}
