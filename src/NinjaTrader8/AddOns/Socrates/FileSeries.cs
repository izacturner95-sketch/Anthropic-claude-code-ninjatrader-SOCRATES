// Socrates NQ - Timestamped values from a file
//
// Steps 5 and 6 read a VIX and seven equity leaders. NinjaTrader's feed carries no US
// equities at all, and the futures substitutes are contract-based with no history across
// a roll, so on that platform neither step can be tested over more than the current
// contract's life. Every step 5 and step 6 run so far has been truncated for that reason
// alone.
//
// Neither step actually needs bars. Step 5 needs a level, a level a lookback ago, and a
// volatility measure; step 6 needs a close and the session's open. That is a lookup
// table, and routing a lookup table through NinjaTrader's data layer means fighting
// instrument definitions, contract rolls and merge policies to get it - and then having
// the backtest range dragged down to whatever the youngest series happens to be, because
// a multi-series backtest cannot begin before all of them.
//
// A file has none of those properties. The primary series decides the range and this
// answers questions about timestamps inside it, in a backtest and live alike.
//
// Plain C# with no NinjaTrader dependencies, like the rest of the engine.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;

namespace Socrates.Data
{
	public struct FileBar
	{
		public bool IsValid;
		public DateTime Time;
		public double Open;
		public double High;
		public double Low;
		public double Close;
	}

	public sealed class FileSeriesSettings
	{
		/// <summary>Label used in every log line about this file. The symbol, normally.</summary>
		public string Name = "file";

		public string Path = string.Empty;

		/// <summary>Period for the ATR computed over the rows, so step 5's threshold still scales to the source's own volatility.</summary>
		public int AtrPeriod = 14;

		/// <summary>How often to re-read while live, in seconds. 0 loads once and never again.</summary>
		public int ReloadSeconds = 60;

		/// <summary>
		/// Correction applied after the UTC conversion, for a platform whose display time
		/// zone is not the machine's. Positive moves file timestamps later.
		/// </summary>
		public int TimestampOffsetMinutes = 0;
	}

	/// <summary>
	/// Rows of "timestamp,open,high,low,close" - or "timestamp,close" - held sorted in
	/// memory and searched by time.
	///
	/// Re-reading happens on a background thread. A file read on the bar thread is usually
	/// quick and occasionally is not: a network path, a file being rewritten underneath
	/// you, a few million rows. Blocking OnBarUpdate is how a strategy gets disabled by
	/// the platform mid-session, and the cost of not risking it is one thread.
	/// </summary>
	public sealed class FileSeries
	{
		private readonly FileSeriesSettings settings;
		private readonly object gate = new object();

		private List<FileBar> bars = new List<FileBar>();
		private List<double> atr = new List<double>();

		private Thread reloader;
		private volatile bool running;

		private int loads;
		private int loadFailures;
		private int rowsRejected;
		private string firstRejectExample = string.Empty;
		private string lastError = string.Empty;
		private DateTime lastWriteSeen = DateTime.MinValue;

		// Whether the thing is actually answering questions, which is the only real test
		// of a data source. A file that loaded 12,000 rows and is never hit is a file
		// whose timestamps do not line up with the chart's.
		private int lookups;
		private int hits;
		private int missesBefore;
		private int missesAfter;

		public FileSeries(FileSeriesSettings settings)
		{
			if (settings == null)
				throw new ArgumentNullException("settings");

			this.settings = settings;
		}

		public string Name { get { return settings.Name; } }
		public string Path { get { return settings.Path; } }
		public int Loads { get { return loads; } }
		public int LoadFailures { get { return loadFailures; } }
		public int RowsRejected { get { return rowsRejected; } }
		public string FirstRejectExample { get { return firstRejectExample; } }
		public string LastError { get { return lastError; } }

		public int Lookups { get { return lookups; } }
		public int Hits { get { return hits; } }
		public int MissesBefore { get { return missesBefore; } }
		public int MissesAfter { get { return missesAfter; } }

