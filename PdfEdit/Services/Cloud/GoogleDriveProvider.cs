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

    // Drive for files; Forms (read-only) so Google Forms can be turned into fillable PDFs.
    private const string Scope = "https://www.googleapis.com/auth/drive https://www.googleapis.com/auth/forms.body.readonly";
    private const string Api = "https://www.googleapis.com/drive/v3";
    private const string Upload = "https://www.googleapis.com/upload/drive/v3";
    private const string FolderMime = "application/vnd.google-apps.folder";
    public const string GoogleDoc = "application/vnd.google-apps.document";
    public const string GoogleSheet = "application/vnd.google-apps.spreadsheet";
    public const string GoogleSlides = "application/vnd.google-apps.presentation";
    public const string GoogleForm = "application/vnd.google-apps.form";

    /// <summary>The files the browser shows: PDFs, Google Docs / Sheets / Slides and Office files.</summary>
    private static readonly string Openable = "(" + string.Join(" or ", new[]
    {
        "application/pdf", FolderMime, GoogleDoc, GoogleSheet, GoogleSlides, GoogleForm,
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document", "application/msword",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "application/vnd.ms-excel",
        "application/vnd.openxmlformats-officedocument.presentationml.presentation", "application/vnd.ms-powerpoint",
        "application/rtf", "application/vnd.oasis.opendocument.text",
    }.Select(m => $"mimeType='{m}'")) + ")";
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
            ? $"sharedWithMe and trashed=false and {Openable}"
            : $"'{Esc(folderId)}' in parents and trashed=false and {Openable}", ct);

    public override Task<List<CloudItem>> SearchAsync(string text, CancellationToken ct = default) =>
        QueryAsync($"name contains '{Esc(text)}' and trashed=false and mimeType!='{FolderMime}' and {Openable}", ct);

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
                string mime = f.GetProperty("mimeType").GetString() ?? "";
                items.Add(new CloudItem(f.GetProperty("id").GetString()!, f.GetProperty("name").GetString() ?? "",
                    mime == FolderMime, size, mod, mime));
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

    public override async Task DownloadAsPdfAsync(CloudItem item, string destPath, CancellationToken ct = default)
    {
        if (item.MimeType == GoogleForm)
        {
            // A form becomes a fillable PDF form: questions and their fields.
            FormSpec spec;
            try { spec = await GoogleForms.FetchByIdAsync(item.Id, ct); }
            catch (InvalidOperationException) { spec = await GoogleForms.FetchAsync($"https://docs.google.com/forms/d/{item.Id}/viewform", ct); }
            if (string.IsNullOrWhiteSpace(spec.Title)) spec.Title = item.Name;
            await Task.Run(() => FormPdfBuilder.Build(spec, destPath), ct);
        }
        else if (item.MimeType.StartsWith("application/vnd.google-apps.")) await ExportPdfAsync(item.Id, destPath, ct);
        else await base.DownloadAsPdfAsync(item, destPath, ct);
    }

    /// <summary>A Google Doc, Sheet or Slides file exported by Google as a PDF.</summary>
    public async Task ExportPdfAsync(string fileId, string destPath, CancellationToken ct = default)
    {
        using var resp = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get,
            $"{Api}/files/{Uri.EscapeDataString(fileId)}/export?mimeType=application/pdf"), ct, HttpCompletionOption.ResponseHeadersRead);
        await SaveToFileAsync(resp, destPath, ct);
    }

    /// <summary>A Google Form's questions (Forms API). Needs the Forms API enabled in the Google project.</summary>
    public async Task<string> GetFormJsonAsync(string formId, CancellationToken ct = default)
    {
        try
        {
            using var resp = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get,
                $"https://forms.googleapis.com/v1/forms/{Uri.EscapeDataString(formId)}"), ct);
            return await resp.Content.ReadAsStringAsync(ct);
        }
        catch (InvalidOperationException ex)
        {
            throw new InvalidOperationException("Google Forms couldn't be read through your Google app. In Google Cloud Console enable the " +
                "Google Forms API for your project, then sign out and connect Google Drive again (Settings → Cloud). " + ex.Message, ex);
        }
    }

    /// <summary>A file's name and type, by ID.</summary>
    public async Task<CloudItem> GetItemAsync(string fileId, CancellationToken ct = default)
    {
        using var doc = await GetJsonAsync($"{Api}/files/{Uri.EscapeDataString(fileId)}?supportsAllDrives=true&fields=id,name,mimeType,size,modifiedTime", ct);
        var f = doc.RootElement;
        string mime = f.GetProperty("mimeType").GetString() ?? "";
        long size = f.TryGetProperty("size", out var s) && long.TryParse(s.GetString(), out var n) ? n : 0;
        return new CloudItem(fileId, f.GetProperty("name").GetString() ?? "Google file", mime == FolderMime, size, null, mime);
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
