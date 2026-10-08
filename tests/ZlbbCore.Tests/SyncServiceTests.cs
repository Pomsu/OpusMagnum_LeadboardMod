using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using ZlbbCore.Tests.Support;

namespace ZlbbCore.Tests
{
	public class SyncServiceTests
	{
		private const string BootstrapPath = "/om/records/changes/2000-01-01T00:00:00Z";

		private sealed class Clock
		{
			public DateTime Now = new DateTime(2026, 10, 8, 10, 0, 0, DateTimeKind.Utc);
		}

		private sealed class Fixture : IDisposable
		{
			public readonly FakeZlbbServer Server = new FakeZlbbServer();
			public readonly TempDir Dir = new TempDir();
			public readonly Clock Clock = new Clock();
			public readonly List<string> Log = new List<string>();

			public string SnapshotPath { get { return Dir.File("om.snapshot"); } }

			public SyncService NewService()
			{
				return new SyncService(new ZlbbClient(Server.BaseUrl), SnapshotPath, () => Clock.Now, s => { lock (Log) Log.Add(s); });
			}

			public void Dispose()
			{
				Server.Dispose();
				Dir.Dispose();
			}
		}

		private static string Rec(string id, string puzzle = "P1", int cost = 100)
		{
			return TestData.RecordJson(id, puzzle, cost: cost);
		}

		private static void WaitUntil(Func<bool> condition, int timeoutMs = 5000)
		{
			var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
			while (!condition())
			{
				if (DateTime.UtcNow > deadline)
					throw new TimeoutException("condition not met");
				Thread.Sleep(10);
			}
		}

		[Fact]
		public void First_launch_downloads_everything_with_exactly_one_request()
		{
			using (var f = new Fixture())
			{
				f.Server.RespondWith(TestData.Array(TestData.Add(Rec("a")), TestData.Add(Rec("b", "P2")), TestData.Add(Rec("c"))));
				var svc = f.NewService();

				SyncOutcome outcome = svc.RunOnce();

				Assert.Equal(SyncState.Fresh, outcome.State);
				Assert.True(outcome.Bootstrap);
				Assert.Equal(3, outcome.Changes);
				Assert.Equal(3, svc.Index.Count);
				Assert.Equal(1, f.Server.RequestCount);
				RecordedRequest request = f.Server.Requests.Single();
				Assert.Equal("GET", request.Method);
				Assert.Equal(BootstrapPath, request.Path);
				Assert.Contains("ZlbbLeaderboard", request.UserAgent);
				Assert.Contains("gzip", request.AcceptEncoding);
				Assert.True(File.Exists(f.SnapshotPath));
				Assert.Equal(f.Clock.Now, svc.LastSyncUtc);
			}
		}

		[Fact]
		public void Next_launch_sends_one_small_diff_since_the_last_sync()
		{
			using (var f = new Fixture())
			{
				DateTime firstSync = f.Clock.Now;
				f.Server.RespondWith(TestData.Array(TestData.Add(Rec("a")), TestData.Add(Rec("b"))));
				f.NewService().RunOnce();

				f.Clock.Now = firstSync.AddHours(5);
				f.Server.RespondWith(TestData.Array(TestData.Remove(Rec("a")), TestData.Add(Rec("c"))));
				var second = f.NewService();
				SyncOutcome outcome = second.RunOnce();

				Assert.False(outcome.Bootstrap);
				Assert.Equal(SyncState.Fresh, outcome.State);
				Assert.Equal(2, f.Server.RequestCount);
				Assert.Equal("/om/records/changes/" + ZlbbClient.FormatSince(firstSync - SyncService.Overlap), f.Server.Requests[1].Path);
				Assert.Equal(new[] { "b", "c" }, second.Index.GetAllRecords().Select(r => r.Id).OrderBy(x => x).ToArray());
				Assert.Equal(f.Clock.Now, second.LastSyncUtc);
			}
		}

		[Fact]
		public void Nothing_is_requested_after_the_startup_sync()
		{
			using (var f = new Fixture())
			{
				f.Server.RespondWith(TestData.Array(TestData.Add(Rec("a"))));
				var svc = f.NewService();
				svc.RunOnce();
				for (int i = 0; i < 20; i++)
				{
					svc.Index.GetHistogram("P1", Metrics.Cost);
					svc.Index.GetRecords("P1");
				}
				Thread.Sleep(400);
				Assert.Equal(1, f.Server.RequestCount);
			}
		}

