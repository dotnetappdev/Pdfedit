using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PdfEdit.Services.Cloud;

/// <summary>A file or folder in cloud storage.</summary>
public sealed record CloudItem(string Id, string Name, bool IsFolder, long Size, DateTime? Modified, string MimeType = "")
{
    public bool IsPdf => !IsFolder && (MimeType == "application/pdf" || Name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase));
    /// <summary>A Google Doc / Sheet / Slides file or an Office file: opened as a PDF copy.</summary>
    public bool IsConvertible => !IsFolder && !IsPdf;
    public string Kind => IsFolder ? "Folder" : IsPdf ? "PDF"
        : MimeType == GoogleDriveProvider.GoogleDoc ? "Google Doc" : MimeType == GoogleDriveProvider.GoogleSheet ? "Google Sheet"
        : MimeType == GoogleDriveProvider.GoogleSlides ? "Google Slides"
        : System.IO.Path.GetExtension(Name).TrimStart('.').ToUpperInvariant() switch
        {
            "DOC" or "DOCX" or "DOCM" or "RTF" or "ODT" => "Word", "XLS" or "XLSX" or "ODS" or "CSV" => "Excel",
            "PPT" or "PPTX" or "ODP" => "PowerPoint", var e => e,
        };
    public string Glyph => IsFolder ? "" : Kind switch
    {
        "PDF" => "", "Google Sheet" or "Excel" => "", "Google Slides" or "PowerPoint" => "", _ => "",
    };
    public string SizeText => IsFolder ? "" : Size switch
    {
        < 1024 => $"{Size} B",
        < 1024 * 1024 => $"{Size / 1024.0:0} KB",
        _ => $"{Size / 1024.0 / 1024.0:0.0} MB",
    };
    public string ModifiedText => Modified?.ToLocalTime().ToString("g") ?? "";
}

/// <summary>
/// A sign-in that lives only in memory (the web version): the OAuth app's details come from the
/// server's configuration and the tokens are dropped when the browser session ends.
/// </summary>
public sealed class CloudSession
{
    public required string ClientId { get; init; }
    /// <summary>Needed by Google; for Microsoft only when the app is a "Web" (confidential) app.</summary>
    public string? ClientSecret { get; init; }
    /// <summary>Microsoft tenant: common, organizations, consumers or a tenant ID.</summary>
    public string? Tenant { get; init; }
    /// <summary>
    /// For testing: send every request to this stand-in server instead, as
    /// {TestServer}/{original host}{original path and query}.
    /// </summary>
    public string? TestServer { get; init; }

    internal string? RefreshToken { get; set; }
    internal string? AccessToken { get; set; }
    internal DateTime AccessExpiry { get; set; }
    public string? AccountName { get; internal set; }
}

