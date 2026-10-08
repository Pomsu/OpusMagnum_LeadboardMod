using System;
using System.IO;
using System.Linq;
using Xunit;
using ZlbbCore.Tests.Support;

namespace ZlbbCore.Tests
{
	/// <summary>
	/// zlbb's maintainer asked that a bug must never lead to repeated downloads of the whole history. These tests cover every way the mod
	/// could end up wanting to download it again and again.
	/// </summary>
	public class DownloadGuardTests
	{
		private const string BootstrapPath = "/om/records/changes/2000-01-01T00:00:00Z";

		private sealed class Fixture : IDisposable
		{
			public readonly FakeZlbbServer Server = new FakeZlbbServer();
			public readonly TempDir Dir = new TempDir();
			public DateTime Now = new DateTime(2026, 10, 8, 10, 0, 0, DateTimeKind.Utc);
			public string SnapshotPath { get { return Dir.File("om.snapshot"); } }
			public string StampPath { get { return SnapshotPath + ".pending"; } }

			public Fixture()
			{
				Server.RespondWith(TestData.Array(TestData.Add(TestData.RecordJson("a", "P1"))));
			}

			public SyncService Service()
			{
				return new SyncService(new ZlbbClient(Server.BaseUrl), SnapshotPath, () => Now);
			}

			public int FullDownloads
			{
				get { return Server.Requests.Count(r => r.Path == BootstrapPath); }
			}

			public void Dispose()
			{
				Server.Dispose();
				Dir.Dispose();
			}
		}

		[Fact]
		public void A_normal_first_start_downloads_once_and_leaves_no_marker_behind()
		{
			using (var f = new Fixture())
			{
				Assert.Equal(SyncState.Fresh, f.Service().RunOnce().State);
				Assert.Equal(1, f.FullDownloads);
				Assert.False(File.Exists(f.StampPath));
				// every later start is only a small diff
				for (int i = 0; i < 5; i++)
				{
					f.Now = f.Now.AddHours(1);
					f.Service().RunOnce();
				}
				Assert.Equal(1, f.FullDownloads);
			}
		}

		[Fact]
		public void Starting_offline_for_the_first_time_does_not_block_the_next_attempt()
		{
			using (var f = new Fixture())
			{
				f.Server.Handler = r => new FakeResponse { Status = 503, Body = "down" };
				Assert.Equal(SyncState.Failed, f.Service().RunOnce().State);
				Assert.False(File.Exists(f.StampPath));          // nothing was downloaded, so nothing to protect

				f.Server.RespondWith(TestData.Array(TestData.Add(TestData.RecordJson("a", "P1"))));
				f.Now = f.Now.AddMinutes(10);
				Assert.Equal(SyncState.Fresh, f.Service().RunOnce().State);   // the next start downloads normally
			}
		}

		[Fact]
		public void A_download_that_cannot_be_stored_is_not_repeated_at_every_start()
		{
			using (var f = new Fixture())
			{
				Directory.CreateDirectory(f.SnapshotPath);       // a folder where the snapshot file belongs: saving fails
				var first = f.Service();
				first.RunOnce();
				Assert.Equal(1, f.FullDownloads);
				Assert.Equal(1, first.Index.Count);              // still usable during this session
				Assert.True(File.Exists(f.StampPath));

				// the following starts (next minutes, hours) must not fetch the history again
				foreach (int minutes in new[] { 1, 30, 200 })
				{
					f.Now = f.Now.AddMinutes(minutes);
					SyncOutcome outcome = f.Service().RunOnce();
					Assert.Equal(SyncState.Failed, outcome.State);
					Assert.Contains("could not be stored", outcome.Error);
				}
				Assert.Equal(1, f.FullDownloads);
			}
		}

		[Fact]
		public void After_six_hours_one_more_automatic_attempt_is_allowed()
		{
			using (var f = new Fixture())
			{
				Directory.CreateDirectory(f.SnapshotPath);
				f.Service().RunOnce();
				f.Now = f.Now.Add(SyncService.PendingBlock).AddMinutes(1);
				f.Service().RunOnce();
				Assert.Equal(2, f.FullDownloads);
			}
		}

		[Fact]
		public void The_players_own_re_download_button_overrides_the_wait()
		{
			using (var f = new Fixture())
			{
				Directory.CreateDirectory(f.SnapshotPath);
				f.Service().RunOnce();
				f.Now = f.Now.AddMinutes(5);
				f.Service().RedownloadAll();
				Assert.Equal(2, f.FullDownloads);
			}
		}

		[Fact]
		public void A_marker_from_the_future_or_with_garbage_is_ignored()
		{
			using (var f = new Fixture())
			{
				File.WriteAllText(f.StampPath, "not a number");
				Assert.Equal(SyncState.Fresh, f.Service().RunOnce().State);
				Assert.Equal(1, f.FullDownloads);
			}
			using (var f = new Fixture())
			{
				File.WriteAllText(f.StampPath, f.Now.AddDays(30).Ticks.ToString());
				Assert.Equal(SyncState.Fresh, f.Service().RunOnce().State);
				Assert.Equal(1, f.FullDownloads);
			}
		}

		[Fact]
		public void A_snapshot_that_is_written_but_cannot_be_read_back_does_not_loop_either()
		{
			using (var f = new Fixture())
			{
				// first start works normally and stores a snapshot
				f.Service().RunOnce();
				// the stored snapshot gets damaged (disk problem, another program): the next start has to download everything again ...
				File.WriteAllBytes(f.SnapshotPath, new byte[] { 1, 2, 3 });
				f.Now = f.Now.AddHours(1);
				f.Service().RunOnce();
				Assert.Equal(2, f.FullDownloads);
				Assert.NotNull(SnapshotStore.Load(f.SnapshotPath));   // ... and then it is healthy again
				f.Now = f.Now.AddHours(1);
				f.Service().RunOnce();
				Assert.Equal(2, f.FullDownloads);                     // so no third download
			}
		}

		[Fact]
		public void A_read_only_cache_folder_blocks_even_the_players_own_re_download()
		{
			using (var f = new Fixture())
			{
				File.WriteAllText(Path.Combine(f.Dir.Path, "blocker"), "x");   // the cache "folder" is really a file
				var service = new SyncService(new ZlbbClient(f.Server.BaseUrl), Path.Combine(f.Dir.Path, "blocker", "om.snapshot"), () => f.Now);
				Assert.Equal(SyncState.Failed, service.RedownloadAll().State);
				Assert.Equal(0, f.Server.RequestCount);
			}
		}

		[Fact]
		public void The_manual_buttons_cannot_be_hammered()
		{
			using (var f = new Fixture())
			{
				var service = f.Service();
				service.RunOnce();
				int requestsBefore = f.Server.RequestCount;
				for (int i = 0; i < 20; i++)
				{
					service.SyncNow();
					service.RedownloadAll();
				}
				Assert.Equal(requestsBefore + 1, f.Server.RequestCount);   // only the first click within a minute makes a request
			}
		}
	}
}
