using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace ZlbbCore
{
	public class JsonFormatException : Exception
	{
		public JsonFormatException(string message) : base(message) { }
	}

	/// <summary>
	/// Small dependency-free JSON reader. Values become: Dictionary&lt;string, object&gt;, List&lt;object&gt;,
	/// string, double, bool or null. <see cref="ReadArray"/> streams the elements of a top-level array one at
	/// a time, so a 20 MB change feed never has to be held in memory as one string or one object graph.
	/// </summary>
	public sealed class MiniJson
	{
		private readonly TextReader reader;
		private readonly char[] buffer = new char[16384];
		private int pos;
		private int len;

		private MiniJson(TextReader reader)
		{
			this.reader = reader;
		}

		public static object Parse(string json)
		{
			var p = new MiniJson(new StringReader(json));
			object value = p.ReadValue();
			p.SkipWhitespace();
			if (p.Peek() != -1)
				throw new JsonFormatException("Unexpected trailing characters");
			return value;
		}

		/// <summary>Streams the elements of a top-level JSON array. Returns the number of elements.</summary>
		public static int ReadArray(TextReader input, Action<object> onElement)
		{
			var p = new MiniJson(input);
			p.SkipWhitespace();
			p.Expect('[');
			int count = 0;
			p.SkipWhitespace();
			if (p.Peek() == ']')
			{
				p.Next();
				return 0;
			}
			while (true)
			{
				onElement(p.ReadValue());
				count++;
				p.SkipWhitespace();
				int c = p.Next();
				if (c == ',')
					continue;
				if (c == ']')
					return count;
				throw new JsonFormatException("Expected ',' or ']' in array");
			}
		}

		// ---- typed access helpers for parsed values ----

		public static Dictionary<string, object> AsObject(object value)
		{
			return value as Dictionary<string, object>;
		}

		public static object Get(Dictionary<string, object> obj, string key)
		{
			object value;
			return obj != null && obj.TryGetValue(key, out value) ? value : null;
		}

		public static string GetString(Dictionary<string, object> obj, string key)
		{
			return Get(obj, key) as string;
		}

		public static double? GetNumber(Dictionary<string, object> obj, string key)
		{
			object value = Get(obj, key);
			return value is double ? (double?)(double)value : null;
		}

		public static bool GetBool(Dictionary<string, object> obj, string key)
		{
			object value = Get(obj, key);
			return value is bool && (bool)value;
		}

		// ---- parser ----

		private int Peek()
		{
			if (pos >= len)
			{
				len = reader.Read(buffer, 0, buffer.Length);
				pos = 0;
				if (len <= 0)
				{
					len = 0;
					return -1;
				}
			}
			return buffer[pos];
		}

		private int Next()
		{
			int c = Peek();
			if (c != -1)
				pos++;
			return c;
		}

		private void SkipWhitespace()
		{
			int c;
			while ((c = Peek()) == ' ' || c == '\t' || c == '\r' || c == '\n')
				pos++;
		}

		private void Expect(char expected)
		{
			int c = Next();
			if (c != expected)
				throw new JsonFormatException("Expected '" + expected + "' but found " + (c == -1 ? "end of input" : "'" + (char)c + "'"));
		}

		private object ReadValue()
		{
			SkipWhitespace();
			int c = Peek();
			switch (c)
			{
				case -1: throw new JsonFormatException("Unexpected end of input");
				case '{': return ReadObject();
				case '[': return ReadList();
				case '"': return ReadString();
				case 't': ReadLiteral("true"); return true;
				case 'f': ReadLiteral("false"); return false;
				case 'n': ReadLiteral("null"); return null;
				default: return ReadNumber();
			}
		}

		private void ReadLiteral(string literal)
		{
			foreach (char expected in literal)
				Expect(expected);
		}

		private Dictionary<string, object> ReadObject()
		{
			Expect('{');
			var result = new Dictionary<string, object>();
			SkipWhitespace();
			if (Peek() == '}')
			{
				Next();
				return result;
			}
			while (true)
			{
				SkipWhitespace();
				string key = ReadString();
				SkipWhitespace();
				Expect(':');
				result[key] = ReadValue();
				SkipWhitespace();
				int c = Next();
				if (c == ',')
					continue;
				if (c == '}')
					return result;
				throw new JsonFormatException("Expected ',' or '}' in object");
			}
		}

		private List<object> ReadList()
		{
			Expect('[');
			var result = new List<object>();
			SkipWhitespace();
			if (Peek() == ']')
			{
				Next();
				return result;
			}
			while (true)
			{
				result.Add(ReadValue());
				SkipWhitespace();
				int c = Next();
				if (c == ',')
					continue;
				if (c == ']')
					return result;
				throw new JsonFormatException("Expected ',' or ']' in array");
			}
		}

		private string ReadString()
		{
			Expect('"');
			var sb = new StringBuilder();
			while (true)
			{
				int c = Next();
				if (c == -1)
					throw new JsonFormatException("Unterminated string");
				if (c == '"')
					return sb.ToString();
				if (c != '\\')
				{
					sb.Append((char)c);
					continue;
				}
				c = Next();
				switch (c)
				{
					case '"': sb.Append('"'); break;
					case '\\': sb.Append('\\'); break;
					case '/': sb.Append('/'); break;
					case 'b': sb.Append('\b'); break;
					case 'f': sb.Append('\f'); break;
					case 'n': sb.Append('\n'); break;
					case 'r': sb.Append('\r'); break;
					case 't': sb.Append('\t'); break;
					case 'u':
						int code = 0;
						for (int i = 0; i < 4; i++)
						{
							int h = Next();
							int digit = h >= '0' && h <= '9' ? h - '0'
								: h >= 'a' && h <= 'f' ? h - 'a' + 10
								: h >= 'A' && h <= 'F' ? h - 'A' + 10
								: -1;
							if (digit < 0)
								throw new JsonFormatException("Invalid \\u escape");
							code = code * 16 + digit;
						}
						sb.Append((char)code);
						break;
					default:
						throw new JsonFormatException("Invalid escape sequence");
				}
			}
		}

		private double ReadNumber()
		{
			var sb = new StringBuilder();
			int c;
			while ((c = Peek()) != -1 && (c >= '0' && c <= '9' || c == '-' || c == '+' || c == '.' || c == 'e' || c == 'E'))
			{
				sb.Append((char)c);
				pos++;
			}
			double value;
			if (sb.Length == 0 || !double.TryParse(sb.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value))
				throw new JsonFormatException("Invalid number");
			return value;
		}
	}
}