		[Fact]
		public void Server_down_after_a_good_sync_keeps_using_the_snapshot()
		{
			using (var f = new Fixture())
			{
				f.Server.RespondWith(TestData.Array(TestData.Add(Rec("a")), TestData.Add(Rec("b"))));
				f.NewService().RunOnce();
				f.Server.Dispose();

				f.Clock.Now = f.Clock.Now.AddDays(1);
				var svc = f.NewService();
				SyncOutcome outcome = svc.RunOnce();

				Assert.Equal(SyncState.Stale, outcome.State);
				Assert.NotNull(outcome.Error);
				Assert.Equal(2, svc.Index.Count);
				Assert.Equal(SyncState.Stale, svc.State);
				Assert.Equal(new DateTime(2026, 10, 8, 10, 0, 0, DateTimeKind.Utc), svc.LastSyncUtc); // not advanced
			}
		}

		[Fact]
		public void First_launch_offline_fails_gracefully_without_writing_a_snapshot()
		{
			using (var f = new Fixture())
			{
				f.Server.Dispose();
				var svc = f.NewService();
				SyncOutcome outcome = svc.RunOnce();
				Assert.Equal(SyncState.Failed, outcome.State);
				Assert.Equal(0, svc.Index.Count);
				Assert.False(File.Exists(f.SnapshotPath));
				Assert.Null(svc.LastSyncUtc);
			}
		}

		[Fact]
		public void Truncated_diff_leaves_the_old_snapshot_and_sync_time_intact()
		{
			using (var f = new Fixture())
			{
				f.Server.RespondWith(TestData.Array(TestData.Add(Rec("a")), TestData.Add(Rec("b"))));
				f.NewService().RunOnce();
				byte[] before = File.ReadAllBytes(f.SnapshotPath);

				f.Clock.Now = f.Clock.Now.AddHours(3);
				f.Server.Handler = r => new FakeResponse { Body = TestData.Array(TestData.Add(Rec("c")), TestData.Add(Rec("d")), TestData.Add(Rec("e"))), Truncate = true };
				var svc = f.NewService();
				SyncOutcome outcome = svc.RunOnce();

				Assert.Equal(SyncState.Stale, outcome.State);
				Assert.Equal(2, svc.Index.Count);
				Assert.Equal(before, File.ReadAllBytes(f.SnapshotPath));
			}
		}

		[Fact]
		public void Truncated_first_download_writes_no_partial_snapshot()
		{
			using (var f = new Fixture())
			{
				f.Server.Handler = r => new FakeResponse { Body = TestData.Array(TestData.Add(Rec("a")), TestData.Add(Rec("b")), TestData.Add(Rec("c"))), Truncate = true };
				var svc = f.NewService();
				SyncOutcome outcome = svc.RunOnce();
				Assert.Equal(SyncState.Failed, outcome.State);
				Assert.Equal(0, svc.Index.Count);
				Assert.False(File.Exists(f.SnapshotPath));
			}
		}

		[Fact]
		public void Http_500_is_a_clean_failure_with_a_single_request()
		{
			using (var f = new Fixture())
			{
				f.Server.Handler = r => new FakeResponse { Status = 500, Body = "oops" };
				SyncOutcome outcome = f.NewService().RunOnce();
				Assert.Equal(SyncState.Failed, outcome.State);
				Assert.Contains("500", outcome.Error);
				Assert.Equal(1, f.Server.RequestCount);
			}
		}

		[Fact]
		public void Http_429_reports_retry_after_and_is_not_retried()
		{
			using (var f = new Fixture())
			{
				f.Server.Handler = r => new FakeResponse { Status = 429, RetryAfter = "120", Body = "slow down" };
				SyncOutcome outcome = f.NewService().RunOnce();
				Assert.Equal(TimeSpan.FromSeconds(120), outcome.RetryAfter);
				Thread.Sleep(300);
				Assert.Equal(1, f.Server.RequestCount);
			}
		}

		[Fact]
		public void Gzip_compressed_answers_are_understood()
		{
			using (var f = new Fixture())
			{
				f.Server.Handler = r => new FakeResponse { Body = TestData.Array(TestData.Add(Rec("a")), TestData.Add(Rec("b"))), Gzip = true };
				var svc = f.NewService();
				Assert.Equal(SyncState.Fresh, svc.RunOnce().State);
				Assert.Equal(2, svc.Index.Count);
			}
		}

