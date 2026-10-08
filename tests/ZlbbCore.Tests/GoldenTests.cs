using System.Collections.Generic;
using System.IO;
using System.Text;
using Xunit;

namespace ZlbbCore.Tests
{
	/// <summary>
	/// Compares the C# parsing + binning with expected histograms that scripts/golden.ps1 computed independently from the
	/// same raw API data (real P007 frontier records). Regenerate both fixtures with scripts/golden.ps1.
	/// </summary>
	public class GoldenTests
	{
		private static string FixturePath(string name)
		{
			return Path.Combine(System.AppContext.BaseDirectory, "fixtures", name);
		}

		private static List<ZlbbRecord> LoadRecords()
		{
			var records = new List<ZlbbRecord>();
			using (var reader = new StreamReader(FixturePath("P007_frontier.json"), Encoding.UTF8))
				MiniJson.ReadArray(reader, e => records.Add(ZlbbRecord.FromJson(e)));
			return records;
		}

		private static Dictionary<string, object> LoadGolden()
		{
			return MiniJson.AsObject(MiniJson.Parse(File.ReadAllText(FixturePath("P007_golden.json"), Encoding.UTF8)));
		}

		[Fact]
		public void All_frontier_records_parse()
		{
			var records = LoadRecords();
			Assert.DoesNotContain(null, records);
			Assert.Equal((int)MiniJson.GetNumber(LoadGolden(), "recordCount").Value, records.Count);
			Assert.All(records, r => Assert.Equal("P007", r.PuzzleId));
		}

		[Fact]
		public void Every_metric_matches_the_independently_computed_histogram()
		{
			var records = LoadRecords();
			var expected = MiniJson.AsObject(MiniJson.Get(LoadGolden(), "metrics"));
			Assert.Equal(Metrics.All.Count, expected.Count);
			foreach (Metric metric in Metrics.All)
			{
				string want = (string)expected[metric.Key];
				Histogram h = Binning.Build(records, metric);
				Assert.True(want == h.ToGameString(), "metric " + metric.Key + ": expected [" + want + "] but was [" + h.ToGameString() + "]");
			}
		}

		[Fact]
		public void Every_metric_matches_with_the_trackless_filter_off()
		{
			var records = LoadRecords();
			var expected = MiniJson.AsObject(MiniJson.Get(LoadGolden(), "trackless"));
			var filter = new RecordFilter { Trackless = false };
			foreach (Metric metric in Metrics.All)
			{
				string want = (string)expected[metric.Key];
				Histogram h = Binning.Build(records, metric, filter);
				Assert.True(want == h.ToGameString(), "metric " + metric.Key + " (no trackless): expected [" + want + "] but was [" + h.ToGameString() + "]");
			}
		}

		[Fact]
		public void Histogram_totals_equal_the_number_of_records_that_have_the_metric()
		{
			var records = LoadRecords();
			Assert.Equal(records.Count, Binning.Build(records, Metrics.Cost).Total);
			Assert.Equal(records.Count, Binning.Build(records, Metrics.Cycles).Total);
		}
	}
}
