using System;
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
			// values 20..24 -> the axis must run up to 24 so a score of 24 is on the chart, not off to the right
			GameHistogram g = Hist(20, 1, 0, 0, 0, 2).ToGameScale();
			Assert.Equal(24, g.Max);
			Assert.StartsWith("24,", g.ToGameString());
		}

		[Fact]
		public void Few_values_get_one_bar_per_unit_and_a_maximum_divisible_by_four()
		{
			// largest value 5 -> axis 0..8 (labels 0,2,4,6,8); value v sits in bar v-1
			GameHistogram g = Hist(1, 3, 0, 2, 0, 1).ToGameScale();
			Assert.Equal(8, g.Max);
			Assert.Equal(1, g.BarWidth);
			Assert.Equal(new[] { 3, 0, 2, 0, 1, 0, 0, 0 }, g.Counts);
			Assert.Equal("8,3,0,2,0,1,0,0,0", g.ToGameString());
		}

		[Fact]
		public void Large_ranges_use_a_fixed_whole_number_bar_width()
		{
			var counts = new int[340 - 20 + 1];  // values 20..340
			counts[0] = 1;
			counts[counts.Length - 1] = 1;
			GameHistogram g = Hist(20, counts).ToGameScale();
			Assert.True(g.BarWidth > 1);
			Assert.Equal(0, g.Max % 4);
			Assert.Equal(0, g.Max % g.BarWidth);
			Assert.Equal(g.Max, g.Counts.Length * g.BarWidth);
			Assert.True(g.Counts.Length <= Histogram.DefaultMaxBuckets + 4);
			Assert.Equal(2, g.Total);
			Assert.Equal(1, g.Counts[(340 - 1) / g.BarWidth]);
			Assert.Equal(1, g.Counts[(20 - 1) / g.BarWidth]);
		}

		[Fact]
		public void A_score_lands_in_the_same_bucket_the_game_uses_for_its_own_marker()
		{
			var counts = Enumerable.Repeat(1, 300).ToArray(); // values 1..300, one each
			GameHistogram g = Hist(1, counts).ToGameScale();
			Assert.True(g.Max >= 300);
			int buckets = g.Counts.Length;
			Assert.Equal(300, g.Total);
			// the game: index = clamp((score - 1) * buckets / max, 0, buckets - 1)
			foreach (int score in new[] { 1, 2, 77, 150, 299, 300 })
			{
				int expected = Math.Max(0, Math.Min(buckets - 1, (score - 1) * buckets / g.Max));
				Assert.Equal((score - 1) / g.BarWidth, expected);
				Assert.True(g.Counts[expected] > 0, "score " + score + " should fall into a non-empty bucket " + expected);
			}
			// every bar covers exactly BarWidth scores
			for (int i = 0; i < buckets; i++)
				Assert.True(g.Counts[i] == g.BarWidth || g.Counts[i] == 0);
		}

		[Fact]
		public void Zero_and_negative_values_are_clamped_into_the_first_bar()
		{
			GameHistogram g = Hist(-2, 1, 0, 1, 0, 1, 1).ToGameScale(); // values -2, 0, 2, 3
			Assert.Equal(4, g.Max);
			Assert.Equal(new[] { 2, 1, 1, 0 }, g.Counts);
		}

		[Fact]
		public void All_values_zero_still_gives_a_valid_scale()
		{
			GameHistogram g = Hist(0, 5).ToGameScale();
			Assert.Equal(4, g.Max);
			Assert.Equal(new[] { 5, 0, 0, 0 }, g.Counts);
			Assert.False(g.IsEmpty);
		}

		[Fact]
		public void Single_value_is_valid()
		{
			GameHistogram g = Hist(42, 7).ToGameScale();
			Assert.Equal(44, g.Max);
			Assert.Equal(7, g.Counts[41]);
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

		// ---- only the best part of the field ----

		[Fact]
		public void ValueAtFraction_finds_the_score_that_covers_that_share_of_records()
		{
			Histogram h = Hist(10, 1, 1, 1, 1); // one record each at 10, 11, 12, 13
			Assert.Equal(10, h.ValueAtFraction(0.0));
			Assert.Equal(10, h.ValueAtFraction(0.25));
			Assert.Equal(11, h.ValueAtFraction(0.5));
			Assert.Equal(12, h.ValueAtFraction(0.75));
			Assert.Equal(13, h.ValueAtFraction(1.0));
			Assert.Equal(13, h.ValueAtFraction(7.0));
		}

		[Fact]
		public void The_long_tail_of_the_worst_scores_is_cut_off()
		{
			var counts = new int[1000];
			for (int v = 1; v <= 100; v++) counts[v - 1] = 20; // the bulk: 2000 records at 1..100
			for (int v = 101; v <= 1000; v++) counts[v - 1] = 1; // a long thin tail up to 1000
			Histogram h = Hist(1, counts);

			GameHistogram all = h.ToGameScale();
			GameHistogram best = h.ToGameScale(Histogram.DefaultMaxBuckets, 0.5);

			Assert.True(all.Max >= 1000);
			Assert.Equal(2900, all.Total);
			Assert.Equal(76, best.Max);               // 50% of 2900 records lie at or below 73 -> axis rounded to 76
			Assert.Equal(76 * 20, best.Total);        // everything beyond the window is left out
			Assert.Equal(1, best.BarWidth);
		}

		[Fact]
		public void Keeping_everything_never_drops_a_record()
		{
			var counts = new int[500];
			for (int i = 0; i < counts.Length; i++) counts[i] = 1 + i % 3;
			Histogram h = Hist(7, counts);
			Assert.Equal(h.Total, h.ToGameScale(Histogram.DefaultMaxBuckets, 1.0).Total);
		}

		[Fact]
		public void Best_default_shows_a_much_narrower_axis_than_the_full_range()
		{
			var counts = new int[1130];
			for (int v = 1; v <= 150; v++) counts[v - 1] = 30;   // 4500 records in the bulk
			for (int v = 151; v <= 1130; v++) counts[v - 1] = 1; // 980 in a thin tail
			Histogram h = Hist(1, counts);
			GameHistogram best = h.ToGameScale(Histogram.DefaultMaxBuckets, Histogram.DefaultKeepFraction);
			Assert.True(best.Max < 600, "axis max was " + best.Max);
			Assert.True(best.Max >= h.ValueAtFraction(Histogram.DefaultKeepFraction));
		}

		[Theory]
		[InlineData(1, 4)]
		[InlineData(4, 4)]
		[InlineData(5, 8)]
		[InlineData(9, 12)]
		[InlineData(10, 12)]
		[InlineData(12, 12)]
		[InlineData(13, 16)]
		[InlineData(23, 24)]
		[InlineData(99, 100)]
		[InlineData(100, 120)]
		[InlineData(137, 160)]
		[InlineData(999, 1000)]
		[InlineData(1130, 1200)]
		[InlineData(10001, 12000)]
		public void Axis_maximum_is_rounded_up_to_a_tidy_number_divisible_by_four(int value, int expected)
		{
			Assert.Equal(expected, Histogram.RoundUpToNiceMaximum(value));
			Assert.Equal(0, expected % 4);
		}

		[Theory]
		[InlineData(0, 1)]
		[InlineData(1, 1)]
		[InlineData(2, 2)]
		[InlineData(3, 4)]
		[InlineData(5, 5)]
		[InlineData(6, 8)]
		[InlineData(9, 10)]
		[InlineData(11, 20)]
		[InlineData(21, 40)]
		[InlineData(41, 50)]
		[InlineData(51, 80)]
		[InlineData(81, 100)]
		[InlineData(300, 400)]
		public void Bar_width_is_a_tidy_whole_number(int minimum, int expected)
		{
			Assert.Equal(expected, Histogram.NiceBarWidth(minimum));
		}

		[Fact]
		public void Invariants_hold_for_many_random_distributions()
		{
			var random = new Random(12345);
			for (int run = 0; run < 300; run++)
			{
				int start = random.Next(-5, 60);
				var counts = new int[random.Next(1, 3000)];
				for (int i = 0; i < counts.Length; i++)
					counts[i] = random.Next(0, 4) == 0 ? random.Next(1, 30) : 0;
				counts[random.Next(counts.Length)] += 1; // never empty
				double keep = run % 3 == 0 ? 1.0 : 0.5 + random.NextDouble() * 0.5;

				Histogram h = Hist(start, counts);
				GameHistogram g = h.ToGameScale(Histogram.DefaultMaxBuckets, keep);

				Assert.True(g.Max > 0);
				Assert.Equal(0, g.Max % 4);
				Assert.Equal(0, g.Max % g.BarWidth);
				Assert.Equal(g.Max, g.Counts.Length * g.BarWidth);
				Assert.True(g.Counts.Length <= Histogram.DefaultMaxBuckets + 4, "buckets " + g.Counts.Length);

				// independent recount: a value v in 1..Max belongs to bar (v-1)/BarWidth, values <= 0 to bar 0, larger values are dropped
				var expected = new int[g.Counts.Length];
				for (int i = 0; i < counts.Length; i++)
				{
					long v = (long)start + i;
					if (counts[i] == 0 || v > g.Max) continue;
					expected[v <= 1 ? 0 : (int)((v - 1) / g.BarWidth)] += counts[i];
				}
				Assert.Equal(expected, g.Counts);
				if (keep >= 1.0)
					Assert.Equal(h.Total, g.Total);
			}
		}
	}
}
