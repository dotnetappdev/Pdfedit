using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.IO;

namespace PdfEdit.Services.Cloud;

/// <summary>
/// OneDrive (personal and work/school) through Microsoft Graph, with the user's own app
/// registration in Microsoft Entra ("Mobile and desktop applications" platform, redirect
/// http://localhost, public client). Shows folders and PDFs; opens, saves back and uploads.
/// </summary>
public sealed class OneDriveProvider : CloudProvider
{
    public static readonly OneDriveProvider Instance = new();

    private const string Scope = "Files.ReadWrite User.Read offline_access";
    private const string Graph = "https://graph.microsoft.com/v1.0";
    private const string Select = "$select=id,name,size,folder,file,lastModifiedDateTime";

    public override string Key => "OneDrive";
    public override string DisplayName => "OneDrive";
    public override string RootId => "root";
    public override bool IsConfigured => !string.IsNullOrWhiteSpace(AppSettings.Current.OneDriveClientId);

    private static string ClientId => AppSettings.Current.OneDriveClientId.Trim();
    private static string Tenant => string.IsNullOrWhiteSpace(AppSettings.Current.OneDriveTenant) ? "common" : AppSettings.Current.OneDriveTenant.Trim();
    private static string Authority => $"https://login.microsoftonline.com/{Uri.EscapeDataString(Tenant)}/oauth2/v2.0";

    protected override async Task<TokenSet> SignInAsync(CancellationToken ct)
    {
        var r = await OAuthLoopback.SignInAsync((redirect, state, challenge) =>
            $"{Authority}/authorize?client_id={Uri.EscapeDataString(ClientId)}&response_type=code" +
            $"&redirect_uri={Uri.EscapeDataString(redirect)}&response_mode=query&scope={Uri.EscapeDataString(Scope)}" +
            $"&state={state}&code_challenge={challenge}&code_challenge_method=S256&prompt=select_account",
            "localhost", ct);
        return await PostTokenAsync($"{Authority}/token", new()
        {
            ["client_id"] = ClientId, ["code"] = r.Code, ["redirect_uri"] = r.RedirectUri,
            ["grant_type"] = "authorization_code", ["code_verifier"] = r.CodeVerifier, ["scope"] = Scope,
        }, ct);
    }

    protected override Task<TokenSet> RefreshAsync(string refreshToken, CancellationToken ct) =>
        PostTokenAsync($"{Authority}/token", new()
        {
            ["client_id"] = ClientId, ["refresh_token"] = refreshToken, ["grant_type"] = "refresh_token", ["scope"] = Scope,
        }, ct);

    protected override async Task<string?> GetAccountNameAsync(CancellationToken ct)
    {
        using var doc = await GetJsonAsync($"{Graph}/me?$select=displayName,userPrincipalName,mail", ct);
        var r = doc.RootElement;
        foreach (var k in new[] { "mail", "userPrincipalName", "displayName" })
            if (r.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String && v.GetString() is { Length: > 0 } s) return s;
        return null;
    }

    private static string ItemPath(string id) => id == "root" ? $"{Graph}/me/drive/root" : $"{Graph}/me/drive/items/{Uri.EscapeDataString(id)}";

    public override Task<List<CloudItem>> ListAsync(string folderId, CancellationToken ct = default) =>
        PagedAsync($"{ItemPath(folderId)}/children?{Select}&$top=200", pdfAndFolders: true, ct);

    public override Task<List<CloudItem>> SearchAsync(string text, CancellationToken ct = default) =>
        PagedAsync($"{Graph}/me/drive/root/search(q='{Uri.EscapeDataString(text.Replace("'", "''"))}')?{Select}&$top=200", pdfAndFolders: false, ct);

