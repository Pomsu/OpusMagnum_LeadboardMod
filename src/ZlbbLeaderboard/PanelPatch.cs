using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using ZlbbCore;

namespace ZlbbLeaderboard
{
	/// <summary>
	/// Shows all 12 zlbb metrics. The game draws exactly three histogram panels (class_140.method_321) per screen; page 0 is the
	/// game's own trio (Cost, Cycles, Area or Instructions), further pages swap in the other metrics. Every panel is still drawn by
	/// the game's own method_321, so the look is the original one; we only add the title of extra metrics and two small page buttons.
	/// </summary>
	internal static class PanelPatch
	{
		/// <summary>Metrics beyond the game's four use enum values from here on, so the game draws them without a title (we add it).</summary>
		private const int ExtraEnumBase = 100;
		private const float PanelWidth = 258f;
		private const float PanelHeight = 376f;

		private sealed class PuzzleInfo
		{
			public bool Production;
			public Dictionary<enum_133, class_132> Vanilla;
			public List<Metric[]> Pages = new List<Metric[]>();
		}

		private static readonly Dictionary<string, PuzzleInfo> Puzzles = new Dictionary<string, PuzzleInfo>();
		private static readonly ConditionalWeakTable<GameHistogram, class_182> ScoreDataCache = new ConditionalWeakTable<GameHistogram, class_182>();
		private static readonly ConditionalWeakTable<Histogram, GameHistogram> GameScaleCache = new ConditionalWeakTable<Histogram, GameHistogram>();
		private static bool failureLogged;

		/// <summary>0 = the game's own panels; 1..n = extra pages.</summary>
		public static int Page;

		/// <summary>Called whenever the game builds its histogram data for a puzzle (ScoreManager.method_1377).</summary>
		public static void Remember(string puzzleId, bool production, Dictionary<enum_133, class_132> vanilla)
		{
			SyncService sync = ZlbbMod.Sync;
			if (sync == null)
				return;
			var info = new PuzzleInfo { Production = production, Vanilla = vanilla };
			info.Pages = MetricPages.ExtraPages(production, metric => !sync.Index.GetHistogram(puzzleId, metric).IsEmpty);
			lock (Puzzles)
				Puzzles[puzzleId] = info;
		}

		public static void Hook(On.class_140.orig_method_321 orig, class_132 data, Vector2 position, bool isInfoScreen)
		{
			PuzzleInfo info = null;
			string puzzleId = null;
			int slot = -1;
			try
			{
				puzzleId = PuzzleIdOf(data);
				slot = SlotOf(data.field_1480);
				if (puzzleId != null && slot >= 0)
					lock (Puzzles)
						Puzzles.TryGetValue(puzzleId, out info);
			}
			catch (Exception e)
			{
				LogFailureOnce(e);
			}

			if (info == null || info.Pages.Count == 0)
			{
				orig(data, position, isInfoScreen);
				return;
			}

			int totalPages = info.Pages.Count + 1;
			if (Page < 0 || Page >= totalPages)
				Page = 0;

			try
			{
				if (Page == 0)
					orig(data, position, isInfoScreen);
				else
					DrawExtraPanel(orig, info, puzzleId, info.Pages[Page - 1], slot, position, isInfoScreen);
				if (slot == 1)
					DrawPager(position, totalPages);
			}
			catch (Exception e)
			{
				LogFailureOnce(e);
				orig(data, position, isInfoScreen);
			}
		}

