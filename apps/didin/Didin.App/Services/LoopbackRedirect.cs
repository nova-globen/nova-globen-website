using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Didin.App.Services;

/// <summary>
/// The desktop redirect: a one-shot listener on a loopback address and a port of the system's choosing
/// (http://127.0.0.1:{port}/signin; the site matches a loopback redirect on any port, RFC 8252 7.3). A plain socket
/// rather than HttpListener, which would need a URL reservation. It answers the redirect's query parameters.
/// </summary>
public static class LoopbackRedirect
{
    public static readonly TimeSpan Wait = TimeSpan.FromMinutes(5);

    /// <param name="authorizeUrl">Makes the authorization request for the redirect address listened on.</param>
    /// <param name="open">Opens that request in the person's browser.</param>
    public static async Task<IReadOnlyDictionary<string, string>> ReceiveAsync(Func<string, string> authorizeUrl, Func<string, Task> open, CancellationToken ct)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        await open(authorizeUrl($"http://127.0.0.1:{port}/signin"));

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(Wait);
        while (true)
        {
            using var client = await listener.AcceptTcpClientAsync(timeout.Token);
            using var stream = client.GetStream();
            var target = await ReadRequestTargetAsync(stream, timeout.Token);
            if (target == null || !target.StartsWith("/signin?", StringComparison.Ordinal))
            {
                await RespondAsync(stream, "404 Not Found", "", timeout.Token);
                continue;   // a favicon or a stray request: keep waiting for the redirect
            }
            await RespondAsync(stream, "200 OK", "<!doctype html><meta charset=\"utf-8\"><title>Didin</title>" +
                "<p style=\"font-family:system-ui;margin:3rem\">You can close this tab and go back to Didin.</p>", timeout.Token);
            return ParseQuery(target["/signin?".Length..]);
        }
    }

    /// <summary>The request line's target ("/signin?code=..."), or null for anything that is not a GET.</summary>
    private static async Task<string> ReadRequestTargetAsync(NetworkStream stream, CancellationToken ct)
    {
        var buffer = new byte[8192];
        var read = 0;
        while (read < buffer.Length)
        {
            var n = await stream.ReadAsync(buffer.AsMemory(read), ct);
            if (n == 0) break;
            read += n;
            if (Encoding.ASCII.GetString(buffer, 0, read).Contains("\r\n\r\n", StringComparison.Ordinal)) break;
        }
        var line = Encoding.ASCII.GetString(buffer, 0, read).Split("\r\n")[0].Split(' ');
        return line.Length >= 2 && line[0] == "GET" ? line[1] : null;
    }

    private static async Task RespondAsync(NetworkStream stream, string status, string html, CancellationToken ct)
    {
        var body = Encoding.UTF8.GetBytes(html);
        var head = Encoding.ASCII.GetBytes($"HTTP/1.1 {status}\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {body.Length}\r\n" +
            "Cache-Control: no-store\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(head, ct);
        await stream.WriteAsync(body, ct);
    }

    internal static Dictionary<string, string> ParseQuery(string query)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var at = pair.IndexOf('=');
            var key = Uri.UnescapeDataString((at < 0 ? pair : pair[..at]).Replace('+', ' '));
            var value = at < 0 ? "" : Uri.UnescapeDataString(pair[(at + 1)..].Replace('+', ' '));
            values.TryAdd(key, value);
        }
        return values;
    }
}
