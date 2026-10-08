using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;
using PdfEdit.Blazor.Components.Editor;
using PdfEdit.Blazor.Services;
using PdfEdit.Models;
using PdfEdit.Services;
using PdfEdit.Services.Cloud;

namespace PdfEdit.Blazor.Components.Pages;

/// <summary>
/// The Tools tab's Snapshot, Select Text and Edit Images, the clipboard, Clean Up Scans and
/// importing a Google Docs link — the Windows app's versions, done in the browser.
/// </summary>
public partial class Editor
{
    private double _highlightOpacity = 0.4;

    public int HighlightOpacityPercent
    {
        get => (int)Math.Round(_highlightOpacity * 100);
        set => _highlightOpacity = Math.Clamp(value, 10, 100) / 100.0;
    }

    // ── Rendering part of a page ─────────────────────────────────────────────

    /// <summary>A box on the page (points from the seen page's top-left) as a PNG, at <paramref name="scale"/> × 96 dpi.</summary>
    private async Task<byte[]?> RenderAreaAsync(int page, double left, double top, double width, double height, double scale = 2)
    {
        if (Doc == null) return null;
        var r = await Doc.Renderer.RenderPageAsync(page, 1.0, scale);
        var (vw, vh) = ViewSize(page);
        double sx = r.PixelWidth / vw, sy = r.PixelHeight / vh;
        int x0 = Math.Clamp((int)Math.Floor(left * sx), 0, r.PixelWidth - 1), y0 = Math.Clamp((int)Math.Floor(top * sy), 0, r.PixelHeight - 1);
        int x1 = Math.Clamp((int)Math.Ceiling((left + width) * sx), x0 + 1, r.PixelWidth), y1 = Math.Clamp((int)Math.Ceiling((top + height) * sy), y0 + 1, r.PixelHeight);
        int w = x1 - x0, h = y1 - y0;
        var src = r.Pixels ?? [];
        var crop = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
            Buffer.BlockCopy(src, ((y0 + y) * r.PixelWidth + x0) * 4, crop, y * w * 4, w * 4);
        return PngEncoder.FromBgra(crop, w, h);
    }

    // ── Snapshot ─────────────────────────────────────────────────────────────

    public byte[]? SnapshotPng { get; private set; }
    public string SnapshotUrl => SnapshotPng == null ? "" : "data:image/png;base64," + Convert.ToBase64String(SnapshotPng);

    private async Task SnapshotAsync(int page, double left, double top, double width, double height)
    {
        if (width < 4 || height < 4) { Toast("Drag a box round what to snap."); return; }
        await RunAsync("Taking the snapshot…", async () =>
        {
            await CommitPendingAsync();
            SnapshotPng = await RenderAreaAsync(page, left, top, width, height, 2);
            _dialog = DialogKind.Snapshot;
            Status("Snapshot taken");
        });
    }

    public async Task CopySnapshotAsync()
    {
        if (SnapshotPng == null) return;
        bool ok = await JS.InvokeAsync<bool>("pdfedit.copyImage", Convert.ToBase64String(SnapshotPng));
        Toast(ok ? "Copied — paste it into another app." : "Your browser didn't allow copying a picture. Use Save instead.", ok ? "success" : "error");
    }

    public async Task SaveSnapshotAsync()
    {
        if (SnapshotPng == null) return;
        var png = SnapshotPng;
        var url = await Store.StageDownloadAsync($"{BaseName} snapshot.png", path => File.WriteAllBytes(path, png));
        await JS.InvokeVoidAsync("pdfedit.download", url);
    }

    // ── Select Text ──────────────────────────────────────────────────────────

    public string SelectedPageText { get; private set; } = "";
    private (int Page, double Left, double Top, double Width, double Height)? _textArea;

