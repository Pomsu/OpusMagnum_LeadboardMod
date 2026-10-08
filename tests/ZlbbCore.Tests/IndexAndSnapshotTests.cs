using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using ZlbbCore.Tests.Support;

namespace ZlbbCore.Tests
{
	public class RecordIndexTests
	{
		private static ZlbbRecord Rec(string id, string puzzle = "P1", int cost = 10)
		{
			return new ZlbbRecord { Id = id, PuzzleId = puzzle, Cost = cost };
		}

		private static RecordChange Add(ZlbbRecord r) { return new RecordChange { Type = ChangeType.Add, Record = r }; }
		private static RecordChange Remove(ZlbbRecord r) { return new RecordChange { Type = ChangeType.Remove, Record = r }; }

		[Fact]
		public void Add_is_idempotent_and_replaces_by_id()
		{
			var index = new RecordIndex();
			index.Apply(Add(Rec("a", cost: 10)));
			index.Apply(Add(Rec("a", cost: 99)));
			Assert.Equal(1, index.Count);
			Assert.Equal(99, index.GetRecords("P1").Single().Cost);
		}

		[Fact]
		public void Remove_of_unknown_id_or_puzzle_is_a_noop()
		{
			var index = new RecordIndex();
			index.Apply(Add(Rec("a")));
			index.Apply(Remove(Rec("zzz")));
			index.Apply(Remove(Rec("a", "OTHER")));
			Assert.Equal(1, index.Count);
		}

		[Fact]
		public void Add_remove_add_ends_up_with_the_record()
		{
			var index = new RecordIndex();
			index.ApplyAll(new[] { Add(Rec("a")), Remove(Rec("a")), Add(Rec("a")) });
			Assert.Equal(1, index.Count);
			index.ApplyAll(new[] { Add(Rec("a")), Remove(Rec("a")) });
			Assert.Equal(0, index.Count);
			Assert.Equal(0, index.PuzzleCount);
		}

		[Fact]
		public void Replaying_the_same_changes_twice_changes_nothing()
		{
			var changes = new[] { Add(Rec("a")), Add(Rec("b", "P2")), Remove(Rec("a")), Add(Rec("c")) };
			var once = new RecordIndex();
			once.ApplyAll(changes);
			var twice = new RecordIndex();
			twice.ApplyAll(changes);
			twice.ApplyAll(changes);
			Assert.Equal(once.Count, twice.Count);
			Assert.Equal(once.GetAllRecords().Select(r => r.Id).OrderBy(x => x), twice.GetAllRecords().Select(r => r.Id).OrderBy(x => x));
		}

		[Fact]
		public void Histograms_are_memoised_until_that_puzzle_changes()
		{
			var index = new RecordIndex();
			index.ApplyAll(new[] { Add(Rec("a", "P1", 10)), Add(Rec("b", "P2", 20)) });
			Histogram first = index.GetHistogram("P1", Metrics.Cost);
			Assert.Same(first, index.GetHistogram("P1", Metrics.Cost));
			Histogram other = index.GetHistogram("P2", Metrics.Cost);

			index.Apply(Add(Rec("c", "P1", 11)));
			Histogram second = index.GetHistogram("P1", Metrics.Cost);
			Assert.NotSame(first, second);
			Assert.Equal(2, second.Total);
			Assert.Same(other, index.GetHistogram("P2", Metrics.Cost));
		}

		[Fact]
		public void Different_filters_are_cached_separately()
		{
			var index = new RecordIndex();
			index.Apply(Add(new ZlbbRecord { Id = "a", PuzzleId = "P1", Cost = 5, Trackless = true }));
			index.Apply(Add(new ZlbbRecord { Id = "b", PuzzleId = "P1", Cost = 6 }));
			Assert.Equal(2, index.GetHistogram("P1", Metrics.Cost).Total);
			Assert.Equal(1, index.GetHistogram("P1", Metrics.Cost, new RecordFilter { Trackless = false }).Total);
			Assert.Equal(1, index.GetHistogram("P1", Metrics.Cost, new RecordFilter { Trackless = true }).Total);
		}

		[Fact]
		public void Unknown_puzzle_gives_an_empty_histogram()
		{
			Assert.True(new RecordIndex().GetHistogram("NOPE", Metrics.Cost).IsEmpty);
		}

		[Fact]
		public async Task Concurrent_writes_and_reads_do_not_throw()
		{
			var index = new RecordIndex();
			var writer = Task.Run(() =>
			{
				for (int i = 0; i < 5000; i++)
					index.Apply(Add(Rec("r" + i, "P" + (i % 5), i % 50)));
			});
			var reader = Task.Run(() =>
			{
				for (int i = 0; i < 2000; i++)
				{
					index.GetHistogram("P" + (i % 5), Metrics.Cost);
					index.GetRecords("P1");
				}
			});
			await Task.WhenAll(writer, reader);
			Assert.Equal(5000, index.Count);
		}
	}