    private async Task<List<CloudItem>> PagedAsync(string url, bool pdfAndFolders, CancellationToken ct)
    {
        var items = new List<CloudItem>();
        string? next = url;
        while (next != null && items.Count < 5000)
        {
            using var doc = await GetJsonAsync(next, ct);
            foreach (var f in doc.RootElement.GetProperty("value").EnumerateArray())
            {
                bool folder = f.TryGetProperty("folder", out _);
                string name = f.GetProperty("name").GetString() ?? "";
                bool pdf = name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);
                if (!(pdf || folder && pdfAndFolders)) continue;
                long size = f.TryGetProperty("size", out var s) && s.TryGetInt64(out var n) ? n : 0;
                DateTime? mod = f.TryGetProperty("lastModifiedDateTime", out var m) && m.TryGetDateTime(out var dt) ? dt : null;
                items.Add(new CloudItem(f.GetProperty("id").GetString()!, name, folder, size, mod));
            }
            next = doc.RootElement.TryGetProperty("@odata.nextLink", out var nl) ? nl.GetString() : null;
        }
        return items.OrderByDescending(i => i.IsFolder).ThenBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public override async Task DownloadAsync(string fileId, string destPath, CancellationToken ct = default)
    {
        using var resp = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get, $"{ItemPath(fileId)}/content"), ct, HttpCompletionOption.ResponseHeadersRead);
        await SaveToFileAsync(resp, destPath, ct);
    }

    public override async Task<CloudItem> UploadNewAsync(string localPath, string folderId, string name, CancellationToken ct = default)
    {
        string url = (folderId == "root" ? $"{Graph}/me/drive/root:" : $"{ItemPath(folderId)}:") +
                     $"/{Uri.EscapeDataString(name)}:/content?@microsoft.graph.conflictBehavior=rename";
        string id = await PutContentAsync(url, localPath, ct);
        return new CloudItem(id, name, false, new FileInfo(localPath).Length, DateTime.UtcNow);
    }

    public override Task UpdateAsync(string fileId, string localPath, CancellationToken ct = default) =>
        PutContentAsync($"{ItemPath(fileId)}/content", localPath, ct);

    /// <summary>Small files in one request; larger ones (over 4 MB) through an upload session.</summary>
    private async Task<string> PutContentAsync(string contentUrl, string localPath, CancellationToken ct)
    {
        byte[] bytes = await File.ReadAllBytesAsync(localPath, ct);
        if (bytes.Length <= 4 * 1024 * 1024)
        {
            using var resp = await SendAsync(() =>
            {
                var c = new ByteArrayContent(bytes);
                c.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
                return new HttpRequestMessage(HttpMethod.Put, contentUrl) { Content = c };
            }, ct);
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            return doc.RootElement.GetProperty("id").GetString()!;
        }

        // Upload session: …/content → …/createUploadSession (items/{id}/… or root:/{name}:/…)
        int q = contentUrl.IndexOf('?');
        string baseUrl = q < 0 ? contentUrl : contentUrl[..q];
        string sessionUrl = baseUrl[..^"/content".Length] + "/createUploadSession";
        string uploadUrl;
        using (var resp = await SendAsync(() => new HttpRequestMessage(HttpMethod.Post, sessionUrl)
               {
                   Content = new StringContent("{\"item\":{\"@microsoft.graph.conflictBehavior\":\"" + (q < 0 ? "replace" : "rename") + "\"}}", Encoding.UTF8, "application/json"),
               }, ct))
        using (var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct)))
            uploadUrl = doc.RootElement.GetProperty("uploadUrl").GetString()!;

        const int chunk = 320 * 1024 * 10;   // multiple of 320 KiB, as Graph requires
        string? id = null;
        for (long offset = 0; offset < bytes.Length; offset += chunk)
        {
            int len = (int)Math.Min(chunk, bytes.Length - offset);
            var c = new ByteArrayContent(bytes, (int)offset, len);
            c.Headers.ContentRange = new ContentRangeHeaderValue(offset, offset + len - 1, bytes.Length);
            // The upload URL is pre-authorised: no Authorization header.
            using var resp = await Http.PutAsync(uploadUrl, c, ct);
            string body = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode) throw new InvalidOperationException($"OneDrive upload failed: {body}");
            if (offset + len >= bytes.Length)
            {
                using var doc = JsonDocument.Parse(body);
                id = doc.RootElement.TryGetProperty("id", out var i) ? i.GetString() : null;
            }
        }
        return id ?? "";
    }

    public override async Task<CloudItem> CreateFolderAsync(string parentId, string name, CancellationToken ct = default)
    {
        string body = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["name"] = name, ["folder"] = new { }, ["@microsoft.graph.conflictBehavior"] = "rename",
        });
        using var resp = await SendAsync(() => new HttpRequestMessage(HttpMethod.Post, $"{ItemPath(parentId)}/children")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        }, ct);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        return new CloudItem(doc.RootElement.GetProperty("id").GetString()!, name, true, 0, DateTime.UtcNow);
    }
}
