using System;
using System.Collections.Generic;

namespace ZlbbCore
{
	/// <summary>Height, width and bounding hexagon of a machine's footprint, measured the way zlbb publishes them (see HelpView on zlbb.faendir.com).</summary>
	public sealed class GeometryMetrics
	{
		/// <summary>Number of rows the footprint spans; measured on all three axes, the smallest value is taken.</summary>
		public int Height;
		/// <summary>Number of columns, in hex widths (half hexes possible); measured on all three axes, the smallest value is taken.</summary>
		public double Width;
		/// <summary>Side length of the smallest regular hexagon of hexes that contains the whole footprint.</summary>
		public int BoundingHex;
	}

	public static class HexGeometry
	{
		/// <summary>
		/// Measures a footprint given as axial hex coordinates (q, r) - the game's HexIndex.Q / HexIndex.R. Returns null for an empty footprint.
		/// Rows run along r; a hex's horizontal position is q + r/2 (pointy hexes, rows offset by half a hex).
		/// </summary>
		public static GeometryMetrics Measure(IList<int> q, IList<int> r)
		{
			int n = q.Count;
			if (n == 0 || r.Count != n)
				return null;

			int bestHeight = int.MaxValue;
			int bestWidthTimesTwo = int.MaxValue;
			var cq = new int[n];
			var cr = new int[n];
			for (int i = 0; i < n; i++)
			{
				cq[i] = q[i];
				cr[i] = r[i];
			}
			for (int axis = 0; axis < 3; axis++)
			{
				int minR = int.MaxValue, maxR = int.MinValue, minX2 = int.MaxValue, maxX2 = int.MinValue;
				for (int i = 0; i < n; i++)
				{
					int x2 = 2 * cq[i] + cr[i]; // twice the horizontal position
					if (cr[i] < minR) minR = cr[i];
					if (cr[i] > maxR) maxR = cr[i];
					if (x2 < minX2) minX2 = x2;
					if (x2 > maxX2) maxX2 = x2;
				}
				bestHeight = Math.Min(bestHeight, maxR - minR + 1);
				bestWidthTimesTwo = Math.Min(bestWidthTimesTwo, maxX2 - minX2 + 2); // (max - min)/2 + 1, doubled
				for (int i = 0; i < n; i++) // rotate by 60 degrees: (q, r) -> (-r, q + r)
				{
					int nq = -cr[i];
					int nr = cq[i] + cr[i];
					cq[i] = nq;
					cr[i] = nr;
				}
			}

			return new GeometryMetrics { Height = bestHeight, Width = bestWidthTimesTwo / 2.0, BoundingHex = BoundingHexagon(q, r) };
		}

		/// <summary>Side length (in hexes) of the smallest hexagon of hexes, centred on a hex, that contains every hex of the footprint.</summary>
		public static int BoundingHexagon(IList<int> q, IList<int> r)
		{
			int n = q.Count;
			int minQ = int.MaxValue, maxQ = int.MinValue, minR = int.MaxValue, maxR = int.MinValue;
			for (int i = 0; i < n; i++)
			{
				if (q[i] < minQ) minQ = q[i];
				if (q[i] > maxQ) maxQ = q[i];
				if (r[i] < minR) minR = r[i];
				if (r[i] > maxR) maxR = r[i];
			}
			int best = int.MaxValue;
			for (int cq = minQ; cq <= maxQ; cq++)
			{
				for (int cr = minR; cr <= maxR; cr++)
				{
					int farthest = 0;
					for (int i = 0; i < n && farthest < best; i++)
						farthest = Math.Max(farthest, Distance(cq, cr, q[i], r[i]));
					if (farthest < best)
						best = farthest;
				}
			}
			return best + 1;
		}

		public static int Distance(int q1, int r1, int q2, int r2)
		{
			int dq = q1 - q2;
			int dr = r1 - r2;
			return (Math.Abs(dq) + Math.Abs(dr) + Math.Abs(dq + dr)) / 2;
		}
	}

	/// <summary>Where a score stands among the zlbb records of a puzzle (lower is better).</summary>
	public sealed class Standing
	{
		public int Better;
		public int Equal;
		public int Total;

		/// <summary>Ties share the best rank.</summary>
		public int Rank
		{
			get { return Better + 1; }
		}

		/// <summary>Share of the records that are at least as good, in percent (small = good).</summary>
		public double TopPercent
		{
			get { return Total == 0 ? 0 : 100.0 * Rank / Total; }
		}

		public string Describe()
		{
			double percent = TopPercent;
			string text = percent < 10 ? percent.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)
				: Math.Round(percent).ToString("0", System.Globalization.CultureInfo.InvariantCulture);
			return "#" + Rank + " of " + Total + "  (top " + text + "%)";
		}
	}
}
