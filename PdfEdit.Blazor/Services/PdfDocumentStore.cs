using System.Collections.Concurrent;
using iText.Kernel.Exceptions;
using iText.Kernel.Pdf;
using PdfEdit.Models;
using PdfEdit.Render;
using PdfEdit.Services;
using PdfDocumentInfo = PdfEdit.Models.PdfDocumentInfo;

namespace PdfEdit.Blazor.Services;

/// <summary>A PDF that needs a password to open.</summary>
public sealed class PasswordRequiredException(bool wrongPassword)
    : Exception(wrongPassword ? "That password isn't right." : "This PDF is protected with a password.")
{
    public bool WrongPassword { get; } = wrongPassword;
}

/// <summary>
/// An uploaded PDF being worked on. Every change writes a new version of the file (v1.pdf,
/// v2.pdf …), so Undo and Redo just step between versions.
/// </summary>
public sealed class PdfSession : IDisposable
{
    public required string Id { get; init; }
    public required string Folder { get; init; }
    public string FileName { get; set; } = "document.pdf";
    public string CurrentPath { get; set; } = "";
    public PdfDocumentInfo Info { get; set; } = new();
    public PdfiumRenderEngine Renderer { get; set; } = new();
    public List<BookmarkItem> Bookmarks { get; set; } = new();
    public List<PdfAnnotationItem> Annotations { get; set; } = new();
    public PdfMetadataInfo Metadata { get; set; } = new();

    /// <summary>Goes up with every change, so page image URLs change and browsers fetch them again.</summary>
    public int Version { get; set; }
    public bool IsModified { get; set; }
    public DateTime LastUsedUtc { get; set; } = DateTime.UtcNow;

    public Stack<string> UndoStack { get; } = new();
    public Stack<string> RedoStack { get; } = new();
    public SemaphoreSlim Lock { get; } = new(1, 1);
    public ConcurrentDictionary<(int Page, double Scale, int Version), byte[]> PageCache { get; } = new();

    internal int NextFileNumber;

    public string ExportsFolder => Path.Combine(Folder, "exports");

    public void Dispose()
    {
        Renderer.Dispose();
        try { Directory.Delete(Folder, recursive: true); } catch { /* best effort */ }
    }
}

/// <summary>
/// Keeps uploaded PDFs in a temporary folder on the server, one per upload, and runs the shared
/// PdfEdit libraries on them: PdfEdit.Core reads, fills and changes them, PdfEdit.Render (Pdfium)
/// draws the pages. Uploads nobody has used for two hours are deleted.
/// </summary>
public sealed class PdfDocumentStore : IDisposable
{
    public const long MaxUploadBytes = 100 * 1024 * 1024;
    private static readonly TimeSpan Idle = TimeSpan.FromHours(2);

    private readonly ConcurrentDictionary<string, PdfSession> _sessions = new();
    private readonly string _root = Path.Combine(Path.GetTempPath(), "PdfEdit.Blazor");
    private readonly Timer _cleanup;

    public PdfFormService Forms { get; } = new();

