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
			using (Stream body = response.GetResponseStream())
			using (var reader = new StreamReader(body, Encoding.UTF8))
			{
				MiniJson.ReadArray(reader, element =>
				{
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
			return stats;
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
