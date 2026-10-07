using System.IO.Compression;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using PdfEdit.Blazor.Components.Editor;
using PdfEdit.Blazor.Services;
using PdfEdit.Models;
using PdfEdit.Services;

namespace PdfEdit.Blazor.Components.Pages;

/// <summary>The ribbon's commands. Each one calls the same PdfEdit.Core code as the Windows app.</summary>
public partial class Editor
{
    private string BaseName => Path.GetFileNameWithoutExtension(Doc?.FileName ?? "document");

    // ── File ─────────────────────────────────────────────────────────────────

    public async Task SaveAsync()
    {
        if (Doc == null) return;
        await RunAsync("Saving…", async () =>
        {
            await CommitPendingAsync();
            await JS.InvokeVoidAsync("pdfedit.download", $"/documents/{Doc.Id}/file?v={Doc.Version}");
            Doc.IsModified = false;
            Status($"Downloaded {Doc.FileName}");
        });
    }

    public async Task PrintAsync()
    {
        if (Doc == null) return;
        await RunAsync("Preparing to print…", async () =>
        {
            await CommitPendingAsync();
            // The browser's PDF viewer prints it.
            await JS.InvokeVoidAsync("pdfedit.openInNewTab", $"/documents/{Doc.Id}/file?inline=1&v={Doc.Version}");
            Status("Opened the PDF in a new tab: print it from there");
        });
    }

    public Task SaveFlattenedAsync() =>
        DownloadExportAsync("Flattening…", $"{BaseName} (flattened).pdf", (src, dest) =>
            Store.Forms.SaveFull(src, dest, new Dictionary<string, string>(), new Dictionary<int, int>(),
                Array.Empty<FreeTextAnnotation>(), flatten: true));

    public Task SaveAsPdfAAsync() => PdfAAsync();

    // ── Search ───────────────────────────────────────────────────────────────

    private async Task SearchKeyAsync(KeyboardEventArgs e)
    {
        if (e.Key == "Enter") await SearchAsync();
    }

    public async Task SearchAsync() => await SearchForAsync(_searchText);

    public async Task SearchForAsync(string text)
    {
        _searchText = text;
        if (Doc == null || string.IsNullOrWhiteSpace(text)) return;
        await RunAsync($"Searching for “{text}”…", async () =>
        {
            var path = Doc.CurrentPath;
            _searchHits = await Task.Run(() => PdfTextExtractorService.FindTextPositions(path, text.Trim(), ignoreCase: true));
            HitIndex = _searchHits.Count > 0 ? 0 : -1;
            ShowRight(RightTab.Search);
            Status(_searchHits.Count == 0 ? $"“{text}” wasn't found" : $"Found {_searchHits.Count} match{(_searchHits.Count == 1 ? "" : "es")}");
            if (HitIndex >= 0) await ShowHitAsync(0);
        });
    }

    public string SearchText => _searchText;

    public async Task ShowHitAsync(int index)
    {
        if (index < 0 || index >= _searchHits.Count) return;
        HitIndex = index;
        var hit = _searchHits[index];
        await ScrollToSpotAsync(hit.PageNumber - 1, ViewTopPercent(hit.PageNumber - 1, hit.Left, hit.Bottom, hit.Width, hit.Height));
    }

    // ── Pages ────────────────────────────────────────────────────────────────

    private Task RotateAsync(int degrees)
    {
        int page = _page;
        return ChangeAsync("Rotating…", $"Rotated page {page + 1}", (src, dest) =>
            Store.Forms.SaveFull(src, dest, new Dictionary<string, string>(), new Dictionary<int, int> { [page] = (degrees + 360) % 360 },
                Array.Empty<FreeTextAnnotation>()));
    }

    private Task DeletePageAsync()
    {
        int page = _page;
        return ChangeAsync("Deleting the page…", $"Deleted page {page + 1}", (src, dest) => Store.Forms.DeletePages(src, dest, [page]));
    }

