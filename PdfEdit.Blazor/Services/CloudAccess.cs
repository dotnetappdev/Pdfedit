using System.Collections.Concurrent;
using System.Net;
using PdfEdit.Services.Cloud;

namespace PdfEdit.Blazor.Services;

/// <summary>
/// Google Drive and OneDrive for one browser session. The OAuth apps come from the server's
/// configuration (PdfEdit:Cloud:Google:ClientId / ClientSecret, PdfEdit:Cloud:OneDrive:ClientId /
/// ClientSecret / Tenant — environment variables or user secrets, never the repository); the
/// sign-in tokens are only kept in memory and go when the session ends.
/// </summary>
public sealed class CloudConnections
{
    public CloudConnections(IConfiguration config)
    {
        string? test = config["PdfEdit:Cloud:TestServer"];
        var providers = new List<CloudProvider>();
        if (config["PdfEdit:Cloud:Google:ClientId"] is { Length: > 0 } gid)
            providers.Add(new GoogleDriveProvider(new CloudSession
            {
                ClientId = gid, ClientSecret = config["PdfEdit:Cloud:Google:ClientSecret"], TestServer = test,
            }));
        if (config["PdfEdit:Cloud:OneDrive:ClientId"] is { Length: > 0 } mid)
            providers.Add(new OneDriveProvider(new CloudSession
            {
                ClientId = mid, ClientSecret = config["PdfEdit:Cloud:OneDrive:ClientSecret"],
                Tenant = config["PdfEdit:Cloud:OneDrive:Tenant"], TestServer = test,
            }));
        Providers = providers;
    }

    /// <summary>The services the server has an OAuth app for (empty when cloud storage isn't set up).</summary>
    public IReadOnlyList<CloudProvider> Providers { get; }

    public CloudProvider? Get(string key) => Providers.FirstOrDefault(p => p.Key == key);
}

/// <summary>
/// Sign-ins waiting for the provider to send the browser back to /cloud/callback, matched by their
/// one-off state value. Each ends after ten minutes.
/// </summary>
public sealed class CloudSignIns
{
    private sealed record Pending(CloudProvider Provider, string Verifier, string RedirectUri, Func<Exception?, Task> Done, DateTime Created);

    private readonly ConcurrentDictionary<string, Pending> _pending = new();

    /// <summary>Starts a sign-in; returns the provider's sign-in page to open.</summary>
    public string Begin(CloudProvider provider, string redirectUri, Func<Exception?, Task> done)
    {
        foreach (var old in _pending.Where(p => DateTime.UtcNow - p.Value.Created > TimeSpan.FromMinutes(10)).ToList())
            _pending.TryRemove(old.Key, out _);
        var (verifier, challenge, state) = OAuthLoopback.NewPkce();
        _pending[state] = new Pending(provider, verifier, redirectUri, done, DateTime.UtcNow);
        return provider.AuthorizeUrl(redirectUri, state, challenge);
    }

    /// <summary>The provider sent the browser back: swap the code for tokens. Returns an error message, or null.</summary>
    public async Task<string?> CompleteAsync(string? state, string? code, string? error, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(state) || !_pending.TryRemove(state, out var p) || DateTime.UtcNow - p.Created > TimeSpan.FromMinutes(10))
            return "This sign-in link has expired or was already used. Start again from PdfEdit.";
        if (!string.IsNullOrEmpty(error) || string.IsNullOrEmpty(code))
        {
            var ex = new InvalidOperationException($"{p.Provider.DisplayName} sign-in was cancelled{(string.IsNullOrEmpty(error) ? "" : $" ({error})")}.");
            await p.Done(ex);
            return ex.Message;
        }
        try
        {
            await p.Provider.CompleteSignInAsync(code, p.RedirectUri, p.Verifier, ct);
            await p.Done(null);
            return null;
        }
        catch (Exception ex)
        {
            await p.Done(ex);
            return ex.Message;
        }
    }
}

public static class CloudEndpoints
{
    public const string CallbackPath = "/cloud/callback";

    /// <summary>Where Google and Microsoft send the browser back after signing in (register it with both apps).</summary>
    public static void MapCloudEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet(CallbackPath, async (string? state, string? code, string? error, CloudSignIns signIns, CancellationToken ct) =>
        {
            string? problem = await signIns.CompleteAsync(state, code, error, ct);
            string message = problem == null ? "Signed in. You can close this window." : WebUtility.HtmlEncode(problem);
            // The sign-in runs in a pop-up: tell the editor and close it.
            string html = $$"""
                <!doctype html><html lang="en"><head><meta charset="utf-8"><title>PdfEdit sign-in</title>
                <meta name="viewport" content="width=device-width,initial-scale=1">
                <style>body{font:15px system-ui,sans-serif;margin:40px;color:#222}</style></head>
                <body><p>{{message}}</p>
                <script>try{window.opener&&window.opener.postMessage('pdfedit-cloud-signin',location.origin)}catch(e){}{{(problem == null ? "setTimeout(()=>window.close(),300);" : "")}}</script>
                </body></html>
                """;
            return Results.Content(html, "text/html");
        });
    }
}
