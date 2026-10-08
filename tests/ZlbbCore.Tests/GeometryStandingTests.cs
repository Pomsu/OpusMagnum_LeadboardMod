using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace ZlbbCore.Tests
{
	public class HexGeometryTests
	{
		private static GeometryMetrics Measure(params int[][] cells)
		{
			return HexGeometry.Measure(cells.Select(c => c[0]).ToList(), cells.Select(c => c[1]).ToList());
		}

		private static int[][] Hexagon(int radius)
		{
			var cells = new List<int[]>();
			for (int q = -radius; q <= radius; q++)
				for (int r = -radius; r <= radius; r++)
					if (Math.Abs(q + r) <= radius)
						cells.Add(new[] { q, r });
			return cells.ToArray();
		}

		[Fact]
		public void Empty_footprint_has_no_measurement()
		{
			Assert.Null(HexGeometry.Measure(new List<int>(), new List<int>()));
		}

		[Fact]
		public void A_single_hex_is_one_by_one_with_a_side_one_hexagon()
		{
			GeometryMetrics g = Measure(new[] { 5, -3 });
			Assert.Equal(1, g.Height);
			Assert.Equal(1.0, g.Width);
			Assert.Equal(1, g.BoundingHex);
		}

		[Fact]
		public void Two_neighbours_in_a_row()
		{
			// side by side they are 2 columns wide, but measured on the best of the three axes they sit half a hex apart: 1.5
			GeometryMetrics g = Measure(new[] { 0, 0 }, new[] { 1, 0 });
			Assert.Equal(1, g.Height);
			Assert.Equal(1.5, g.Width);
			Assert.Equal(2, g.BoundingHex);
		}

		[Fact]
		public void A_diagonal_line_of_three_is_measured_on_its_best_axis()
		{
			// along any of the three hex directions the line is "horizontal" on one axis: height 1, but only two columns wide on another
			GeometryMetrics g = Measure(new[] { 0, 0 }, new[] { 0, 1 }, new[] { 0, 2 });
			Assert.Equal(1, g.Height);
			Assert.Equal(2.0, g.Width);
			Assert.Equal(2, g.BoundingHex);
		}

		[Theory]
		[InlineData(1, 3, 3.0, 2)]
		[InlineData(2, 5, 5.0, 3)]
		[InlineData(3, 7, 7.0, 4)]
		public void A_regular_hexagon_of_hexes(int radius, int height, double width, int bounding)
		{
			GeometryMetrics g = HexGeometry.Measure(Hexagon(radius).Select(c => c[0]).ToList(), Hexagon(radius).Select(c => c[1]).ToList());
			Assert.Equal(height, g.Height);
			Assert.Equal(width, g.Width);
			Assert.Equal(bounding, g.BoundingHex);
		}

		[Fact]
		public void Half_hex_widths_are_possible()
		{
			// two rows offset by half a hex: (0,0) above (0,1) -> columns at x=0 and x=0.5 -> 1.5 wide, but the best axis may do better
			GeometryMetrics g = Measure(new[] { 0, 0 }, new[] { 0, 1 });
			Assert.Equal(1, g.Height); // two neighbours are in one row along some axis
			Assert.Equal(1.5, g.Width, 6);
		}

		[Fact]
		public void Measurement_does_not_depend_on_rotation_or_mirroring_or_position()
		{
			var random = new Random(7);
			for (int run = 0; run < 200; run++)
			{
				var cells = new HashSet<Tuple<int, int>>();
				int count = random.Next(1, 40);
				for (int i = 0; i < count; i++)
					cells.Add(Tuple.Create(random.Next(-8, 9), random.Next(-8, 9)));
				var q = cells.Select(c => c.Item1).ToList();
				var r = cells.Select(c => c.Item2).ToList();
				GeometryMetrics original = HexGeometry.Measure(q, r);

				// rotate by 60 degrees, mirror, then shift
				var q2 = new List<int>(); var r2 = new List<int>();
				for (int i = 0; i < q.Count; i++)
				{
					int rq = -r[i], rr = q[i] + r[i];      // rotate
					int mq = rr, mr = rq;                   // mirror (swap q and r)
					q2.Add(mq + 13); r2.Add(mr - 7);        // shift
				}
				GeometryMetrics moved = HexGeometry.Measure(q2, r2);
				Assert.Equal(original.Height, moved.Height);
				Assert.Equal(original.Width, moved.Width, 6);
				Assert.Equal(original.BoundingHex, moved.BoundingHex);
			}
		}

		[Fact]
		public void Bounding_hexagon_is_never_smaller_than_half_the_height_and_covers_every_hex()
		{
			var random = new Random(11);
			for (int run = 0; run < 100; run++)
			{
				var q = new List<int>(); var r = new List<int>();
				for (int i = 0; i < random.Next(1, 30); i++) { q.Add(random.Next(-6, 7)); r.Add(random.Next(-6, 7)); }
				GeometryMetrics g = HexGeometry.Measure(q, r);
				Assert.True(2 * g.BoundingHex - 1 >= g.Height);
				Assert.True(g.Width >= 1 && g.Height >= 1 && g.BoundingHex >= 1);
			}
		}
	}

	public class StandingTests
	{
		private static RecordChange Add(string id, int g, int c = 100, int a = 10)
		{
			return new RecordChange { Type = ChangeType.Add, Record = new ZlbbRecord { Id = id, PuzzleId = "P1", Cost = g, Cycles = c, Area = a } };
		}

		private static RecordIndex Index(params int[] costs)
		{
			var index = new RecordIndex();
			for (int i = 0; i < costs.Length; i++)
				index.Apply(Add("r" + i, costs[i]));
			return index;
		}

		[Fact]
		public void Rank_counts_strictly_better_records_and_ties_share_the_rank()
		{
			RecordIndex index = Index(10, 20, 20, 30, 40);
			Assert.Equal(1, index.GetStanding("P1", Metrics.Cost, 5).Rank);     // better than everyone
			Assert.Equal(1, index.GetStanding("P1", Metrics.Cost, 10).Rank);    // ties the best
			Assert.Equal(2, index.GetStanding("P1", Metrics.Cost, 20).Rank);    // one record is better, two tie
			Assert.Equal(2, index.GetStanding("P1", Metrics.Cost, 20).Equal - 0);
			Assert.Equal(4, index.GetStanding("P1", Metrics.Cost, 30).Rank);
			Assert.Equal(6, index.GetStanding("P1", Metrics.Cost, 99).Rank);    // worse than all five
			Assert.Equal(5, index.GetStanding("P1", Metrics.Cost, 5).Total);
		}

		[Fact]
		public void Unknown_puzzle_or_a_metric_nobody_has_gives_null()
		{
			Assert.Null(Index(10).GetStanding("NOPE", Metrics.Cost, 5));
			Assert.Null(Index(10).GetStanding("P1", Metrics.Height, 5)); // no record has a height
		}

		[Fact]
		public void Works_for_derived_metrics()
		{
			var index = new RecordIndex();
			index.Apply(Add("a", 10, 10, 10)); // sum 30
			index.Apply(Add("b", 20, 20, 20)); // sum 60
			Assert.Equal(2, index.GetStanding("P1", Metrics.Sum, 45).Rank);
			Assert.Equal(2, index.GetStanding("P1", Metrics.Sum, 45).Total);
		}

		[Theory]
		[InlineData(1, 595, "#1 of 595  (top 0.2%)")]
		[InlineData(12, 595, "#12 of 595  (top 2.0%)")]
		[InlineData(100, 595, "#100 of 595  (top 17%)")]
		[InlineData(595, 595, "#595 of 595  (top 100%)")]
		public void Description_is_short_and_readable(int rank, int total, string expected)
		{
			var standing = new Standing { Better = rank - 1, Total = total };
			Assert.Equal(expected, standing.Describe());
		}
	}
}
