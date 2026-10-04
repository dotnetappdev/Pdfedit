using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace PdfEdit.Services.Cloud;

/// <summary>
/// The desktop OAuth sign-in (RFC 8252): opens the provider's sign-in page in the browser and
/// catches the reply on a one-off listener on this PC (127.0.0.1 / localhost), with PKCE so the
/// code can't be used by anything else.
/// </summary>
public static class OAuthLoopback
{
    public sealed record Result(string Code, string RedirectUri, string CodeVerifier);

    /// <param name="buildAuthUrl">Makes the sign-in URL from (redirectUri, state, codeChallenge).</param>
    /// <param name="host">"127.0.0.1" (Google) or "localhost" (Microsoft).</param>
    public static async Task<Result> SignInAsync(Func<string, string, string, string> buildAuthUrl, string host, CancellationToken ct)
    {
        string verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        string challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        string state = Base64Url(RandomNumberGenerator.GetBytes(16));

        var v4 = new TcpListener(IPAddress.Loopback, 0);
        v4.Start();
        int port = ((IPEndPoint)v4.LocalEndpoint).Port;
        TcpListener? v6 = null;
        if (host == "localhost")
        {
            // "localhost" may resolve to ::1 in the browser: listen there too.
            try { v6 = new TcpListener(IPAddress.IPv6Loopback, port); v6.Start(); } catch { v6 = null; }
        }
        string redirect = $"http://{host}:{port}/";

        try
        {
            string url = buildAuthUrl(redirect, state, challenge);
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromMinutes(5));
            while (true)
            {
                var accepts = new List<Task<TcpClient>> { v4.AcceptTcpClientAsync(timeout.Token).AsTask() };
                if (v6 != null) accepts.Add(v6.AcceptTcpClientAsync(timeout.Token).AsTask());
                TcpClient accepted;
                try { accepted = await await Task.WhenAny(accepts); }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    throw new TimeoutException("Sign-in wasn't finished within 5 minutes. Please try again.");
                }
                using var client = accepted;
                var query = await ReadRequestQueryAsync(client, timeout.Token);
                if (query == null) continue;   // e.g. a favicon request
                query.TryGetValue("error", out var error);
                query.TryGetValue("code", out var code);
                query.TryGetValue("state", out var gotState);
                bool ok = error == null && code != null && gotState == state;
                await RespondAsync(client, ok
                    ? "You're signed in. You can close this tab and go back to PdfEdit."
                    : "Sign-in didn't work" + (error != null ? $" ({WebUtility.HtmlEncode(error)})" : "") + ". You can close this tab.");
                if (error != null)
                {
                    query.TryGetValue("error_description", out var desc);
                    throw new InvalidOperationException(desc ?? error);
                }
                if (!ok) throw new InvalidOperationException("The sign-in reply didn't match. Please try again.");
                return new Result(code!, redirect, verifier);
            }
        }
        finally
        {
            v4.Stop();
            v6?.Stop();
        }
    }

    private static async Task<Dictionary<string, string>?> ReadRequestQueryAsync(TcpClient client, CancellationToken ct)
    {
        var stream = client.GetStream();
        var buffer = new byte[8192];
        var sb = new StringBuilder();
        while (!sb.ToString().Contains("\r\n"))
        {
            int n = await stream.ReadAsync(buffer, ct);
            if (n == 0) break;
            sb.Append(Encoding.ASCII.GetString(buffer, 0, n));
            if (sb.Length > 65536) break;
        }
        string line = sb.ToString().Split("\r\n")[0];          // GET /?code=…&state=… HTTP/1.1
        var parts = line.Split(' ');
        if (parts.Length < 2 || parts[0] != "GET") return null;
        int q = parts[1].IndexOf('?');
        if (q < 0) return null;
        var result = new Dictionary<string, string>();
        foreach (var pair in parts[1][(q + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = pair.IndexOf('=');
            string k = Uri.UnescapeDataString(eq < 0 ? pair : pair[..eq]);
            string v = eq < 0 ? "" : Uri.UnescapeDataString(pair[(eq + 1)..].Replace('+', ' '));
            result[k] = v;
        }
        return result.ContainsKey("code") || result.ContainsKey("error") ? result : null;
    }

    private static async Task RespondAsync(TcpClient client, string message)
    {
        string html = $"<!doctype html><html><head><meta charset=\"utf-8\"><title>PdfEdit</title></head>" +
                      $"<body style=\"font-family:Segoe UI,sans-serif;text-align:center;padding-top:80px;color:#333\">" +
                      $"<h2>PdfEdit</h2><p>{WebUtility.HtmlEncode(message)}</p></body></html>";
        byte[] body = Encoding.UTF8.GetBytes(html);
        string head = $"HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n";
        var stream = client.GetStream();
        await stream.WriteAsync(Encoding.ASCII.GetBytes(head));
        await stream.WriteAsync(body);
        await stream.FlushAsync();
    }

    private static string Base64Url(byte[] data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
