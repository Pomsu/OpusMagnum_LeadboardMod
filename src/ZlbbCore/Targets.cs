using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ZlbbCore
{
	/// <summary>What to aim for on one metric of one puzzle (lower is better).</summary>
	public sealed class Targets
	{
		/// <summary>The best distinct scores, best first.</summary>
		public double[] Best = new double[0];
		/// <summary>The score that puts a solve in the top 10% / top 25% of the zlbb records.</summary>
		public double Top10;
		public double Top25;
		public int Total;

		public static Targets From(List<double> values, int count)
		{
			if (values.Count == 0)
				return null;
			values.Sort();
			var best = new List<double>();
			foreach (double value in values)
			{
				if (best.Count >= count)
					break;
				if (best.Count == 0 || Math.Abs(best[best.Count - 1] - value) > 1e-9)
					best.Add(value);
			}
			return new Targets { Best = best.ToArray(), Top10 = AtFraction(values, 0.10), Top25 = AtFraction(values, 0.25), Total = values.Count };
		}

		private static double AtFraction(List<double> sorted, double fraction)
		{
			int index = (int)Math.Ceiling(fraction * sorted.Count) - 1;
			return sorted[Math.Max(0, Math.Min(sorted.Count - 1, index))];
		}

		/// <summary>"20  21  22  23  25" for the best scores.</summary>
		public string DescribeBest()
		{
			var sb = new StringBuilder();
			foreach (double value in Best)
			{
				if (sb.Length > 0)
					sb.Append("  ");
				sb.Append(Format(value));
			}
			return sb.ToString();
		}

		/// <summary>"top 10%: 87 or less   top 25%: 140 or less" (lower is better).</summary>
		public string DescribeThresholds()
		{
			return "top 10%: " + Format(Top10) + " or less   top 25%: " + Format(Top25) + " or less";
		}

		/// <summary>Whole numbers without decimals, halves with one, large numbers with a thin grouping.</summary>
		public static string Format(double value)
		{
			if (Math.Abs(value - Math.Round(value)) < 1e-9)
			{
				long whole = (long)Math.Round(value);
				return Math.Abs(whole) >= 10000 ? whole.ToString("N0", CultureInfo.InvariantCulture).Replace(",", " ") : whole.ToString(CultureInfo.InvariantCulture);
			}
			return value.ToString("0.0#", CultureInfo.InvariantCulture);
		}
	}

	/// <summary>How a solve compares with the zlbb records on cost, cycles and area (instructions on production puzzles) together.</summary>
	public sealed class Domination
	{
		/// <summary>Records at least as good on all three and strictly better on one.</summary>
		public int Dominating;
		/// <summary>Records with exactly the same three scores.</summary>
		public int Equal;
		public int Total;

		public bool OnFrontier
		{
			get { return Dominating == 0; }
		}

		public string Describe(bool production)
		{
			string metrics = production ? "cost, cycles and instructions" : "cost, cycles and area";
			if (Dominating == 0)
				return "no record beats it on " + metrics;
			return Dominating + (Dominating == 1 ? " record beats" : " records beat") + " it on " + metrics;
		}
	}
}