    private async Task SelectTextAsync(int page, double left, double top, double width, double height)
    {
        if (Doc == null) return;
        if (width < 4 || height < 4) { Toast("Drag over the text to select it."); return; }
        await CommitPendingAsync();
        var u = ToUser(page, left, top, width, height);
        var path = Doc.CurrentPath;
        var chunks = await Task.Run(() => PageTextLocator.GetChunks(path, page + 1));
        var inside = chunks.Where(c => c.X1 > u.Left && c.X0 < u.Left + u.Width && c.MidY > u.Bottom && c.MidY < u.Bottom + u.Height).ToList();
        // Lines from the top down, words left to right.
        var lines = new List<List<TextChunk>>();
        foreach (var c in inside.OrderByDescending(c => c.MidY))
        {
            var line = lines.FirstOrDefault(l => Math.Abs(l[0].MidY - c.MidY) < Math.Max(2, (c.Top - c.Bottom) * 0.5));
            if (line == null) lines.Add([c]); else line.Add(c);
        }
        SelectedPageText = string.Join("\n", lines.Select(l =>
        {
            var sorted = l.OrderBy(c => c.X0).ToList();
            var sb = new System.Text.StringBuilder(sorted[0].Text);
            for (int i = 1; i < sorted.Count; i++)
            {
                if (sorted[i].X0 - sorted[i - 1].X1 > (sorted[i].Top - sorted[i].Bottom) * 0.2 && !sb.ToString().EndsWith(' ') && !sorted[i].Text.StartsWith(' ')) sb.Append(' ');
                sb.Append(sorted[i].Text);
            }
            return sb.ToString().Trim();
        }).Where(s => s.Length > 0));
        _textArea = (page, left, top, width, height);
        _dialog = DialogKind.SelectedText;
        Status(SelectedPageText.Length == 0 ? "No text there — it may be a picture (Snapshot copies it)" : $"Selected {SelectedPageText.Length} characters");
    }

    public async Task CopySelectedTextAsync()
    {
        await JS.InvokeVoidAsync("pdfedit.copyText", SelectedPageText);
        Toast("Copied.", "success");
    }

    /// <summary>Highlights (or underlines, strikes through) the selected area.</summary>
    public void MarkSelectedArea(HighlightKind kind)
    {
        if (_textArea is not { } a) return;
        _items.Add(new PageItem
        {
            Kind = ItemKind.Highlight, Page = a.Page, Left = a.Left, Top = a.Top, Width = a.Width, Height = a.Height,
            Color = kind == HighlightKind.Highlight ? _highlightColor : _strokeColor, Markup = kind, Opacity = _highlightOpacity,
        });
        _dialog = DialogKind.None;
        Status("Added — Apply Changes or Download writes it into the PDF");
    }

    public async Task SnapshotSelectedAreaAsync()
    {
        if (_textArea is not { } a) return;
        await SnapshotAsync(a.Page, a.Left, a.Top, a.Width, a.Height);
    }

    /// <summary>Explain, Summarise, Rewrite, Translate or Ask about the selected text (AI chat).</summary>
    public async Task AskAboutSelectionAsync(string action, string? question = null)
    {
        if (SelectedPageText.Length == 0) return;
        string prompt = action switch
        {
            "explain" => "Explain this text from the PDF in plain English:\n\n",
            "summarise" => "Summarise this text from the PDF in a few bullet points:\n\n",
            "rewrite" => "Rewrite this text from the PDF so it's clearer and easier to read, keeping the meaning:\n\n",
            "translate" => $"Translate this text from the PDF into {TranslateLanguage}:\n\n",
            _ => (string.IsNullOrWhiteSpace(question) ? "What does this mean?" : question.Trim()) + "\n\nText from the PDF:\n\n",
        };
        _dialog = DialogKind.None;
        await SendAiAsync(prompt + SelectedPageText);
    }

    // ── Edit Images ──────────────────────────────────────────────────────────

    public PageImageInfo? EditingImage { get; private set; }
    /// <summary>Where the picture has been dragged to (PDF rectangle), until Apply.</summary>
    public (double Left, double Bottom, double Width, double Height)? ImageMove { get; private set; }

