using System;
using System.Collections.Generic;
using System.Globalization;
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
		public Metric Metric;
		/// <summary>Title we have to draw ourselves (the game only knows four titles); null for the game's own metrics.</summary>
		public string Title;
		public bool IsInfoScreen;
	}

	/// <summary>
	/// The game's puzzle screens have room for three histogram panels. We leave those alone (apart from writing the player's rank under
	/// them) and add one button ("ALL METRICS") to the middle panel that opens <see cref="AllMetricsScreen"/>, a big window with every zlbb
	/// metric at once. All panels, in both places, are drawn by the game's own class_140.method_321, so the look is the original one.
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
				int slot = SlotOf(data.field_1480);
				string puzzleId = PuzzleIdOf(data);
				if (slot < 0 || puzzleId == null)
					return;
				PuzzleInfo info;
				lock (Puzzles)
					if (!Puzzles.TryGetValue(puzzleId, out info))
						return;
				if (info.Metrics.Count == 0)
					return;

				Metric metric = MetricOf(data.field_1480, info.Production);
				DrawStanding(info, metric, PlayerValue(info, metric), position);

				if (slot == 1) // one button per screen: on the middle panel
				{
					string label = "ALL " + info.Metrics.Count + " METRICS";
					if (class_140.method_313(label, position + new Vector2(24f, 20f), 210, 44).method_824(true, true))
						UI.OpenScreen(new AllMetricsScreen(info));
				}
			}
			catch (Exception e)
			{
				LogFailureOnce(e);
			}
		}

		/// <summary>Builds the game's data object for one metric: our zlbb histogram plus the player's own scores where they are known.</summary>
		public static PanelSpec BuildPanel(PuzzleInfo info, Metric metric)
		{
			var panel = new class_132();
			string title = null;
			bool showsPlayer = false;
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
				showsPlayer = true;
			}
			else
			{
				panel.field_1480 = (enum_133)ExtraEnumBase;
				panel.field_1481 = "ZLBB." + info.Id + "." + metric.Key;
				title = metric.Name;
				// derived and measured metrics: the player's marker comes from our own calculation
				double? value = PlayerValue(info, metric);
				if (value.HasValue)
				{
					panel.field_1484 = (int)Math.Min(Math.Round(value.Value), int.MaxValue);
					class_132 cost;
					if (info.Vanilla != null && info.Vanilla.TryGetValue(enum_133.Cost, out cost) && cost.field_1485.method_1085())
						panel.field_1485 = panel.field_1484; // on the finish screen the game shows "now" markers
					showsPlayer = true;
				}
			}

			class_182 scoreData = ScoreDataFor(ZlbbMod.Sync.Index.GetGameHistogram(info.Id, metric));
			if (scoreData != null)
				panel.field_1482 = scoreData;
			return new PanelSpec { Panel = panel, Metric = metric, Title = title, IsInfoScreen = showsPlayer };
		}

		public static void DrawPanel(PuzzleInfo info, PanelSpec spec, Vector2 position)
		{
			class_140.method_321(spec.Panel, position, spec.IsInfoScreen);
			if (!string.IsNullOrEmpty(spec.Title))
			{
				Vector2 center = Bounds2.WithSize(position, new Vector2(258f, 376f)).Center;
				class_135.method_290(spec.Title.method_634().method_441(), center + new Vector2(2f, 151f), class_238.field_1990.field_2142, class_181.field_1719,
					(enum_0)1, 1f, 0.6f, float.MaxValue, float.MaxValue, 0, default(Color), null, int.MaxValue, false, true);
			}
			DrawStanding(info, spec.Metric, PlayerValue(info, spec.Metric), position);
		}

		// ---- the player's own value and rank ----

		/// <summary>The player's score for one of the game's four metrics: this run's, else the selected solution's.</summary>
		private static int? Score(PuzzleInfo info, enum_133 type)
		{
			class_132 entry;
			if (info.Vanilla == null || !info.Vanilla.TryGetValue(type, out entry))
				return null;
			if (entry.field_1485.method_1085())
				return entry.field_1485.method_1087();
			if (entry.field_1484.method_1085())
				return entry.field_1484.method_1087();
			return null;
		}

		/// <summary>
		/// The player's value for any metric we can calculate: the game's four scores, the sums and products of them, and height / width /
		/// bounding hexagon once the solution has run in the modded game. Null for rate and the "@infinity" metrics (they need a steady-state simulation).
		/// </summary>
		public static double? PlayerValue(PuzzleInfo info, Metric metric)
		{
			int? cost = Score(info, enum_133.Cost), cycles = Score(info, enum_133.Cycles);
			int? area = Score(info, enum_133.Footprint), instructions = Score(info, enum_133.Instructions);

			if (metric == Metrics.Cost) return cost;
			if (metric == Metrics.Cycles) return cycles;
			if (metric == Metrics.Area) return area;
			if (metric == Metrics.Instructions) return instructions;
			if (metric == Metrics.Sum) return cost.HasValue && cycles.HasValue && area.HasValue ? (double?)((double)cost.Value + cycles.Value + area.Value) : null;
			if (metric == Metrics.SumProduction) return cost.HasValue && cycles.HasValue && instructions.HasValue ? (double?)((double)cost.Value + cycles.Value + instructions.Value) : null;
			if (metric == Metrics.Sum4) return cost.HasValue && cycles.HasValue && area.HasValue && instructions.HasValue ? (double?)((double)cost.Value + cycles.Value + area.Value + instructions.Value) : null;
			if (metric == Metrics.CostTimesCycles) return cost.HasValue && cycles.HasValue ? (double?)((double)cost.Value * cycles.Value) : null;
			if (metric == Metrics.CostTimesArea) return cost.HasValue && area.HasValue ? (double?)((double)cost.Value * area.Value) : null;
			if (metric == Metrics.CyclesTimesArea) return cycles.HasValue && area.HasValue ? (double?)((double)cycles.Value * area.Value) : null;
			if (metric == Metrics.CostTimesInstructions) return cost.HasValue && instructions.HasValue ? (double?)((double)cost.Value * instructions.Value) : null;
			if (metric == Metrics.CyclesTimesInstructions) return cycles.HasValue && instructions.HasValue ? (double?)((double)cycles.Value * instructions.Value) : null;

			if ((metric == Metrics.Height || metric == Metrics.Width || metric == Metrics.BoundingHex) && cost.HasValue && cycles.HasValue && area.HasValue && instructions.HasValue)
			{
				SolveGeometry geometry = SolveMetrics.Find(SolveMetrics.KeyFor(info.Id, cost.Value, cycles.Value, area.Value, instructions.Value));
				if (geometry != null)
					return metric == Metrics.Height ? geometry.Height : metric == Metrics.Width ? geometry.Width : geometry.BoundingHex;
			}
			return null;
		}

		/// <summary>Writes the player's value and rank among the zlbb records into the empty list area under a panel.</summary>
		private static void DrawStanding(PuzzleInfo info, Metric metric, double? value, Vector2 panelPosition)
		{
			if (metric == null)
				return;
			string first, second = null;
			if (value.HasValue)
			{
				Standing standing = ZlbbMod.Sync.Index.GetStanding(info.Id, metric, value.Value);
				if (standing == null)
					return;
				first = "YOUR SOLVE: " + FormatValue(value.Value);
				second = standing.Describe();
			}
			else if (metric == Metrics.Height || metric == Metrics.Width || metric == Metrics.BoundingHex)
				first = "run this solution once to measure it";
			else
				first = "needs a steady-state run (not measurable)";

			Vector2 center = new Vector2(129f, 0f);
			WriteSmall(first, panelPosition + new Vector2(center.X, 128f));
			if (second != null)
				WriteSmall(second, panelPosition + new Vector2(center.X, 106f));
		}

		private static void WriteSmall(string text, Vector2 position)
		{
			class_135.method_290(text, position, class_238.field_1990.field_2140, class_181.field_1719,
				(enum_0)1, 1f, 0.6f, 240f, float.MaxValue, 0, default(Color), null, int.MaxValue, false, true);
		}

		private static string FormatValue(double value)
		{
			return Math.Abs(value - Math.Round(value)) < 1e-9
				? ((long)Math.Round(value)).ToString("N0", CultureInfo.InvariantCulture).Replace(",", " ")
				: value.ToString("0.0#", CultureInfo.InvariantCulture);
		}

		// ---- helpers ----

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

		private static Metric MetricOf(enum_133 type, bool production)
		{
			switch (type)
			{
				case enum_133.Cost: return Metrics.Cost;
				case enum_133.Cycles: return Metrics.Cycles;
				case enum_133.Footprint: return Metrics.Area;
				case enum_133.Instructions: return Metrics.Instructions;
				default: return null;
			}
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
