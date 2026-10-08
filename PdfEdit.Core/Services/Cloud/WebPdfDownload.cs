using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;

namespace PdfEdit.Services.Cloud;

/// <summary>
/// Opens a PDF from a web address (File → From Link): downloads it, checks it really is a PDF and
/// gives back where it was saved. On a server, <c>blockLocalNetwork</c> refuses addresses inside the
/// server's own network (localhost, private ranges, cloud metadata), checked when the connection is
/// made so a name that resolves somewhere else later can't get round it.
/// </summary>
public static class WebPdfDownload
{
    /// <summary>A web address worth trying: http or https, with a host.</summary>
    public static Uri? Parse(string? text)
    {
        text = text?.Trim();
        if (string.IsNullOrEmpty(text)) return null;
        if (!text.Contains("://")) text = "https://" + text;
        return Uri.TryCreate(text, UriKind.Absolute, out var uri)
               && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
               && uri.Host.Length > 0 ? uri : null;
    }

    /// <summary>Downloads <paramref name="url"/> into <paramref name="folder"/>; returns the file and its name.</summary>
    public static async Task<(string Path, string Name)> DownloadAsync(Uri url, string folder, long maxBytes,
        bool blockLocalNetwork, CancellationToken ct = default)
    {
        // The proxy (if the machine uses one) is the way out to the web: connections to it are allowed.
        var proxies = new System.Collections.Concurrent.ConcurrentDictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        using var http = CreateClient(blockLocalNetwork, proxies);
        // Redirects are followed one at a time, so every address is checked before it's fetched.
        HttpResponseMessage? resp = null;
        for (int hop = 0; ; hop++)
        {
            if (blockLocalNetwork) await EnsurePublicAsync(url, ct);
            if (!HttpClient.DefaultProxy.IsBypassed(url) && HttpClient.DefaultProxy.GetProxy(url) is { } proxy)
                proxies[$"{proxy.Host}:{proxy.Port}"] = true;
            resp?.Dispose();
            resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            if ((int)resp.StatusCode is >= 300 and < 400 && resp.Headers.Location is { } next)
            {
                if (hop >= 5) throw new InvalidOperationException("That link redirects too many times.");
                url = next.IsAbsoluteUri ? next : new Uri(url, next);
                if (url.Scheme != Uri.UriSchemeHttps && url.Scheme != Uri.UriSchemeHttp)
                    throw new InvalidOperationException("That link leads somewhere that isn't a web address.");
                continue;
            }
            break;
        }
        using var _ = resp;
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"The site answered {(int)resp.StatusCode} {resp.ReasonPhrase}: the PDF isn't there or isn't public.");
        if (resp.Content.Headers.ContentLength > maxBytes)
            throw new InvalidOperationException($"That file is too big (the limit is {maxBytes / 1024 / 1024} MB).");

        string name = FileNameFor(resp.Content.Headers.ContentDisposition, url);
        Directory.CreateDirectory(folder);
        string path = System.IO.Path.Combine(folder, Guid.NewGuid().ToString("N") + ".pdf");
        await using (var src = await resp.Content.ReadAsStreamAsync(ct))
        await using (var dest = File.Create(path))
        {
            var buffer = new byte[81920];
            long total = 0;
            int n;
            while ((n = await src.ReadAsync(buffer, ct)) > 0)
            {
                total += n;
                if (total > maxBytes) throw new InvalidOperationException($"That file is too big (the limit is {maxBytes / 1024 / 1024} MB).");
                await dest.WriteAsync(buffer.AsMemory(0, n), ct);
            }
        }
        if (!LooksLikePdf(path))
        {
            File.Delete(path);
            throw new InvalidOperationException("That link isn't a PDF (it may be a web page about the file). Use the link that downloads the PDF itself.");
        }
        return (path, name);
    }

    private const string LocalRefused = "Links to addresses inside this server's own network can't be opened.";

    /// <summary>Refuses a host that is (or resolves to) an address inside the local network.</summary>
    private static async Task EnsurePublicAsync(Uri url, CancellationToken ct)
    {
        var addresses = IPAddress.TryParse(url.IdnHost.Trim('[', ']'), out var literal)
            ? [literal]
            : await Dns.GetHostAddressesAsync(url.IdnHost, ct);
        if (addresses.Length == 0 || addresses.Any(IsLocalNetwork)) throw new InvalidOperationException(LocalRefused);
    }

    private static HttpClient CreateClient(bool blockLocalNetwork, System.Collections.Concurrent.ConcurrentDictionary<string, bool> proxies)
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(15),
        };
        if (blockLocalNetwork)
        {
            // A direct connection only goes to a public address (a name can't be switched to a local
            // one between the check and the connection). A proxy the server is set up to use is fine:
            // it's the server's own way out, and the target was checked before the request.
            handler.ConnectCallback = async (context, ct) =>
            {
                bool viaProxy = proxies.ContainsKey($"{context.DnsEndPoint.Host}:{context.DnsEndPoint.Port}");
                var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, ct);
                var allowed = viaProxy ? addresses : addresses.Where(a => !IsLocalNetwork(a)).ToArray();
                if (allowed.Length == 0) throw new HttpRequestException(LocalRefused);
                var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                try
                {
                    await socket.ConnectAsync(allowed, context.DnsEndPoint.Port, ct);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch { socket.Dispose(); throw; }
            };
        }
        var http = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(2) };
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("PdfEdit", "1.3"));
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/pdf"));
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*", 0.5));
        return http;
    }

    /// <summary>Loopback, private, link-local (cloud metadata), carrier-grade NAT, multicast and unspecified addresses.</summary>
    public static bool IsLocalNetwork(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (IPAddress.IsLoopback(ip) || ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any) || ip.Equals(IPAddress.None)) return true;
        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = ip.GetAddressBytes();
            return b[0] == 0 || b[0] == 10 || b[0] == 127
                || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
                || (b[0] == 192 && b[1] == 168)
                || (b[0] == 169 && b[1] == 254)
                || (b[0] == 100 && b[1] >= 64 && b[1] <= 127)
                || b[0] >= 224;
        }
        return ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6Multicast || ip.IsIPv6UniqueLocal;
    }

    private static string FileNameFor(ContentDispositionHeaderValue? disposition, Uri url)
    {
        string? name = disposition?.FileNameStar ?? disposition?.FileName?.Trim('"');
        if (string.IsNullOrWhiteSpace(name))
        {
            name = Uri.UnescapeDataString(url.Segments.LastOrDefault()?.Trim('/') ?? "");
            if (string.IsNullOrWhiteSpace(name)) name = url.Host;
        }
        foreach (var c in System.IO.Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        if (!name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) name += ".pdf";
        return name.Length > 120 ? name[^120..] : name;
    }

    // "%PDF-" near the start (some servers put a few bytes before it).
    private static bool LooksLikePdf(string path)
    {
        var head = new byte[1024];
        int n;
        using (var f = File.OpenRead(path)) n = f.Read(head, 0, head.Length);
        return System.Text.Encoding.ASCII.GetString(head, 0, n).Contains("%PDF-");
    }
}