    private async Task PickImageAsync(int page, double x, double y)
    {
        if (Doc == null) return;
        await CommitPendingAsync();
        var p = ToUserPoint(page, x, y);
        var path = Doc.CurrentPath;
        var images = await Task.Run(() => ImageEditService.GetImages(path, page + 1));
        var hit = images.Where(i => p.X >= i.Left && p.X <= i.Left + i.Width && p.Y >= i.Bottom && p.Y <= i.Bottom + i.Height)
                        .OrderBy(i => i.Width * i.Height).FirstOrDefault();
        EditingImage = hit;
        ImageMove = null;
        Status(hit == null
            ? images.Count == 0 ? "There are no pictures on this page that can be edited" : "Click on a picture"
            : $"Picture {hit.PixelWidth} × {hit.PixelHeight} — drag to move, drag the corner to resize, or use the buttons");
    }

    public void ImageBoxMoved(double leftPct, double topPct, double widthPct, double heightPct)
    {
        if (EditingImage is not { } img) return;
        int page = img.PageNumber - 1;
        var (pw, ph) = ViewSize(page);
        double w = Math.Max(4, widthPct / 100 * pw), h = Math.Max(4, heightPct / 100 * ph);
        double l = Math.Clamp(leftPct / 100 * pw, 0, pw - w), t = Math.Clamp(topPct / 100 * ph, 0, ph - h);
        var u = ToUser(page, l, t, w, h);
        ImageMove = (u.Left, u.Bottom, u.Width, u.Height);
    }

    public (double Left, double Bottom, double Width, double Height) ImageRect =>
        ImageMove ?? (EditingImage is { } i ? (i.Left, i.Bottom, i.Width, i.Height) : default);

    public async Task ApplyImageMoveAsync()
    {
        if (EditingImage is not { } img || ImageMove is not { } m) return;
        await ChangeAsync("Moving the picture…", "Picture moved", (src, dest) =>
            ImageEditService.MoveResize(src, dest, img.PageNumber, img.Index, m.Left, m.Bottom, m.Width, m.Height));
        await ReselectImageAsync(img, m.Left + m.Width / 2, m.Bottom + m.Height / 2);
    }

    public async Task DeleteImageAsync()
    {
        if (EditingImage is not { } img) return;
        await ChangeAsync("Deleting the picture…", "Picture deleted — Undo brings it back", (src, dest) => ImageEditService.Delete(src, dest, img.PageNumber, img.Index));
        EditingImage = null;
        ImageMove = null;
    }

    public async Task ReplaceImageAsync(IBrowserFile file)
    {
        if (EditingImage is not { } img) return;
        using var ms = new MemoryStream();
        await using (var s = file.OpenReadStream(20 * 1024 * 1024)) await s.CopyToAsync(ms);
        var bytes = ms.ToArray();
        await ChangeAsync("Replacing the picture…", "Picture replaced", (src, dest) => ImageEditService.Replace(src, dest, img.PageNumber, img.Index, bytes));
        await ReselectImageAsync(img, img.Left + img.Width / 2, img.Bottom + img.Height / 2);
    }

    public async Task SaveImageAsync()
    {
        if (EditingImage is not { } img || Doc == null) return;
        var doc = Doc;
        await RunAsync("Getting the picture…", async () =>
        {
            var (bytes, ext) = await Task.Run(() => ImageEditService.GetImageFile(doc.CurrentPath, img.PageNumber, img.Index));
            var url = await Store.ExportAsync(doc, $"{BaseName} page {img.PageNumber} picture {img.Index + 1}{ext}", path => File.WriteAllBytes(path, bytes));
            await JS.InvokeVoidAsync("pdfedit.download", url);
        });
    }

    public void CloseImageEdit()
    {
        EditingImage = null;
        ImageMove = null;
    }

    private async Task ReselectImageAsync(PageImageInfo old, double cx, double cy)
    {
        ImageMove = null;
        if (Doc == null) return;
        var images = await Task.Run(() => ImageEditService.GetImages(Doc.CurrentPath, old.PageNumber));
        EditingImage = images.FirstOrDefault(i => i.Index == old.Index)
                       ?? images.FirstOrDefault(i => cx >= i.Left && cx <= i.Left + i.Width && cy >= i.Bottom && cy <= i.Bottom + i.Height);
    }

