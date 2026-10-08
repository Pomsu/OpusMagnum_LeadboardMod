using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using ZlbbCore;

namespace ZlbbLeaderboard
{
	/// <summary>Height, width and bounding hexagon of one of the player's solves.</summary>
	internal sealed class SolveGeometry
	{
		public int Height;
		public double Width;
		public int BoundingHex;
	}

	/// <summary>
	/// The game only stores four scores per solution (cost, cycles, area, instructions). Height, width and bounding hexagon have to be
	/// measured from the machine's footprint, which the simulation only has while a solution runs. When a run finishes we measure it and
	/// remember the result under the solution's four scores, in memory and in a small text file, so the puzzle screen can show it later too.
	/// Rate and the "@infinity" metrics need a steady-state simulation the game does not do, so they cannot be measured.
	/// </summary>
	internal static class SolveMetrics
	{
		private static readonly object Sync = new object();
		private static readonly Dictionary<string, SolveGeometry> Known = new Dictionary<string, SolveGeometry>();
		private static string filePath;
		private static readonly FieldInfo EditorSim = FindField(typeof(SolutionEditorScreen), "field_4022");
		private static readonly FieldInfo FinishHistograms = typeof(class_257).GetField("field_2065", BindingFlags.Instance | BindingFlags.NonPublic);

		public static void Load(string directory)
		{
			lock (Sync)
			{
				filePath = Path.Combine(directory, "solves.txt");
				Known.Clear();
				try
				{
					if (!File.Exists(filePath))
						return;
					foreach (string line in File.ReadAllLines(filePath))
					{
						string[] parts = line.Split('|');
						int height, bounding;
						double width;
						if (parts.Length == 8 && int.TryParse(parts[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out height)
							&& double.TryParse(parts[6], NumberStyles.Float, CultureInfo.InvariantCulture, out width)
							&& int.TryParse(parts[7], NumberStyles.Integer, CultureInfo.InvariantCulture, out bounding))
							Known[string.Join("|", parts, 0, 5)] = new SolveGeometry { Height = height, Width = width, BoundingHex = bounding };
					}
				}
				catch (Exception e) when (!(e is OutOfMemoryException))
				{
					// a damaged or locked file must never stop the mod from starting
					ZlbbMod.Log("could not read solves.txt: " + e.Message);
				}
			}
		}

		public static string KeyFor(string puzzleId, int cost, int cycles, int area, int instructions)
		{
			return puzzleId + "|" + cost + "|" + cycles + "|" + area + "|" + instructions;
		}

		public static SolveGeometry Find(string key)
		{
			lock (Sync)
			{
				SolveGeometry geometry;
				return Known.TryGetValue(key, out geometry) ? geometry : null;
			}
		}

		public static void Hook(On.class_257.orig_ctor orig, class_257 self, SolutionEditorScreen editor, bool param_4182)
		{
			orig(self, editor, param_4182);
			try
			{
				CaptureFinishedRun(self, editor);
			}
			catch (Exception e)
			{
				ZlbbMod.Log("could not measure the finished run: " + e.Message);
			}
		}

		/// <summary>Called when the game shows the "your machine has successfully created the product" screen (class_257 constructor).</summary>
		public static void CaptureFinishedRun(class_257 finishScreen, SolutionEditorScreen editor)
		{
			if (EditorSim == null || FinishHistograms == null || !ZlbbMod.Enabled)
				return;
			Solution solution = editor.method_502();
			Puzzle puzzle = solution.method_1934();
			if (puzzle == null || puzzle.field_2782 || string.IsNullOrEmpty(puzzle.field_2766))
				return;

			var histograms = FinishHistograms.GetValue(finishScreen) as Dictionary<enum_133, class_132>;
			if (histograms == null)
				return;
			int cost, cycles, area, instructions;
			if (!TryNow(histograms, enum_133.Cost, out cost) || !TryNow(histograms, enum_133.Cycles, out cycles)
				|| !TryNow(histograms, enum_133.Footprint, out area) || !TryNow(histograms, enum_133.Instructions, out instructions))
				return;

			Maybe<Sim> sim = (Maybe<Sim>)EditorSim.GetValue(editor);
			if (!sim.method_1085())
				return;
			var q = new List<int>();
			var r = new List<int>();
			foreach (HexIndex hex in sim.method_1087().method_484())
			{
				q.Add(hex.Q);
				r.Add(hex.R);
			}
			GeometryMetrics measured = HexGeometry.Measure(q, r);
			if (measured == null)
				return;
			if (measured.BoundingHex <= 0 || q.Count != area)
				ZlbbMod.Log("footprint has " + q.Count + " hexes but the game reports area " + area + " for " + puzzle.field_2766);

			string key = KeyFor(puzzle.field_2766, cost, cycles, area, instructions);
			var geometry = new SolveGeometry { Height = measured.Height, Width = measured.Width, BoundingHex = measured.BoundingHex };
			lock (Sync)
			{
				Known[key] = geometry;
				Save();
			}
			ZlbbMod.Log("measured " + puzzle.field_2766 + " solve (" + cost + "g/" + cycles + "c/" + area + "a/" + instructions + "i): height " + geometry.Height
				+ ", width " + geometry.Width.ToString(CultureInfo.InvariantCulture) + ", bounding hex " + geometry.BoundingHex);
		}

		private static bool TryNow(Dictionary<enum_133, class_132> histograms, enum_133 type, out int value)
		{
			value = 0;
			class_132 entry;
			if (!histograms.TryGetValue(type, out entry) || !entry.field_1485.method_1085())
				return false;
			value = entry.field_1485.method_1087();
			return true;
		}

		private static void Save()
		{
			if (filePath == null)
				return;
			try
			{
				Directory.CreateDirectory(Path.GetDirectoryName(filePath));
				var lines = new List<string>();
				foreach (var pair in Known)
					lines.Add(pair.Key + "|" + pair.Value.Height + "|" + pair.Value.Width.ToString(CultureInfo.InvariantCulture) + "|" + pair.Value.BoundingHex);
				File.WriteAllLines(filePath + ".tmp", lines.ToArray());
				if (File.Exists(filePath))
					File.Delete(filePath);
				File.Move(filePath + ".tmp", filePath);
			}
			catch (Exception e)
			{
				if (e is IOException || e is UnauthorizedAccessException)
					ZlbbMod.Log("could not save solves.txt: " + e.Message);
				else
					throw;
			}
		}

		private static FieldInfo FindField(Type type, string name)
		{
			for (; type != null; type = type.BaseType)
			{
				FieldInfo field = type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly);
				if (field != null)
					return field;
			}
			return null;
		}
	}
}
