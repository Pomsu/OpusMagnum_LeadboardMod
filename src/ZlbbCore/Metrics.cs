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

		/// <summary>All metrics in the order the website lists them.</summary>
		public static readonly IReadOnlyList<Metric> All = new List<Metric>
		{
			Cost, Instructions, Cycles, Area, Height, Width, BoundingHex, Rate, AreaInf, HeightInf, WidthInf, BoundingHexInf,
		}.AsReadOnly();

		public static Metric ByKey(string key)
		{
			foreach (Metric metric in All)
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
