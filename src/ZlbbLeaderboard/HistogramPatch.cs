using System;
using System.Collections.Generic;
using System.Reflection;
using ZlbbCore;

namespace ZlbbLeaderboard
{
	/// <summary>
	/// Feeds zlbb data into the game's own histograms. The game builds each histogram from a private dictionary on
	/// ScoreManager (key "B.&lt;puzzleId&gt;.&lt;metric&gt;" -> "start,count0,count1,..."); we fill that dictionary just before
	/// ScoreManager.method_1377 runs, so the game normalises and draws the histogram itself, in its original style.
	/// See docs/GAME_HOOKS.md.
	/// </summary>
	internal static class HistogramPatch
	{
		private static readonly FieldInfo ScoreStrings = typeof(ScoreManager).GetField("field_3006", BindingFlags.Instance | BindingFlags.NonPublic);
		private static readonly MethodInfo SaveKey = typeof(ScoreManager).GetMethod("method_1366", BindingFlags.Instance | BindingFlags.NonPublic);

		/// <summary>The four metrics the vanilla histogram panels show.</summary>
		private static readonly KeyValuePair<enum_133, Metric>[] Mapping =
		{
			new KeyValuePair<enum_133, Metric>(enum_133.Cycles, Metrics.Cycles),
			new KeyValuePair<enum_133, Metric>(enum_133.Cost, Metrics.Cost),
			new KeyValuePair<enum_133, Metric>(enum_133.Footprint, Metrics.Area),
			new KeyValuePair<enum_133, Metric>(enum_133.Instructions, Metrics.Instructions),
		};

		private static readonly HashSet<string> LoggedPuzzles = new HashSet<string>();

		public static Dictionary<enum_133, class_132> Hook(On.ScoreManager.orig_method_1377 orig, ScoreManager self, Puzzle puzzle, Maybe<Solution> solution)
		{
			try
			{
				Inject(self, puzzle);
			}
			catch (Exception e)
			{
				// never break the game over a leaderboard problem: fall back to the vanilla (empty) histograms
				ZlbbMod.Log("could not inject histograms: " + e.Message);
			}
			Dictionary<enum_133, class_132> result = orig(self, puzzle, solution);
			try
			{
				if (puzzle != null && !puzzle.field_2782 && !string.IsNullOrEmpty(puzzle.field_2766))
					PanelPatch.Remember(puzzle.field_2766, puzzle.field_2767, puzzle.field_2779.method_1085(), result);
			}
			catch (Exception e)
			{
				ZlbbMod.Log("could not prepare extra pages: " + e.Message);
			}
			return result;
		}

		private static void Inject(ScoreManager self, Puzzle puzzle)
		{
			SyncService sync = ZlbbMod.Sync;
			if (sync == null || puzzle == null || puzzle.field_2782 || string.IsNullOrEmpty(puzzle.field_2766))
				return;
			var strings = ScoreStrings == null ? null : ScoreStrings.GetValue(self) as Dictionary<string, string>;
			if (strings == null || SaveKey == null)
				return;

			string puzzleId = puzzle.field_2766;
			var summary = new List<string>();
			foreach (var pair in Mapping)
			{
				Histogram histogram = sync.Index.GetHistogram(puzzleId, pair.Value);
				if (histogram.IsEmpty)
					continue;
				string key = (string)SaveKey.Invoke(self, new object[] { puzzle, pair.Key });
				GameHistogram scaled = histogram.ToGameScale(Histogram.DefaultMaxBuckets, Histogram.DefaultKeepFraction);
				strings[key] = scaled.ToGameString();
				summary.Add(pair.Value.Key + " n=" + scaled.Total + " max=" + scaled.Max);
			}

			bool firstTime;
			lock (LoggedPuzzles)
				firstTime = LoggedPuzzles.Add(puzzleId);
			if (firstTime)
				ZlbbMod.Log("histograms for " + puzzleId + ": " + (summary.Count == 0 ? "no zlbb data (vanilla)" : string.Join(", ", summary.ToArray())));
		}
	}
}
