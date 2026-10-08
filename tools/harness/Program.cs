using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using ZlbbCore;

// Usage:
//   harness sync [baseUrl]      one full download + one diff against the live API into a temp folder (2 requests), prints timing/memory
//   harness hist <PuzzleId>     prints all 12 histograms for a puzzle from the snapshot left by "sync" (no network)
internal static class Program
{
	private static string Dir => Path.Combine(Path.GetTempPath(), "zlbb-harness");

	private static int Main(string[] args)
	{
		string command = args.Length > 0 ? args[0] : "sync";
		Directory.CreateDirectory(Dir);
		string snapshot = Path.Combine(Dir, "om.snapshot");

		if (command == "sync")
		{
			string baseUrl = args.Length > 1 ? args[1] : ZlbbClient.DefaultBaseUrl;
			if (File.Exists(snapshot))
				File.Delete(snapshot);
			var service = new SyncService(new ZlbbClient(baseUrl), snapshot, null, s => Console.WriteLine("  log: " + s));

			var watch = Stopwatch.StartNew();
			SyncOutcome first = service.RunOnce();
			Console.WriteLine($"full download: {first.State}, {first.Changes} changes, {watch.Elapsed.TotalSeconds:F1} s, index {service.Index.Count} records / {service.Index.PuzzleCount} puzzles");
			Console.WriteLine($"snapshot size: {new FileInfo(snapshot).Length / 1024.0 / 1024.0:F1} MB, peak working set {Process.GetCurrentProcess().PeakWorkingSet64 / 1024 / 1024} MB");

			watch.Restart();
			var next = new SyncService(new ZlbbClient(baseUrl), snapshot, null, s => Console.WriteLine("  log: " + s));
			SyncOutcome second = next.RunOnce();
			Console.WriteLine($"next-launch diff: {second.State}, bootstrap={second.Bootstrap}, {second.Changes} changes, {watch.Elapsed.TotalSeconds:F1} s (includes loading the snapshot)");
			return first.State == SyncState.Fresh && second.State == SyncState.Fresh ? 0 : 1;
		}

		if (command == "hist")
		{
			string puzzle = args.Length > 1 ? args[1] : "P007";
			Snapshot loaded = SnapshotStore.Load(snapshot);
			if (loaded == null)
			{
				Console.WriteLine("no snapshot - run 'harness sync' first");
				return 1;
			}
			var index = new RecordIndex();
			foreach (ZlbbRecord r in loaded.Records)
				index.Apply(new RecordChange { Type = ChangeType.Add, Record = r });
			Console.WriteLine($"{puzzle}: {index.CountForPuzzle(puzzle)} records (snapshot from {loaded.LastSyncUtc:u})");
			foreach (Metric metric in Metrics.All)
			{
				Histogram h = index.GetHistogram(puzzle, metric);
				Console.WriteLine($"{metric.Key,-3} {metric.Name,-17} n={h.Total,-4} range {(h.IsEmpty ? "-" : h.Start + ".." + (h.Start + h.Counts.Length - 1))}");
				if (!h.IsEmpty && h.Counts.Length <= 60)
				{
					int max = h.Counts.Max();
					for (int i = 0; i < h.Counts.Length; i++)
						Console.WriteLine($"      {h.Start + i,5} {new string('#', (int)Math.Ceiling(40.0 * h.Counts[i] / max)),-40} {h.Counts[i]}");
				}
			}
			return 0;
		}

		Console.WriteLine("unknown command");
		return 2;
	}
}