    private Task InsertBlankAsync(bool before)
    {
        int page = _page;
        return ChangeAsync("Inserting a page…", before ? $"Inserted a blank page before page {page + 1}" : $"Inserted a blank page after page {page + 1}",
            (src, dest) =>
            {
                if (before) Store.Forms.InsertPageBefore(src, dest, page);
                else Store.Forms.InsertBlankPage(src, dest, page);
            });
    }

    private Task DuplicatePageAsync()
    {
        int page = _page;
        return ChangeAsync("Duplicating…", $"Duplicated page {page + 1}", (src, dest) => Store.Forms.DuplicatePage(src, dest, page));
    }

    private async Task MovePageAsync(int by)
    {
        int page = _page, target = page + by;
        if (target < 0 || target >= PageCount) return;
        var order = Enumerable.Range(0, PageCount).ToList();
        (order[page], order[target]) = (order[target], order[page]);
        await ChangeAsync("Moving the page…", $"Moved page {page + 1} {(by < 0 ? "up" : "down")}", (src, dest) => Store.Forms.ReorderPages(src, dest, order));
        await GoToPageAsync(target);
    }

    /// <summary>Saves the pages in <paramref name="range"/> ("1-3, 5") as a new PDF.</summary>
    public Task ExtractRangeAsync(string range)
    {
        var pages = WatermarkService.ParseRange(range, PageCount).Select(p => p - 1).ToList();
        if (pages.Count == 0) { Toast("Enter the pages to extract, like 1-3, 5.", "error"); return Task.CompletedTask; }
        return DownloadExportAsync("Extracting pages…", $"{BaseName} (pages {range.Replace(" ", "")}).pdf",
            (src, dest) => Store.Forms.ExtractPages(src, dest, pages));
    }

    public Task DeleteRangeAsync(string range)
    {
        var pages = WatermarkService.ParseRange(range, PageCount).Select(p => p - 1).ToList();
        if (pages.Count == 0) { Toast("Enter the pages to delete, like 2-4.", "error"); return Task.CompletedTask; }
        if (pages.Count >= PageCount) { Toast("You can't delete every page.", "error"); return Task.CompletedTask; }
        return ChangeAsync("Deleting pages…", $"Deleted {pages.Count} page{(pages.Count == 1 ? "" : "s")}", (src, dest) => Store.Forms.DeletePages(src, dest, pages));
    }

    private async Task ExportPageImageAsync()
    {
        if (Doc == null) return;
        int page = _page;
        await RunAsync("Making the image…", async () =>
        {
            await CommitPendingAsync();
            var png = await Store.RenderPageAsync(Doc, page, 3);
            var url = await Store.ExportAsync(Doc, $"{BaseName} page {page + 1}.png", path => File.WriteAllBytes(path, png));
            await JS.InvokeVoidAsync("pdfedit.download", url);
            Status($"Downloaded page {page + 1} as an image");
        });
    }

    private Task SplitAsync() =>
        DownloadExportAsync("Splitting…", $"{BaseName} (split).zip", (src, dest) =>
        {
            var folder = Directory.CreateTempSubdirectory("pdfedit-split-").FullName;
            try
            {
                var named = Path.Combine(folder, "src", PdfDocumentStore.SafeName(BaseName) + ".pdf");
                Directory.CreateDirectory(Path.GetDirectoryName(named)!);
                File.Copy(src, named);
                var outFolder = Path.Combine(folder, "pages");
                Store.Forms.SplitPdf(named, outFolder);
                ZipFile.CreateFromDirectory(outFolder, dest);
            }
            finally { Directory.Delete(folder, true); }
        });

    /// <summary>Adds the uploaded PDFs and pictures to the end of the document.</summary>
    public async Task MergeAsync(IReadOnlyList<IBrowserFile> files)
    {
        if (Doc == null || files.Count == 0) return;
        var folder = Directory.CreateTempSubdirectory("pdfedit-merge-").FullName;
        try
        {
            var paths = await SaveUploadsAsync(files, folder);
            await ChangeAsync("Merging…", $"Added {files.Count} file{(files.Count == 1 ? "" : "s")} to the end",
                (src, dest) => PdfToolsService.CombineFiles(new[] { src }.Concat(paths), dest));
        }
        finally { try { Directory.Delete(folder, true); } catch { } }
    }

