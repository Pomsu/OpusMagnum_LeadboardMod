using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ZlbbCore
{
	public sealed class Snapshot
	{
		/// <summary>Time of the last successful sync (taken before that request started).</summary>
		public DateTime LastSyncUtc;
		public List<ZlbbRecord> Records = new List<ZlbbRecord>();
	}

	/// <summary>
	/// One binary file holding the last sync time and all records, so they can never disagree. Written to a temp file and
	/// renamed, so a crash mid-write leaves the previous snapshot intact. Anything unreadable loads as null (= bootstrap again).
	/// </summary>
	public static class SnapshotStore
	{
		public const int SchemaVersion = 1;
		private const uint Magic = 0x53424C5A; // "ZLBS"

		/// <summary>True when a file can be created next to the snapshot (checked before a big download, so a read-only folder cannot cause endless re-downloads).</summary>
		public static bool CanWrite(string path)
		{
			try
			{
				string directory = Path.GetDirectoryName(path);
				if (!string.IsNullOrEmpty(directory))
					Directory.CreateDirectory(directory);
				string probe = path + ".probe";
				File.WriteAllBytes(probe, new byte[] { 1 });
				File.Delete(probe);
				return true;
			}
			catch (Exception e)
			{
				if (e is IOException || e is UnauthorizedAccessException || e is NotSupportedException || e is ArgumentException || e is System.Security.SecurityException)
					return false;
				throw;
			}
		}

		public static void Save(string path, Snapshot snapshot)
		{
			string directory = Path.GetDirectoryName(path);
			if (!string.IsNullOrEmpty(directory))
				Directory.CreateDirectory(directory);
			string temp = path + ".tmp";
			using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
			using (var writer = new BinaryWriter(stream, Encoding.UTF8))
			{
				writer.Write(Magic);
				writer.Write(SchemaVersion);
				writer.Write(snapshot.LastSyncUtc.ToUniversalTime().Ticks);
				writer.Write(snapshot.Records.Count);
				foreach (ZlbbRecord r in snapshot.Records)
					WriteRecord(writer, r);
				writer.Write(Magic); // trailer: proves the file was written completely
				writer.Flush();
				stream.Flush(true);
			}
			if (File.Exists(path))
				File.Replace(temp, path, null); // atomic swap: a crash can never leave the snapshot half there
			else
				File.Move(temp, path);
		}

		public static Snapshot Load(string path)
		{
			try
			{
				if (!File.Exists(path))
					return null;
				using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
				using (var reader = new BinaryReader(stream, Encoding.UTF8))
				{
					if (reader.ReadUInt32() != Magic || reader.ReadInt32() != SchemaVersion)
						return null;
					long ticks = reader.ReadInt64();
					int count = reader.ReadInt32();
					if (count < 0 || ticks < DateTime.MinValue.Ticks || ticks > DateTime.MaxValue.Ticks)
						return null;
					var snapshot = new Snapshot { LastSyncUtc = new DateTime(ticks, DateTimeKind.Utc) };
					snapshot.Records.Capacity = Math.Min(count, 1000000);
					for (int i = 0; i < count; i++)
						snapshot.Records.Add(ReadRecord(reader));
					if (reader.ReadUInt32() != Magic || stream.Position != stream.Length)
						return null;
					return snapshot;
				}
			}
			catch (Exception e)
			{
				// anything a damaged file can provoke while being read: treat as "no usable snapshot"
				if (e is IOException || e is EndOfStreamException || e is ArgumentException || e is UnauthorizedAccessException || e is InvalidDataException
					|| e is FormatException || e is OverflowException)
					return null;
				throw;
			}
		}

		private static void WriteRecord(BinaryWriter w, ZlbbRecord r)
		{
			w.Write(r.Id);
			w.Write(r.PuzzleId);
			WriteNullableString(w, r.Author);
			w.Write(r.Cost);
			w.Write(r.Instructions);
			w.Write(r.Cycles);
			w.Write(r.Area);
			w.Write(r.Overlap);
			w.Write(r.Trackless);
			WriteNullable(w, r.Height);
			WriteNullable(w, r.Width);
			WriteNullable(w, r.BoundingHex);
			WriteNullable(w, r.Rate);
			WriteNullable(w, r.AreaInfLevel);
			WriteNullable(w, r.AreaInfValue);
			WriteNullable(w, r.HeightInf);
			WriteNullable(w, r.WidthInf);
			WriteNullable(w, r.BoundingHexInf);
		}

		private static ZlbbRecord ReadRecord(BinaryReader r)
		{
			return new ZlbbRecord
			{
				Id = r.ReadString(),
				PuzzleId = r.ReadString(),
				Author = ReadNullableString(r),
				Cost = r.ReadInt32(),
				Instructions = r.ReadInt32(),
				Cycles = r.ReadInt32(),
				Area = r.ReadInt32(),
				Overlap = r.ReadBoolean(),
				Trackless = r.ReadBoolean(),
				Height = ReadNullable(r),
				Width = ReadNullable(r),
				BoundingHex = ReadNullable(r),
				Rate = ReadNullable(r),
				AreaInfLevel = ReadNullable(r),
				AreaInfValue = ReadNullable(r),
				HeightInf = ReadNullable(r),
				WidthInf = ReadNullable(r),
				BoundingHexInf = ReadNullable(r),
			};
		}

		private static void WriteNullable(BinaryWriter w, double? value)
		{
			w.Write(value.HasValue);
			if (value.HasValue)
				w.Write(value.Value);
		}

		private static double? ReadNullable(BinaryReader r)
		{
			return r.ReadBoolean() ? r.ReadDouble() : (double?)null;
		}

		private static void WriteNullableString(BinaryWriter w, string value)
		{
			w.Write(value != null);
			if (value != null)
				w.Write(value);
		}

		private static string ReadNullableString(BinaryReader r)
		{
			return r.ReadBoolean() ? r.ReadString() : null;
		}
	}
}
