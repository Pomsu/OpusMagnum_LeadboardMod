using System.Collections.Generic;
using System.IO;
using Xunit;

namespace ZlbbCore.Tests
{
	public class MiniJsonTests
	{
		/// <summary>Hands out one character per Read call, like a slow network stream delivering tiny chunks.</summary>
		private sealed class OneCharReader : TextReader
		{
			private readonly string text;
			private int index;

			public OneCharReader(string text) { this.text = text; }

			public override int Read(char[] buffer, int offset, int count)
			{
				if (index >= text.Length)
					return 0;
				buffer[offset] = text[index++];
				return 1;
			}
		}

		[Fact]
		public void Parses_nested_values()
		{
			var obj = MiniJson.AsObject(MiniJson.Parse(" { \"a\": [1, 2.5, -3e2], \"b\": {\"c\": true, \"d\": null, \"e\": false}, \"s\": \"x\" } "));
			var list = (List<object>)obj["a"];
			Assert.Equal(new object[] { 1.0, 2.5, -300.0 }, list.ToArray());
			var b = MiniJson.AsObject(obj["b"]);
			Assert.True(MiniJson.GetBool(b, "c"));
			Assert.False(MiniJson.GetBool(b, "e"));
			Assert.Null(MiniJson.Get(b, "d"));
			Assert.Equal("x", MiniJson.GetString(obj, "s"));
		}

		[Fact]
		public void Parses_string_escapes_and_unicode()
		{
			string value = (string)MiniJson.Parse("\"q\\\"b\\\\s\\/n\\n t\\tu\\u00e4\\u2211\"");
			Assert.Equal("q\"b\\s/n\n t\tu\u00e4\u2211", value);
		}

		[Fact]
		public void Parses_empty_containers()
		{
			Assert.Empty((List<object>)MiniJson.Parse("[]"));
			Assert.Empty(MiniJson.AsObject(MiniJson.Parse("{}")));
		}

		[Fact]
		public void ReadArray_streams_elements_in_order()
		{
			var seen = new List<double>();
			int count = MiniJson.ReadArray(new StringReader("[1, 2,3 ,\n4]"), e => seen.Add((double)e));
			Assert.Equal(4, count);
			Assert.Equal(new[] { 1.0, 2.0, 3.0, 4.0 }, seen);
		}

		[Fact]
		public void ReadArray_handles_empty_array()
		{
			Assert.Equal(0, MiniJson.ReadArray(new StringReader("  [ ] "), e => Assert.Fail("no elements expected")));
		}

		[Fact]
		public void ReadArray_works_when_the_source_delivers_one_char_at_a_time()
		{
			string json = "[{\"id\":\"a\\u0041\",\"n\":12.5},{\"id\":\"b\",\"n\":-1}]";
			var ids = new List<string>();
			MiniJson.ReadArray(new OneCharReader(json), e => ids.Add(MiniJson.GetString(MiniJson.AsObject(e), "id")));
			Assert.Equal(new[] { "aA", "b" }, ids);
		}

		[Theory]
		[InlineData("")]
		[InlineData("[1, 2")]
		[InlineData("[1 2]")]
		[InlineData("{\"a\" 1}")]
		[InlineData("{\"a\": }")]
		[InlineData("\"unterminated")]
		[InlineData("\"bad \\q escape\"")]
		[InlineData("\"bad \\u12G4\"")]
		[InlineData("tru")]
		[InlineData("[1] trailing")]
		[InlineData("-")]
		public void Malformed_input_throws_JsonFormatException(string json)
		{
			Assert.Throws<JsonFormatException>(() => MiniJson.Parse(json));
		}

		[Fact]
		public void ReadArray_on_truncated_stream_throws_after_delivering_complete_elements()
		{
			var seen = new List<string>();
			Assert.Throws<JsonFormatException>(() =>
				MiniJson.ReadArray(new StringReader("[{\"id\":\"a\"},{\"id\":\"b"), e => seen.Add(MiniJson.GetString(MiniJson.AsObject(e), "id"))));
			Assert.Equal(new[] { "a" }, seen);
		}

		[Fact]
		public void Typed_getters_tolerate_wrong_types_and_missing_keys()
		{
			var obj = MiniJson.AsObject(MiniJson.Parse("{\"n\":\"text\",\"s\":5}"));
			Assert.Null(MiniJson.GetNumber(obj, "n"));
			Assert.Null(MiniJson.GetString(obj, "s"));
			Assert.False(MiniJson.GetBool(obj, "missing"));
			Assert.Null(MiniJson.GetString(null, "x"));
		}
	}
}