    /// <summary>Inserts the uploaded PDF after the current page.</summary>
    public async Task InsertPdfAsync(IBrowserFile file, bool atStart)
    {
        if (Doc == null) return;
        var folder = Directory.CreateTempSubdirectory("pdfedit-insert-").FullName;
        try
        {
            var path = (await SaveUploadsAsync([file], folder))[0];
            int after = atStart ? -1 : _page;
            await ChangeAsync("Inserting…", $"Inserted {file.Name}", (src, dest) =>
            {
                if (after < 0) PdfToolsService.CombineFiles([path, src], dest);
                else Store.Forms.InsertPdfAt(src, path, dest, after);
            });
        }
        finally { try { Directory.Delete(folder, true); } catch { } }
    }

    // ── Document ─────────────────────────────────────────────────────────────

    public Task SetMetadataAsync(PdfMetadataInfo meta) =>
        ChangeAsync("Saving the properties…", "Document properties saved", (src, dest) => Store.Forms.SetMetadata(src, dest, meta));

    public Task WatermarkAsync(WatermarkOptions options) =>
        ChangeAsync("Adding the watermark…", "Watermark added", (src, dest) => WatermarkService.Apply(src, dest, options, _page + 1));

    private Task RemoveWatermarkAsync()
    {
        if (Doc == null) return Task.CompletedTask;
        if (!WatermarkService.HasWatermark(Doc.CurrentPath))
        {
            Toast("This PDF has no watermark added by PdfEdit.");
            return Task.CompletedTask;
        }
        return ChangeAsync("Removing the watermark…", "Watermark removed", (src, dest) => WatermarkService.Remove(src, dest));
    }

    public Task PageNumbersAsync(string format, string position, float size) =>
        ChangeAsync("Numbering pages…", "Page numbers added", (src, dest) => Store.Forms.AddPageNumbers(src, dest, format, size, 18f, position));

    public Task HeaderFooterAsync(string? header, string? footer, float size, string alignment) =>
        ChangeAsync("Adding the header and footer…", "Header and footer added",
            (src, dest) => Store.Forms.AddHeaderFooter(src, dest, header, footer, size, 18f, alignment));

    public Task BatesAsync(string prefix, int start, int digits, string suffix, string position) =>
        ChangeAsync("Adding Bates numbers…", "Bates numbers added",
            (src, dest) => Store.Forms.AddBatesNumbers(src, dest, start, digits, prefix, suffix, 8f, 18f, position));

    private async Task CompressAsync()
    {
        (long Original, long Compressed) sizes = default;
        await ChangeAsync("Compressing…", "Compressed", (src, dest) => sizes = Store.Forms.CompressPdf(src, dest));
        if (sizes.Original > 0)
            Toast($"Compressed from {UpdateSize(sizes.Original)} to {UpdateSize(sizes.Compressed)}.", "success");
    }

    private static string UpdateSize(long bytes) =>
        bytes >= 1024 * 1024 ? $"{bytes / 1024d / 1024:0.0} MB" : bytes >= 10 * 1024 ? $"{bytes / 1024d:0} KB" : $"{bytes / 1024d:0.0} KB";

    private Task GreyscaleAsync() =>
        ChangeAsync("Converting to greyscale…", "Converted to greyscale", (src, dest) => PageLayoutService.Greyscale(src, dest, null));

    public Task ResizeAsync(string paper, ResizeFit fit) =>
        ChangeAsync("Resizing pages…", $"Pages resized to {paper}", (src, dest) =>
        {
            var size = PageLayoutService.PaperSizes.First(p => p.Name == paper);
            PageLayoutService.ResizePages(src, dest, size.Width, size.Height, fit);
        });