		[Fact]
		public void Unusable_entries_are_skipped_and_the_rest_is_applied()
		{
			using (var f = new Fixture())
			{
				f.Server.RespondWith("[{\"type\":\"ADD\",\"record\":{}},{\"type\":\"WEIRD\",\"record\":" + Rec("x") + "}," + TestData.Add(Rec("good")) + ",5,null]");
				var svc = f.NewService();
				SyncOutcome outcome = svc.RunOnce();
				Assert.Equal(SyncState.Fresh, outcome.State);
				Assert.Equal(1, outcome.Changes);
				Assert.Equal("good", svc.Index.GetAllRecords().Single().Id);
			}
		}

		[Fact]
		public void Invalid_json_is_a_failure_not_a_crash()
		{
			using (var f = new Fixture())
			{
				f.Server.RespondWith("<html>not json</html>");
				SyncOutcome outcome = f.NewService().RunOnce();
				Assert.Equal(SyncState.Failed, outcome.State);
			}
		}

		[Fact]
		public async Task A_slow_server_only_blocks_the_calling_thread_and_the_index_stays_readable()
		{
			using (var f = new Fixture())
			{
				f.Server.Handler = r => new FakeResponse { Body = TestData.Array(TestData.Add(Rec("a"))), DelayMs = 800 };
				var svc = f.NewService();
				Task<SyncOutcome> sync = Task.Run(() => svc.RunOnce());
				WaitUntil(() => f.Server.RequestCount == 1);

				Assert.Equal(SyncState.Running, svc.State);
				Assert.Equal(0, svc.Index.Count);
				Assert.True(svc.Index.GetHistogram("P1", Metrics.Cost).IsEmpty);

				Assert.Equal(SyncState.Fresh, (await sync).State);
				Assert.Equal(1, svc.Index.Count);
			}
		}

		[Fact]
		public async Task A_second_sync_while_one_is_running_is_refused()
		{
			using (var f = new Fixture())
			{
				f.Server.Handler = r => new FakeResponse { Body = TestData.Array(TestData.Add(Rec("a"))), DelayMs = 600 };
				var svc = f.NewService();
				Task<SyncOutcome> first = Task.Run(() => svc.RunOnce());
				WaitUntil(() => f.Server.RequestCount == 1);

				SyncOutcome second = svc.RunOnce();
				Assert.NotNull(second.Skipped);
				Assert.Equal(SyncState.Fresh, (await first).State);
				Assert.Equal(1, f.Server.RequestCount);
			}
		}

		[Fact]
		public void Manual_sync_has_a_cooldown()
		{
			using (var f = new Fixture())
			{
				f.Server.RespondWith(TestData.Array(TestData.Add(Rec("a"))));
				var svc = f.NewService();
				svc.RunOnce();
				DateTime lastSync = f.Clock.Now; // every successful sync so far happened at this moment

				Assert.Null(svc.SyncNow().Skipped);
				Assert.Equal(2, f.Server.RequestCount);

				f.Clock.Now = f.Clock.Now.AddSeconds(30);
				SyncOutcome refused = svc.SyncNow();
				Assert.Contains("30", refused.Skipped);
				Assert.Equal(2, f.Server.RequestCount);

				f.Clock.Now = f.Clock.Now.AddSeconds(31);
				Assert.Null(svc.SyncNow().Skipped);
				Assert.Equal(3, f.Server.RequestCount);
				Assert.Equal("/om/records/changes/" + ZlbbClient.FormatSince(lastSync - SyncService.Overlap), f.Server.Requests[2].Path);
			}
		}

		[Fact]
		public void Corrupt_snapshot_triggers_a_fresh_full_download()
		{
			using (var f = new Fixture())
			{
				File.WriteAllBytes(f.SnapshotPath, new byte[] { 1, 2, 3, 4, 5 });
				f.Server.RespondWith(TestData.Array(TestData.Add(Rec("a"))));
				SyncOutcome outcome = f.NewService().RunOnce();
				Assert.True(outcome.Bootstrap);
				Assert.Equal(BootstrapPath, f.Server.Requests.Single().Path);
				Assert.NotNull(SnapshotStore.Load(f.SnapshotPath));
			}
		}

		[Fact]
		public void Snapshot_dated_in_the_future_is_distrusted()
		{
			using (var f = new Fixture())
			{
				SnapshotStore.Save(f.SnapshotPath, new Snapshot { LastSyncUtc = f.Clock.Now.AddDays(30) });
				f.Server.RespondWith(TestData.Array(TestData.Add(Rec("a"))));
				SyncOutcome outcome = f.NewService().RunOnce();
				Assert.True(outcome.Bootstrap);
				Assert.Equal(BootstrapPath, f.Server.Requests.Single().Path);
			}
		}

