using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;

namespace ZlbbCore
{
	/// <summary>The server answered with a non-success status.</summary>
	public class ZlbbHttpException : Exception
	{
		public int StatusCode { get; private set; }
		public TimeSpan? RetryAfter { get; private set; }

		public ZlbbHttpException(int statusCode, TimeSpan? retryAfter)
			: base("zlbb answered HTTP " + statusCode)
		{
			StatusCode = statusCode;
			RetryAfter = retryAfter;
		}
	}

	public sealed class FetchStats
	{
		public int Entries;
		public int Skipped;
		/// <summary>The server's own clock (HTTP Date header) when it answered; used so a wrong PC clock cannot make a sync skip changes.</summary>
		public DateTime? ServerTimeUtc;
	}

	/// <summary>Stops reading after a byte limit, so a runaway answer cannot eat the game's memory.</summary>
	internal sealed class LimitedStream : Stream
	{
		private readonly Stream inner;
		private readonly long limit;
		private long total;

		public LimitedStream(Stream inner, long limit)
		{
			this.inner = inner;
			this.limit = limit;
		}

		public override int Read(byte[] buffer, int offset, int count)
		{
			int read = inner.Read(buffer, offset, count);
			total += read;
			if (total > limit)
				throw new IOException("the answer from zlbb is larger than " + limit / (1024 * 1024) + " MB");
			return read;
		}

		public override bool CanRead { get { return true; } }
		public override bool CanSeek { get { return false; } }
		public override bool CanWrite { get { return false; } }
		public override long Length { get { throw new NotSupportedException(); } }
		public override long Position { get { throw new NotSupportedException(); } set { throw new NotSupportedException(); } }
		public override void Flush() { }
		public override long Seek(long offset, SeekOrigin origin) { throw new NotSupportedException(); }
		public override void SetLength(long value) { throw new NotSupportedException(); }
		public override void Write(byte[] buffer, int offset, int count) { throw new NotSupportedException(); }
		protected override void Dispose(bool disposing)
		{
			if (disposing)
				inner.Dispose();
			base.Dispose(disposing);
		}
	}

	/// <summary>
	/// Minimal HTTP client for the zlbb change feed. Blocking: always call it from a background thread, never from the
	/// game's render thread. No retries on purpose - a failed sync is simply tried again at the next game start.
	/// </summary>
	public sealed class ZlbbClient
	{
		public const string DefaultBaseUrl = "https://zlbb.faendir.com";

		public string BaseUrl { get; private set; }
		public string UserAgent { get; set; }

		/// <summary>The whole change history is about 22 MB; anything far beyond that is not a zlbb answer.</summary>
		public long MaxResponseBytes = 300L * 1024 * 1024;
		public int MaxEntries = 3000000;

		static ZlbbClient()
		{
			try
			{
				// the game's .NET Framework 4.x predates TLS 1.2 as a default; 3072 = Tls12
				ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
			}
			catch (NotSupportedException)
			{
			}
		}

		public ZlbbClient(string baseUrl = DefaultBaseUrl)
		{
			BaseUrl = baseUrl.TrimEnd('/');
			UserAgent = "ZlbbLeaderboard/0.1 (Opus Magnum Quintessential mod; https://github.com/Pomsu/OpusMagnum_LeadboradMod)";
		}

		public static string FormatSince(DateTime utc)
		{
			return utc.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
		}

		/// <summary>
		/// GET /om/records/changes/{since}; streams the answer and calls <paramref name="onChange"/> for each usable entry.
		/// Entries it cannot understand are skipped (counted), so one odd entry never aborts a sync.
		/// Throws <see cref="ZlbbHttpException"/>, <see cref="WebException"/>, <see cref="IOException"/> or <see cref="JsonFormatException"/> on failure.
		/// </summary>
		public FetchStats FetchChanges(DateTime sinceUtc, Action<RecordChange> onChange, int timeoutMs)
		{
			var stats = new FetchStats();
			var request = (HttpWebRequest)WebRequest.Create(BaseUrl + "/om/records/changes/" + FormatSince(sinceUtc));
			request.Method = "GET";
			request.UserAgent = UserAgent;
			request.Accept = "application/json";
			request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
			request.Timeout = timeoutMs;
			request.ReadWriteTimeout = timeoutMs;

			HttpWebResponse response;
			try
			{
				response = (HttpWebResponse)request.GetResponse();
			}
			catch (WebException e)
			{
				var errorResponse = e.Response as HttpWebResponse;
				if (errorResponse != null)
				{
					using (errorResponse)
						throw new ZlbbHttpException((int)errorResponse.StatusCode, ParseRetryAfter(errorResponse.Headers["Retry-After"]));
				}
				throw;
			}

			using (response)
			{
				// never follow a redirect from https to plain http
				if (request.RequestUri.Scheme == Uri.UriSchemeHttps && response.ResponseUri.Scheme != Uri.UriSchemeHttps)
					throw new IOException("zlbb redirected to an insecure address");
				DateTime serverTime;
				if (DateTime.TryParse(response.Headers[HttpResponseHeader.Date], CultureInfo.InvariantCulture,
					DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out serverTime))
					stats.ServerTimeUtc = serverTime;

				using (Stream body = new LimitedStream(response.GetResponseStream(), MaxResponseBytes))
				using (var reader = new StreamReader(body, Encoding.UTF8))
					ReadChanges(reader, stats, onChange);
			}
			return stats;
		}

		private void ReadChanges(TextReader reader, FetchStats stats, Action<RecordChange> onChange)
		{
			MiniJson.ReadArray(reader, element =>
			{
				if (stats.Entries + stats.Skipped >= MaxEntries)
					throw new IOException("the answer from zlbb has more than " + MaxEntries + " entries");
				RecordChange change = RecordChange.FromJson(element);
				if (change == null)
				{
					stats.Skipped++;
					return;
				}
				stats.Entries++;
				onChange(change);
			});
		}

		private static TimeSpan? ParseRetryAfter(string value)
		{
			int seconds;
			if (!string.IsNullOrEmpty(value) && int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out seconds))
				return TimeSpan.FromSeconds(seconds);
			return null;
		}
	}
}
