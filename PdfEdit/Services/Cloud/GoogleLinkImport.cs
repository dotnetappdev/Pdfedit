using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;

namespace PdfEdit.Services.Cloud;

/// <summary>
/// Import from a Google Docs / Sheets / Slides (or Drive file) link as a PDF. With Google Drive
/// connected any file you can see works; without it, files shared as "Anyone with the link" do.
/// </summary>
public static class GoogleLinkImport
{
    private static readonly HttpClient Http = new(new HttpClientHandler { AllowAutoRedirect = true }) { Timeout = TimeSpan.FromMinutes(5) };

    private static readonly Regex DocRx = new(@"docs\.google\.com/(document|spreadsheets|presentation|drawings)/(?:u/\d+/)?d/([A-Za-z0-9_-]{20,})", RegexOptions.IgnoreCase);
    private static readonly Regex FileRx = new(@"drive\.google\.com/(?:file/(?:u/\d+/)?d/|open\?id=|uc\?(?:[^#]*&)?id=)([A-Za-z0-9_-]{20,})", RegexOptions.IgnoreCase);

    /// <summary>(kind, id) from a link, or null if it isn't a Google Docs / Drive link.</summary>
    public static (string Kind, string Id)? Parse(string link)
    {
        if (GoogleForms.IsFormLink(link)) return ("form", "");
        var m = DocRx.Match(link);
        if (m.Success) return (m.Groups[1].Value.ToLowerInvariant(), m.Groups[2].Value);
        m = FileRx.Match(link);
        return m.Success ? ("file", m.Groups[1].Value) : null;
    }

    /// <summary>Downloads the linked file as a PDF into <paramref name="folder"/>; returns its path.</summary>
    public static async Task<string> ImportAsync(string link, string folder, CancellationToken ct = default)
    {
        // A Google Form becomes a fillable PDF form.
        if (GoogleForms.IsFormLink(link)) return await GoogleForms.ImportAsync(link, folder, ct);

        var parsed = Parse(link) ?? throw new InvalidOperationException("That isn't a Google Docs, Sheets, Slides, Forms or Drive link.");
        Directory.CreateDirectory(folder);

        // Connected: the API works for private files too.
        var google = GoogleDriveProvider.Instance;
        if (google.IsConnected)
        {
            try
            {
                var item = await google.GetItemAsync(parsed.Id, ct);
                string name = Path.GetFileNameWithoutExtension(item.IsPdf ? item.Name : item.Name + ".x");
                string dest = Free(folder, name);
                await google.DownloadAsPdfAsync(item, dest, ct);
                return dest;
            }
            catch (InvalidOperationException) when (!ct.IsCancellationRequested) { /* not visible to this account: try the public link */ }
        }

        string url = parsed.Kind switch
        {
            "document" => $"https://docs.google.com/document/d/{parsed.Id}/export?format=pdf",
            "spreadsheets" => $"https://docs.google.com/spreadsheets/d/{parsed.Id}/export?format=pdf",
            "presentation" => $"https://docs.google.com/presentation/d/{parsed.Id}/export/pdf",
            "drawings" => $"https://docs.google.com/drawings/d/{parsed.Id}/export/pdf",
            _ => $"https://drive.google.com/uc?export=download&id={parsed.Id}",
        };
        using var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        string type = resp.Content.Headers.ContentType?.MediaType ?? "";
        if (!resp.IsSuccessStatusCode || type.Contains("html"))
            throw new InvalidOperationException(
                "Google wouldn't share that file. Either connect Google Drive (Settings → Cloud) with an account that can see it, " +
                "or set the file's sharing to “Anyone with the link”.");

        string fileName = resp.Content.Headers.ContentDisposition?.FileNameStar ?? resp.Content.Headers.ContentDisposition?.FileName?.Trim('"') ?? "Google document.pdf";
        string baseName = Path.GetFileNameWithoutExtension(fileName);
        string ext = Path.GetExtension(fileName).ToLowerInvariant();
        string target = Free(folder, baseName);
        if (type == "application/pdf" || ext == ".pdf")
        {
            await using var fs = File.Create(target);
            await resp.Content.CopyToAsync(fs, ct);
            return target;
        }
        // A Word / Office file shared on Drive: download, then convert here.
        string tmp = Path.Combine(Path.GetTempPath(), "PdfEdit-" + Guid.NewGuid().ToString("N")[..8] + (ext.Length > 0 ? ext : ".docx"));
        try
        {
            await using (var fs = File.Create(tmp)) await resp.Content.CopyToAsync(fs, ct);
            await Task.Run(() => OfficeConversionService.Convert(tmp, target), ct);
            return target;
        }
        finally { try { File.Delete(tmp); } catch { } }
    }

    private static string Free(string folder, string name)
    {
        string safe = string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim();
        if (safe.Length == 0) safe = "Google document";
        string dest = Path.Combine(folder, safe + ".pdf");
        for (int i = 2; File.Exists(dest); i++) dest = Path.Combine(folder, $"{safe} ({i}).pdf");
        return dest;
    }
}
