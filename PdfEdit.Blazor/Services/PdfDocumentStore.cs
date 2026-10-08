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
    /// <summary>The PDF's hyperlinks (/Link annotations), clickable on the page.</summary>
    public List<PdfLinkInfo> Links { get; set; } = new();
    /// <summary>PdfEdit's own annotations, shown on the page as editable items (and left out of the page image).</summary>
    public List<OwnAnnotation> Own { get; set; } = new();
    public PdfMetadataInfo Metadata { get; set; } = new();
    /// <summary>Signature fields that have been signed (their appearance stays on the page).</summary>
    public HashSet<string> SignedFields { get; set; } = new();
    /// <summary>The cloud file this was opened from or last saved to (Save back updates it).</summary>
    public (string Provider, string FileId, string Name)? CloudFile { get; set; }
    /// <summary>The web address it was opened from (From Link or ?url=), so a link to one of its pages can be shared.</summary>
    public string? SourceUrl { get; set; }

    /// <summary>Goes up with every change, so page image URLs change and browsers fetch them again.</summary>
    public int Version { get; set; }
    public bool IsModified { get; set; }
    public DateTime LastUsedUtc { get; set; } = DateTime.UtcNow;
    /// <summary>Its window has gone (a reload, or closed with a draft kept): let it go after a short while unless a reload picks it up.</summary>
    public bool Released { get; set; }

    public Stack<string> UndoStack { get; } = new();
    public Stack<string> RedoStack { get; } = new();
    /// <summary>The file name each version had, so Undo / Redo bring a renamed document's name back (Translate).</summary>
    public Dictionary<string, string> NameOfVersion { get; } = new();
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
    /// <summary>How long a released document waits for its window to be reloaded.</summary>
    private static readonly TimeSpan ReleasedIdle = TimeSpan.FromMinutes(15);

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

    /// <summary>A reloaded page picks the document up again: it's in use, not released.</summary>
    public PdfSession? Reclaim(string id)
    {
        var s = Get(id);
        if (s != null) s.Released = false;
        return s;
    }

    /// <summary>
    /// The window working on it has gone but kept a draft: the document stays a little while so a
    /// reload carries on with it (Undo included), then goes like any idle one.
    /// </summary>
    public void Release(PdfSession session)
    {
        session.Released = true;
        session.LastUsedUtc = DateTime.UtcNow;
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
            session.NameOfVersion[session.CurrentPath] = session.FileName;
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
            session.NameOfVersion[session.CurrentPath] = session.FileName;
            session.CurrentPath = from.Pop();
            if (session.NameOfVersion.TryGetValue(session.CurrentPath, out var name)) session.FileName = name;
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
        session.Links = await Task.Run(() => { try { return LinkService.GetLinks(path); } catch { return new List<PdfLinkInfo>(); } });
        session.Own = await Task.Run(() => PdfAnnotationReader.ReadOwn(path));
        var own = session.Own.Select(o => o.Id).ToHashSet();
        session.Annotations = (await Task.Run(() => PdfAnnotationReader.Read(path)))
            .Where(a => a.Id == null || !own.Contains(a.Id)).ToList();
        session.Metadata = await Task.Run(() => { try { return forms.GetMetadata(path); } catch { return new PdfMetadataInfo(); } });
        session.SignedFields = await Task.Run(() =>
        {
            try
            {
                using var pdf = new PdfDocument(new PdfReader(path));
                return new iText.Signatures.SignatureUtil(pdf).GetSignatureNames().ToHashSet();
            }
            catch { return new HashSet<string>(); }
        });
        PdfTextExtractorService.InvalidateCache(path);

        // Draw the pages without the form's own widgets: the page shows its fields as HTML inputs
        // on top, and the PDF's appearances (old values) would show through them.
        // PdfEdit's own annotations are drawn as editable items on the page, so they're left out too.
        var renderPath = path;
        if (info.FormFields.Count > 0 || own.Count > 0)
        {
            renderPath = Path.ChangeExtension(path, ".render.pdf");
            if (!File.Exists(renderPath)) await Task.Run(() => CopyWithoutWidgets(path, renderPath, own));
        }
        var renderer = new PdfiumRenderEngine();
        await renderer.LoadAsync(renderPath);
        var old = session.Renderer;
        session.Renderer = renderer;
        old.Dispose();
        session.PageCache.Clear();
        session.Version++;
    }

    private static void CopyWithoutWidgets(string source, string dest, ISet<string> own)
    {
        using var doc = new PdfDocument(new PdfReader(source), new PdfWriter(dest));
        for (int i = 1; i <= doc.GetNumberOfPages(); i++)
        {
            var page = doc.GetPage(i);
            foreach (var annot in page.GetAnnotations().ToList())
            {
                if (PdfName.Widget.Equals(annot.GetSubtype()) && !IsSignedSignature(annot.GetPdfObject()))
                    page.RemoveAnnotation(annot);
                else if (PdfFormService.TrackedId(annot.GetPdfObject()) is { } id && own.Contains(id))
                    page.RemoveAnnotation(annot);
            }
        }
    }

    /// <summary>A signed signature field's widget: its appearance is the visible signature, so it stays.</summary>
    private static bool IsSignedSignature(PdfDictionary widget)
    {
        var field = widget.ContainsKey(PdfName.FT) ? widget : widget.GetAsDictionary(PdfName.Parent) ?? widget;
        return PdfName.Sig.Equals(field.GetAsName(PdfName.FT)) && field.Get(PdfName.V) != null;
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

    // ── Continue on another device ───────────────────────────────────────────

    /// <summary>How long a "continue on your phone" link works.</summary>
    public static readonly TimeSpan HandoffLife = TimeSpan.FromMinutes(15);
    private readonly ConcurrentDictionary<string, (string Path, string Name, DateTime Expires)> _handoffs = new();

    /// <summary>
    /// A copy of the document's current version for another device to open (its own copy, so either
    /// side can close it). Returns the code for the link: long and random, so it can't be guessed.
    /// </summary>
    public string CreateHandoff(PdfSession session)
    {
        var code = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        var folder = Path.Combine(_root, "downloads", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "handoff.pdf");
        File.Copy(session.CurrentPath, path);
        _handoffs[code] = (path, session.FileName, DateTime.UtcNow + HandoffLife);
        return code;
    }

    /// <summary>Opens a handed-over copy as a new document, or null when the link is wrong or has expired.</summary>
    public async Task<PdfSession?> OpenHandoffAsync(string code)
    {
        if (!_handoffs.TryGetValue(code, out var h) || h.Expires < DateTime.UtcNow || !File.Exists(h.Path)) return null;
        await using var stream = File.OpenRead(h.Path);
        return await OpenAsync(stream, h.Name);
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
            if (DateTime.UtcNow - s.LastUsedUtc > (s.Released ? ReleasedIdle : Idle) && _sessions.TryRemove(id, out _))
                s.Dispose();
        foreach (var (code, h) in _handoffs)
            if (h.Expires < DateTime.UtcNow) _handoffs.TryRemove(code, out _);
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
