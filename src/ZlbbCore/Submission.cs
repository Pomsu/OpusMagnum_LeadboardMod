using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;

namespace ZlbbCore
{
	/// <summary>Validates the name a submission is published under.</summary>
	public static class AuthorName
	{
		public const int MaxLength = 32;

		/// <summary>Trims the input and checks it: 1-32 characters, letters, digits, space, '_', '-' and '.'.</summary>
		public static bool TryClean(string input, out string cleaned, out string error)
		{
			cleaned = (input ?? "").Trim();
			error = null;
			if (cleaned.Length == 0)
				error = "the name is empty";
			else if (LooksLikeSoftware(cleaned))
				error = "please use your own name (e.g. your Reddit or Discord username), not the name of a program";
			else if (cleaned.Length > MaxLength)
				error = "the name is longer than " + MaxLength + " characters";
			else
				foreach (char c in cleaned)
					if (!IsAllowed(c))
					{
						error = "'" + c + "' is not allowed (letters, digits, space, _ - . only)";
						break;
					}
			return error == null;
		}

		private static readonly string[] SoftwareNames = { "zlbb", "zlbbleaderboard", "zlbb leaderboard", "quintessential", "opus magnum", "opusmagnum", "mod", "default", "modded lightning", "moddedlightning" };

		/// <summary>zlbb's maintainer asks for names that identify a human; the mod must never publish under its own or the game's name.</summary>
		public static bool LooksLikeSoftware(string name)
		{
			string normalized = (name ?? "").Trim().ToLowerInvariant();
			return Array.IndexOf(SoftwareNames, normalized) >= 0;
		}

		public static bool IsAllowed(char c)
		{
			return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == ' ' || c == '_' || c == '-' || c == '.';
		}
	}

	/// <summary>Builds a multipart/form-data request body (RFC 7578) with text fields and binary files.</summary>
	public sealed class MultipartBody
	{
		private readonly string boundary = "----ZlbbLeaderboard" + Guid.NewGuid().ToString("N");
		private readonly MemoryStream stream = new MemoryStream();
		private bool finished;

		public string ContentType
		{
			get { return "multipart/form-data; boundary=" + boundary; }
		}

		public MultipartBody AddField(string name, string value)
		{
			Begin();
			Write("Content-Disposition: form-data; name=\"" + name + "\"\r\n\r\n");
			WriteBytes(Encoding.UTF8.GetBytes(value ?? ""));
			Write("\r\n");
			return this;
		}

		public MultipartBody AddFile(string name, string fileName, string contentType, byte[] data)
		{
			Begin();
			Write("Content-Disposition: form-data; name=\"" + name + "\"; filename=\"" + fileName + "\"\r\nContent-Type: " + contentType + "\r\n\r\n");
			WriteBytes(data);
			Write("\r\n");
			return this;
		}

		public byte[] Build()
		{
			if (!finished)
			{
				Write("--" + boundary + "--\r\n");
				finished = true;
			}
			return stream.ToArray();
		}

		private void Begin()
		{
			if (finished)
				throw new InvalidOperationException("the body is already complete");
			Write("--" + boundary + "\r\n");
		}

		private void Write(string text)
		{
			WriteBytes(Encoding.UTF8.GetBytes(text));
		}

		private void WriteBytes(byte[] bytes)
		{
			stream.Write(bytes, 0, bytes.Length);
		}
	}

	public enum SubmitOutcome
	{
		Success,
		NothingBeaten,
		AlreadyPresent,
		Failure,
	}

	public sealed class SubmitResult
	{
		public SubmitOutcome Outcome;
		/// <summary>zlbb answers with a link to its Discord announcement or an error text; show it as a reference, not as prose.</summary>
		public string Message;

