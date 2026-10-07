using System.Collections.Concurrent;
using iText.Kernel.Pdf;
using PdfDocumentInfo = PdfEdit.Models.PdfDocumentInfo;
using PdfEdit.Models;
using PdfEdit.Render;
using PdfEdit.Services;

namespace PdfEdit.Blazor.Services;

/// <summary>An uploaded PDF being viewed and filled in on the site.</summary>
public sealed class PdfSession : IDisposable
{
    public required string Id { get; init; }
    public required string FileName { get; init; }
    public required string Folder { get; init; }
    public required string SourcePath { get; init; }
    public required PdfDocumentInfo Info { get; init; }
    public required PdfiumRenderEngine Renderer { get; init; }
    public DateTime LastUsedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>The last filled-in copy saved for download, if any.</summary>
    public string? FilledPath { get; set; }

    /// <summary>Rendered page images, keyed by page index and scale.</summary>
    public ConcurrentDictionary<(int Page, double Scale), byte[]> PageCache { get; } = new();

    public void Dispose()
    {
        Renderer.Dispose();
        try { Directory.Delete(Folder, recursive: true); } catch { /* best effort */ }
    }
}

/// <summary>
/// Keeps uploaded PDFs in a temporary folder on the server, one per upload, and uses the shared
/// PdfEdit libraries on them: PdfEdit.Core reads and fills the form, PdfEdit.Render (Pdfium) draws
/// the pages. Uploads nobody has used for an hour are deleted.
/// </summary>
public sealed class PdfDocumentStore : IDisposable
{
    public const long MaxUploadBytes = 50 * 1024 * 1024;
    private static readonly TimeSpan Idle = TimeSpan.FromHours(1);

    private readonly ConcurrentDictionary<string, PdfSession> _sessions = new();
    private readonly string _root = Path.Combine(Path.GetTempPath(), "PdfEdit.Blazor");
    private readonly PdfFormService _forms = new();
    private readonly Timer _cleanup;

    public PdfDocumentStore()
    {
        Directory.CreateDirectory(_root);
        _cleanup = new Timer(_ => RemoveIdle(), null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
    }

    /// <summary>Saves <paramref name="pdf"/> and opens it. Throws when it isn't a readable PDF.</summary>
    public async Task<PdfSession> OpenAsync(Stream pdf, string fileName, CancellationToken ct = default)
    {
        var id = Guid.NewGuid().ToString("N");
        var folder = Path.Combine(_root, id);
        Directory.CreateDirectory(folder);
        var source = Path.Combine(folder, "source.pdf");
        try
        {
            await using (var file = File.Create(source))
                await pdf.CopyToAsync(file, ct);

            var info = await Task.Run(() => _forms.LoadDocument(source), ct);
            // iText also lists a radio group's or checkbox's unnamed child widgets as fields called
            // "Name." — duplicates of a real widget, which would sit on top of it.
            var names = info.FormFields.Select(f => f.Name).ToHashSet();
            info.FormFields.RemoveAll(f => f.Name.EndsWith('.') && names.Contains(f.Name.TrimEnd('.')));
            // Draw the pages without the form's own widgets: the page shows its fields as HTML
            // inputs on top, and the PDF's appearances (old values) would show through them.
            var renderPath = info.FormFields.Count > 0 ? Path.Combine(folder, "render.pdf") : source;
            if (renderPath != source) await Task.Run(() => CopyWithoutWidgets(source, renderPath), ct);
            var renderer = new PdfiumRenderEngine();
            await renderer.LoadAsync(renderPath);

            var session = new PdfSession
            {
                Id = id, FileName = Path.GetFileName(fileName), Folder = folder,
                SourcePath = source, Info = info, Renderer = renderer,
            };
            _sessions[id] = session;
            return session;
        }
        catch
        {
            try { Directory.Delete(folder, recursive: true); } catch { }
            throw;
        }
    }

    public PdfSession? Get(string id)
    {
        if (!_sessions.TryGetValue(id, out var s)) return null;
        s.LastUsedUtc = DateTime.UtcNow;
        return s;
    }

    /// <summary>A page as a PNG. <paramref name="scale"/> 1 = 96 pixels per inch.</summary>
    public async Task<byte[]> RenderPageAsync(PdfSession session, int pageIndex, double scale)
    {
        scale = Math.Clamp(Math.Round(scale, 2), 0.25, 4);
        if (session.PageCache.TryGetValue((pageIndex, scale), out var png)) return png;

        var page = await session.Renderer.RenderPageAsync(pageIndex, zoom: 1.0, dpiScale: scale);
        png = PngEncoder.FromBgra(page.Pixels ?? [], page.PixelWidth, page.PixelHeight);
        session.PageCache[(pageIndex, scale)] = png;
        return png;
    }

    /// <summary>
    /// Writes <paramref name="values"/> (field name → value) into a copy of the PDF for download.
    /// Returns any per-field problems PdfEdit.Core reports.
    /// </summary>
    public List<string> SaveFilled(PdfSession session, Dictionary<string, string> values,
        Dictionary<string, string> checkboxOnValues, bool flatten)
    {
        var dest = Path.Combine(session.Folder, $"filled-{DateTime.UtcNow:yyyyMMddHHmmss}.pdf");
        var errors = _forms.SaveFull(session.SourcePath, dest, values,
            pageRotations: new Dictionary<int, int>(),
            freeTextAnnotations: [],
            flatten: flatten,
            fieldExportValues: checkboxOnValues);
        if (session.FilledPath != null && session.FilledPath != dest)
            try { File.Delete(session.FilledPath); } catch { }
        session.FilledPath = dest;
        return errors;
    }

    private static void CopyWithoutWidgets(string source, string dest)
    {
        using var doc = new PdfDocument(new PdfReader(source), new PdfWriter(dest));
        for (int i = 1; i <= doc.GetNumberOfPages(); i++)
        {
            var page = doc.GetPage(i);
            foreach (var annot in page.GetAnnotations().ToList())
                if (PdfName.Widget.Equals(annot.GetSubtype()))
                    page.RemoveAnnotation(annot);
        }
    }

    private void RemoveIdle()
    {
        foreach (var (id, s) in _sessions)
            if (DateTime.UtcNow - s.LastUsedUtc > Idle && _sessions.TryRemove(id, out _))
                s.Dispose();
    }

    public void Dispose()
    {
        _cleanup.Dispose();
        foreach (var s in _sessions.Values) s.Dispose();
        _sessions.Clear();
    }
}