		private static void DrawExtraPanel(On.class_140.orig_method_321 orig, PuzzleInfo info, string puzzleId, Metric[] page, int slot, Vector2 position, bool isInfoScreen)
		{
			Metric metric = slot < page.Length ? page[slot] : null;
			var panel = new class_132();
			string title = null;

			enum_133? real = metric == null ? null : RealEnumOf(metric);
			if (real.HasValue)
			{
				// one of the game's four metrics (e.g. Instructions or Area on a later page): native title, native number format, our score data
				panel.field_1480 = real.Value;
				panel.field_1481 = "ZLBB." + puzzleId + "." + metric.Key;
				class_132 own;
				if (info.Vanilla != null && info.Vanilla.TryGetValue(real.Value, out own))
				{
					panel.field_1483 = own.field_1483;
					panel.field_1484 = own.field_1484;
					panel.field_1485 = own.field_1485;
				}
			}
			else
			{
				panel.field_1480 = (enum_133)(ExtraEnumBase + (metric == null ? 0 : Metrics.IndexOf(metric)));
				panel.field_1481 = "ZLBB." + puzzleId + "." + (metric == null ? "none" : metric.Key);
				title = metric == null ? "" : metric.Name;
			}

			if (metric != null)
			{
				class_182 scoreData = ScoreDataFor(ZlbbMod.Sync.Index.GetHistogram(puzzleId, metric));
				if (scoreData != null)
					panel.field_1482 = scoreData;
			}

			orig(panel, position, isInfoScreen);

			if (title != null && title.Length > 0)
			{
				Vector2 center = Bounds2.WithSize(position, new Vector2(PanelWidth, PanelHeight)).Center;
				class_135.method_290(title.method_634().method_441(), center + new Vector2(2f, 151f), class_238.field_1990.field_2142, class_181.field_1719,
					(enum_0)1, 1f, 0.6f, float.MaxValue, float.MaxValue, 0, default(Color), null, int.MaxValue, false, true);
			}
		}

		/// <summary>Two small buttons in the middle panel's header corners, plus a note about where the data comes from.</summary>
		private static void DrawPager(Vector2 position, int totalPages)
		{
			if (class_140.method_313("<", position + new Vector2(10f, 336f), 28, 28).method_824(true, true))
				Page = (Page + totalPages - 1) % totalPages;
			if (class_140.method_313(">", position + new Vector2(PanelWidth - 38f, 336f), 28, 28).method_824(true, true))
				Page = (Page + 1) % totalPages;

			string note = "zlbb frontier records - page " + (Page + 1) + "/" + totalPages;
			class_135.method_290(note, position + new Vector2(PanelWidth / 2f, 30f), class_238.field_1990.field_2140, class_181.field_1719,
				(enum_0)1, 1f, 0.6f, float.MaxValue, float.MaxValue, 0, default(Color), null, int.MaxValue, false, true);
		}

		private static class_182 ScoreDataFor(Histogram histogram)
		{
			if (histogram == null || histogram.IsEmpty)
				return null;
			GameHistogram scaled = GameScaleCache.GetValue(histogram, h => h.ToGameScale());
			if (scaled.IsEmpty)
				return null;
			return ScoreDataCache.GetValue(scaled, g =>
			{
				int max = 0;
				foreach (int count in g.Counts)
					if (count > max)
						max = count;
				var normalised = new float[g.Counts.Length];
				for (int i = 0; i < normalised.Length; i++)
					normalised[i] = max == 0 ? 0f : (float)g.Counts[i] / max;
				return new class_182 { field_1725 = g.Max, field_1726 = normalised };
			});
		}

		private static enum_133? RealEnumOf(Metric metric)
		{
			if (metric == Metrics.Cost) return enum_133.Cost;
			if (metric == Metrics.Cycles) return enum_133.Cycles;
			if (metric == Metrics.Area) return enum_133.Footprint;
			if (metric == Metrics.Instructions) return enum_133.Instructions;
			return null;
		}

		/// <summary>Which of the three on-screen positions a panel occupies: Cost, Cycles, then Area/Instructions.</summary>
		private static int SlotOf(enum_133 type)
		{
			switch (type)
			{
				case enum_133.Cost: return 0;
				case enum_133.Cycles: return 1;
				case enum_133.Footprint:
				case enum_133.Instructions: return 2;
				default: return -1;
			}
		}

		/// <summary>Panels created by the game carry the save key "B.&lt;puzzleId&gt;.&lt;metric&gt;".</summary>
		private static string PuzzleIdOf(class_132 data)
		{
			string key = data == null ? null : data.field_1481;
			if (key == null || !key.StartsWith("B."))
				return null;
			int last = key.LastIndexOf('.');
			return last > 2 ? key.Substring(2, last - 2) : null;
		}

		private static void LogFailureOnce(Exception e)
		{
			if (failureLogged)
				return;
			failureLogged = true;
			ZlbbMod.Log("panel patch failed, showing the game's own panels: " + e);
		}
	}
}