    public Task NUpAsync(int columns, int rows, bool borders) =>
        ChangeAsync("Arranging pages…", $"{columns * rows} pages per sheet", (src, dest) =>
        {
            var a4 = PageLayoutService.PaperSizes.First(p => p.Name == "A4");
            bool landscape = columns > rows;
            PageLayoutService.NUp(src, dest, new NUpOptions(columns, rows,
                landscape ? a4.Height : a4.Width, landscape ? a4.Width : a4.Height, borders));
        });

    private Task BookletAsync() =>
        ChangeAsync("Making a booklet…", "Booklet made: print double-sided, flip on the short edge", (src, dest) =>
        {
            var a4 = PageLayoutService.PaperSizes.First(p => p.Name == "A4");
            PageLayoutService.Booklet(src, dest, a4.Height, a4.Width);
        });

    private Task PdfAAsync() =>
        DownloadExportAsync("Making a PDF/A copy…", $"{BaseName} (PDF-A).pdf", (src, dest) => Store.Forms.ConvertToPdfA(src, dest));

    // ── Security ─────────────────────────────────────────────────────────────

    public Task ProtectAsync(string? userPassword, string? ownerPassword, bool allowPrinting, bool allowCopying) =>
        DownloadExportAsync("Protecting…", $"{BaseName} (protected).pdf",
            (src, dest) => Store.Forms.EncryptPdf(src, dest, userPassword, ownerPassword, allowPrinting, allowCopying));

    public async Task SanitizeAsync(SanitizeOptions options)
    {
        string summary = "";
        await ChangeAsync("Removing hidden information…", "Hidden information removed", (src, dest) => summary = SanitizeService.Sanitize(src, dest, options));
        if (!string.IsNullOrWhiteSpace(summary)) Toast(summary, "success");
    }

    // ── Form data ────────────────────────────────────────────────────────────

    private Task ExportDataAsync()
    {
        var values = new Dictionary<string, string>(Values);
        return DownloadExportAsync("Exporting the form data…", $"{BaseName} (data).txt",
            (src, dest) => Store.Forms.ExportFormData(src, dest, values));
    }

    public async Task ImportDataAsync(IBrowserFile file)
    {
        var folder = Directory.CreateTempSubdirectory("pdfedit-data-").FullName;
        try
        {
            var path = (await SaveUploadsAsync([file], folder))[0];
            var data = Store.Forms.ImportFormData(path);
            int n = 0;
            foreach (var (name, value) in data)
                if (Values.ContainsKey(name)) { Values[name] = value; n++; }
            Toast(n == 0 ? "None of the names in that file match this form's fields." : $"Filled {n} field{(n == 1 ? "" : "s")}.", n == 0 ? "error" : "success");
        }
        catch (Exception ex) { Toast("Couldn't read that file: " + ex.Message, "error"); }
        finally { try { Directory.Delete(folder, true); } catch { } }
    }

    private void ResetForm()
    {
        foreach (var k in _original.Keys) Values[k] = _original[k];
        Status("Form reset");
    }

    private async Task CheckRequired()
    {
        if (Doc == null) return;
        var missing = Doc.Info.FormFields.Where(f => f.IsRequired && string.IsNullOrWhiteSpace(Values.GetValueOrDefault(f.Name)))
                                         .GroupBy(f => f.Name).Select(g => g.First()).ToList();
        if (missing.Count == 0) { Toast("Every required field is filled in.", "success"); return; }
        Toast($"{missing.Count} required field{(missing.Count == 1 ? " is" : "s are")} still empty: {string.Join(", ", missing.Take(5).Select(f => f.Name))}{(missing.Count > 5 ? "…" : "")}", "error");
        ShowRight(RightTab.Fields);
        await SelectFieldAsync(missing[0]);
    }

    public async Task SelectFieldAsync(FormFieldInfo f)
    {
        SelectedField = f.Name;
        await ScrollToSpotAsync(f.PageNumber - 1, ViewTopPercent(f.PageNumber - 1, f.Left, f.Bottom, f.Width, f.Height));
        await FocusAsync(FieldElementId(f));
    }