		public int RowCount
		{
			get { lock (gate) { return bars.Count; } }
		}

		public DateTime FirstTime
		{
			get { lock (gate) { return bars.Count > 0 ? bars[0].Time : DateTime.MinValue; } }
		}

		public DateTime LastTime
		{
			get { lock (gate) { return bars.Count > 0 ? bars[bars.Count - 1].Time : DateTime.MinValue; } }
		}

		public static bool LooksUsable(string path)
		{
			return !string.IsNullOrEmpty(path) && File.Exists(path.Trim());
		}

		/// <summary>
		/// One blocking read. Called from State.DataLoaded, where blocking is fine and a
		/// failure can still be reported before a single bar is processed - rather than
		/// becoming a step that mysteriously never confirms.
		/// </summary>
		public bool Load(out string error)
		{
			error = null;

			try
			{
				string path = settings.Path.Trim();

				if (!File.Exists(path))
				{
					error = "no file at " + path;
					lastError = error;
					loadFailures++;
					return false;
				}

				lastWriteSeen = File.GetLastWriteTimeUtc(path);

				string text;

				// Shared read, because the thing writing this file may well be writing it
				// right now, and an exclusive open would fail for no good reason.
				using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
				using (StreamReader reader = new StreamReader(stream))
				{
					text = reader.ReadToEnd();
				}

				int accepted = Ingest(text);
				loads++;

				if (accepted == 0)
				{
					error = "read " + text.Length + " characters but no usable rows";
					lastError = error;
					return false;
				}

				return true;
			}
			catch (Exception ex)
			{
				loadFailures++;
				error = ex.Message;
				lastError = ex.Message;
				return false;
			}
		}

		/// <summary>Begin watching for changes. Live only; a backtest reads once.</summary>
		public void StartWatching()
		{
			if (running || settings.ReloadSeconds <= 0)
				return;

			running = true;

			reloader = new Thread(WatchLoop);
			reloader.IsBackground = true;
			reloader.Name = "SocratesFile-" + settings.Name;
			reloader.Start();
		}

		public void Stop()
		{
			running = false;

			Thread t = reloader;
			reloader = null;

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

		private void WatchLoop()
		{
			while (running)
			{
				// Slept in one-second slices so shutdown is prompt rather than delayed by a
				// whole reload interval.
				for (int i = 0; i < settings.ReloadSeconds && running; i++)
					Thread.Sleep(1000);

				if (!running)
					return;

				try
				{
					string path = settings.Path.Trim();

					if (!File.Exists(path))
						continue;

					// Only re-read when the file has actually changed. Polling a timestamp
					// costs nothing; re-parsing a large file every minute for no reason does.
					DateTime written = File.GetLastWriteTimeUtc(path);

					if (written <= lastWriteSeen)
						continue;

					string error;
					Load(out error);
				}
				catch (Exception ex)
				{
					lastError = ex.Message;
				}
			}
		}

		/// <summary>
		/// Parse and replace. Rows key by timestamp, so a file that is rewritten with an
		/// overlapping window updates those rows rather than duplicating them.
		/// </summary>
		private int Ingest(string text)
		{
			if (string.IsNullOrEmpty(text))
				return 0;

			Dictionary<DateTime, FileBar> merged = new Dictionary<DateTime, FileBar>();

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

				FileBar bar;

				if (!TryParseRow(line, out bar))
				{
					rowsRejected++;

					// The first bad row, kept verbatim. "412 rows rejected" tells you
					// something is wrong; the row itself tells you what.
					if (firstRejectExample.Length == 0)
						firstRejectExample = line.Length > 80 ? line.Substring(0, 80) + "..." : line;

					continue;
				}

				merged[bar.Time] = bar;
				accepted++;
			}

			if (accepted == 0)
				return 0;

			List<FileBar> ordered = new List<FileBar>(merged.Values);
			ordered.Sort(CompareByTime);

			List<double> computed = ComputeAtr(ordered, settings.AtrPeriod);

			lock (gate)
			{
				bars = ordered;
				atr = computed;
			}

			return accepted;
		}