    // ── Clipboard ────────────────────────────────────────────────────────────

    private PageItem? _clipItem;
    private DesignItem? _clipDesign;

    public bool CanCopy => DesignMode ? SelectedDesignItem != null : SelectedItem != null;

    private async Task CopyAsync()
    {
        if (DesignMode)
        {
            if (SelectedDesignItem is not { } d) return;
            _clipDesign = d.Clone();
            if (d.Type == "text" && d.Text is { Length: > 0 } dt) await TryCopyText(dt);
            Status("Copied");
            return;
        }
        if (SelectedItem is not { } item) return;
        _clipItem = item.Copy();
        if (item.Kind is ItemKind.Text or ItemKind.Callout or ItemKind.Note && item.Text.Length > 0) await TryCopyText(item.Text);
        Status($"Copied {item.Describe().ToLowerInvariant()}");
    }

    private async Task CutAsync()
    {
        await CopyAsync();
        if (DesignMode) DeleteDesignItem();
        else if (SelectedItem is { Locked: false } item) { _items.Remove(item); SelectedItemId = null; Status("Cut"); }
    }

    private async Task TryCopyText(string text)
    {
        try { await JS.InvokeVoidAsync("pdfedit.copyText", text); } catch { /* clipboard blocked: still copied inside PdfEdit */ }
    }

    /// <summary>Pastes what was copied here; otherwise a picture (or text) from the computer's clipboard.</summary>
    private async Task PasteAsync()
    {
        if (DesignMode && _clipDesign != null)
        {
            DesignCheckpoint();
            var d = _clipDesign.Clone();
            d.X += 12; d.Y += 12;
            _clipDesign = d.Clone();
            Design.Elements ??= new();
            Design.Elements.Add(d);
            SelectedDesignId = d.Id;
            Status("Pasted");
            return;
        }
        if (!DesignMode && _clipItem != null && Doc != null)
        {
            var item = _clipItem.Copy();
            int page = _page;
            var (pw, ph) = ViewSize(page);
            double dx = item.Page == page ? 12 : 0;
            item.Page = page;
            item.Left = Math.Clamp(item.Left + dx, 0, Math.Max(0, pw - item.Width));
            item.Top = Math.Clamp(item.Top + dx, 0, Math.Max(0, ph - item.Height));
            if (item.Points != null) item.Points = item.Points.Select(p => new PointD(p.X + dx, p.Y + dx)).ToList();
            _clipItem = item.Copy();
            _items.Add(item);
            SelectedItemId = item.Id;
            Status($"Pasted {item.Describe().ToLowerInvariant()} — Apply Changes or Download writes it into the PDF");
            return;
        }
        // From the computer's clipboard (the browser asks the first time).
        try
        {
            var clip = await JS.InvokeAsync<ClipboardContent?>("pdfedit.readClipboard");
            if (clip?.Image is { Length: > 0 } b64) { await PastePictureAsync(Convert.FromBase64String(b64)); return; }
            if (clip?.Text is { Length: > 0 } text && !DesignMode && Doc != null)
            {
                var (pw, _) = ViewSize(_page);
                _items.Add(new PageItem
                {
                    Kind = ItemKind.Text, Page = _page, Left = 72, Top = 72, Width = Math.Min(pw - 100, Math.Max(80, text.Length * _fontSize * 0.5)),
                    Height = _fontSize * 1.45 * Math.Max(1, text.Split('\n').Length), FontSize = _fontSize, Color = _textColor, Text = text,
                });
                Status("Pasted the text — drag it into place");
                return;
            }
        }
        catch { /* clipboard not allowed */ }
        Toast("Nothing to paste. Copy something first (Ctrl+C), or press Ctrl+V to paste a picture from another app.");
    }

    public sealed record ClipboardContent(string? Image, string? Text);

    /// <summary>A picture pasted with Ctrl+V (from the browser's paste event).</summary>
    [JSInvokable]
    public async Task OnPastePicture(string base64)
    {
        try { await PastePictureAsync(Convert.FromBase64String(base64)); }
        catch (Exception ex) { Toast("Couldn't paste that picture: " + ex.Message, "error"); }
        await InvokeAsync(StateHasChanged);
    }

