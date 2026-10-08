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

		/// <summary>The most bars the game's histogram panel shows legibly (230 px plot area, ~2 px per bar).</summary>
		public const int DefaultMaxBuckets = 125;

		/// <summary>Only the best part of the field is shown: scores up to this fraction of the records (lower is better). The long tail of the worst scores is cut off.</summary>
		public const double DefaultKeepFraction = 0.85;

		/// <summary>
		/// Smallest score such that at least <paramref name="fraction"/> of the records score at or below it (lower scores are better).
		/// </summary>
		public int ValueAtFraction(double fraction)
		{
			if (IsEmpty)
				return 0;
			long total = Total;
			long target = (long)Math.Ceiling(Math.Max(0.0, Math.Min(1.0, fraction)) * total);
			long cumulative = 0;
			for (int i = 0; i < Counts.Length; i++)
			{
				cumulative += Counts[i];
				if (cumulative >= target && cumulative > 0)
					return Start + i;
			}
			return Start + Counts.Length - 1;
		}

		/// <summary>
		/// Converts to the game's histogram scale for the best <paramref name="keepFraction"/> of the records. In the game's data the
		/// first number is the axis MAXIMUM (labels 0, max/4, max/2, 3max/4, max), followed by counts for buckets spread evenly over
		/// 0..max; a score v lands in bucket clamp((v-1)*buckets/max, 0, buckets-1) - the same integer formula the game uses to place
		/// its own markers. Every bar covers a fixed whole number of units (<see cref="GameHistogram.BarWidth"/>), the maximum is a
		/// round number divisible by 4, and scores beyond the window are not drawn (the game parks its own markers at the right edge).
		/// </summary>
		public GameHistogram ToGameScale(int maxBuckets = DefaultMaxBuckets, double keepFraction = 1.0)
		{
			if (IsEmpty)
				return new GameHistogram();
			int last = Start + Counts.Length - 1;
			int top = Math.Max(1, keepFraction < 1.0 ? Math.Min(ValueAtFraction(keepFraction), last) : last);
			top = RoundUpToNiceMaximum(top);
			int width = NiceBarWidth((top + maxBuckets - 1) / maxBuckets);
			int max = (int)Math.Min(int.MaxValue / 2, ((long)top + 4L * width - 1) / (4L * width) * (4L * width)); // divisible by 4 bars and by the bar width
			int buckets = max / width;
			var counts = new int[buckets];
			for (int i = 0; i < Counts.Length; i++)
			{
				if (Counts[i] == 0)
					continue;
				long value = (long)Start + i;
				if (value > max)
					continue; // beyond the shown window: the worst scores are intentionally left out
				long index = (value - 1) * buckets / max;
				if (index < 0) index = 0;
				if (index > buckets - 1) index = buckets - 1;
				counts[index] += Counts[i];
			}
			return new GameHistogram { Max = max, BarWidth = width, Counts = counts };
		}

		/// <summary>Rounds up so the axis labels (max/4, max/2, 3max/4) come out as whole, tidy numbers: 12, 24, 160, 1200, ...</summary>
		internal static int RoundUpToNiceMaximum(int value)
		{
			if (value < 10)
				return (value + 3) / 4 * 4;
			long unit = 4;
			for (long probe = 100; probe <= value; probe *= 10)
				unit *= 10;
			long rounded = ((long)value + unit - 1) / unit * unit;
			return (int)Math.Min(rounded, int.MaxValue / 2);
		}

		/// <summary>Smallest tidy bar width (1, 2, 4, 5, 8, 10, 20, 25, 40, 50, ...) that is at least <paramref name="minimum"/> units.</summary>
		internal static int NiceBarWidth(int minimum)
		{
			if (minimum <= 1)
				return 1;
			int[] steps = { 1, 2, 4, 5, 8 };
			for (long decade = 1; decade < int.MaxValue / 20; decade *= 10)
				foreach (int step in steps)
					if (step * decade >= minimum)
						return (int)(step * decade);
			return minimum;
		}
	}

	/// <summary>Histogram in the game's own format: axis maximum plus counts over equally wide buckets from 0 to <see cref="Max"/>.</summary>
	public sealed class GameHistogram
	{
		public int Max;
		/// <summary>How many score units one bar covers (the same for every bar: fixed resolution).</summary>
		public int BarWidth = 1;
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
		/// All metrics that have data for this puzzle, in display order: the game's own three first (Cost, Cycles, Area or Instructions),
		/// then the rest in the website's order. Metrics without data (e.g. height on production puzzles) are left out.
		/// </summary>
		public static List<Metric> DisplayOrder(bool production, Func<Metric, bool> hasData)
		{
			var order = new List<Metric>();
			foreach (Metric metric in Vanilla(production))
				if (hasData(metric))
					order.Add(metric);
			foreach (Metric metric in Metrics.All)
				if (!order.Contains(metric) && hasData(metric))
					order.Add(metric);
			return order;
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