		/// <summary>A short sentence for the player.</summary>
		public string Describe()
		{
			switch (Outcome)
			{
				case SubmitOutcome.Success: return "New record! Your solution is now on the zlbb leaderboard.";
				case SubmitOutcome.NothingBeaten: return "Accepted, but it does not beat any record on the leaderboard.";
				case SubmitOutcome.AlreadyPresent: return "This solution is already on the leaderboard.";
				default: return "zlbb did not accept this solution.";
			}
		}
	}

	public sealed class ZlbbSubmitter
	{
		public const int DefaultTimeoutMs = 300000;

		private readonly ZlbbClient client;

		/// <summary>zlbb processes a submission before it answers, so the default is generous (5 minutes).</summary>
		public int TimeoutMs = DefaultTimeoutMs;

		public ZlbbSubmitter(ZlbbClient client)
		{
			this.client = client;
		}

		/// <summary>
		/// POST /om/submit: the solution file, the author name and the recorded GIF. Blocking and slow (zlbb answers after processing), so call
		/// it from a background thread. Never throws for expected problems: network errors and rejections come back as <see cref="SubmitOutcome.Failure"/>.
		/// </summary>
		public SubmitResult Submit(string author, byte[] solution, byte[] gif, string gifFileName = "solution.gif")
		{
			try
			{
				var body = new MultipartBody()
					.AddField("author", author)
					.AddFile("solution", "solution.solution", "application/octet-stream", solution)
					.AddFile("gifData", gifFileName, "image/gif", gif);
				byte[] bytes = body.Build();

				var request = (HttpWebRequest)WebRequest.Create(client.BaseUrl + "/om/submit");
				request.Method = "POST";
				request.UserAgent = client.UserAgent;
				request.Accept = "application/json";
				request.ContentType = body.ContentType;
				request.ContentLength = bytes.Length;
				request.Timeout = TimeoutMs;
				request.ReadWriteTimeout = TimeoutMs;
				request.AllowWriteStreamBuffering = false;
				request.ServicePoint.Expect100Continue = false; // some proxies answer 417 to "Expect: 100-continue"
				using (Stream upload = request.GetRequestStream())
					upload.Write(bytes, 0, bytes.Length);

				string text;
				try
				{
					using (var response = (HttpWebResponse)request.GetResponse())
						text = ReadAll(response);
				}
				catch (WebException e)
				{
					var errorResponse = e.Response as HttpWebResponse;
					if (errorResponse == null)
						return Failed("could not reach zlbb: " + e.Message);
					using (errorResponse)
						return Failed("zlbb answered HTTP " + (int)errorResponse.StatusCode + ": " + Shorten(ReadAll(errorResponse)));
				}
				return Parse(text);
			}
			catch (Exception e)
			{
				if (e is IOException || e is WebException || e is JsonFormatException || e is InvalidOperationException)
					return Failed(e.Message);
				throw;
			}
		}

		public static SubmitResult Parse(string json)
		{
			var obj = MiniJson.AsObject(MiniJson.Parse(json));
			string result = MiniJson.GetString(obj, "result");
			string message = MiniJson.GetString(obj, "message");
			switch (result)
			{
				case "SUCCESS": return new SubmitResult { Outcome = SubmitOutcome.Success, Message = message };
				case "NOTHING_BEATEN": return new SubmitResult { Outcome = SubmitOutcome.NothingBeaten, Message = message };
				case "ALREADY_PRESENT": return new SubmitResult { Outcome = SubmitOutcome.AlreadyPresent, Message = message };
				default: return new SubmitResult { Outcome = SubmitOutcome.Failure, Message = message ?? "unexpected answer: " + Shorten(json) };
			}
		}

		private static SubmitResult Failed(string message)
		{
			return new SubmitResult { Outcome = SubmitOutcome.Failure, Message = message };
		}

		private static string ReadAll(HttpWebResponse response)
		{
			using (var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
				return reader.ReadToEnd();
		}

		private static string Shorten(string text)
		{
			text = (text ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
			return text.Length > 160 ? text.Substring(0, 160) + "..." : text;
		}
	}
}
