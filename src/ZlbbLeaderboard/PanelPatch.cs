using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Quintessential;
using ZlbbCore;

namespace ZlbbLeaderboard
{
	/// <summary>Everything the big window needs to know about one puzzle.</summary>
	internal sealed class PuzzleInfo
	{
		public string Id;
		public string Name;
		public bool Production;
		/// <summary>The game's own histogram data for the four vanilla metrics (carries the player's best / this / now scores).</summary>
		public Dictionary<enum_133, class_132> Vanilla;
		/// <summary>Metrics that have zlbb data for this puzzle, in display order.</summary>
		public List<Metric> Metrics = new List<Metric>();
	}

	/// <summary>One histogram panel ready to be drawn by the game's own renderer.</summary>
	internal sealed class PanelSpec
	{
		public class_132 Panel;
		/// <summary>Title we have to draw ourselves (the game only knows four titles); null for the game's own metrics.</summary>
		public string Title;
		public bool IsInfoScreen;
	}

	/// <summary>
	/// The game's puzzle screens have room for three histogram panels. We leave those alone and add one button ("ALL METRICS") to the
	/// middle panel that opens <see cref="AllMetricsScreen"/>, a big window with every zlbb metric at once. All panels, in both
	/// places, are drawn by the game's own class_140.method_321, so the look is the original one.
	/// </summary>
	internal static class PanelPatch
	{
		/// <summary>Metrics beyond the game's four use this enum value, so the game draws them without a title and with plain numbers (we add the title).</summary>
		private const int ExtraEnumBase = 100;

		private static readonly Dictionary<string, PuzzleInfo> Puzzles = new Dictionary<string, PuzzleInfo>();
		private static readonly ConditionalWeakTable<GameHistogram, class_182> ScoreDataCache = new ConditionalWeakTable<GameHistogram, class_182>();
		private static bool failureLogged;

		/// <summary>Called whenever the game builds its histogram data for a puzzle (ScoreManager.method_1377).</summary>
		public static void Remember(string puzzleId, string puzzleName, bool production, Dictionary<enum_133, class_132> vanilla)
		{
			SyncService sync = ZlbbMod.Sync;
			if (sync == null)
				return;
			var info = new PuzzleInfo { Id = puzzleId, Name = puzzleName, Production = production, Vanilla = vanilla };
			info.Metrics = MetricPages.DisplayOrder(production, metric => !sync.Index.GetGameHistogram(puzzleId, metric).IsEmpty);
			lock (Puzzles)
				Puzzles[puzzleId] = info;
		}

		public static void Hook(On.class_140.orig_method_321 orig, class_132 data, Vector2 position, bool isInfoScreen)
		{
			orig(data, position, isInfoScreen); // the game's own panel, unchanged
			try
			{
				if (SlotOf(data.field_1480) != 1)
					return; // one button per screen: on the middle panel
				string puzzleId = PuzzleIdOf(data);
				PuzzleInfo info = null;
				if (puzzleId != null)
					lock (Puzzles)
						Puzzles.TryGetValue(puzzleId, out info);
				if (info == null || info.Metrics.Count == 0)
					return;

				string label = "ALL " + info.Metrics.Count + " METRICS";
				if (class_140.method_313(label, position + new Vector2(24f, 20f), 210, 44).method_824(true, true))
					UI.OpenScreen(new AllMetricsScreen(info));
			}
			catch (Exception e)
			{
				LogFailureOnce(e);
			}
		}

