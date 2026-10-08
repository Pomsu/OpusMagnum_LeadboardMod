using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;
using ZlbbCore.Tests.Support;

namespace ZlbbCore.Tests
{
	public class TargetsTests
	{
		private static RecordIndex Index(params int[] costs)
		{
			var index = new RecordIndex();
			for (int i = 0; i < costs.Length; i++)
				index.Apply(new RecordChange { Type = ChangeType.Add, Record = new ZlbbRecord { Id = "r" + i, PuzzleId = "P1", Cost = costs[i], Cycles = 100, Area = 10 } });
			return index;
		}

		[Fact]
		public void Best_are_the_lowest_distinct_scores_and_thresholds_follow_the_distribution()
		{
			// 20 records: 10,10,11,12,...,28  (one duplicate at the top)
			int[] costs = new[] { 10, 10 }.Concat(Enumerable.Range(11, 18)).ToArray();
			Targets t = Index(costs).GetTargets("P1", Metrics.Cost);
			Assert.Equal(new double[] { 10, 11, 12, 13, 14 }, t.Best);
			Assert.Equal(20, t.Total);
			Assert.Equal(10, t.Top10);   // 10% of 20 records = the 2nd best record, which is the duplicate 10
			Assert.Equal(13, t.Top25);   // 25% of 20 = the 5th best record (10, 10, 11, 12, 13)
		}

		[Fact]
		public void Fewer_records_than_requested_is_fine()
		{
			Targets t = Index(30, 40).GetTargets("P1", Metrics.Cost, 5);
			Assert.Equal(new double[] { 30, 40 }, t.Best);
			Assert.Equal(30, t.Top10);
			Assert.Equal(30, t.Top25);
		}

		[Fact]
		public void Unknown_puzzle_or_a_metric_without_data_gives_null()
		{
			Assert.Null(Index(30).GetTargets("NOPE", Metrics.Cost));
			Assert.Null(Index(30).GetTargets("P1", Metrics.Height));
		}

		[Fact]
		public void Results_are_memoised_until_the_puzzle_changes()
		{
			RecordIndex index = Index(10, 20, 30);
			Targets first = index.GetTargets("P1", Metrics.Cost);
			Assert.Same(first, index.GetTargets("P1", Metrics.Cost));
			index.Apply(new RecordChange { Type = ChangeType.Add, Record = new ZlbbRecord { Id = "new", PuzzleId = "P1", Cost = 5 } });
			Targets second = index.GetTargets("P1", Metrics.Cost);
			Assert.NotSame(first, second);
			Assert.Equal(5, second.Best[0]);
		}

		[Theory]
		[InlineData(20, "20")]
		[InlineData(3.5, "3.5")]
		[InlineData(4.25, "4.25")]
		[InlineData(12345, "12 345")]
		[InlineData(9999, "9999")]
		public void Numbers_are_formatted_for_display(double value, string expected)
		{
			Assert.Equal(expected, Targets.Format(value));
		}

		[Fact]
		public void Descriptions_read_well()
		{
			Targets t = new Targets { Best = new double[] { 20, 21, 22 }, Top10 = 87, Top25 = 140, Total = 200 };
			Assert.Equal("20  21  22", t.DescribeBest());
			Assert.Equal("top 10%: 87 or less   top 25%: 140 or less", t.DescribeThresholds());
		}
	}

	public class DominationTests
	{
		private static RecordIndex Index(params int[][] gca)
		{
			var index = new RecordIndex();
			for (int i = 0; i < gca.Length; i++)
				index.Apply(new RecordChange { Type = ChangeType.Add, Record = new ZlbbRecord { Id = "r" + i, PuzzleId = "P1", Cost = gca[i][0], Cycles = gca[i][1], Area = gca[i][2], Instructions = gca[i][3] } });
			return index;
		}

		[Fact]
		public void A_solve_on_the_frontier_is_beaten_by_no_record()
		{
			RecordIndex index = Index(new[] { 10, 100, 10, 0 }, new[] { 20, 50, 10, 0 }, new[] { 30, 30, 30, 0 });
			Domination d = index.GetDomination("P1", false, 15, 60, 8); // cheaper than #2/#3, faster than #1, smaller than all
			Assert.True(d.OnFrontier);
			Assert.Equal(0, d.Dominating);
			Assert.Equal(3, d.Total);
		}

		[Fact]
		public void Records_that_are_at_least_as_good_everywhere_count_as_beating_the_solve()
		{
			RecordIndex index = Index(new[] { 10, 100, 10, 0 }, new[] { 20, 50, 10, 0 }, new[] { 30, 30, 30, 0 });
			Domination d = index.GetDomination("P1", false, 25, 60, 12);
			Assert.Equal(1, d.Dominating); // only {20,50,10} is no worse in all three
			Assert.False(d.OnFrontier);
		}

		[Fact]
		public void An_identical_record_is_a_tie_not_a_beating()
		{
			RecordIndex index = Index(new[] { 10, 100, 10, 0 });
			Domination d = index.GetDomination("P1", false, 10, 100, 10);
			Assert.Equal(0, d.Dominating);
			Assert.Equal(1, d.Equal);
			Assert.True(d.OnFrontier);
		}

		[Fact]
		public void Production_puzzles_compare_instructions_instead_of_area()
		{
			RecordIndex index = Index(new[] { 10, 100, 99, 5 });
			Assert.Equal(1, index.GetDomination("P1", true, 20, 200, 6).Dominating);   // instructions 5 <= 6, area is ignored
			Assert.Equal(0, index.GetDomination("P1", false, 20, 200, 6).Dominating);  // area 99 > 6
		}