    private async Task PastePictureAsync(byte[] png)
    {
        double iw, ih;
        try
        {
            var data = iText.IO.Image.ImageDataFactory.Create(png);
            (iw, ih) = (data.GetWidth(), data.GetHeight());
        }
        catch { Toast("That isn't a picture PdfEdit can use.", "error"); return; }
        if (DesignMode)
        {
            DesignCheckpoint();
            double w = Math.Min(300, iw * 0.75), h = w * ih / Math.Max(1, iw);
            var d = new DesignItem { Type = "image", X = 60, Y = 60, W = w, H = h, Signature = Convert.ToBase64String(png) };
            Design.Elements ??= new();
            Design.Elements.Add(d);
            SelectedDesignId = d.Id;
            Status("Pasted the picture");
            return;
        }
        if (Doc == null) return;
        var (pw, ph) = ViewSize(_page);
        double sw = Math.Min(pw * 0.6, iw * 0.75), sh = sw * ih / Math.Max(1, iw);
        if (sh > ph * 0.6) { sh = ph * 0.6; sw = sh * iw / Math.Max(1, ih); }
        var item = new PageItem
        {
            Kind = ItemKind.Picture, Page = _page, Left = (pw - sw) / 2, Top = (ph - sh) / 3, Width = sw, Height = sh,
            Image = png, ImageUrl = "data:image/png;base64," + Convert.ToBase64String(png),
        };
        _items.Add(item);
        SelectedItemId = item.Id;
        Status("Pasted the picture — drag it into place; Apply Changes or Download writes it into the PDF");
        await Task.CompletedTask;
    }

    // ── Clean Up Scans ───────────────────────────────────────────────────────

    public List<PageScan> CleanupScans { get; private set; } = new();
    public ScanFindings? CleanupFindings { get; private set; }
    public bool CleanupRemoveBlank { get; set; } = true;
    public bool CleanupStraighten { get; set; } = true;
    public bool CleanupSplit { get; set; }

    private async Task ShowCleanupAsync()
    {
        if (Doc == null) return;
        await RunAsync("Looking at the pages…", async () =>
        {
            await CommitPendingAsync();
            var scans = new List<PageScan>();
            for (int i = 0; i < PageCount; i++)
            {
                Status($"Looking at page {i + 1} of {PageCount}…");
                StateHasChanged();
                var r = await Doc.Renderer.RenderPageAsync(i, 1.0, 1.0);
                int index = i;
                scans.Add(await Task.Run(() => ScanCleanup.Analyse(index, r.Pixels ?? [], r.PixelWidth, r.PixelHeight)));
            }
            CleanupScans = scans;
            CleanupFindings = ScanCleanup.Describe(scans);
            CleanupRemoveBlank = CleanupFindings.CanRemoveBlank;
            CleanupStraighten = CleanupFindings.CanStraighten;
            CleanupSplit = false;
            _dialog = DialogKind.Cleanup;
            Status("Clean Up Scans");
        });
    }

    public async Task ApplyCleanupAsync()
    {
        var plans = ScanCleanup.Plan(CleanupScans, CleanupRemoveBlank, CleanupStraighten, CleanupSplit);
        _dialog = DialogKind.None;
        if (plans.Count == 0) { Toast("Nothing to change."); return; }
        string summary = "";
        await ChangeAsync("Cleaning up the scans…", "Scans cleaned up — Undo puts them back", (src, dest) => summary = ScanCleanup.Apply(src, dest, plans));
        if (summary.Length > 0) Toast(summary, "success");
    }

    // ── From Google Docs ─────────────────────────────────────────────────────

    private static readonly HttpClient GoogleHttp = new() { Timeout = TimeSpan.FromMinutes(2) };

