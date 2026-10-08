using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ZlbbCore
{
	/// <summary>Counts per consecutive integer value, starting at <see cref="Start"/> (same shape the game's histogram uses).</summary>
	public sealed class Histogram
	{
		public int Start;
		public int[] Counts = new int[0];

		public int Total
		{
			get
			{
				int total = 0;
				foreach (int count in Counts)
					total += count;
				return total;
			}
		}

		public bool IsEmpty
		{
			get { return Counts.Length == 0; }
		}

		/// <summary>Plain "start,count0,count1,..." (one count per consecutive integer). NOT what the game reads - see <see cref="ToGameScale"/>.</summary>
		public string ToGameString()
		{
			var sb = new StringBuilder();
			sb.Append(Start.ToString(CultureInfo.InvariantCulture));
			foreach (int count in Counts)
				sb.Append(',').Append(count.ToString(CultureInfo.InvariantCulture));
			return sb.ToString();
		}

		/// <summary>The maximum number of bars the game's histogram panel can show legibly (258 px panel, 230 px plot).</summary>
		public const int DefaultMaxBuckets = 115;

		/// <summary>
		/// Converts to the game's histogram scale. In the game's data the first number is the axis MAXIMUM (labels 0, max/4, max/2,
		/// 3max/4, max), followed by counts for buckets spread evenly over 0..max; a score v lands in bucket
		/// clamp((v-1)*buckets/max, 0, buckets-1) - the same integer formula the game uses to place its own markers.
		/// </summary>
		public GameHistogram ToGameScale(int maxBuckets = DefaultMaxBuckets)
		{
			if (IsEmpty)
				return new GameHistogram();
			int last = Start + Counts.Length - 1;
			int max = Math.Max(last, 1);
			int buckets = Math.Max(1, Math.Min(max, maxBuckets));
			var counts = new int[buckets];
			for (int i = 0; i < Counts.Length; i++)
			{
				if (Counts[i] == 0)
					continue;
				long value = (long)Start + i;
				long index = (value - 1) * buckets / max;
				if (index < 0) index = 0;
				if (index > buckets - 1) index = buckets - 1;
				counts[index] += Counts[i];
			}
			return new GameHistogram { Max = max, Counts = counts };
		}
	}

	/// <summary>Histogram in the game's own format: axis maximum plus counts over equally wide buckets from 0 to <see cref="Max"/>.</summary>
	public sealed class GameHistogram
	{
		public int Max;
		public int[] Counts = new int[0];

		public bool IsEmpty
		{
			get { return Counts.Length == 0 || Max <= 0; }
		}

		public int Total
		{
			get
			{
				int total = 0;
				foreach (int count in Counts)
					total += count;
				return total;
			}
		}

		/// <summary>"max,count0,count1,..." - the exact string format ScoreManager reads. The game normalises by the largest count.</summary>
		public string ToGameString()
		{
			var sb = new StringBuilder();
			sb.Append(Max.ToString(CultureInfo.InvariantCulture));
			foreach (int count in Counts)
				sb.Append(',').Append(count.ToString(CultureInfo.InvariantCulture));
			return sb.ToString();
		}
	}

	/// <summary>Which metrics go on which page. Page 0 is the game's own trio (Cost, Cycles, Area or Instructions); the rest follow.</summary>
	public static class MetricPages
	{
		public const int PanelsPerPage = 3;

		/// <summary>The three metrics the game shows by itself for a puzzle.</summary>
		public static Metric[] Vanilla(bool production)
		{
			return new[] { Metrics.Cost, Metrics.Cycles, production ? Metrics.Instructions : Metrics.Area };
		}

		/// <summary>
		/// The pages after the game's own page: every other metric that has data for this puzzle, in the website's order, three per page.
		/// Metrics without any data (e.g. height on production puzzles) are left out, so no page is empty.
		/// </summary>
		public static List<Metric[]> ExtraPages(bool production, Func<Metric, bool> hasData)
		{
			var vanilla = new List<Metric>(Vanilla(production));
			var pages = new List<Metric[]>();
			var current = new List<Metric>();
			foreach (Metric metric in Metrics.All)
			{
				if (vanilla.Contains(metric) || !hasData(metric))
					continue;
				current.Add(metric);
				if (current.Count == PanelsPerPage)
				{
					pages.Add(current.ToArray());
					current.Clear();
				}
			}
			if (current.Count > 0)
				pages.Add(current.ToArray());
			return pages;
		}
	}

	public static class Binning
	{
		/// <summary>Hard safety cap so one absurd value can never allocate a gigantic array.</summary>
		public const int HardMaxBins = 5000;

		/// <summary>
		/// Builds a histogram of <paramref name="metric"/> over the records that pass <paramref name="filter"/>.
		/// Values are rounded to the nearest integer (one bin per integer, like the game). Records without the metric are skipped.
		/// If the range exceeds <paramref name="maxBins"/> (or the hard cap) the upper outliers are dropped, keeping the lowest values.
		/// </summary>
		public static Histogram Build(IEnumerable<ZlbbRecord> records, Metric metric, RecordFilter filter = null, int maxBins = 0)
		{
			var values = new List<int>();
			foreach (ZlbbRecord record in records)
			{
				if (filter != null && !filter.Matches(record))
					continue;
				double? value = metric.Get(record);
				if (!value.HasValue || Math.Abs(value.Value) > int.MaxValue / 2)
					continue;
				values.Add((int)Math.Round(value.Value, MidpointRounding.AwayFromZero));
			}
			if (values.Count == 0)
				return new Histogram();

			int min = int.MaxValue;
			int max = int.MinValue;
			foreach (int v in values)
			{
				if (v < min) min = v;
				if (v > max) max = v;
			}

			int limit = maxBins > 0 ? Math.Min(maxBins, HardMaxBins) : HardMaxBins;
			long range = (long)max - min + 1;
			if (range > limit)
			{
				max = min + limit - 1;
				range = limit;
			}

			var counts = new int[range];
			foreach (int v in values)
				if (v <= max)
					counts[v - min]++;
			return new Histogram { Start = min, Counts = counts };
		}
	}
}