		[Fact]
		public void Unknown_puzzle_gives_null_and_descriptions_name_the_metrics()
		{
			Assert.Null(Index().GetDomination("NOPE", false, 1, 1, 1));
			Assert.Equal("no record beats it on cost, cycles and area", new Domination { Total = 5 }.Describe(false));
			Assert.Equal("1 record beats it on cost, cycles and instructions", new Domination { Dominating = 1 }.Describe(true));
			Assert.Equal("12 records beat it on cost, cycles and area", new Domination { Dominating = 12 }.Describe(false));
		}
	}

	public class BarRangeTests
	{
		[Fact]
		public void Bar_ranges_tile_the_axis_without_gaps()
		{
			GameHistogram g = GameScale.Build(Enumerable.Range(1, 600).ToList());
			Assert.True(g.BarWidth > 1);
			int previousTo = 0;
			for (int i = 0; i < g.Counts.Length; i++)
			{
				int from, to;
				g.BarRange(i, out from, out to);
				if (i == 0)
				{
					Assert.Equal(0, from);          // the first bar also takes everything at or below zero
					Assert.Equal(g.BarWidth, to);
				}
				else
				{
					Assert.Equal(previousTo + 1, from);               // no gap and no overlap
					Assert.Equal(g.BarWidth, to - from + 1);          // every other bar covers exactly BarWidth scores
				}
				previousTo = to;
			}
			Assert.Equal(g.Max, previousTo);
		}

		[Fact]
		public void Every_score_is_in_the_bar_whose_range_contains_it_and_the_count_matches()
		{
			var values = Enumerable.Range(1, 600).Select(v => v).ToList();
			GameHistogram g = GameScale.Build(values);
			foreach (int score in new[] { 1, 2, 77, 250, 599, 600 })
			{
				int bar = g.BarOf(score);
				int from, to;
				g.BarRange(bar, out from, out to);
				Assert.InRange(score, from, to);
				Assert.True(g.Counts[bar] > 0);
			}
		}

		[Fact]
		public void A_score_beyond_the_shown_range_has_no_bar()
		{
			GameHistogram g = GameScale.Build(new List<int> { 1, 2, 3, 4, 5 });
			Assert.Equal(-1, g.BarOf(g.Max + 1));
			Assert.Equal(0, g.BarOf(0));
			Assert.Equal(-1, new GameHistogram().BarOf(5));
		}
	}

	public class RedownloadTests
	{
		private static string Add(string id, string puzzle = "P1")
		{
			return TestData.Add(TestData.RecordJson(id, puzzle));
		}

		[Fact]
		public void Redownload_asks_for_the_whole_history_and_replaces_the_data()
		{
			using (var server = new FakeZlbbServer())
			using (var dir = new TempDir())
			{
				DateTime clock = new DateTime(2026, 10, 8, 10, 0, 0, DateTimeKind.Utc);
				var service = new SyncService(new ZlbbClient(server.BaseUrl), dir.File("om.snapshot"), () => clock);
				server.RespondWith(TestData.Array(Add("old1"), Add("old2")));
				service.RunOnce();
				Assert.Equal(2, service.Index.Count);

				clock = clock.AddMinutes(5);
				server.RespondWith(TestData.Array(Add("new1"), Add("new2"), Add("new3")));
				SyncOutcome outcome = service.RedownloadAll();

				Assert.Equal(SyncState.Fresh, outcome.State);
				Assert.True(outcome.Bootstrap);
				Assert.Equal("/om/records/changes/2000-01-01T00:00:00Z", server.Requests.Last().Path);
				Assert.Equal(new[] { "new1", "new2", "new3" }, service.Index.GetAllRecords().Select(r => r.Id).OrderBy(x => x).ToArray());
				Assert.Equal(3, SnapshotStore.Load(dir.File("om.snapshot")).Records.Count);
			}
		}

		[Fact]
		public void A_failed_redownload_keeps_the_old_data()
		{
			using (var server = new FakeZlbbServer())
			using (var dir = new TempDir())
			{
				DateTime clock = new DateTime(2026, 10, 8, 10, 0, 0, DateTimeKind.Utc);
				var service = new SyncService(new ZlbbClient(server.BaseUrl), dir.File("om.snapshot"), () => clock);
				server.RespondWith(TestData.Array(Add("old1"), Add("old2")));
				service.RunOnce();

				clock = clock.AddMinutes(5);
				server.Handler = r => new FakeResponse { Status = 503, Body = "down" };
				SyncOutcome outcome = service.RedownloadAll();

				Assert.Equal(SyncState.Stale, outcome.State);
				Assert.Equal(2, service.Index.Count);
				Assert.Equal(2, SnapshotStore.Load(dir.File("om.snapshot")).Records.Count);
			}
		}

		[Fact]
		public void Redownload_shares_the_manual_cooldown_and_a_later_normal_sync_is_a_small_diff_again()
		{
			using (var server = new FakeZlbbServer())
			using (var dir = new TempDir())
			{
				DateTime clock = new DateTime(2026, 10, 8, 10, 0, 0, DateTimeKind.Utc);
				var service = new SyncService(new ZlbbClient(server.BaseUrl), dir.File("om.snapshot"), () => clock);
				server.RespondWith(TestData.Array(Add("a")));
				service.RunOnce();
				Assert.Null(service.RedownloadAll().Skipped);
				Assert.NotNull(service.RedownloadAll().Skipped); // within 60 s
				clock = clock.AddMinutes(2);
				SyncOutcome later = service.SyncNow();
				Assert.Null(later.Skipped);
				Assert.False(later.Bootstrap);                    // the forced full download does not stick to later syncs
				Assert.NotEqual("/om/records/changes/2000-01-01T00:00:00Z", server.Requests.Last().Path);
			}
		}
	}
}