    public PdfDocumentStore()
    {
        Directory.CreateDirectory(_root);
        _cleanup = new Timer(_ => RemoveIdle(), null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
    }

    // ── Opening ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Saves <paramref name="pdf"/> and opens it. A password-protected PDF needs
    /// <paramref name="password"/>; the working copy has the password removed (Protect adds one back).
    /// </summary>
    public async Task<PdfSession> OpenAsync(Stream pdf, string fileName, string? password = null, CancellationToken ct = default)
    {
        var id = Guid.NewGuid().ToString("N");
        var folder = Path.Combine(_root, id);
        Directory.CreateDirectory(folder);
        var session = new PdfSession { Id = id, Folder = folder, FileName = SafeName(fileName) };
        try
        {
            var upload = Path.Combine(folder, "upload.pdf");
            await using (var file = File.Create(upload))
                await pdf.CopyToAsync(file, ct);

            var first = NewVersionPath(session);
            await Task.Run(() => Unlock(upload, first, password), ct);
            File.Delete(upload);
            session.CurrentPath = first;
            await ReloadAsync(session);
            _sessions[id] = session;
            return session;
        }
        catch
        {
            session.Dispose();
            throw;
        }
    }

    /// <summary>Opens a file on the server (the samples), as if it had been uploaded.</summary>
    public async Task<PdfSession> OpenFileAsync(string path, string? password = null)
    {
        await using var stream = File.OpenRead(path);
        return await OpenAsync(stream, Path.GetFileName(path), password);
    }

    /// <summary>A new, empty document with <paramref name="pages"/> A4 pages.</summary>
    public async Task<PdfSession> CreateBlankAsync(int pages = 1)
    {
        var temp = Path.Combine(_root, $"blank-{Guid.NewGuid():N}.pdf");
        PdfToolsService.CreateBlankPdf(temp, pages);
        try
        {
            var s = await OpenFileAsync(temp);
            s.FileName = "Untitled.pdf";
            return s;
        }
        finally { File.Delete(temp); }
    }

    private static void Unlock(string source, string dest, string? password)
    {
        var props = new ReaderProperties();
        if (!string.IsNullOrEmpty(password)) props.SetPassword(System.Text.Encoding.UTF8.GetBytes(password));
        bool encrypted;
        try
        {
            using var reader = new PdfReader(source, props);
            reader.SetUnethicalReading(true);
            using var doc = new PdfDocument(reader);
            encrypted = reader.IsEncrypted();
        }
        catch (BadPasswordException)
        {
            throw new PasswordRequiredException(wrongPassword: !string.IsNullOrEmpty(password));
        }

        if (!encrypted) { File.Copy(source, dest); return; }

        // Owner-password-only files open without one; either way the working copy is unlocked.
        var again = new ReaderProperties();
        if (!string.IsNullOrEmpty(password)) again.SetPassword(System.Text.Encoding.UTF8.GetBytes(password));
        using var rd = new PdfReader(source, again);
        rd.SetUnethicalReading(true);
        using var src = new PdfDocument(rd);
        using var outDoc = new PdfDocument(new PdfWriter(dest));
        src.CopyPagesTo(1, src.GetNumberOfPages(), outDoc, new iText.Forms.PdfPageFormCopier());
    }

    public PdfSession? Get(string id)
    {
        if (!_sessions.TryGetValue(id, out var s)) return null;
        s.LastUsedUtc = DateTime.UtcNow;
        return s;
    }

    public void Close(PdfSession session)
    {
        if (_sessions.TryRemove(session.Id, out _)) session.Dispose();
    }

    // ── Changing ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Runs <paramref name="change"/>(current file, new file) and makes the new file the current
    /// version. The old version goes on the Undo stack.
    /// </summary>
    public async Task ApplyAsync(PdfSession session, Action<string, string> change)
    {
        await session.Lock.WaitAsync();
        try
        {
            var dest = NewVersionPath(session);
            await Task.Run(() => change(session.CurrentPath, dest));
            if (!File.Exists(dest)) throw new InvalidOperationException("The change didn't produce a file.");
            session.UndoStack.Push(session.CurrentPath);
            session.RedoStack.Clear();
            session.CurrentPath = dest;
            session.IsModified = true;
            await ReloadAsync(session);
        }
        finally { session.Lock.Release(); }
    }

    public Task<bool> UndoAsync(PdfSession session) => StepAsync(session, session.UndoStack, session.RedoStack);

    public Task<bool> RedoAsync(PdfSession session) => StepAsync(session, session.RedoStack, session.UndoStack);

    private static async Task<bool> StepAsync(PdfSession session, Stack<string> from, Stack<string> to)
    {
        await session.Lock.WaitAsync();
        try
        {
            if (from.Count == 0) return false;
            to.Push(session.CurrentPath);
            session.CurrentPath = from.Pop();
            session.IsModified = true;
            await ReloadAsync(session);
            return true;
        }
        finally { session.Lock.Release(); }
    }

    /// <summary>Re-reads the current version: fields, bookmarks, comments, metadata, renderer.</summary>
    private static async Task ReloadAsync(PdfSession session)
    {
        var forms = new PdfFormService();
        var path = session.CurrentPath;
        var info = await Task.Run(() => forms.LoadDocument(path));
        // iText also lists a radio group's or checkbox's unnamed child widgets as fields called
        // "Name." — duplicates of a real widget, which would sit on top of it.
        var names = info.FormFields.Select(f => f.Name).ToHashSet();
        info.FormFields.RemoveAll(f => f.Name.EndsWith('.') && names.Contains(f.Name.TrimEnd('.')));

        session.Info = info;
        session.Bookmarks = await Task.Run(() => { try { return forms.GetBookmarks(path); } catch { return new List<BookmarkItem>(); } });
        session.Annotations = await Task.Run(() => PdfAnnotationReader.Read(path));
        session.Metadata = await Task.Run(() => { try { return forms.GetMetadata(path); } catch { return new PdfMetadataInfo(); } });
        PdfTextExtractorService.InvalidateCache(path);

        // Draw the pages without the form's own widgets: the page shows its fields as HTML inputs
        // on top, and the PDF's appearances (old values) would show through them.
        var renderPath = path;
        if (info.FormFields.Count > 0)
        {
            renderPath = Path.ChangeExtension(path, ".render.pdf");
            if (!File.Exists(renderPath)) await Task.Run(() => CopyWithoutWidgets(path, renderPath));
        }
        var renderer = new PdfiumRenderEngine();
        await renderer.LoadAsync(renderPath);
        var old = session.Renderer;
        session.Renderer = renderer;
        old.Dispose();
        session.PageCache.Clear();
        session.Version++;
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

    private static string NewVersionPath(PdfSession session) =>
        Path.Combine(session.Folder, $"v{Interlocked.Increment(ref session.NextFileNumber)}.pdf");

    // ── Pages and exports ────────────────────────────────────────────────────

    /// <summary>A page as a PNG. <paramref name="scale"/> 1 = 96 pixels per inch.</summary>
    public async Task<byte[]> RenderPageAsync(PdfSession session, int pageIndex, double scale)
    {
        scale = Math.Clamp(Math.Round(scale, 2), 0.1, 4);
        var key = (pageIndex, scale, session.Version);
        if (session.PageCache.TryGetValue(key, out var png)) return png;

        var page = await session.Renderer.RenderPageAsync(pageIndex, zoom: 1.0, dpiScale: scale);
        png = PngEncoder.FromBgra(page.Pixels ?? [], page.PixelWidth, page.PixelHeight);
        session.PageCache[key] = png;
        return png;
    }

    /// <summary>
    /// Makes a file for the user to download: <paramref name="write"/>(path) writes it into the
    /// session's exports folder. Returns the URL to download it from.
    /// </summary>
    public async Task<string> ExportAsync(PdfSession session, string fileName, Action<string> write)
    {
        Directory.CreateDirectory(session.ExportsFolder);
        var name = SafeName(fileName);
        var path = Path.Combine(session.ExportsFolder, name);
        if (File.Exists(path)) File.Delete(path);
        await Task.Run(() => write(path));
        return $"/documents/{session.Id}/exports/{Uri.EscapeDataString(name)}";
    }

    public string? ExportPath(PdfSession session, string name)
    {
        var path = Path.Combine(session.ExportsFolder, SafeName(name));
        return File.Exists(path) ? path : null;
    }

    /// <summary>
    /// A file to download that doesn't belong to an open document (a design, for instance).
    /// <paramref name="write"/>(path) writes it; returns its URL. Removed after two hours.
    /// </summary>
    public async Task<string> StageDownloadAsync(string fileName, Action<string> write)
    {
        var id = Guid.NewGuid().ToString("N");
        var folder = Path.Combine(_root, "downloads", id);
        Directory.CreateDirectory(folder);
        var name = SafeName(fileName);
        await Task.Run(() => write(Path.Combine(folder, name)));
        return $"/downloads/{id}/{Uri.EscapeDataString(name)}";
    }

    public string? DownloadPath(string id, string name)
    {
        if (!Guid.TryParseExact(id, "N", out _)) return null;
        var path = Path.Combine(_root, "downloads", id, SafeName(name));
        return File.Exists(path) ? path : null;
    }

    /// <summary>Keeps just a plain file name (no folders), so it can't point outside the session.</summary>
    public static string SafeName(string? name)
    {
        name = Path.GetFileName(name ?? "");
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return string.IsNullOrWhiteSpace(name) ? "document.pdf" : name;
    }

    private void RemoveIdle()
    {
        foreach (var (id, s) in _sessions)
            if (DateTime.UtcNow - s.LastUsedUtc > Idle && _sessions.TryRemove(id, out _))
                s.Dispose();
        try
        {
            var downloads = new DirectoryInfo(Path.Combine(_root, "downloads"));
            if (downloads.Exists)
                foreach (var d in downloads.GetDirectories())
                    if (DateTime.UtcNow - d.CreationTimeUtc > Idle) d.Delete(true);
        }
        catch { /* best effort */ }
    }

    public void Dispose()
    {
        _cleanup.Dispose();
        foreach (var s in _sessions.Values) s.Dispose();
        _sessions.Clear();
    }
}
