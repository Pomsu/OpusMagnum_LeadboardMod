using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Xunit;
using ZlbbCore.Tests.Support;

namespace ZlbbCore.Tests
{
	public class AuthorNameTests
	{
		[Theory]
		[InlineData("Uwe", "Uwe")]
		[InlineData("  Uwe Poms  ", "Uwe Poms")]
		[InlineData("a_b-c.d 1", "a_b-c.d 1")]
		[InlineData("x", "x")]
		public void Good_names_are_trimmed_and_accepted(string input, string expected)
		{
			string cleaned, error;
			Assert.True(AuthorName.TryClean(input, out cleaned, out error));
			Assert.Equal(expected, cleaned);
			Assert.Null(error);
		}

		[Theory]
		[InlineData("")]
		[InlineData("   ")]
		[InlineData(null)]
		[InlineData("this name is much too long to be accepted")]
		[InlineData("semi;colon")]
		[InlineData("slash/name")]
		[InlineData("quote\"name")]
		[InlineData("new\nline")]
		[InlineData("umläut")]
		public void Bad_names_are_rejected_with_a_reason(string input)
		{
			string cleaned, error;
			Assert.False(AuthorName.TryClean(input, out cleaned, out error));
			Assert.False(string.IsNullOrEmpty(error));
		}

		[Theory]
		[InlineData("ZlbbLeaderboard")]
		[InlineData("zlbb")]
		[InlineData("  Quintessential ")]
		[InlineData("Opus Magnum")]
		[InlineData("mod")]
		[InlineData("Default")]
		public void Names_of_programs_are_refused_because_a_name_should_identify_a_person(string input)
		{
			string cleaned, error;
			Assert.False(AuthorName.TryClean(input, out cleaned, out error));
			Assert.Contains("your own name", error);
		}

		[Theory]
		[InlineData("zlbb_fan")]
		[InlineData("Modesty")]
		[InlineData("u/some_reddit_user")]
		public void Names_that_merely_contain_such_words_are_fine(string input)
		{
			string cleaned, error;
			// 'u/...' contains a slash, which is not allowed - only the first two must pass
			bool ok = AuthorName.TryClean(input, out cleaned, out error);
			Assert.Equal(!input.Contains("/"), ok);
		}

		[Fact]
		public void Exactly_32_characters_are_fine_33_are_not()
		{
			string cleaned, error;
			Assert.True(AuthorName.TryClean(new string('a', 32), out cleaned, out error));
			Assert.False(AuthorName.TryClean(new string('a', 33), out cleaned, out error));
		}
	}

	public class MultipartBodyTests
	{
		private static string AsText(byte[] bytes)
		{
			return Encoding.GetEncoding("ISO-8859-1").GetString(bytes); // 1:1 bytes <-> chars, so binary content can be searched
		}

		[Fact]
		public void Contains_every_field_and_file_with_the_right_headers()
		{
			var body = new MultipartBody().AddField("author", "Uwe").AddFile("solution", "a.solution", "application/octet-stream", new byte[] { 1, 2, 3 });
			string text = AsText(body.Build());
			string boundary = body.ContentType.Substring(body.ContentType.IndexOf("boundary=") + 9);

			Assert.StartsWith("multipart/form-data; boundary=", body.ContentType);
			Assert.Contains("--" + boundary + "\r\nContent-Disposition: form-data; name=\"author\"\r\n\r\nUwe\r\n", text);
			Assert.Contains("name=\"solution\"; filename=\"a.solution\"\r\nContent-Type: application/octet-stream\r\n\r\n", text);
			Assert.EndsWith("--" + boundary + "--\r\n", text);
		}

		[Fact]
		public void Binary_content_survives_byte_for_byte_including_crlf_and_zero_bytes()
		{
			byte[] data = Enumerable.Range(0, 256).Select(i => (byte)i).Concat(new byte[] { 13, 10, 45, 45, 0, 0, 13, 10 }).ToArray();
			byte[] built = new MultipartBody().AddFile("gifData", "x.gif", "image/gif", data).Build();
			int start = IndexOf(built, new byte[] { 13, 10, 13, 10 }) + 4;
			byte[] extracted = built.Skip(start).Take(data.Length).ToArray();
			Assert.Equal(data, extracted);
		}

		[Fact]
		public void Utf8_text_is_encoded_as_utf8()
		{
			byte[] built = new MultipartBody().AddField("author", "ü").Build();
			Assert.True(IndexOf(built, new byte[] { 0xC3, 0xBC }) > 0);
		}

		[Fact]
		public void Every_body_gets_its_own_boundary_and_cannot_be_extended_after_building()
		{
			var a = new MultipartBody();
			var b = new MultipartBody();
			Assert.NotEqual(a.ContentType, b.ContentType);
			a.AddField("x", "1");
			a.Build();
			Assert.Throws<InvalidOperationException>(() => a.AddField("y", "2"));
		}

		private static int IndexOf(byte[] haystack, byte[] needle)
		{
			for (int i = 0; i <= haystack.Length - needle.Length; i++)
			{
				bool match = true;
				for (int j = 0; j < needle.Length && match; j++)
					match = haystack[i + j] == needle[j];
				if (match)
					return i;
			}
			return -1;
		}
	}

