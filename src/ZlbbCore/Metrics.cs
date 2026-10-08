using System;
using System.Collections.Generic;

namespace ZlbbCore
{
	/// <summary>A scored dimension of a record. Keys and getters mirror the zlbb website's visualizer.</summary>
	public sealed class Metric
	{
		public string Key { get; private set; }
		public string Name { get; private set; }
		private readonly Func<ZlbbRecord, double?> getter;

		public Metric(string key, string name, Func<ZlbbRecord, double?> getter)
		{
			Key = key;
			Name = name;
			this.getter = getter;
		}

		/// <summary>The metric value, or null when this record has none (the record is then left out, never counted as 0).</summary>
		public double? Get(ZlbbRecord record)
		{
			double? value = getter(record);
			if (value.HasValue && (double.IsNaN(value.Value) || double.IsInfinity(value.Value)))
				return null;
			return value;
		}
	}

	public static class Metrics
	{
		public static readonly Metric Cost = new Metric("g", "Cost", r => r.Cost);
		public static readonly Metric Instructions = new Metric("i", "Instructions", r => r.Instructions);
		public static readonly Metric Cycles = new Metric("c", "Cycles", r => r.Cycles);
		public static readonly Metric Area = new Metric("a", "Area", r => r.Area);
		public static readonly Metric Height = new Metric("h", "Height", r => r.Height);
		public static readonly Metric Width = new Metric("w", "Width", r => r.Width);
		public static readonly Metric BoundingHex = new Metric("b", "Bounding Hex", r => r.BoundingHex);
		public static readonly Metric Rate = new Metric("r", "Rate", r => r.Rate);
		public static readonly Metric AreaInf = new Metric("aI", "Area@inf", r =>
			r.AreaInfValue.HasValue && r.AreaInfLevel.HasValue ? r.AreaInfValue.Value * Math.Pow(10, 5 * r.AreaInfLevel.Value) : (double?)null);
		public static readonly Metric HeightInf = new Metric("hI", "Height@inf", r => r.HeightInf);
		public static readonly Metric WidthInf = new Metric("wI", "Width@inf", r => r.WidthInf);
		public static readonly Metric BoundingHexInf = new Metric("bI", "Bounding Hex@inf", r => r.BoundingHexInf);

		/// <summary>zlbb's "Sum" category: Cost + Cycles + Area. The community's most important single number; not part of <see cref="All"/>.</summary>
		public static readonly Metric Sum = new Metric("sum", "Sum", r => (double)r.Cost + r.Cycles + r.Area);

		/// <summary>The same for production puzzles, where zlbb sums Cost + Cycles + Instructions.</summary>
		public static readonly Metric SumProduction = new Metric("sumP", "Sum", r => (double)r.Cost + r.Cycles + r.Instructions);

		public static Metric SumFor(bool production)
		{
			return production ? SumProduction : Sum;
		}

		/// <summary>zlbb's "Sum4": Cost + Cycles + Area + Instructions (normal puzzles only).</summary>
		public static readonly Metric Sum4 = new Metric("sum4", "Sum4", r => (double)r.Cost + r.Cycles + r.Area + r.Instructions);

		// products used by zlbb's "GX", "CX", "AX" and "IX" categories
		public static readonly Metric CostTimesCycles = new Metric("gc", "Cost x Cycles", r => (double)r.Cost * r.Cycles);
		public static readonly Metric CostTimesArea = new Metric("ga", "Cost x Area", r => (double)r.Cost * r.Area);
		public static readonly Metric CyclesTimesArea = new Metric("ca", "Cycles x Area", r => (double)r.Cycles * r.Area);
		public static readonly Metric CostTimesInstructions = new Metric("gi", "Cost x Instr.", r => (double)r.Cost * r.Instructions);
		public static readonly Metric CyclesTimesInstructions = new Metric("ci", "Cycles x Instr.", r => (double)r.Cycles * r.Instructions);

		/// <summary>The derived metrics zlbb has categories for, in display order after the Sum: Sum4 and the products that exist for this puzzle type.</summary>
		public static List<Metric> DerivedAfterSum(bool production)
		{
			return production
				? new List<Metric> { CostTimesCycles, CostTimesInstructions, CyclesTimesInstructions }
				: new List<Metric> { Sum4, CostTimesCycles, CostTimesArea, CyclesTimesArea };
		}

		private static readonly Metric[] Derived =
		{
			Sum, SumProduction, Sum4, CostTimesCycles, CostTimesArea, CyclesTimesArea, CostTimesInstructions, CyclesTimesInstructions,
		};

		/// <summary>The 12 raw metrics in the order the website lists them (the derived sums are separate).</summary>
		public static readonly IReadOnlyList<Metric> All = new List<Metric>
		{
			Cost, Instructions, Cycles, Area, Height, Width, BoundingHex, Rate, AreaInf, HeightInf, WidthInf, BoundingHexInf,
		}.AsReadOnly();

		public static int IndexOf(Metric metric)
		{
			for (int i = 0; i < All.Count; i++)
				if (All[i] == metric)
					return i;
			return -1;
		}

		public static Metric ByKey(string key)
		{
			foreach (Metric metric in All)
				if (metric.Key == key)
					return metric;
			foreach (Metric metric in Derived)
				if (metric.Key == key)
					return metric;
			return null;
		}
	}

	/// <summary>Modifier filter. A null flag means "do not care".</summary>
	public sealed class RecordFilter
	{
		public bool? Overlap;
		public bool? Trackless;

		public bool Matches(ZlbbRecord record)
		{
			return (!Overlap.HasValue || record.Overlap == Overlap.Value)
				&& (!Trackless.HasValue || record.Trackless == Trackless.Value);
		}
	}
}
