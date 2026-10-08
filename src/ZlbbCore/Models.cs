using System;
using System.Collections.Generic;

namespace ZlbbCore
{
	/// <summary>One zlbb record (a frontier solution) reduced to what the histograms need.</summary>
	public sealed class ZlbbRecord
	{
		public string Id;
		public string PuzzleId;
		public string Author;

		public int Cost;
		public int Instructions;
		public int Cycles;
		public int Area;
		public bool Overlap;
		public bool Trackless;

		// null for puzzle types / records where the metric does not exist (e.g. height on production puzzles)
		public double? Height;
		public double? Width;
		public double? BoundingHex;
		public double? Rate;
		public double? AreaInfLevel;
		public double? AreaInfValue;
		public double? HeightInf;
		public double? WidthInf;
		public double? BoundingHexInf;

		/// <summary>Maps an OmRecordDTO (parsed by <see cref="MiniJson"/>); returns null when it has no id or puzzle.</summary>
		public static ZlbbRecord FromJson(object value)
		{
			var obj = MiniJson.AsObject(value);
			if (obj == null)
				return null;
			string id = MiniJson.GetString(obj, "id");
			string puzzleId = MiniJson.GetString(MiniJson.AsObject(MiniJson.Get(obj, "puzzle")), "id");
			var score = MiniJson.AsObject(MiniJson.Get(obj, "score"));
			if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(puzzleId) || score == null)
				return null;

			return new ZlbbRecord
			{
				Id = id,
				PuzzleId = puzzleId,
				Author = MiniJson.GetString(obj, "author"),
				Cost = ToInt(MiniJson.GetNumber(score, "cost")),
				Instructions = ToInt(MiniJson.GetNumber(score, "instructions")),
				Cycles = ToInt(MiniJson.GetNumber(score, "cycles")),
				Area = ToInt(MiniJson.GetNumber(score, "area")),
				Overlap = MiniJson.GetBool(score, "overlap"),
				Trackless = MiniJson.GetBool(score, "trackless"),
				Height = MiniJson.GetNumber(score, "height"),
				Width = MiniJson.GetNumber(score, "width"),
				BoundingHex = MiniJson.GetNumber(score, "boundingHex"),
				Rate = MiniJson.GetNumber(score, "rate"),
				AreaInfLevel = MiniJson.GetNumber(score, "areaINFLevel"),
				AreaInfValue = MiniJson.GetNumber(score, "areaINFValue"),
				HeightInf = MiniJson.GetNumber(score, "heightINF"),
				WidthInf = MiniJson.GetNumber(score, "widthINF"),
				BoundingHexInf = MiniJson.GetNumber(score, "boundingHexINF"),
			};
		}

		private static int ToInt(double? value)
		{
			return value.HasValue ? (int)Math.Round(value.Value) : 0;
		}
	}

	public enum ChangeType
	{
		Add,
		Remove,
	}

	/// <summary>One entry of GET /om/records/changes/{since}.</summary>
	public sealed class RecordChange
	{
		public ChangeType Type;
		public ZlbbRecord Record;

		/// <summary>Returns null for entries it cannot understand (unknown type or broken record) so one bad entry never aborts a sync.</summary>
		public static RecordChange FromJson(object value)
		{
			var obj = MiniJson.AsObject(value);
			if (obj == null)
				return null;
			string type = MiniJson.GetString(obj, "type");
			ChangeType changeType;
			if (type == "ADD")
				changeType = ChangeType.Add;
			else if (type == "REMOVE")
				changeType = ChangeType.Remove;
			else
				return null;
			ZlbbRecord record = ZlbbRecord.FromJson(MiniJson.Get(obj, "record"));
			return record == null ? null : new RecordChange { Type = changeType, Record = record };
		}
	}
}