		[Fact]
		public void Unwritable_snapshot_location_still_gives_a_working_index_but_retries_next_launch()
		{
			using (var f = new Fixture())
			{
				// a directory where the snapshot file should be makes saving fail
				Directory.CreateDirectory(f.SnapshotPath);
				f.Server.RespondWith(TestData.Array(TestData.Add(Rec("a"))));
				var svc = f.NewService();
				SyncOutcome outcome = svc.RunOnce();
				Assert.Equal(SyncState.Fresh, outcome.State);
				Assert.Equal(1, svc.Index.Count);
				Assert.Null(svc.LastSyncUtc);
				Assert.Contains(f.Log, l => l.Contains("could not save snapshot"));
			}
		}

		[Fact]
		public void Updated_event_fires_after_the_index_changed()
		{
			using (var f = new Fixture())
			{
				f.Server.RespondWith(TestData.Array(TestData.Add(Rec("a"))));
				var svc = f.NewService();
				int seen = -1;
				svc.Updated += () => seen = svc.Index.Count;
				svc.RunOnce();
				Assert.Equal(1, seen);
			}
		}

		// ---- replay of real data ----

		private static List<object> LoadFixtureRecords()
		{
			var raw = new List<object>();
			using (var reader = new StreamReader(Path.Combine(AppContext.BaseDirectory, "fixtures", "P007_frontier.json"), Encoding.UTF8))
				MiniJson.ReadArray(reader, raw.Add);
			return raw;
		}

		private static string ChangesJson(string type, IEnumerable<object> records)
		{
			var list = new List<object>();
			foreach (object record in records)
				list.Add(new Dictionary<string, object> { { "type", type }, { "record", record } });
			return TestData.Serialize(list);
		}

		[Fact]
		public void Replaying_the_real_P007_change_log_reproduces_the_golden_histograms()
		{
			using (var f = new Fixture())
			{
				List<object> raw = LoadFixtureRecords();
				f.Server.RespondWith(ChangesJson("ADD", raw));
				var svc = f.NewService();
				Assert.Equal(SyncState.Fresh, svc.RunOnce().State);
				Assert.Equal(595, svc.Index.Count);

				var golden = MiniJson.AsObject(MiniJson.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "P007_golden.json"), Encoding.UTF8)));
				var expected = MiniJson.AsObject(MiniJson.Get(golden, "metrics"));
				foreach (Metric metric in Metrics.All)
					Assert.Equal((string)expected[metric.Key], svc.Index.GetHistogram("P007", metric).ToGameString());

				// the second launch's diff removes one record: counts and memoised histograms follow
				int costBefore = svc.Index.GetHistogram("P007", Metrics.Cost).Total;
				f.Clock.Now = f.Clock.Now.AddHours(1);
				f.Server.RespondWith(ChangesJson("REMOVE", raw.Take(1)));
				var next = f.NewService();
				Assert.Equal(SyncState.Fresh, next.RunOnce().State);
				Assert.Equal(594, next.Index.Count);
				Assert.Equal(costBefore - 1, next.Index.GetHistogram("P007", Metrics.Cost).Total);
			}
		}

		[Fact]
		public void A_realistic_sized_bootstrap_is_fast_and_streams()
		{
			using (var f = new Fixture())
			{
				// ~24k records like the live change log
				var sb = new StringBuilder("[");
				for (int i = 0; i < 24000; i++)
				{
					if (i > 0) sb.Append(',');
					sb.Append(TestData.Add(TestData.RecordJson("rec-" + i, "P" + (i % 183), cost: 20 + i % 300, cycles: 100 + i % 500, area: 10 + i % 60)));
				}
				sb.Append(']');
				f.Server.RespondWith(sb.ToString());
				var svc = f.NewService();

				var watch = System.Diagnostics.Stopwatch.StartNew();
				SyncOutcome outcome = svc.RunOnce();
				watch.Stop();

				Assert.Equal(SyncState.Fresh, outcome.State);
				Assert.Equal(24000, svc.Index.Count);
				Assert.True(watch.ElapsedMilliseconds < 5000, "bootstrap took " + watch.ElapsedMilliseconds + " ms");
				Assert.Equal(24000, SnapshotStore.Load(f.SnapshotPath).Records.Count);
			}
		}
	}
}