		private static int CompareByTime(FileBar a, FileBar b)
		{
			return a.Time.CompareTo(b.Time);
		}

		/// <summary>
		/// The timestamp may be epoch seconds, epoch milliseconds, NinjaTrader's own
		/// "yyyyMMdd HHmmss", or a general date-time string. A string carrying an explicit
		/// zone is honoured; one without is read as UTC. NinjaTrader's format is the one
		/// exception - it is exchange local time by definition and is not converted.
		///
		/// That last rule is a stated contract rather than a guess, and the startup banner
		/// prints the first row's time beside the chart's first bar so a mismatch is one
		/// line rather than a week. A file an hour out does not fail - it answers every
		/// lookup from the wrong hour, and the only symptom is results quietly worse.
		/// </summary>
		private bool TryParseRow(string line, out FileBar bar)
		{
			bar = default(FileBar);

			string[] parts = line.Split(',', ';');

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
				// Past a threshold no plausible second-based stamp reaches, it is millis.
				DateTime utc = epoch > 100000000000.0
					? new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(epoch)
					: new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(epoch);

				local = ToPlatformTime(utc);
				return true;
			}

			// NinjaTrader's own import format, which is what tools/to_ninjatrader_csv.py
			// emits. It is already in exchange local time - converting it as if it were UTC
			// would move every row by the machine's offset, which is the exact failure this
			// class spends so much output warning about. So it is matched explicitly and
			// left alone.
			DateTime exact;

			if (DateTime.TryParseExact(raw, "yyyyMMdd HHmmss", CultureInfo.InvariantCulture,
					DateTimeStyles.None, out exact)
				|| DateTime.TryParseExact(raw, "yyyyMMdd", CultureInfo.InvariantCulture,
					DateTimeStyles.None, out exact))
			{
				local = settings.TimestampOffsetMinutes != 0
					? exact.AddMinutes(settings.TimestampOffsetMinutes)
					: exact;

				return true;
			}

			DateTimeOffset parsed;

			if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture,
				DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out parsed))
			{
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

		/// <summary>Wilder's ATR over the rows, so step 5's threshold scales to the source exactly as it would on a platform series.</summary>
		private static List<double> ComputeAtr(List<FileBar> series, int period)
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
		/// The most recent row at or before a time, its ATR, and the close a lookback
		/// earlier. Misses are counted by kind: before the file starts and after it ends
		/// are different problems, and telling them apart is the difference between "get
		/// more history" and "the file has stopped updating".
		/// </summary>
		public bool TryGetAt(DateTime when, int lookbackRows,
			out FileBar bar, out double barAtr, out double referenceClose)
		{
			bar = default(FileBar);
			barAtr = 0;
			referenceClose = 0;

			lock (gate)
			{
				lookups++;

				if (bars.Count == 0)
				{
					missesBefore++;
					return false;
				}

				if (when < bars[0].Time)
				{
					missesBefore++;
					return false;
				}

				int index = IndexAtOrBefore(when);

				if (index < 0)
				{
					missesBefore++;
					return false;
				}

				hits++;

				bar = bars[index];
				barAtr = index < atr.Count ? atr[index] : 0;

				int refIndex = index - Math.Max(1, lookbackRows);
				referenceClose = refIndex >= 0 ? bars[refIndex].Close : bars[0].Close;
				return true;
			}
		}

		/// <summary>How far behind the requested time the newest row is. The staleness the confirmations already know what to do with.</summary>
		public double MinutesBehind(DateTime when)
		{
			lock (gate)
			{
				if (bars.Count == 0)
					return double.MaxValue;

				DateTime newest = bars[bars.Count - 1].Time;

				if (when <= newest)
					return 0;

				double behind = (when - newest).TotalMinutes;

				if (behind > 0)
					missesAfter++;

				return behind;
			}
		}

		/// <summary>The open of the first row of the day containing a time - the reference a leader's percent move is measured from.</summary>
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
