using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.IO;

namespace PdfEdit.Services.Cloud;

/// <summary>
/// Google Drive (Drive API v3) with the user's own OAuth client ("Desktop app" type in Google
/// Cloud Console). Shows folders and PDFs; opens, saves back and uploads new files.
/// </summary>
public sealed class GoogleDriveProvider : CloudProvider
{
    public static readonly GoogleDriveProvider Instance = new();

    private const string Scope = "https://www.googleapis.com/auth/drive";
    private const string Api = "https://www.googleapis.com/drive/v3";
    private const string Upload = "https://www.googleapis.com/upload/drive/v3";
    private const string FolderMime = "application/vnd.google-apps.folder";
    /// <summary>Pseudo-folder for files other people shared.</summary>
    public const string SharedWithMe = "shared-with-me";

    public override string Key => "GoogleDrive";
    public override string DisplayName => "Google Drive";
    public override string RootId => "root";
    public override bool IsConfigured =>
        !string.IsNullOrWhiteSpace(AppSettings.Current.GoogleClientId) && !string.IsNullOrWhiteSpace(AppSettings.Current.GoogleClientSecret);

    private static string ClientId => AppSettings.Current.GoogleClientId.Trim();
    private static string ClientSecret => AppSettings.Current.GoogleClientSecret.Trim();

    protected override async Task<TokenSet> SignInAsync(CancellationToken ct)
    {
        var r = await OAuthLoopback.SignInAsync((redirect, state, challenge) =>
            "https://accounts.google.com/o/oauth2/v2/auth" +
            $"?client_id={Uri.EscapeDataString(ClientId)}&redirect_uri={Uri.EscapeDataString(redirect)}" +
            $"&response_type=code&scope={Uri.EscapeDataString(Scope)}&state={state}" +
            $"&code_challenge={challenge}&code_challenge_method=S256&access_type=offline&prompt=consent",
            "127.0.0.1", ct);
        return await PostTokenAsync("https://oauth2.googleapis.com/token", new()
        {
            ["code"] = r.Code, ["client_id"] = ClientId, ["client_secret"] = ClientSecret,
            ["redirect_uri"] = r.RedirectUri, ["grant_type"] = "authorization_code", ["code_verifier"] = r.CodeVerifier,
        }, ct);
    }

    protected override Task<TokenSet> RefreshAsync(string refreshToken, CancellationToken ct) =>
        PostTokenAsync("https://oauth2.googleapis.com/token", new()
        {
            ["client_id"] = ClientId, ["client_secret"] = ClientSecret,
            ["refresh_token"] = refreshToken, ["grant_type"] = "refresh_token",
        }, ct);

    protected override async Task<string?> GetAccountNameAsync(CancellationToken ct)
    {
        using var doc = await GetJsonAsync($"{Api}/about?fields=user(displayName,emailAddress)", ct);
        var u = doc.RootElement.GetProperty("user");
        return u.TryGetProperty("emailAddress", out var e) ? e.GetString() : u.GetProperty("displayName").GetString();
    }

    public override Task<List<CloudItem>> ListAsync(string folderId, CancellationToken ct = default) =>
        QueryAsync(folderId == SharedWithMe
            ? $"sharedWithMe and trashed=false and (mimeType='application/pdf' or mimeType='{FolderMime}')"
            : $"'{Esc(folderId)}' in parents and trashed=false and (mimeType='application/pdf' or mimeType='{FolderMime}')", ct);

    public override Task<List<CloudItem>> SearchAsync(string text, CancellationToken ct = default) =>
        QueryAsync($"name contains '{Esc(text)}' and trashed=false and mimeType='application/pdf'", ct);

    private async Task<List<CloudItem>> QueryAsync(string q, CancellationToken ct)
    {
        var items = new List<CloudItem>();
        string? page = null;
        do
        {
            string url = $"{Api}/files?q={Uri.EscapeDataString(q)}&orderBy=folder,name_natural&pageSize=200" +
                         "&supportsAllDrives=true&includeItemsFromAllDrives=true" +
                         "&fields=nextPageToken,files(id,name,mimeType,size,modifiedTime)" +
                         (page != null ? $"&pageToken={Uri.EscapeDataString(page)}" : "");
            using var doc = await GetJsonAsync(url, ct);
            foreach (var f in doc.RootElement.GetProperty("files").EnumerateArray())
            {
                long size = f.TryGetProperty("size", out var s) && long.TryParse(s.GetString(), out var n) ? n : 0;
                DateTime? mod = f.TryGetProperty("modifiedTime", out var m) && m.TryGetDateTime(out var dt) ? dt : null;
                items.Add(new CloudItem(f.GetProperty("id").GetString()!, f.GetProperty("name").GetString() ?? "",
                    f.GetProperty("mimeType").GetString() == FolderMime, size, mod));
            }
            page = doc.RootElement.TryGetProperty("nextPageToken", out var t) ? t.GetString() : null;
        } while (page != null && items.Count < 5000);
        return items;
    }

    public override async Task DownloadAsync(string fileId, string destPath, CancellationToken ct = default)
    {
        using var resp = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get,
            $"{Api}/files/{Uri.EscapeDataString(fileId)}?alt=media&supportsAllDrives=true"), ct, HttpCompletionOption.ResponseHeadersRead);
        await SaveToFileAsync(resp, destPath, ct);
    }

    public override async Task<CloudItem> UploadNewAsync(string localPath, string folderId, string name, CancellationToken ct = default)
    {
        byte[] bytes = await File.ReadAllBytesAsync(localPath, ct);
        string meta = JsonSerializer.Serialize(new { name, mimeType = "application/pdf", parents = new[] { folderId } });
        using var resp = await SendAsync(() =>
        {
            var content = new MultipartContent("related");
            content.Add(new StringContent(meta, Encoding.UTF8, "application/json"));
            var file = new ByteArrayContent(bytes);
            file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
            content.Add(file);
            return new HttpRequestMessage(HttpMethod.Post,
                $"{Upload}/files?uploadType=multipart&supportsAllDrives=true&fields=id,name,size,modifiedTime") { Content = content };
        }, ct);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        return new CloudItem(doc.RootElement.GetProperty("id").GetString()!, name, false, bytes.LongLength, DateTime.UtcNow);
    }

    public override async Task UpdateAsync(string fileId, string localPath, CancellationToken ct = default)
    {
        byte[] bytes = await File.ReadAllBytesAsync(localPath, ct);
        using var _ = await SendAsync(() =>
        {
            var file = new ByteArrayContent(bytes);
            file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
            return new HttpRequestMessage(HttpMethod.Patch,
                $"{Upload}/files/{Uri.EscapeDataString(fileId)}?uploadType=media&supportsAllDrives=true") { Content = file };
        }, ct);
    }

    public override async Task<CloudItem> CreateFolderAsync(string parentId, string name, CancellationToken ct = default)
    {
        string meta = JsonSerializer.Serialize(new { name, mimeType = FolderMime, parents = new[] { parentId } });
        using var resp = await SendAsync(() => new HttpRequestMessage(HttpMethod.Post, $"{Api}/files?supportsAllDrives=true&fields=id")
        {
            Content = new StringContent(meta, Encoding.UTF8, "application/json"),
        }, ct);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        return new CloudItem(doc.RootElement.GetProperty("id").GetString()!, name, true, 0, DateTime.UtcNow);
    }

    /// <summary>Escapes a value for a Drive query string literal.</summary>
    private static string Esc(string s) => s.Replace("\\", "\\\\").Replace("'", "\\'");
}