    public static string FieldElementId(FormFieldInfo f) => $"field-{f.Name.GetHashCode():x}-{f.WidgetIndex}";

    // ── Export ───────────────────────────────────────────────────────────────

    public Task ExportFromMenuAsync(ExportFormat format) => ExportAsync(format);

    private Task ExportAsync(ExportFormat format) => format switch
    {
        ExportFormat.Word => DownloadExportAsync("Exporting to Word…", BaseName + ".docx", (s, d) => PdfToolsService.ExportToWord(s, d)),
        ExportFormat.Excel => DownloadExportAsync("Exporting to Excel…", BaseName + ".xlsx", (s, d) => ExcelExportService.Export(s, d)),
        ExportFormat.Html => DownloadExportAsync("Exporting to HTML…", BaseName + ".html", (s, d) => DocumentConvertService.ToHtml(s, d)),
        ExportFormat.Markdown => DownloadExportAsync("Exporting to Markdown…", BaseName + ".md", (s, d) => DocumentConvertService.ToMarkdown(s, d)),
        ExportFormat.Epub => DownloadExportAsync("Exporting to ePub…", BaseName + ".epub", (s, d) => DocumentConvertService.ToEpub(s, d)),
        ExportFormat.Text => DownloadExportAsync("Exporting the text…", BaseName + ".txt", (s, d) =>
        {
            int pages = PageCount;
            var text = Enumerable.Range(1, pages).Select(p => PdfTextExtractorService.GetPageText(s, p));
            File.WriteAllText(d, string.Join("\n\f\n", text));
        }),
        ExportFormat.Pictures => DownloadExportAsync("Extracting pictures…", BaseName + " (pictures).zip", (s, d) =>
        {
            var folder = Directory.CreateTempSubdirectory("pdfedit-pics-").FullName;
            try
            {
                var (saved, _) = ImageExtractService.ExtractAll(s, folder);
                if (saved == 0) File.WriteAllText(Path.Combine(folder, "No pictures found.txt"), "This PDF has no pictures to extract.");
                ZipFile.CreateFromDirectory(folder, d);
            }
            finally { Directory.Delete(folder, true); }
        }),
        ExportFormat.Images => ExportRenderedAsync(asSlides: false),
        ExportFormat.PowerPoint => ExportRenderedAsync(asSlides: true),
        _ => Task.CompletedTask,
    };

    /// <summary>Page images (a ZIP of PNGs) or a PowerPoint with one slide per page.</summary>
    private async Task ExportRenderedAsync(bool asSlides)
    {
        if (Doc == null) return;
        await RunAsync(asSlides ? "Exporting to PowerPoint…" : "Making page images…", async () =>
        {
            await CommitPendingAsync();
            var doc = Doc;
            var pngs = new List<byte[]>();
            for (int p = 0; p < PageCount; p++)
            {
                Status($"Drawing page {p + 1} of {PageCount}…");
                pngs.Add(await Store.RenderPageAsync(doc, p, asSlides ? 1.5 : 2));
            }
            string url;
            if (asSlides)
            {
                var slides = pngs.Select((png, i) =>
                {
                    var (w, h) = PageSize(i);
                    if (Sideways(i)) (w, h) = (h, w);
                    return new PptxSlide(png, w, h, PdfTextExtractorService.GetPageText(doc.CurrentPath, i + 1));
                }).ToList();
                url = await Store.ExportAsync(doc, BaseName + ".pptx", path => PptxExportService.Export(path, slides));
            }
            else
            {
                url = await Store.ExportAsync(doc, BaseName + " (pages).zip", path =>
                {
                    using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
                    for (int i = 0; i < pngs.Count; i++)
                    {
                        using var s = zip.CreateEntry($"{BaseName} page {i + 1:000}.png", CompressionLevel.NoCompression).Open();
                        s.Write(pngs[i]);
                    }
                });
            }
            await JS.InvokeVoidAsync("pdfedit.download", url);
            Status(asSlides ? "Downloaded the PowerPoint" : $"Downloaded {pngs.Count} page images");
        });
    }
}
