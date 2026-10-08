using System;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;
using ZlbbCore.Tests.Support;

namespace ZlbbCore.Tests
{
	/// <summary>Regression tests for problems found in the independent code review.</summary>
	public class ReviewFixTests
	{
		private static string Rec(string id)
		{
			return TestData.Add(TestData.RecordJson(id, "P1"));
		}

		[Fact]
		public void Numbers_beyond_double_range_become_infinity_instead_of_aborting_the_parse()
		{
			Assert.Equal(double.PositiveInfinity, MiniJson.Parse("1e999"));
			Assert.Equal(double.NegativeInfinity, MiniJson.Parse("-1e999"));
			// and a record carrying one is still readable; the infinite value is simply ignored by the metrics
			ZlbbRecord r = ZlbbRecord.FromJson(MiniJson.Parse(TestData.RecordJson("x", "P1", rate: "1e999")));
			Assert.NotNull(r);
			Assert.Null(Metrics.Rate.Get(r));
		}

		[Theory]
		[InlineData("-")]
		[InlineData("e5")]
		[InlineData("1e")]
		[InlineData("--1")]
		public void Malformed_numbers_are_still_rejected(string text)
		{
			Assert.Throws<JsonFormatException>(() => MiniJson.Parse(text));
		}

		[Fact]
		public void An_answer_larger_than_the_byte_limit_is_refused_without_changing_anything()
		{
			using (var f = new SyncFixture())
			{
				f.Server.RespondWith(TestData.Array(Enumerable.Range(0, 200).Select(i => Rec("r" + i)).ToArray()));
				var client = new ZlbbClient(f.Server.BaseUrl) { MaxResponseBytes = 5000 };
				var service = new SyncService(client, f.SnapshotPath);
				SyncOutcome outcome = service.RunOnce();
				Assert.Equal(SyncState.Failed, outcome.State);
				Assert.Contains("larger than", outcome.Error);
				Assert.Equal(0, service.Index.Count);
				Assert.False(File.Exists(f.SnapshotPath));
			}
		}

		[Fact]
		public void An_answer_with_too_many_entries_is_refused()
		{
			using (var f = new SyncFixture())
			{
				f.Server.RespondWith(TestData.Array(Enumerable.Range(0, 50).Select(i => Rec("r" + i)).ToArray()));
				var client = new ZlbbClient(f.Server.BaseUrl) { MaxEntries = 10 };
				SyncOutcome outcome = new SyncService(client, f.SnapshotPath).RunOnce();
				Assert.Equal(SyncState.Failed, outcome.State);
				Assert.Contains("entries", outcome.Error);
			}
		}

		[Fact]
		public void The_sync_time_follows_the_servers_clock_when_the_pc_clock_is_wrong()
		{
			using (var f = new SyncFixture())
			{
				f.Server.RespondWith(TestData.Array(Rec("a")));
				DateTime wrongPcClock = DateTime.UtcNow.AddHours(5); // a PC clock five hours fast
				var service = new SyncService(new ZlbbClient(f.Server.BaseUrl), f.SnapshotPath, () => wrongPcClock) { PreferServerClock = true };
				Assert.Equal(SyncState.Fresh, service.RunOnce().State);
				Assert.True(Math.Abs((service.LastSyncUtc.Value - DateTime.UtcNow).TotalMinutes) < 5, "last sync was " + service.LastSyncUtc);

				// the next launch asks for changes since the SERVER's time minus the overlap, not since the fast local clock
				var next = new SyncService(new ZlbbClient(f.Server.BaseUrl), f.SnapshotPath, () => wrongPcClock) { PreferServerClock = true };
				next.RunOnce();
				string since = f.Server.Requests.Last().Path.Substring("/om/records/changes/".Length);
				DateTime asked = DateTime.Parse(since, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal);
				Assert.True(Math.Abs((asked - (DateTime.UtcNow - SyncService.Overlap)).TotalMinutes) < 5, "asked for changes since " + since);
			}
		}

		[Fact]
		public void Without_the_flag_the_local_clock_is_still_used()
		{
			using (var f = new SyncFixture())
			{
				f.Server.RespondWith(TestData.Array(Rec("a")));
				DateTime fake = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);
				var service = new SyncService(new ZlbbClient(f.Server.BaseUrl), f.SnapshotPath, () => fake);
				service.RunOnce();
				Assert.Equal(fake, service.LastSyncUtc);
			}
		}

		[Fact]
		public void A_cache_location_that_cannot_be_written_means_no_download_at_all()
		{
			using (var f = new SyncFixture())
			{
				f.Server.RespondWith(TestData.Array(Rec("a"), Rec("b")));
				// the "folder" for the cache is really a file, so nothing can be created there
				File.WriteAllText(Path.Combine(f.Dir.Path, "blocker"), "x");
				var service = new SyncService(new ZlbbClient(f.Server.BaseUrl), Path.Combine(f.Dir.Path, "blocker", "om.snapshot"));
				SyncOutcome outcome = service.RunOnce();
				Assert.NotEqual(SyncState.Running, service.State);
				Assert.Equal(SyncState.Failed, outcome.State);
				Assert.Contains("cannot be written", outcome.Error);
				Assert.Equal(0, f.Server.RequestCount);   // the 22 MB history is never fetched if it could not be kept
			}
		}

		[Fact]
		public void Saving_over_an_existing_snapshot_keeps_one_consistent_file()
		{
			using (var dir = new TempDir())
			{
				string path = dir.File("s.bin");
				for (int round = 1; round <= 5; round++)
				{
					var snapshot = new Snapshot { LastSyncUtc = new DateTime(2026, 1, round, 0, 0, 0, DateTimeKind.Utc) };
					for (int i = 0; i < round; i++)
						snapshot.Records.Add(new ZlbbRecord { Id = "r" + i, PuzzleId = "P" });
					SnapshotStore.Save(path, snapshot);
					Snapshot loaded = SnapshotStore.Load(path);
					Assert.Equal(round, loaded.Records.Count);
					Assert.False(File.Exists(path + ".tmp"));
				}
			}
		}

		// a small reusable fixture (server + temp folder)
		private sealed class SyncFixture : IDisposable
		{
			public readonly FakeZlbbServer Server = new FakeZlbbServer();
			public readonly TempDir Dir = new TempDir();
			public string SnapshotPath { get { return Dir.File("om.snapshot"); } }

			public void Dispose()
			{
				Server.Dispose();
				Dir.Dispose();
			}
		}
	}
}