/// <summary>
/// Shared parts of Google Drive and OneDrive: sign-in with the user's own OAuth app, tokens kept
/// encrypted for this Windows user (or only in memory for a <see cref="CloudSession"/>), access
/// tokens refreshed as needed, and authorised requests.
/// </summary>
public abstract class CloudProvider
{
    protected static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(10) };

    private string? _accessToken;
    private DateTime _accessExpiry;

    /// <summary>The in-memory sign-in, or null for the desktop app (settings and Windows-protected tokens).</summary>
    protected CloudSession? Session { get; }

    protected CloudProvider() { }
    protected CloudProvider(CloudSession session) => Session = session;

    /// <summary>Key used in settings ("GoogleDrive" / "OneDrive").</summary>
    public abstract string Key { get; }
    public abstract string DisplayName { get; }
    /// <summary>The ID of the top folder.</summary>
    public abstract string RootId { get; }
    /// <summary>The user has entered their app credentials in Settings.</summary>
    public abstract bool IsConfigured { get; }

    public bool IsConnected => Session != null
        ? Session.RefreshToken != null || Session.AccessToken != null
        : AppSettings.Current.CloudTokens.ContainsKey(Key);
    public string? AccountName => Session != null
        ? Session.AccountName
        : AppSettings.Current.CloudAccounts.TryGetValue(Key, out var a) ? a : null;

    /// <summary>Opens the browser to sign in (desktop) and keeps the refresh token.</summary>
    public async Task ConnectAsync(CancellationToken ct = default)
    {
        if (!IsConfigured) throw new InvalidOperationException($"Add your {DisplayName} app details in Settings → Cloud first.");
        var r = await OAuthLoopback.SignInAsync(AuthorizeUrl, LoopbackHost, ct);
        await CompleteSignInAsync(r.Code, r.RedirectUri, r.CodeVerifier, ct);
    }

    /// <summary>
    /// Finishes a sign-in: swaps the code the provider sent back to <paramref name="redirectUri"/>
    /// for tokens and keeps them. The web version calls this from its sign-in callback page.
    /// </summary>
    public async Task CompleteSignInAsync(string code, string redirectUri, string codeVerifier, CancellationToken ct = default)
    {
        SaveTokens(await ExchangeCodeAsync(code, redirectUri, codeVerifier, ct));
        string? account = null;
        try { account = await GetAccountNameAsync(ct); } catch { }
        if (Session != null)
        {
            Session.AccountName = account ?? "Signed in";
            return;
        }
        AppSettings.Current.CloudAccounts[Key] = account ?? "Signed in";
        AppSettings.Current.Save();
    }

    public void Disconnect()
    {
        _accessToken = null;
        if (Session != null)
        {
            Session.RefreshToken = Session.AccessToken = Session.AccountName = null;
            return;
        }
        AppSettings.Current.CloudTokens.Remove(Key);
        AppSettings.Current.CloudAccounts.Remove(Key);
        AppSettings.Current.Save();
    }

    /// <summary>The provider's sign-in page for (redirectUri, state, PKCE code challenge).</summary>
    public abstract string AuthorizeUrl(string redirectUri, string state, string codeChallenge);
    /// <summary>Host the desktop sign-in listens on: "127.0.0.1" (Google) or "localhost" (Microsoft).</summary>
    protected abstract string LoopbackHost { get; }

    public abstract Task<List<CloudItem>> ListAsync(string folderId, CancellationToken ct = default);
    public abstract Task<List<CloudItem>> SearchAsync(string text, CancellationToken ct = default);
    public abstract Task DownloadAsync(string fileId, string destPath, CancellationToken ct = default);

    /// <summary>
    /// Gets any listed file as a PDF: PDFs as they are, Google Docs / Sheets / Slides exported by
    /// Google, Office files converted (by the service where it can, else on this PC).
    /// </summary>
    public virtual async Task DownloadAsPdfAsync(CloudItem item, string destPath, CancellationToken ct = default)
    {
        if (item.IsPdf) { await DownloadAsync(item.Id, destPath, ct); return; }
        string tmp = Path.Combine(Path.GetTempPath(), "PdfEdit-" + Guid.NewGuid().ToString("N")[..8] + Path.GetExtension(item.Name));
        try
        {
            await DownloadAsync(item.Id, tmp, ct);
            await Task.Run(() => OfficeConversionService.Convert(tmp, destPath), ct);
        }
        finally { try { File.Delete(tmp); } catch { } }
    }
    /// <summary>Uploads a new file into a folder; returns it.</summary>
    public abstract Task<CloudItem> UploadNewAsync(string localPath, string folderId, string name, CancellationToken ct = default);
    /// <summary>Replaces the contents of an existing file.</summary>
    public abstract Task UpdateAsync(string fileId, string localPath, CancellationToken ct = default);
    public abstract Task<CloudItem> CreateFolderAsync(string parentId, string name, CancellationToken ct = default);

    protected abstract Task<TokenSet> ExchangeCodeAsync(string code, string redirectUri, string codeVerifier, CancellationToken ct);
    protected abstract Task<TokenSet> RefreshAsync(string refreshToken, CancellationToken ct);
    protected abstract Task<string?> GetAccountNameAsync(CancellationToken ct);

    protected sealed record TokenSet(string AccessToken, string? RefreshToken, int ExpiresIn);

    protected static TokenSet ParseTokens(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;
        if (r.TryGetProperty("error", out var err))
            throw new InvalidOperationException(r.TryGetProperty("error_description", out var d) ? d.GetString() : err.ToString());
        return new TokenSet(
            r.GetProperty("access_token").GetString()!,
            r.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : null,
            r.TryGetProperty("expires_in", out var e) && e.TryGetInt32(out int s) ? s : 3600);
    }

    protected async Task<TokenSet> PostTokenAsync(string url, Dictionary<string, string> form, CancellationToken ct)
    {
        using var resp = await Http.PostAsync(Route(url), new FormUrlEncodedContent(form), ct);
        string body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            string msg = body;
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("error_description", out var d)) msg = d.GetString() ?? body;
                else if (doc.RootElement.TryGetProperty("error", out var e)) msg = e.ToString();
            }
            catch { }
            throw new InvalidOperationException($"Sign-in failed: {msg}");
        }
        return ParseTokens(body);
    }

    /// <summary>A URL as it is sent: unchanged, or pointed at the session's stand-in test server.</summary>
    protected string Route(string url)
    {
        if (Session?.TestServer is not { Length: > 0 } test) return url;
        var u = new Uri(url);
        return test.TrimEnd('/') + "/" + u.Host + u.PathAndQuery;
    }

    private void SaveTokens(TokenSet t)
    {
        if (Session != null)
        {
            Session.AccessToken = t.AccessToken;
            Session.AccessExpiry = DateTime.UtcNow.AddSeconds(t.ExpiresIn - 60);
            if (t.RefreshToken != null) Session.RefreshToken = t.RefreshToken;
            return;
        }
        _accessToken = t.AccessToken;
        _accessExpiry = DateTime.UtcNow.AddSeconds(t.ExpiresIn - 60);
        if (t.RefreshToken != null)
        {
            AppSettings.Current.CloudTokens[Key] = Protect(t.RefreshToken);
            AppSettings.Current.Save();
        }
    }

    protected async Task<string> AccessTokenAsync(CancellationToken ct)
    {
        if (Session != null)
        {
            if (Session.AccessToken != null && DateTime.UtcNow < Session.AccessExpiry) return Session.AccessToken;
            if (Session.RefreshToken == null)
                throw new InvalidOperationException(Session.AccessToken == null
                    ? $"Connect {DisplayName} first." : $"Your {DisplayName} sign-in has expired. Please connect again.");
            try { SaveTokens(await RefreshAsync(Session.RefreshToken, ct)); }
            catch (InvalidOperationException ex) when (ex.Message.Contains("invalid_grant") || ex.Message.Contains("expired") || ex.Message.Contains("revoked"))
            {
                Disconnect();
                throw new InvalidOperationException($"Your {DisplayName} sign-in has expired. Please connect again.", ex);
            }
            return Session.AccessToken!;
        }
        if (_accessToken != null && DateTime.UtcNow < _accessExpiry) return _accessToken;
        if (!AppSettings.Current.CloudTokens.TryGetValue(Key, out var stored))
            throw new InvalidOperationException($"Connect {DisplayName} first (Settings → Cloud).");
        string refresh;
        try { refresh = Unprotect(stored); }
        catch { Disconnect(); throw new InvalidOperationException($"Please connect {DisplayName} again."); }
        TokenSet t;
        try { t = await RefreshAsync(refresh, ct); }
        catch (InvalidOperationException ex) when (ex.Message.Contains("invalid_grant") || ex.Message.Contains("expired") || ex.Message.Contains("revoked"))
        {
            Disconnect();
            throw new InvalidOperationException($"Your {DisplayName} sign-in has expired. Please connect again.", ex);
        }
        SaveTokens(t);
        return t.AccessToken;
    }

    /// <summary>Sends an authorised request; throws with the service's message on failure.</summary>
    protected async Task<HttpResponseMessage> SendAsync(Func<HttpRequestMessage> make, CancellationToken ct, HttpCompletionOption option = HttpCompletionOption.ResponseContentRead)
    {
        for (int attempt = 0; ; attempt++)
        {
            var req = make();
            if (Session?.TestServer != null && req.RequestUri != null) req.RequestUri = new Uri(Route(req.RequestUri.ToString()));
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await AccessTokenAsync(ct));
            var resp = await Http.SendAsync(req, option, ct);
            if (resp.IsSuccessStatusCode) return resp;
            if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized && attempt == 0)
            {
                _accessToken = null;   // expired early: refresh once and retry
                if (Session != null) Session.AccessToken = null;
                resp.Dispose();
                continue;
            }
            string body = await resp.Content.ReadAsStringAsync(ct);
            resp.Dispose();
            throw new InvalidOperationException($"{DisplayName}: {ErrorMessage(body, resp.ReasonPhrase)}");
        }
    }

    protected async Task<JsonDocument> GetJsonAsync(string url, CancellationToken ct)
    {
        using var resp = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get, url), ct);
        return JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
    }

    protected static async Task SaveToFileAsync(HttpResponseMessage resp, string destPath, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
        string tmp = destPath + ".part";
        await using (var fs = File.Create(tmp))
            await resp.Content.CopyToAsync(fs, ct);
        File.Move(tmp, destPath, overwrite: true);
    }

    private static string ErrorMessage(string body, string? fallback)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var r = doc.RootElement;
            if (r.TryGetProperty("error", out var e))
            {
                if (e.ValueKind == JsonValueKind.Object && e.TryGetProperty("message", out var m)) return m.GetString() ?? body;
                if (e.ValueKind == JsonValueKind.String) return e.GetString() ?? body;
            }
        }
        catch { }
        return string.IsNullOrWhiteSpace(body) ? fallback ?? "request failed" : body.Length > 300 ? body[..300] : body;
    }

    private static string Protect(string s) =>
        Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(s), null, DataProtectionScope.CurrentUser));

    private static string Unprotect(string s) =>
        Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(s), null, DataProtectionScope.CurrentUser));
}
