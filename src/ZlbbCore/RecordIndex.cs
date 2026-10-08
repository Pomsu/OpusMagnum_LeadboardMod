using System;
using System.Collections.Generic;

namespace ZlbbCore
{
	/// <summary>
	/// In-memory set of zlbb records grouped by puzzle, with memoised histograms. Thread-safe: the sync thread applies
	/// changes while the render thread asks for histograms.
	/// </summary>
	public sealed class RecordIndex
	{
		private sealed class CachedHistogram
		{
			public int Version;
			public Histogram Histogram;
		}

		private sealed class CachedGameHistogram
		{
			public int Version;
			public GameHistogram Histogram;
		}

		private readonly Dictionary<string, CachedGameHistogram> gameHistograms = new Dictionary<string, CachedGameHistogram>();
		private readonly object sync = new object();
		private readonly Dictionary<string, Dictionary<string, ZlbbRecord>> puzzles = new Dictionary<string, Dictionary<string, ZlbbRecord>>();
		private readonly Dictionary<string, int> versions = new Dictionary<string, int>();
		private readonly Dictionary<string, CachedHistogram> histograms = new Dictionary<string, CachedHistogram>();
		private int count;

		public int Count
		{
			get { lock (sync) return count; }
		}

		public int PuzzleCount
		{
			get { lock (sync) return puzzles.Count; }
		}

		/// <summary>Idempotent: adding an existing id replaces it, removing an unknown id does nothing.</summary>
		public void Apply(RecordChange change)
		{
			lock (sync)
			{
				ZlbbRecord record = change.Record;
				Dictionary<string, ZlbbRecord> byId;
				puzzles.TryGetValue(record.PuzzleId, out byId);
				if (change.Type == ChangeType.Add)
				{
					if (byId == null)
					{
						byId = new Dictionary<string, ZlbbRecord>();
						puzzles[record.PuzzleId] = byId;
					}
					if (!byId.ContainsKey(record.Id))
						count++;
					byId[record.Id] = record;
					Touch(record.PuzzleId);
				}
				else if (byId != null && byId.Remove(record.Id))
				{
					count--;
					if (byId.Count == 0)
						puzzles.Remove(record.PuzzleId);
					Touch(record.PuzzleId);
				}
			}
		}

		public void ApplyAll(IEnumerable<RecordChange> changes)
		{
			foreach (RecordChange change in changes)
				Apply(change);
		}

		public int CountForPuzzle(string puzzleId)
		{
			lock (sync)
			{
				Dictionary<string, ZlbbRecord> byId;
				return puzzles.TryGetValue(puzzleId, out byId) ? byId.Count : 0;
			}
		}

		public List<ZlbbRecord> GetRecords(string puzzleId)
		{
			lock (sync)
			{
				Dictionary<string, ZlbbRecord> byId;
				return puzzles.TryGetValue(puzzleId, out byId) ? new List<ZlbbRecord>(byId.Values) : new List<ZlbbRecord>();
			}
		}

		public List<ZlbbRecord> GetAllRecords()
		{
			lock (sync)
			{
				var all = new List<ZlbbRecord>(count);
				foreach (var byId in puzzles.Values)
					all.AddRange(byId.Values);
				return all;
			}
		}

		/// <summary>Histogram of a metric for a puzzle; memoised until that puzzle's records change. Empty when the puzzle is unknown.</summary>
		public Histogram GetHistogram(string puzzleId, Metric metric, RecordFilter filter = null, int maxBins = 0)
		{
			lock (sync)
			{
				Dictionary<string, ZlbbRecord> byId;
				if (!puzzles.TryGetValue(puzzleId, out byId))
					return new Histogram();

				string key = puzzleId + "|" + metric.Key + "|" + (filter == null ? "-" : filter.Overlap + "/" + filter.Trackless) + "|" + maxBins;
				int version;
				versions.TryGetValue(puzzleId, out version);
				CachedHistogram cached;
				if (histograms.TryGetValue(key, out cached) && cached.Version == version)
					return cached.Histogram;

				Histogram built = Binning.Build(byId.Values, metric, filter, maxBins);
				histograms[key] = new CachedHistogram { Version = version, Histogram = built };
				return built;
			}
		}

		/// <summary>
		/// The histogram in the game's own scale (best <paramref name="keepFraction"/> of the records, fixed bar width), built straight from
		/// the scores. Memoised until that puzzle's records change; the very same instance is returned meanwhile. Empty when the puzzle is unknown.
		/// </summary>
		public GameHistogram GetGameHistogram(string puzzleId, Metric metric, RecordFilter filter = null, int maxBuckets = Histogram.DefaultMaxBuckets, double keepFraction = Histogram.DefaultKeepFraction)
		{
			lock (sync)
			{
				Dictionary<string, ZlbbRecord> byId;
				if (!puzzles.TryGetValue(puzzleId, out byId))
					return new GameHistogram();

				string key = puzzleId + "|" + metric.Key + "|" + (filter == null ? "-" : filter.Overlap + "/" + filter.Trackless) + "|" + maxBuckets + "|" + keepFraction;
				int version;
				versions.TryGetValue(puzzleId, out version);
				CachedGameHistogram cached;
				if (gameHistograms.TryGetValue(key, out cached) && cached.Version == version)
					return cached.Histogram;

				GameHistogram built = GameScale.Build(Binning.SortedValues(byId.Values, metric, filter), maxBuckets, keepFraction);
				gameHistograms[key] = new CachedGameHistogram { Version = version, Histogram = built };
				return built;
			}
		}

		private void Touch(string puzzleId)
		{
			int version;
			versions.TryGetValue(puzzleId, out version);
			versions[puzzleId] = version + 1;
		}
	}
}
