using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ZlbbCore.Tests.Support
{
	public sealed class FakeResponse
	{
		public int Status = 200;
		public string Body = "[]";
		public bool Gzip;
		public int DelayMs;
		/// <summary>Announce the full length, send half of it, then drop the connection.</summary>
		public bool Truncate;
		public string RetryAfter;
	}

	public sealed class RecordedRequest
	{
		public string Method;
		public string Path;
		public string UserAgent;
		public string AcceptEncoding;
		public string ContentType;
		public byte[] Body = new byte[0];
	}

	/// <summary>Tiny in-process stand-in for zlbb.faendir.com; records every request so tests can count them.</summary>
	public sealed class FakeZlbbServer : IDisposable
	{
		private readonly HttpListener listener = new HttpListener();
		private readonly List<RecordedRequest> requests = new List<RecordedRequest>();
		private bool disposed;

		public string BaseUrl { get; private set; }
		public Func<RecordedRequest, FakeResponse> Handler = r => new FakeResponse();

		public FakeZlbbServer()
		{
			var probe = new TcpListener(IPAddress.Loopback, 0);
			probe.Start();
			int port = ((IPEndPoint)probe.LocalEndpoint).Port;
			probe.Stop();
			BaseUrl = "http://localhost:" + port;
			listener.Prefixes.Add(BaseUrl + "/");
			listener.Start();
			Task.Run(() => AcceptLoop());
		}

		public List<RecordedRequest> Requests
		{
			get { lock (requests) return new List<RecordedRequest>(requests); }
		}

		public int RequestCount
		{
			get { lock (requests) return requests.Count; }
		}

		public void RespondWith(string body)
		{
			Handler = r => new FakeResponse { Body = body };
		}

		private void AcceptLoop()
		{
			while (listener.IsListening)
			{
				HttpListenerContext context;
				try
				{
					context = listener.GetContext();
				}
				catch (Exception)
				{
					return;
				}
				Task.Run(() => Handle(context));
			}
		}

		private void Handle(HttpListenerContext context)
		{
			try
			{
				var recorded = new RecordedRequest
				{
					Method = context.Request.HttpMethod,
					Path = context.Request.Url.AbsolutePath,
					UserAgent = context.Request.UserAgent,
					AcceptEncoding = context.Request.Headers["Accept-Encoding"],
					ContentType = context.Request.ContentType,
				};
				if (context.Request.HasEntityBody)
				{
					using (var copy = new MemoryStream())
					{
						context.Request.InputStream.CopyTo(copy);
						recorded.Body = copy.ToArray();
					}
				}
				lock (requests)
					requests.Add(recorded);

				FakeResponse response = Handler(recorded);
				if (response.DelayMs > 0)
					Thread.Sleep(response.DelayMs);

				context.Response.StatusCode = response.Status;
				if (response.RetryAfter != null)
					context.Response.Headers["Retry-After"] = response.RetryAfter;
				context.Response.ContentType = "application/json";

				byte[] body = Encoding.UTF8.GetBytes(response.Body);
				if (response.Gzip)
				{
					using (var buffer = new MemoryStream())
					{
						using (var gzip = new GZipStream(buffer, CompressionMode.Compress, true))
							gzip.Write(body, 0, body.Length);
						body = buffer.ToArray();
					}
					context.Response.Headers["Content-Encoding"] = "gzip";
				}

				context.Response.ContentLength64 = body.Length;
				if (response.Truncate)
				{
					context.Response.OutputStream.Write(body, 0, body.Length / 2);
					context.Response.OutputStream.Flush();
					context.Response.Abort();
					return;
				}
				context.Response.OutputStream.Write(body, 0, body.Length);
				context.Response.OutputStream.Close();
			}
			catch (Exception)
			{
				// client went away; fine for a fake
			}
		}

		public void Dispose()
		{
			if (disposed)
				return;
			disposed = true;
			try { listener.Stop(); } catch (Exception) { }
			try { listener.Close(); } catch (Exception) { }
		}
	}

	/// <summary>Builds JSON in the shapes the zlbb API returns.</summary>
	public static class TestData
	{
		public static string RecordJson(string id, string puzzle, int cost = 100, int cycles = 200, int area = 30, int instructions = 10,
			bool trackless = false, bool overlap = false, string height = "4", string rate = "12.0")
		{
			return "{\"id\":\"" + id + "\",\"puzzle\":{\"id\":\"" + puzzle + "\",\"displayName\":\"x\"},\"score\":{\"cost\":" + cost + ",\"instructions\":" + instructions +
				",\"overlap\":" + (overlap ? "true" : "false") + ",\"trackless\":" + (trackless ? "true" : "false") + ",\"cycles\":" + cycles + ",\"area\":" + area +
				",\"height\":" + height + ",\"width\":4.0,\"boundingHex\":3,\"rate\":" + rate + ",\"areaINFLevel\":0,\"areaINFValue\":" + area + ".0,\"heightINF\":4.0,\"widthINF\":4.0,\"boundingHexINF\":3.0},\"author\":null}";
		}

		public static string Add(string recordJson)
		{
			return "{\"type\":\"ADD\",\"record\":" + recordJson + "}";
		}

		public static string Remove(string recordJson)
		{
			return "{\"type\":\"REMOVE\",\"record\":" + recordJson + "}";
		}

		public static string Array(params string[] items)
		{
			return "[" + string.Join(",", items) + "]";
		}

		public static string Serialize(object value)
		{
			var sb = new StringBuilder();
			Write(sb, value);
			return sb.ToString();
		}

		private static void Write(StringBuilder sb, object value)
		{
			if (value == null) sb.Append("null");
			else if (value is bool) sb.Append((bool)value ? "true" : "false");
			else if (value is double) sb.Append(((double)value).ToString("R", System.Globalization.CultureInfo.InvariantCulture));
			else if (value is string)
			{
				sb.Append('"');
				foreach (char c in (string)value)
				{
					if (c == '"' || c == '\\') sb.Append('\\').Append(c);
					else if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
					else sb.Append(c);
				}
				sb.Append('"');
			}
			else if (value is Dictionary<string, object>)
			{
				sb.Append('{');
				bool first = true;
				foreach (var pair in (Dictionary<string, object>)value)
				{
					if (!first) sb.Append(',');
					first = false;
					Write(sb, pair.Key);
					sb.Append(':');
					Write(sb, pair.Value);
				}
				sb.Append('}');
			}
			else if (value is List<object>)
			{
				sb.Append('[');
				bool first = true;
				foreach (object item in (List<object>)value)
				{
					if (!first) sb.Append(',');
					first = false;
					Write(sb, item);
				}
				sb.Append(']');
			}
			else throw new InvalidOperationException("unsupported type " + value.GetType());
		}
	}

	/// <summary>Temp directory removed on dispose.</summary>
	public sealed class TempDir : IDisposable
	{
		public string Path { get; private set; }

		public TempDir()
		{
			Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "zlbbtests", Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(Path);
		}

		public string File(string name)
		{
			return System.IO.Path.Combine(Path, name);
		}

		public void Dispose()
		{
			try { Directory.Delete(Path, true); } catch (Exception) { }
		}
	}
}
