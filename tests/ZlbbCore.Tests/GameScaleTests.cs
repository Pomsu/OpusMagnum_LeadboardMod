using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace ZlbbCore.Tests
{
	public class GameScaleTests
	{
		private static Histogram Hist(int start, params int[] counts)
		{
			return new Histogram { Start = start, Counts = counts };
		}

		[Fact]
		public void Empty_histogram_stays_empty()
		{
			Assert.True(new Histogram().ToGameScale().IsEmpty);
		}

		[Fact]
		public void First_number_is_the_axis_maximum_not_the_start_value()
		{
			// values 20..24 -> the axis must run to 24 so a score of 24 sits at the right edge, not off-chart
			GameHistogram g = Hist(20, 1, 0, 0, 0, 2).ToGameScale();
			Assert.Equal(24, g.Max);
			Assert.StartsWith("24,", g.ToGameString());
		}

		[Fact]
		public void Few_values_use_one_bucket_per_unit_from_zero()
		{
			// max 5 -> 5 buckets over 0..5; value v lands in bucket (v-1)*5/5 = v-1
			GameHistogram g = Hist(1, 3, 0, 2, 0, 1).ToGameScale();
			Assert.Equal(5, g.Max);
			Assert.Equal(new[] { 3, 0, 2, 0, 1 }, g.Counts);
			Assert.Equal("5,3,0,2,0,1", g.ToGameString());
		}

		[Fact]
		public void Large_ranges_are_squeezed_into_at_most_115_buckets()
		{
			var counts = new int[340 - 20 + 1];  // values 20..340
			counts[0] = 1;                       // value 20
			counts[counts.Length - 1] = 1;       // value 340
			GameHistogram g = Hist(20, counts).ToGameScale();
			Assert.Equal(340, g.Max);
			Assert.Equal(GameScaleConstants.Buckets, g.Counts.Length);
			Assert.Equal(2, g.Total);
			Assert.Equal(1, g.Counts[g.Counts.Length - 1]); // the largest value is in the last bucket
		}

		[Fact]
		public void A_score_lands_in_the_same_bucket_the_game_uses_for_its_own_marker()
		{
			// the game: index = clamp((score - 1) * buckets / max, 0, buckets - 1)
			var counts = new int[300];
			for (int i = 0; i < counts.Length; i++) counts[i] = 1; // values 1..300, one each
			GameHistogram g = Hist(1, counts).ToGameScale();
			Assert.Equal(300, g.Max);
			int buckets = g.Counts.Length;
			foreach (int score in new[] { 1, 2, 77, 150, 299, 300 })
			{
				int expected = System.Math.Max(0, System.Math.Min(buckets - 1, (score - 1) * buckets / g.Max));
				Assert.True(g.Counts[expected] > 0, "score " + score + " should fall into a non-empty bucket " + expected);
			}
			Assert.Equal(300, g.Total);
		}

		[Fact]
		public void Zero_and_negative_values_are_clamped_into_the_first_bucket()
		{
			GameHistogram g = Hist(-2, 1, 0, 1, 0, 1, 1).ToGameScale(); // values -2,0,2,3
			Assert.Equal(3, g.Max);
			Assert.Equal(4, g.Total);
			Assert.True(g.Counts[0] >= 2);
		}

		[Fact]
		public void All_values_zero_still_gives_a_valid_scale()
		{
			GameHistogram g = Hist(0, 5).ToGameScale();
			Assert.Equal(1, g.Max);
			Assert.Equal(new[] { 5 }, g.Counts);
			Assert.False(g.IsEmpty);
		}

		[Fact]
		public void Single_value_is_valid_and_sits_in_the_last_bucket()
		{
			GameHistogram g = Hist(42, 7).ToGameScale();
			Assert.Equal(42, g.Max);
			Assert.Equal(7, g.Counts[g.Counts.Length - 1]);
			Assert.Equal(7, g.Total);
		}

		[Fact]
		public void Output_satisfies_what_the_game_requires()
		{
			// ScoreManager.method_1378 needs >= 2 comma separated ints; class_140.method_321 needs max > 0 and at least one bucket
			GameHistogram g = Hist(20, 1, 0, 0, 0, 2).ToGameScale();
			string[] parts = g.ToGameString().Split(',');
			Assert.True(parts.Length >= 2);
			Assert.All(parts, p => Assert.True(int.TryParse(p, out _)));
			Assert.True(int.Parse(parts[0]) > 0);
		}
	}

	internal static class GameScaleConstants
	{
		public const int Buckets = Histogram.DefaultMaxBuckets;
	}

	public class MetricPagesTests
	{
		private static List<string> Keys(IEnumerable<Metric[]> pages)
		{
			return pages.Select(p => string.Join(",", p.Select(m => m.Key))).ToList();
		}

		[Fact]
		public void Vanilla_trio_depends_on_the_puzzle_type()
		{
			Assert.Equal(new[] { "g", "c", "a" }, MetricPages.Vanilla(false).Select(m => m.Key).ToArray());
			Assert.Equal(new[] { "g", "c", "i" }, MetricPages.Vanilla(true).Select(m => m.Key).ToArray());
		}

		[Fact]
		public void Normal_puzzle_gets_three_extra_pages_covering_the_other_nine_metrics()
		{
			var pages = MetricPages.ExtraPages(false, m => true);
			Assert.Equal(new[] { "i,h,w", "b,r,aI", "hI,wI,bI" }, Keys(pages));
		}

		[Fact]
		public void Production_puzzle_swaps_instructions_and_area()
		{
			var pages = MetricPages.ExtraPages(true, m => true);
			Assert.Equal(new[] { "a,h,w", "b,r,aI", "hI,wI,bI" }, Keys(pages));
		}

		[Fact]
		public void Metrics_without_data_are_left_out_and_no_page_is_empty()
		{
			var noGeometry = new HashSet<string> { "h", "w", "b", "hI", "wI", "bI" };
			var pages = MetricPages.ExtraPages(true, m => !noGeometry.Contains(m.Key));
			Assert.Equal(new[] { "a,r,aI" }, Keys(pages));
			Assert.Empty(MetricPages.ExtraPages(false, m => false));
		}

		[Fact]
		public void Every_metric_appears_exactly_once_across_all_pages()
		{
			foreach (bool production in new[] { false, true })
			{
				var all = MetricPages.Vanilla(production).Concat(MetricPages.ExtraPages(production, m => true).SelectMany(p => p)).Select(m => m.Key).ToList();
				Assert.Equal(12, all.Count);
				Assert.Equal(12, all.Distinct().Count());
			}
		}
	}
}
