using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace ZlbbCore.Tests
{
	public class DerivedMetricsTests
	{
		private static ZlbbRecord Rec(int g, int c, int a, int i, string id = "r", string puzzle = "P1", bool trackless = false)
		{
			return new ZlbbRecord { Id = id, PuzzleId = puzzle, Cost = g, Cycles = c, Area = a, Instructions = i, Trackless = trackless };
		}

		// the example records from zlbb's own category list: "60g/35c/9a/T/L (g+c+a=104)", "60g/34c/11a/5i/L (g+c+a+i=110)", "40g/82c/7a/2b/T/L (c·a=574)"
		[Fact]
		public void Sums_and_products_match_the_examples_shown_on_the_zlbb_site()
		{
			Assert.Equal(104, Metrics.Sum.Get(Rec(60, 35, 9, 0)));
			Assert.Equal(110, Metrics.Sum4.Get(Rec(60, 34, 11, 5)));
			Assert.Equal(574, Metrics.CyclesTimesArea.Get(Rec(40, 82, 7, 0)));
			Assert.Equal(2700, Metrics.CostTimesCycles.Get(Rec(50, 54, 7, 0)));
			Assert.Equal(1820, Metrics.CostTimesArea.Get(Rec(140, 15, 13, 0)));
		}

		[Fact]
		public void Production_puzzles_sum_instructions_instead_of_area()
		{
			ZlbbRecord r = Rec(120, 291, 78, 123);
			Assert.Equal(120 + 291 + 123, Metrics.SumProduction.Get(r));
			Assert.Equal(120 + 291 + 78, Metrics.Sum.Get(r));
			Assert.Same(Metrics.SumProduction, Metrics.SumFor(true));
			Assert.Same(Metrics.Sum, Metrics.SumFor(false));
		}

		[Fact]
		public void Instruction_products_exist_for_production_puzzles()
		{
			ZlbbRecord r = Rec(100, 200, 30, 12);
			Assert.Equal(1200, Metrics.CostTimesInstructions.Get(r));
			Assert.Equal(2400, Metrics.CyclesTimesInstructions.Get(r));
		}

		[Fact]
		public void Derived_metrics_are_found_by_key_and_are_not_part_of_the_twelve_raw_ones()
		{
			foreach (string key in new[] { "sum", "sumP", "sum4", "gc", "ga", "ca", "gi", "ci" })
				Assert.NotNull(Metrics.ByKey(key));
			Assert.Equal(12, Metrics.All.Count);
			Assert.DoesNotContain(Metrics.Sum, Metrics.All);
		}

		[Fact]
		public void Derived_metrics_per_puzzle_type()
		{
			Assert.Equal("sum4,gc,ga,ca", string.Join(",", Metrics.DerivedAfterSum(false).Select(m => m.Key)));
			Assert.Equal("gc,gi,ci", string.Join(",", Metrics.DerivedAfterSum(true).Select(m => m.Key)));
		}

		[Fact]
		public void Products_can_be_huge_without_overflowing()
		{
			ZlbbRecord r = Rec(2000000, 2000000, 5, 0); // 4e12 does not fit an int
			double? product = Metrics.CostTimesCycles.Get(r);
			Assert.Equal(4e12, product.Value, 3);
			// such a value is simply left out of the histogram instead of corrupting it
			Assert.Empty(Binning.SortedValues(new[] { r }, Metrics.CostTimesCycles));
		}
	}

	public class GameScaleFromValuesTests
	{
		private static List<int> Sorted(params int[] values)
		{
			var list = values.ToList();
			list.Sort();
			return list;
		}

		[Fact]
		public void Products_in_the_millions_keep_all_their_data_with_a_wide_fixed_bar_width()
		{
			var values = Enumerable.Range(1, 3000).Select(i => i * 1700).ToList(); // up to 5,100,000
			GameHistogram g = GameScale.Build(values);
			Assert.Equal(3000, g.Total);
			Assert.True(g.Max >= 5100000);
			Assert.True(g.BarWidth >= 40000);
			Assert.Equal(0, g.Max % 4);
			Assert.Equal(g.Max, g.Counts.Length * g.BarWidth);
		}

		[Fact]
		public void The_best_share_cuts_the_tail_of_a_huge_range()
		{
			var values = new List<int>();
			for (int i = 0; i < 900; i++) values.Add(1000 + i);          // the bulk: 1000..1899
			for (int i = 0; i < 100; i++) values.Add(1000000 + i * 5000); // a very long tail
			values.Sort();
			GameHistogram g = GameScale.Build(values, Histogram.DefaultMaxBuckets, 0.85);
			Assert.True(g.Max < 3000, "axis max was " + g.Max);
			Assert.Equal(900, g.Total);
		}

		[Fact]
		public void Empty_input_gives_an_empty_histogram()
		{
			Assert.True(GameScale.Build(new List<int>()).IsEmpty);
		}

		[Fact]
		public void Same_result_as_the_integer_histogram_route()
		{
			var values = Sorted(20, 22, 22, 23, 20, 20, 340, 77, 78, 79, 150);
			var histogram = new Histogram { Start = 20, Counts = new int[321] };
			foreach (int v in values) histogram.Counts[v - 20]++;
			GameHistogram a = GameScale.Build(values, Histogram.DefaultMaxBuckets, 0.85);
			GameHistogram b = histogram.ToGameScale(Histogram.DefaultMaxBuckets, 0.85);
			Assert.Equal(a.ToGameString(), b.ToGameString());
		}
	}

	public class RecordIndexGameHistogramTests
	{
		private static RecordChange Add(string id, string puzzle, int g, int c, int a)
		{
			return new RecordChange { Type = ChangeType.Add, Record = new ZlbbRecord { Id = id, PuzzleId = puzzle, Cost = g, Cycles = c, Area = a } };
		}

		[Fact]
		public void Is_memoised_until_the_puzzle_changes_and_isolated_per_puzzle()
		{
			var index = new RecordIndex();
			index.ApplyAll(new[] { Add("a", "P1", 10, 100, 5), Add("b", "P1", 12, 90, 6), Add("c", "P2", 50, 50, 50) });
			GameHistogram first = index.GetGameHistogram("P1", Metrics.Sum);
			Assert.Same(first, index.GetGameHistogram("P1", Metrics.Sum));
			GameHistogram other = index.GetGameHistogram("P2", Metrics.Sum);

			index.Apply(Add("d", "P1", 11, 95, 5));
			GameHistogram second = index.GetGameHistogram("P1", Metrics.Sum);
			Assert.NotSame(first, second);
			Assert.Equal(3, second.Total);
			Assert.Same(other, index.GetGameHistogram("P2", Metrics.Sum));
		}

		[Fact]
		public void Sum_histogram_counts_each_record_once()
		{
			var index = new RecordIndex();
			for (int i = 0; i < 40; i++)
				index.Apply(Add("r" + i, "P1", 10 + i, 100 + i, 5));
			GameHistogram sums = index.GetGameHistogram("P1", Metrics.Sum, null, Histogram.DefaultMaxBuckets, 1.0);
			Assert.Equal(40, sums.Total);
			Assert.True(sums.Max >= 115 + 2 * 39);
		}

		[Fact]
		public void Unknown_puzzle_is_empty()
		{
			Assert.True(new RecordIndex().GetGameHistogram("NOPE", Metrics.Sum).IsEmpty);
		}
	}
}