	public class SnapshotStoreTests
	{
		private static Snapshot Sample()
		{
			return new Snapshot
			{
				LastSyncUtc = new DateTime(2026, 10, 8, 9, 30, 15, DateTimeKind.Utc),
				Records =
				{
					new ZlbbRecord { Id = "full", PuzzleId = "P007", Author = "someone", Cost = 40, Instructions = 6, Cycles = 143, Area = 12, Overlap = true, Trackless = true,
						Height = 4, Width = 3.5, BoundingHex = 3, Rate = 24.5, AreaInfLevel = 1, AreaInfValue = 2.5, HeightInf = 4, WidthInf = 3.5, BoundingHexInf = 3 },
					new ZlbbRecord { Id = "sparse ä∑", PuzzleId = "P077", Cost = 120, Instructions = 123, Cycles = 291, Area = 78 },
				}
			};
		}

		[Fact]
		public void Round_trip_keeps_every_field_including_nulls()
		{
			using (var dir = new TempDir())
			{
				SnapshotStore.Save(dir.File("s.bin"), Sample());
				Snapshot loaded = SnapshotStore.Load(dir.File("s.bin"));
				Assert.NotNull(loaded);
				Assert.Equal(Sample().LastSyncUtc, loaded.LastSyncUtc);
				Assert.Equal(DateTimeKind.Utc, loaded.LastSyncUtc.Kind);
				ZlbbRecord full = loaded.Records[0];
				Assert.Equal("someone", full.Author);
				Assert.True(full.Overlap && full.Trackless);
				Assert.Equal(3.5, full.Width);
				Assert.Equal(2.5, full.AreaInfValue);
				ZlbbRecord sparse = loaded.Records[1];
				Assert.Equal("sparse ä∑", sparse.Id);
				Assert.Null(sparse.Author);
				Assert.Null(sparse.Height);
				Assert.Null(sparse.AreaInfLevel);
				Assert.Equal(78, sparse.Area);
			}
		}

		[Fact]
		public void Missing_file_loads_as_null()
		{
			using (var dir = new TempDir())
				Assert.Null(SnapshotStore.Load(dir.File("nope.bin")));
		}

		[Theory]
		[InlineData("truncate")]
		[InlineData("badmagic")]
		[InlineData("trailing")]
		[InlineData("badschema")]
		[InlineData("empty")]
		[InlineData("garbage")]
		public void Damaged_files_load_as_null(string damage)
		{
			using (var dir = new TempDir())
			{
				string path = dir.File("s.bin");
				SnapshotStore.Save(path, Sample());
				byte[] bytes = File.ReadAllBytes(path);
				switch (damage)
				{
					case "truncate": bytes = bytes.Take(bytes.Length - 7).ToArray(); break;
					case "badmagic": bytes[0] ^= 0xFF; break;
					case "trailing": bytes = bytes.Concat(new byte[] { 1, 2, 3 }).ToArray(); break;
					case "badschema": bytes[4] = 99; break;
					case "empty": bytes = new byte[0]; break;
					case "garbage": bytes = Enumerable.Range(0, 500).Select(i => (byte)(i * 31)).ToArray(); break;
				}
				File.WriteAllBytes(path, bytes);
				Assert.Null(SnapshotStore.Load(path));
			}
		}

		[Fact]
		public void Save_replaces_an_existing_snapshot_and_leaves_no_temp_file()
		{
			using (var dir = new TempDir())
			{
				string path = dir.File("s.bin");
				SnapshotStore.Save(path, Sample());
				var second = Sample();
				second.Records.RemoveAt(1);
				SnapshotStore.Save(path, second);
				Assert.Single(SnapshotStore.Load(path).Records);
				Assert.False(File.Exists(path + ".tmp"));
			}
		}

		[Fact]
		public void A_leftover_temp_file_from_a_crash_is_ignored()
		{
			using (var dir = new TempDir())
			{
				string path = dir.File("s.bin");
				SnapshotStore.Save(path, Sample());
				File.WriteAllBytes(path + ".tmp", new byte[] { 1, 2, 3 });
				Assert.Equal(2, SnapshotStore.Load(path).Records.Count);
				SnapshotStore.Save(path, Sample()); // overwrites the stale temp
				Assert.Equal(2, SnapshotStore.Load(path).Records.Count);
			}
		}
	}
}
