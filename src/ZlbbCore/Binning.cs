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

		/// <summary>
		/// The string format the game reads for a histogram: "start,count0,count1,...". The game normalises by the
		/// largest count itself, so raw counts are written. Needs at least one count (the game ignores shorter strings).
		/// </summary>
		public string ToGameString()
		{
			var sb = new StringBuilder();
			sb.Append(Start.ToString(CultureInfo.InvariantCulture));
			foreach (int count in Counts)
				sb.Append(',').Append(count.ToString(CultureInfo.InvariantCulture));
			return sb.ToString();
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
