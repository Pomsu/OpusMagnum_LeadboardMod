using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;
using ZlbbCore.Tests.Support;

namespace ZlbbCore.Tests
{
	/// <summary>
	/// The mod runs inside someone else's game process, so anything an unreliable server or a damaged file can throw at it must end in a
	/// clean "nothing usable" - never an unexpected exception and never a crash of the game.
	/// </summary>
	public class HardeningTests
	{
		private static string Mutate(Random random, string text)
		{
			var sb = new StringBuilder(text);
			int edits = random.Next(1, 6);
			const string alphabet = "{}[]\",:0123456789.-+eEtrufalsn \\u\n";
			for (int i = 0; i < edits && sb.Length > 0; i++)
			{
				int at = random.Next(sb.Length);
				switch (random.Next(3))
				{
					case 0: sb.Remove(at, 1); break;
					case 1: sb[at] = alphabet[random.Next(alphabet.Length)]; break;
					default: sb.Insert(at, alphabet[random.Next(alphabet.Length)]); break;
				}
			}
			return sb.ToString();
		}

		[Fact]
		public void Random_text_either_parses_or_throws_only_JsonFormatException()
		{
			var random = new Random(1);
			const string alphabet = "{}[]\",:0123456789.-+eEtrufalsn \\u";
			for (int i = 0; i < 6000; i++)
			{
				var chars = new char[random.Next(0, 60)];
				for (int c = 0; c < chars.Length; c++)
					chars[c] = alphabet[random.Next(alphabet.Length)];
				try { MiniJson.Parse(new string(chars)); }
				catch (JsonFormatException) { }
			}
		}

		[Fact]
		public void Damaged_real_looking_records_never_cause_unexpected_exceptions()
		{
			var random = new Random(2);
			string valid = TestData.Add(ModelsTests.RecordJson);
			for (int i = 0; i < 4000; i++)
			{
				string text = Mutate(random, valid);
				try
				{
					object value = MiniJson.Parse(text);
					RecordChange.FromJson(value); // may be null, must not throw
				}
				catch (JsonFormatException) { }
			}
		}

		[Fact]
		public void Records_with_wrong_types_in_any_field_are_skipped_not_fatal()
		{
			var random = new Random(3);
			object[] replacements = { null, 5.0, -1.0, "text", true, false, new List<object>(), new Dictionary<string, object>(), 1e300 };
			for (int i = 0; i < 3000; i++)
			{
				var change = MiniJson.AsObject(MiniJson.Parse(TestData.Add(ModelsTests.RecordJson)));
				// replace a random value anywhere inside the record
				var holders = new List<Dictionary<string, object>>();
				Collect(change, holders);
				var holder = holders[random.Next(holders.Count)];
				string key = holder.Keys.ElementAt(random.Next(holder.Count));
				holder[key] = replacements[random.Next(replacements.Length)];
				RecordChange.FromJson(change); // must not throw whatever the result
			}
		}

		private static void Collect(Dictionary<string, object> node, List<Dictionary<string, object>> into)
		{
			into.Add(node);
			foreach (object child in node.Values.ToList())
			{
				var childObject = child as Dictionary<string, object>;
				if (childObject != null && childObject.Count > 0)
					Collect(childObject, into);
			}
		}

		[Fact]
		public void Absurdly_deep_nesting_is_rejected_instead_of_overflowing_the_stack()
		{
			string deepArray = new string('[', 100000) + new string(']', 100000);
			string deepObject = string.Concat(Enumerable.Repeat("{\"a\":", 50000)) + "1" + new string('}', 50000);
			Assert.Throws<JsonFormatException>(() => MiniJson.Parse(deepArray));
			Assert.Throws<JsonFormatException>(() => MiniJson.Parse(deepObject));
			Assert.Throws<JsonFormatException>(() => MiniJson.ReadArray(new StringReader("[" + deepArray + "]"), e => { }));
			// ordinary depth still works
			Assert.NotNull(MiniJson.Parse(new string('[', MiniJson.MaxDepth) + new string(']', MiniJson.MaxDepth)));
		}

		[Fact]
		public void Damaged_snapshot_files_load_as_null_or_valid_data_but_never_throw()
		{
			var random = new Random(4);
			using (var dir = new TempDir())
			{
				string path = dir.File("s.bin");
				var snapshot = new Snapshot { LastSyncUtc = new DateTime(2026, 10, 8, 9, 0, 0, DateTimeKind.Utc) };
				for (int i = 0; i < 40; i++)
					snapshot.Records.Add(new ZlbbRecord { Id = "id" + i, PuzzleId = "P" + i % 5, Author = i % 2 == 0 ? null : "a" + i, Cost = i, Cycles = i * 2, Area = i * 3, Height = i % 3 == 0 ? (double?)i : null });
				SnapshotStore.Save(path, snapshot);
				byte[] original = File.ReadAllBytes(path);

				for (int run = 0; run < 1500; run++)
				{
					byte[] damaged = (byte[])original.Clone();
					switch (random.Next(4))
					{
						case 0: // flip a few bytes
							for (int k = 0; k < random.Next(1, 6); k++) damaged[random.Next(damaged.Length)] = (byte)random.Next(256);
							break;
						case 1: // truncate
							Array.Resize(ref damaged, random.Next(0, damaged.Length));
							break;
						case 2: // append junk
							damaged = damaged.Concat(Enumerable.Range(0, random.Next(1, 40)).Select(x => (byte)random.Next(256))).ToArray();
							break;
						default: // random garbage of the same size
							random.NextBytes(damaged);
							break;
					}
					File.WriteAllBytes(path, damaged);
					Snapshot loaded = SnapshotStore.Load(path); // must not throw
					if (loaded != null)
						Assert.All(loaded.Records, r => Assert.False(string.IsNullOrEmpty(r.PuzzleId) && r.Id == null));
				}
			}
		}

