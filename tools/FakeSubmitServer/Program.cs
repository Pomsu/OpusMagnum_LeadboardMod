using System.Net;
using System.Text;

// A stand-in for zlbb's POST /om/submit so the in-game submit flow can be tried without publishing anything.
//   dotnet run --project tools/FakeSubmitServer -- [success|nothing|already|fail] [port]
// then start the game with the environment variable ZLBB_SUBMIT_URL=http://localhost:<port>
string mode = args.Length > 0 ? args[0] : "success";
int port = args.Length > 1 ? int.Parse(args[1]) : 5099;
string answer = mode switch
{
	"nothing" => "{\"result\":\"NOTHING_BEATEN\",\"message\":null}",
	"already" => "{\"result\":\"ALREADY_PRESENT\",\"message\":null}",
	"fail" => "{\"result\":\"FAILURE\",\"message\":\"fake server says no\"}",
	_ => "{\"result\":\"SUCCESS\",\"message\":\"https://discord.example/fake-message\"}",
};

var listener = new HttpListener();
listener.Prefixes.Add($"http://localhost:{port}/");
listener.Start();
Console.WriteLine($"fake zlbb submit server on http://localhost:{port}/ answering '{mode}' (Ctrl+C to stop)");

while (true)
{
	HttpListenerContext context = listener.GetContext();
	using var copy = new MemoryStream();
	context.Request.InputStream.CopyTo(copy);
	byte[] body = copy.ToArray();
	string text = Encoding.Latin1.GetString(body);
	Console.WriteLine($"{DateTime.Now:HH:mm:ss} {context.Request.HttpMethod} {context.Request.Url!.AbsolutePath}  {body.Length} bytes  {context.Request.ContentType}");
	foreach (string name in new[] { "author", "solution", "gifData" })
	{
		int at = text.IndexOf($"name=\"{name}\"", StringComparison.Ordinal);
		Console.WriteLine($"   part '{name}': {(at >= 0 ? "present" : "MISSING")}");
	}
	int authorAt = text.IndexOf("name=\"author\"", StringComparison.Ordinal);
	if (authorAt >= 0)
	{
		int start = text.IndexOf("\r\n\r\n", authorAt, StringComparison.Ordinal) + 4;
		Console.WriteLine($"   author = {text.Substring(start, text.IndexOf("\r\n", start, StringComparison.Ordinal) - start)}");
	}
	int gifAt = text.IndexOf("GIF89a", StringComparison.Ordinal);
	Console.WriteLine($"   GIF header {(gifAt >= 0 ? "found" : "not found")}");

	if (context.Request.Url.AbsolutePath != "/om/submit")
	{
		context.Response.StatusCode = 404;
		context.Response.Close();
		continue;
	}
	byte[] reply = Encoding.UTF8.GetBytes(answer);
	context.Response.ContentType = "application/json";
	context.Response.ContentLength64 = reply.Length;
	context.Response.OutputStream.Write(reply, 0, reply.Length);
	context.Response.Close();
}