    /// <summary>
    /// From Link: the web address of a PDF, downloaded on the server and opened. Addresses inside the
    /// server's own network are refused (so a link can't reach the server's neighbours).
    /// </summary>
    public async Task OpenWebLinkAsync(string link)
    {
        if (PdfEdit.Services.Cloud.WebPdfDownload.Parse(link) is not { } url)
        {
            Toast("That isn't a web address. It should look like https://example.com/form.pdf or a docs.google.com link.", "error");
            return;
        }
        _dialog = DialogKind.None;
        var folder = Directory.CreateTempSubdirectory("pdfedit-link-").FullName;
        try
        {
            string? pdf = null, name = "document.pdf";
            await RunAsync($"Downloading from {url.Host}…", async () =>
            {
                try { (pdf, name) = await PdfEdit.Services.Cloud.WebPdfDownload.DownloadAsync(url, folder, PdfDocumentStore.MaxUploadBytes, blockLocalNetwork: true); }
                catch (HttpRequestException ex) { throw new InvalidOperationException("Couldn't download that PDF: " + ex.Message); }
                catch (TaskCanceledException) { throw new InvalidOperationException("The site took too long to send the PDF."); }
            });
            if (pdf != null && File.Exists(pdf))
            {
                await OpenPathAsync(pdf, name, null);
                if (Doc != null && Doc.FileName == PdfDocumentStore.SafeName(name)) Doc.SourceUrl = url.AbsoluteUri;
            }
        }
        finally { try { Directory.Delete(folder, true); } catch { } }
    }

    /// <summary>
    /// A Google Doc, Sheet or Slides file shared as "Anyone with the link", fetched as a PDF (the
    /// Windows app also uses a connected account; here use Import from Cloud for private files).
    /// </summary>
    public async Task ImportGoogleLinkAsync(string link)
    {
        var parsed = GoogleLinkImport.Parse(link.Trim());
        if (parsed is not { } p)
        {
            await OpenWebLinkAsync(link);
            return;
        }
        _dialog = DialogKind.None;
        string id = Uri.EscapeDataString(p.Id);
        string url = p.Kind switch
        {
            "document" => $"https://docs.google.com/document/d/{id}/export?format=pdf",
            "spreadsheets" => $"https://docs.google.com/spreadsheets/d/{id}/export?format=pdf",
            "presentation" => $"https://docs.google.com/presentation/d/{id}/export/pdf",
            "drawings" => $"https://docs.google.com/drawings/d/{id}/export/pdf",
            _ => $"https://drive.google.com/uc?export=download&id={id}",
        };
        var folder = Directory.CreateTempSubdirectory("pdfedit-google-").FullName;
        try
        {
            string? pdf = null, name = "Google document.pdf";
            await RunAsync("Importing from Google…", async () =>
            {
                using var resp = await GoogleHttp.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                string type = resp.Content.Headers.ContentType?.MediaType ?? "";
                if (!resp.IsSuccessStatusCode || type.Contains("html"))
                    throw new InvalidOperationException("Google wouldn't share that file. Set its sharing to “Anyone with the link”, or open it with Import from Cloud.");
                if (resp.Content.Headers.ContentLength > PdfDocumentStore.MaxUploadBytes) throw new InvalidOperationException("That file is too big.");
                string fileName = resp.Content.Headers.ContentDisposition?.FileNameStar ?? resp.Content.Headers.ContentDisposition?.FileName?.Trim('"') ?? name;
                string ext = Path.GetExtension(fileName).ToLowerInvariant();
                var download = Path.Combine(folder, "download" + (ext.Length > 0 ? ext : ".pdf"));
                await using (var fs = File.Create(download)) await resp.Content.CopyToAsync(fs);
                name = Path.GetFileNameWithoutExtension(fileName) + ".pdf";
                if (type == "application/pdf" || ext == ".pdf") pdf = download;
                else
                {
                    pdf = Path.Combine(folder, "converted.pdf");
                    await Task.Run(() => OfficeConversionService.Convert(download, pdf));
                }
            });
            if (pdf != null && File.Exists(pdf)) await OpenPathAsync(pdf, name, null);
        }
        finally { try { Directory.Delete(folder, true); } catch { } }
    }
}