		/// <summary>Builds the game's data object for one metric: our zlbb histogram plus the player's own scores where the game knows them.</summary>
		public static PanelSpec BuildPanel(PuzzleInfo info, Metric metric)
		{
			var panel = new class_132();
			string title = null;
			bool derived = false;
			enum_133? real = RealEnumOf(metric);
			if (real.HasValue)
			{
				// one of the game's four metrics: native title, native number format, and the player's scores
				panel.field_1480 = real.Value;
				panel.field_1481 = "ZLBB." + info.Id + "." + metric.Key;
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
				panel.field_1480 = (enum_133)ExtraEnumBase;
				panel.field_1481 = "ZLBB." + info.Id + "." + metric.Key;
				title = metric.Name;
				enum_133[] parts;
				bool product;
				if (derived = DerivedParts(metric, out parts, out product))
				{
					// sums and products of the game's four scores: the player's own marker is computed from them
					panel.field_1484 = Combine(info, parts, product, c => c.field_1484);
					panel.field_1485 = Combine(info, parts, product, c => c.field_1485);
				}
			}

			class_182 scoreData = ScoreDataFor(ZlbbMod.Sync.Index.GetGameHistogram(info.Id, metric));
			if (scoreData != null)
				panel.field_1482 = scoreData;
			return new PanelSpec { Panel = panel, Title = title, IsInfoScreen = real.HasValue || derived };
		}

		/// <summary>Which of the game's four scores a derived metric is built from, and whether they are added or multiplied.</summary>
		private static bool DerivedParts(Metric metric, out enum_133[] parts, out bool product)
		{
			product = false;
			parts = null;
			if (metric == Metrics.Sum) parts = new[] { enum_133.Cost, enum_133.Cycles, enum_133.Footprint };
			else if (metric == Metrics.SumProduction) parts = new[] { enum_133.Cost, enum_133.Cycles, enum_133.Instructions };
			else if (metric == Metrics.Sum4) parts = new[] { enum_133.Cost, enum_133.Cycles, enum_133.Footprint, enum_133.Instructions };
			else
			{
				product = true;
				if (metric == Metrics.CostTimesCycles) parts = new[] { enum_133.Cost, enum_133.Cycles };
				else if (metric == Metrics.CostTimesArea) parts = new[] { enum_133.Cost, enum_133.Footprint };
				else if (metric == Metrics.CyclesTimesArea) parts = new[] { enum_133.Cycles, enum_133.Footprint };
				else if (metric == Metrics.CostTimesInstructions) parts = new[] { enum_133.Cost, enum_133.Instructions };
				else if (metric == Metrics.CyclesTimesInstructions) parts = new[] { enum_133.Cycles, enum_133.Instructions };
			}
			return parts != null;
		}

		private static Maybe<int> Combine(PuzzleInfo info, enum_133[] parts, bool product, Func<class_132, Maybe<int>> pick)
		{
			long total = product ? 1 : 0;
			foreach (enum_133 part in parts)
			{
				class_132 source;
				if (info.Vanilla == null || !info.Vanilla.TryGetValue(part, out source))
					return struct_18.field_1431;
				Maybe<int> value = pick(source);
				if (!value.method_1085())
					return struct_18.field_1431;
				total = product ? total * value.method_1087() : total + value.method_1087();
			}
			return (int)Math.Min(total, int.MaxValue);
		}

		public static void DrawPanel(PanelSpec spec, Vector2 position)
		{
			class_140.method_321(spec.Panel, position, spec.IsInfoScreen);
			if (!string.IsNullOrEmpty(spec.Title))
			{
				Vector2 center = Bounds2.WithSize(position, new Vector2(258f, 376f)).Center;
				class_135.method_290(spec.Title.method_634().method_441(), center + new Vector2(2f, 151f), class_238.field_1990.field_2142, class_181.field_1719,
					(enum_0)1, 1f, 0.6f, float.MaxValue, float.MaxValue, 0, default(Color), null, int.MaxValue, false, true);
			}
		}

		private static class_182 ScoreDataFor(GameHistogram scaled)
		{
			if (scaled == null || scaled.IsEmpty)
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

		/// <summary>Panels created by the game carry the save key "B.&lt;puzzleId&gt;.&lt;metric&gt;"; ours start with "ZLBB." and are ignored.</summary>
		private static string PuzzleIdOf(class_132 data)
		{
			string key = data == null ? null : data.field_1481;
			if (key == null || !key.StartsWith("B."))
				return null;
			int last = key.LastIndexOf('.');
			return last > 2 ? key.Substring(2, last - 2) : null;
		}

		public static void LogFailureOnce(Exception e)
		{
			if (failureLogged)
				return;
			failureLogged = true;
			ZlbbMod.Log("panel patch failed, showing the game's own panels: " + e);
		}
	}
}