	public class SubmitterTests
	{
		private static readonly byte[] Solution = Enumerable.Range(0, 300).Select(i => (byte)(i * 7)).ToArray();
		private static readonly byte[] Gif = Encoding.ASCII.GetBytes("GIF89a").Concat(Enumerable.Range(0, 5000).Select(i => (byte)(i * 13))).ToArray();

		[Theory]
		[InlineData("SUCCESS", SubmitOutcome.Success)]
		[InlineData("NOTHING_BEATEN", SubmitOutcome.NothingBeaten)]
		[InlineData("ALREADY_PRESENT", SubmitOutcome.AlreadyPresent)]
		[InlineData("FAILURE", SubmitOutcome.Failure)]
		[InlineData("SOMETHING_NEW", SubmitOutcome.Failure)]
		public void Each_answer_maps_to_an_outcome(string answer, SubmitOutcome expected)
		{
			using (var server = new FakeZlbbServer())
			{
				server.RespondWith("{\"result\":\"" + answer + "\",\"message\":\"https://discord.example/msg/1\"}");
				SubmitResult result = new ZlbbSubmitter(new ZlbbClient(server.BaseUrl)).Submit("Uwe", Solution, Gif);
				Assert.Equal(expected, result.Outcome);
				Assert.False(string.IsNullOrEmpty(result.Describe()));
			}
		}

		[Fact]
		public void The_request_is_a_multipart_post_to_om_submit_carrying_author_solution_and_gif_unchanged()
		{
			using (var server = new FakeZlbbServer())
			{
				server.RespondWith("{\"result\":\"SUCCESS\",\"message\":null}");
				new ZlbbSubmitter(new ZlbbClient(server.BaseUrl)).Submit("Uwe Test", Solution, Gif);

				RecordedRequest request = server.Requests.Single();
				Assert.Equal("POST", request.Method);
				Assert.Equal("/om/submit", request.Path);
				Assert.StartsWith("multipart/form-data; boundary=", request.ContentType);
				Assert.Contains("ZlbbLeaderboard", request.UserAgent);

				string text = Encoding.GetEncoding("ISO-8859-1").GetString(request.Body);
				Assert.Contains("name=\"author\"\r\n\r\nUwe Test\r\n", text);
				Assert.Contains("name=\"solution\"; filename=", text);
				Assert.Contains("name=\"gifData\"; filename=", text);
				// both binary payloads arrive byte for byte
				Assert.True(Contains(request.Body, Solution));
				Assert.True(Contains(request.Body, Gif));
			}
		}

		[Fact]
		public void A_rejection_comes_back_as_failure_with_the_servers_reason()
		{
			using (var server = new FakeZlbbServer())
			{
				server.Handler = r => new FakeResponse { Status = 400, Body = "no gif" };
				SubmitResult result = new ZlbbSubmitter(new ZlbbClient(server.BaseUrl)).Submit("Uwe", Solution, Gif);
				Assert.Equal(SubmitOutcome.Failure, result.Outcome);
				Assert.Contains("400", result.Message);
				Assert.Contains("no gif", result.Message);
			}
		}

		[Fact]
		public void Server_error_unreachable_server_and_garbage_answers_never_throw()
		{
			using (var server = new FakeZlbbServer())
			{
				var submitter = new ZlbbSubmitter(new ZlbbClient(server.BaseUrl));
				server.Handler = r => new FakeResponse { Status = 500, Body = "oops" };
				Assert.Equal(SubmitOutcome.Failure, submitter.Submit("Uwe", Solution, Gif).Outcome);
				server.RespondWith("<html>not json</html>");
				Assert.Equal(SubmitOutcome.Failure, submitter.Submit("Uwe", Solution, Gif).Outcome);
				server.RespondWith("[]");
				Assert.Equal(SubmitOutcome.Failure, submitter.Submit("Uwe", Solution, Gif).Outcome);
				server.Dispose();
				SubmitResult down = submitter.Submit("Uwe", Solution, Gif);
				Assert.Equal(SubmitOutcome.Failure, down.Outcome);
				Assert.False(string.IsNullOrEmpty(down.Message));
			}
		}

		[Fact]
		public void A_slow_answer_runs_into_the_timeout_and_is_reported_as_failure()
		{
			using (var server = new FakeZlbbServer())
			{
				server.Handler = r => new FakeResponse { Body = "{\"result\":\"SUCCESS\"}", DelayMs = 2500 };
				var submitter = new ZlbbSubmitter(new ZlbbClient(server.BaseUrl)) { TimeoutMs = 600 };
				var watch = System.Diagnostics.Stopwatch.StartNew();
				SubmitResult result = submitter.Submit("Uwe", Solution, Gif);
				Assert.Equal(SubmitOutcome.Failure, result.Outcome);
				Assert.True(watch.ElapsedMilliseconds < 2400, "took " + watch.ElapsedMilliseconds + " ms");
			}
		}

		private static bool Contains(byte[] haystack, byte[] needle)
		{
			for (int i = 0; i <= haystack.Length - needle.Length; i++)
			{
				int j = 0;
				while (j < needle.Length && haystack[i + j] == needle[j]) j++;
				if (j == needle.Length) return true;
			}
			return false;
		}
	}
}