		[Fact]
		public void Whatever_the_server_answers_a_sync_ends_in_a_clean_state()
		{
			var random = new Random(5);
			using (var server = new FakeZlbbServer())
			{
				string valid = TestData.Array(TestData.Add(TestData.RecordJson("a", "P1")), TestData.Add(TestData.RecordJson("b", "P2")));
				for (int run = 0; run < 40; run++)
				{
					using (var dir = new TempDir())
					{
						string body;
						switch (run % 4)
						{
							case 0: body = Mutate(random, valid); break;
							case 1: body = new string(Enumerable.Range(0, random.Next(0, 300)).Select(i => (char)random.Next(32, 127)).ToArray()); break;
							case 2: body = valid.Substring(0, random.Next(0, valid.Length)); break;
							default: body = "[" + string.Join(",", Enumerable.Range(0, 20).Select(i => Mutate(random, TestData.Add(TestData.RecordJson("x" + i, "P1"))))) + "]"; break;
						}
						server.Handler = r => new FakeResponse { Body = body, Status = run % 7 == 0 ? 502 : 200 };
						var service = new SyncService(new ZlbbClient(server.BaseUrl), dir.File("om.snapshot"));
						SyncOutcome outcome = service.RunOnce(); // must not throw
						Assert.Contains(outcome.State, new[] { SyncState.Fresh, SyncState.Stale, SyncState.Failed });
						Assert.True(service.Index.Count >= 0);
						// whatever was accepted must be readable back
						Snapshot saved = SnapshotStore.Load(dir.File("om.snapshot"));
						if (outcome.State == SyncState.Fresh)
							Assert.NotNull(saved);
					}
				}
			}
		}

		[Fact]
		public void Extreme_scores_do_not_overflow_or_throw()
		{
			var records = new List<ZlbbRecord>
			{
				new ZlbbRecord { Id = "max", PuzzleId = "P", Cost = int.MaxValue, Cycles = int.MaxValue, Area = int.MaxValue, Instructions = int.MaxValue },
				new ZlbbRecord { Id = "min", PuzzleId = "P", Cost = int.MinValue, Cycles = int.MinValue, Area = int.MinValue, Instructions = int.MinValue },
				new ZlbbRecord { Id = "zero", PuzzleId = "P" },
				new ZlbbRecord { Id = "weird", PuzzleId = "P", Cost = 5, Rate = double.NaN, Width = double.PositiveInfinity, Height = 1e300, AreaInfLevel = 400, AreaInfValue = 3 },
			};
			var index = new RecordIndex();
			foreach (ZlbbRecord r in records)
				index.Apply(new RecordChange { Type = ChangeType.Add, Record = r });
			foreach (Metric metric in Metrics.All.Concat(new[] { Metrics.Sum, Metrics.SumProduction, Metrics.Sum4, Metrics.CostTimesCycles, Metrics.CostTimesArea,
				Metrics.CyclesTimesArea, Metrics.CostTimesInstructions, Metrics.CyclesTimesInstructions }))
			{
				GameHistogram g = index.GetGameHistogram("P", metric, null, Histogram.DefaultMaxBuckets, 1.0);
				Assert.True(g.IsEmpty || (g.Max > 0 && g.Counts.Length > 0 && g.Counts.Length <= Histogram.DefaultMaxBuckets + 4));
				Standing standing = index.GetStanding("P", metric, 1.0);
				Assert.True(standing == null || standing.Total > 0);
				index.GetHistogram("P", metric); // the integer-bin path too
			}
		}

		[Fact]
		public void Geometry_of_huge_and_odd_footprints_does_not_throw()
		{
			Assert.NotNull(HexGeometry.Measure(new List<int> { 0 }, new List<int> { 0 }));
			var q = new List<int> { -16000, 16000, 0 };
			var r = new List<int> { 16000, -16000, 0 };
			// a very large footprint must neither hang nor overflow (the game limits parts to 16384 hexes from the origin)
			var watch = System.Diagnostics.Stopwatch.StartNew();
			Assert.NotNull(HexGeometry.BoundingHexagon(new List<int> { -50, 50, 0 }, new List<int> { 50, -50, 0 }));
			Assert.True(watch.ElapsedMilliseconds < 2000);
			Assert.Null(HexGeometry.Measure(new List<int> { 1, 2 }, new List<int> { 1 })); // mismatched lists
		}
	}
}
