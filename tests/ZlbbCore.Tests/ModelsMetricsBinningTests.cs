using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace ZlbbCore.Tests
{
	public class ModelsTests
	{
		// Shape taken from a real GET /om/puzzle/P007/records?includeFrontier=true entry (unknown extra fields included on purpose).
		public const string RecordJson = @"{""id"":""40g-6i-143c-12a"",""puzzle"":{""id"":""P007"",""displayName"":""Stabilized Water"",""group"":{""id"":""CHAPTER_1""},""type"":""NORMAL""},
			""score"":{""cost"":40,""instructions"":6,""overlap"":false,""trackless"":true,""cycles"":143,""area"":12,""height"":4,""width"":4.0,""boundingHex"":3,""rate"":24.0,
			""areaINFLevel"":0,""areaINFValue"":12.0,""heightINF"":4.0,""widthINF"":4.0,""boundingHexINF"":3.0,""somethingNew"":1},
			""gif"":""http://x"",""author"":null,""futureField"":[1,2]}";

		[Fact]
		public void Record_maps_all_fields_and_ignores_unknown_ones()
		{
			ZlbbRecord r = ZlbbRecord.FromJson(MiniJson.Parse(RecordJson));
			Assert.Equal("40g-6i-143c-12a", r.Id);
			Assert.Equal("P007", r.PuzzleId);
			Assert.Equal(40, r.Cost);
			Assert.Equal(6, r.Instructions);
			Assert.Equal(143, r.Cycles);
			Assert.Equal(12, r.Area);
			Assert.True(r.Trackless);
			Assert.False(r.Overlap);
			Assert.Equal(4.0, r.Height);
			Assert.Equal(24.0, r.Rate);
			Assert.Null(r.Author);
		}

		[Fact]
		public void Record_keeps_null_metrics_as_null_not_zero()
		{
			ZlbbRecord r = ZlbbRecord.FromJson(MiniJson.Parse(
				@"{""id"":""x"",""puzzle"":{""id"":""P077""},""score"":{""cost"":120,""instructions"":123,""cycles"":291,""area"":78,""overlap"":false,""trackless"":false,""height"":null,""width"":null,""boundingHex"":null,""rate"":46.0}}"));
			Assert.Null(r.Height);
			Assert.Null(r.Width);
			Assert.Null(r.BoundingHex);
			Assert.Equal(46.0, r.Rate);
		}

		[Theory]
		[InlineData("{}")]
		[InlineData(@"{""id"":""x""}")]
		[InlineData(@"{""id"":""x"",""puzzle"":{}}")]
		[InlineData(@"{""id"":"""",""puzzle"":{""id"":""P1""},""score"":{}}")]
		[InlineData("5")]
		[InlineData("null")]
		public void Record_returns_null_for_unusable_input(string json)
		{
			Assert.Null(ZlbbRecord.FromJson(MiniJson.Parse(json)));
		}

		[Fact]
		public void Change_parses_add_and_remove_and_skips_unknown_entries()
		{
			string rec = ModelsTests.RecordJson;
			var add = RecordChange.FromJson(MiniJson.Parse(@"{""type"":""ADD"",""record"":" + rec + "}"));
			var remove = RecordChange.FromJson(MiniJson.Parse(@"{""type"":""REMOVE"",""record"":" + rec + "}"));
			Assert.Equal(ChangeType.Add, add.Type);
			Assert.Equal(ChangeType.Remove, remove.Type);
			Assert.Equal("P007", add.Record.PuzzleId);
			Assert.Null(RecordChange.FromJson(MiniJson.Parse(@"{""type"":""UPDATE"",""record"":" + rec + "}")));
			Assert.Null(RecordChange.FromJson(MiniJson.Parse(@"{""type"":""ADD"",""record"":{}}")));
			Assert.Null(RecordChange.FromJson(MiniJson.Parse(@"{""record"":" + rec + "}")));
		}
	}

	public class MetricsTests
	{
		private static ZlbbRecord Rec(int g = 10, int i = 2, int c = 100, int a = 5, bool overlap = false, bool trackless = false)
		{
			return new ZlbbRecord { Id = "r", PuzzleId = "P1", Cost = g, Instructions = i, Cycles = c, Area = a, Overlap = overlap, Trackless = trackless };
		}

		[Fact]
		public void There_are_twelve_unique_metrics_with_the_websites_keys()
		{
			Assert.Equal(new[] { "g", "i", "c", "a", "h", "w", "b", "r", "aI", "hI", "wI", "bI" }, Metrics.All.Select(m => m.Key).ToArray());
			Assert.Equal(12, Metrics.All.Select(m => m.Name).Distinct().Count());
			Assert.Same(Metrics.Cycles, Metrics.ByKey("c"));
			Assert.Null(Metrics.ByKey("zz"));
		}

		[Fact]
		public void Core_metrics_read_the_record()
		{
			ZlbbRecord r = Rec(g: 40, i: 6, c: 143, a: 12);
			Assert.Equal(40, Metrics.Cost.Get(r));
			Assert.Equal(6, Metrics.Instructions.Get(r));
			Assert.Equal(143, Metrics.Cycles.Get(r));
			Assert.Equal(12, Metrics.Area.Get(r));
		}

		[Fact]
		public void Missing_optional_metrics_are_null()
		{
			ZlbbRecord r = Rec();
			foreach (string key in new[] { "h", "w", "b", "r", "aI", "hI", "wI", "bI" })
				Assert.Null(Metrics.ByKey(key).Get(r));
		}

		[Theory]
		[InlineData(0, 12.0, 12.0)]
		[InlineData(1, 3.5, 350000.0)]
		[InlineData(2, 2.0, 2.0e10)]
		public void Area_at_infinity_scales_by_ten_to_the_five_times_level(double level, double value, double expected)
		{
			ZlbbRecord r = Rec();
			r.AreaInfLevel = level;
			r.AreaInfValue = value;
			Assert.Equal(expected, Metrics.AreaInf.Get(r).Value, 6);
		}

		[Fact]
		public void Area_at_infinity_needs_both_parts()
		{
			ZlbbRecord r = Rec();
			r.AreaInfValue = 5;
			Assert.Null(Metrics.AreaInf.Get(r));
		}

		[Fact]
		public void Non_finite_values_are_treated_as_missing()
		{
			ZlbbRecord r = Rec();
			r.Rate = double.PositiveInfinity;
			r.Width = double.NaN;
			Assert.Null(Metrics.Rate.Get(r));
			Assert.Null(Metrics.Width.Get(r));
		}

		[Fact]
		public void Filter_matches_modifiers()
		{
			var trackless = Rec(trackless: true);
			var normal = Rec();
			var overlap = Rec(overlap: true);
			Assert.True(new RecordFilter().Matches(trackless));
			Assert.True(new RecordFilter { Trackless = true }.Matches(trackless));
			Assert.False(new RecordFilter { Trackless = true }.Matches(normal));
			Assert.True(new RecordFilter { Trackless = false, Overlap = false }.Matches(normal));
			Assert.False(new RecordFilter { Overlap = false }.Matches(overlap));
		}
	}

	public class BinningTests
	{
		private static ZlbbRecord Cost(int g, bool trackless = false)
		{
			return new ZlbbRecord { Id = "r" + g, PuzzleId = "P1", Cost = g, Trackless = trackless };
		}

		private static List<ZlbbRecord> Costs(params int[] g)
		{
			return g.Select(v => Cost(v)).ToList();
		}

		[Fact]
		public void Empty_input_gives_empty_histogram()
		{
			Histogram h = Binning.Build(new ZlbbRecord[0], Metrics.Cost);
			Assert.True(h.IsEmpty);
			Assert.Equal(0, h.Total);
		}

		[Fact]
		public void Counts_values_per_consecutive_integer_starting_at_the_minimum()
		{
			Histogram h = Binning.Build(Costs(20, 22, 22, 23, 20, 20), Metrics.Cost);
			Assert.Equal(20, h.Start);
			Assert.Equal(new[] { 3, 0, 2, 1 }, h.Counts);
			Assert.Equal(6, h.Total);
		}

		[Fact]
		public void Single_value_gives_one_bin()
		{
			Histogram h = Binning.Build(Costs(7, 7, 7), Metrics.Cost);
			Assert.Equal(7, h.Start);
			Assert.Equal(new[] { 3 }, h.Counts);
		}

		[Fact]
		public void Game_string_is_start_then_raw_counts()
		{
			Assert.Equal("20,3,0,2,1", Binning.Build(Costs(20, 22, 22, 23, 20, 20), Metrics.Cost).ToGameString());
		}

		[Fact]
		public void Records_without_the_metric_are_skipped_not_counted_as_zero()
		{
			var records = new List<ZlbbRecord> { Cost(10), Cost(10) };
			Histogram h = Binning.Build(records, Metrics.Height);
			Assert.True(h.IsEmpty);

			records[0].Height = 4;
			records[1].Height = 5;
			h = Binning.Build(records, Metrics.Height);
			Assert.Equal(4, h.Start);
			Assert.Equal(new[] { 1, 1 }, h.Counts);
		}

		[Fact]
		public void Half_values_round_away_from_zero()
		{
			var records = new List<ZlbbRecord> { Cost(1), Cost(1), Cost(1) };
			records[0].Width = 3.5;
			records[1].Width = 4.0;
			records[2].Width = 4.4;
			Histogram h = Binning.Build(records, Metrics.Width);
			Assert.Equal(4, h.Start);
			Assert.Equal(new[] { 3 }, h.Counts);
		}

		[Fact]
		public void Filter_restricts_which_records_are_counted()
		{
			var records = new List<ZlbbRecord> { Cost(10, trackless: true), Cost(11), Cost(11), Cost(12, trackless: true) };
			Histogram h = Binning.Build(records, Metrics.Cost, new RecordFilter { Trackless = false });
			Assert.Equal(11, h.Start);
			Assert.Equal(new[] { 2 }, h.Counts);
		}

		[Fact]
		public void Range_above_max_bins_drops_the_upper_outliers()
		{
			Histogram h = Binning.Build(Costs(10, 11, 12, 500), Metrics.Cost, maxBins: 5);
			Assert.Equal(10, h.Start);
			Assert.Equal(new[] { 1, 1, 1, 0, 0 }, h.Counts);
			Assert.Equal(3, h.Total);
		}

		[Fact]
		public void Hard_cap_protects_against_absurd_ranges()
		{
			Histogram h = Binning.Build(Costs(1, 1000000000), Metrics.Cost);
			Assert.Equal(Binning.HardMaxBins, h.Counts.Length);
			Assert.Equal(1, h.Total);
		}

		[Fact]
		public void Negative_start_is_supported()
		{
			var records = new List<ZlbbRecord> { Cost(1), Cost(1) };
			records[0].Height = -2;
			records[1].Height = 0;
			Histogram h = Binning.Build(records, Metrics.Height);
			Assert.Equal(-2, h.Start);
			Assert.Equal(new[] { 1, 0, 1 }, h.Counts);
			Assert.Equal("-2,1,0,1", h.ToGameString());
		}

		[Fact]
		public void Result_is_deterministic_regardless_of_input_order()
		{
			var a = Binning.Build(Costs(5, 3, 9, 3), Metrics.Cost).ToGameString();
			var b = Binning.Build(Costs(3, 3, 9, 5), Metrics.Cost).ToGameString();
			Assert.Equal(a, b);
		}
	}
}
