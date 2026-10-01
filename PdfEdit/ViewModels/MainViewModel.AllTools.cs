using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using PdfEdit.Services;

namespace PdfEdit.ViewModels;

/// <summary>
/// Commands behind the "All tools" panel (Acrobat's All tools list) that did not already exist:
/// create a PDF, combine files, export to Word, Scan &amp; OCR and request e-signatures.
/// Everything else in the panel reuses the existing commands.
/// </summary>
public partial class MainViewModel
{
    public ICommand CreateBlankPdfCommand { get; private set; } = null!;
    public ICommand CreatePdfFromImagesCommand { get; private set; } = null!;
    public ICommand CombineFilesCommand { get; private set; } = null!;
    public ICommand ExportWordCommand { get; private set; } = null!;
    public ICommand OcrCurrentPageCommand { get; private set; } = null!;
    public ICommand OcrMakeSearchableCommand { get; private set; } = null!;
    public ICommand ScanCommand { get; private set; } = null!;
    public ICommand RequestSignaturesCommand { get; private set; } = null!;

    private const string ImageFilter = "Images (*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff)|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff";

    private void InitAllToolsCommands()
    {
        CreateBlankPdfCommand      = new AsyncRelayCommand(CreateBlankPdfAsync);
        CreatePdfFromImagesCommand = new AsyncRelayCommand(CreatePdfFromImagesAsync);
        CombineFilesCommand        = new AsyncRelayCommand(CombineFilesAsync);
        ExportWordCommand          = new AsyncRelayCommand(ExportWordAsync, () => HasDocument);
        OcrCurrentPageCommand      = new AsyncRelayCommand(OcrCurrentPageAsync, () => HasDocument);
        OcrMakeSearchableCommand   = new AsyncRelayCommand(OcrMakeSearchableAsync, () => HasDocument);
        ScanCommand                = new AsyncRelayCommand(ScanAsync);
        RequestSignaturesCommand   = new AsyncRelayCommand(RequestSignaturesAsync, () => HasDocument);
    }

    private static string? AskSavePath(string title, string filter, string fileName)
    {
        var dlg = new SaveFileDialog { Title = title, Filter = filter, FileName = fileName, AddExtension = true };
        return dlg.ShowDialog() == true ? dlg.FileName : null;
    }

    private string SuggestName(string suffix, string ext) =>
        (_currentFilePath != null ? System.IO.Path.GetFileNameWithoutExtension(_currentFilePath) : "document") + suffix + ext;

    // ── Create a PDF ──────────────────────────────────────────────────────────

    private async Task CreateBlankPdfAsync()
    {
        var dest = AskSavePath("Create blank PDF", "PDF Files (*.pdf)|*.pdf", "blank.pdf");
        if (dest == null) return;
        try
        {
            await Task.Run(() => PdfToolsService.CreateBlankPdf(dest));
            await LoadDocumentAsync(dest);
            ToastService.Instance.Success("Blank PDF created.");
        }
        catch (Exception ex) { Dialogs.AppDialog.ShowError("Could not create the PDF.", ex); }
    }

    private async Task CreatePdfFromImagesAsync()
    {
        var open = new OpenFileDialog { Title = "Create PDF from images", Filter = ImageFilter, Multiselect = true };
        if (open.ShowDialog() != true || open.FileNames.Length == 0) return;
        var dest = AskSavePath("Save PDF", "PDF Files (*.pdf)|*.pdf",
            System.IO.Path.GetFileNameWithoutExtension(open.FileNames[0]) + ".pdf");
        if (dest == null) return;
        try
        {
            await Task.Run(() => PdfToolsService.CreatePdfFromImages(open.FileNames, dest));
            await LoadDocumentAsync(dest);
            ToastService.Instance.Success($"Created a {open.FileNames.Length}-page PDF from images.");
        }
        catch (Exception ex) { Dialogs.AppDialog.ShowError("Could not create the PDF from images.", ex); }
    }

    // ── Combine files ─────────────────────────────────────────────────────────

    private async Task CombineFilesAsync()
    {
        var open = new OpenFileDialog
        {
            Title = _currentFilePath != null ? "Combine with the open PDF — choose PDFs or images to add" : "Combine files — choose PDFs or images",
            Filter = "PDFs and images|*.pdf;*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff|PDF Files (*.pdf)|*.pdf|" + ImageFilter,
            Multiselect = true,
        };
        if (open.ShowDialog() != true || open.FileNames.Length == 0) return;

        var files = new List<string>();
        if (_currentFilePath != null) files.Add(_currentFilePath);
        files.AddRange(open.FileNames);
        if (files.Count < 2 && !PdfToolsService.IsImage(files[0]))
        {
            ToastService.Instance.Info("Choose at least two files to combine.");
            return;
        }

        var dest = AskSavePath("Save combined PDF", "PDF Files (*.pdf)|*.pdf", "combined.pdf");
        if (dest == null) return;
        try
        {
            int pages = await Task.Run(() => PdfToolsService.CombineFiles(files, dest));
            await LoadDocumentAsync(dest);
            ToastService.Instance.Success($"Combined {files.Count} files into {pages} pages.");
        }
        catch (Exception ex) { Dialogs.AppDialog.ShowError("Combine failed.", ex); }
    }

    // ── Export to Word ────────────────────────────────────────────────────────

    private async Task ExportWordAsync()
    {
        if (_currentFilePath == null) return;
        var dest = AskSavePath("Export to Word", "Word Document (*.docx)|*.docx", SuggestName("", ".docx"));
        if (dest == null) return;
        try
        {
            int pages = await Task.Run(() => PdfToolsService.ExportToWord(_currentFilePath, dest));
            StatusText = $"Exported {pages} page(s) to {System.IO.Path.GetFileName(dest)} (text only — layout and images are not converted).";
            ToastService.Instance.Success("Exported to Word.");
        }
        catch (Exception ex) { Dialogs.AppDialog.ShowError("Export to Word failed.", ex); }
    }

    // ── Scan & OCR ────────────────────────────────────────────────────────────

    // Render at 2× (≈192 dpi) — enough detail for Windows OCR without huge images.
    private const double OcrZoom = 2.0;

    private async Task OcrCurrentPageAsync()
    {
        if (_document == null) return;
        try
        {
            StatusText = $"Recognising text on page {_currentPageIndex + 1}…";
            var size = _document.PageSizes[_currentPageIndex];
            var bmp = await _renderService.RenderPageAsync(_currentPageIndex, OcrZoom);
            var (text, words) = await OcrService.RecognizeAsync(bmp, size.Width, size.Height);
            if (string.IsNullOrWhiteSpace(text))
            {
                StatusText = "No text was recognised on this page.";
                ToastService.Instance.Info("No text recognised on this page.");
                return;
            }
            Clipboard.SetText(text);
            StatusText = $"Recognised {words.Count} words on page {_currentPageIndex + 1} — copied to the clipboard.";
            Dialogs.AppDialog.ShowInfo(text.Length > 3000 ? text[..3000] + "…" : text,
                $"Recognised text — page {_currentPageIndex + 1} (copied to clipboard)");
        }
        catch (Exception ex) { Dialogs.AppDialog.ShowError("Text recognition failed.", ex); }
    }

    /// <summary>
    /// Acrobat "Scan &amp; OCR → Recognise text": OCRs every page that has no text layer and saves a
    /// copy with invisible, searchable text over the scanned image.
    /// </summary>
    private async Task OcrMakeSearchableAsync()
    {
        if (_document == null || _currentFilePath == null) return;
        var dest = AskSavePath("Save searchable PDF", "PDF Files (*.pdf)|*.pdf", SuggestName("_ocr", ".pdf"));
        if (dest == null) return;

        try
        {
            IsLoading = true;
            var byPage = new Dictionary<int, List<OcrWord>>();
            int skipped = 0;
            for (int i = 0; i < _document.PageCount; i++)
            {
                // Pages that already contain real text don't need OCR.
                if (PdfTextExtractorService.GetPageText(_currentFilePath, i + 1).Trim().Length > 20) { skipped++; continue; }

                StatusText = $"OCR: page {i + 1} of {_document.PageCount}…";
                var size = _document.PageSizes[i];
                var bmp = await _renderService.RenderPageAsync(i, OcrZoom);
                var (_, words) = await OcrService.RecognizeAsync(bmp, size.Width, size.Height);
                byPage[i + 1] = words;
            }

            string src = _currentFilePath;
            int written = await Task.Run(() => PdfToolsService.AddInvisibleTextLayer(src, dest, byPage));
            await LoadDocumentAsync(dest);
            StatusText = $"Searchable PDF saved: {written} words recognised on {byPage.Count} page(s)" +
                         (skipped > 0 ? $", {skipped} page(s) already had text." : ".");
            ToastService.Instance.Success("OCR complete — the PDF is now searchable.");
        }
        catch (Exception ex) { Dialogs.AppDialog.ShowError("OCR failed.", ex); }
        finally { IsLoading = false; }
    }

    /// <summary>
    /// Scan from a TWAIN / WIA scanner (Scan dialog with preview and the standard controls), then
    /// make a new PDF or add the pages to the open one — searchable when OCR is ticked.
    /// </summary>
    private async Task ScanAsync()
    {
        var dlg = new Dialogs.ScanDialog(HasDocument && _currentFilePath != null) { Owner = Application.Current.MainWindow };
        if (dlg.ShowDialog() != true || dlg.Pages.Count == 0) return;
        var pages = dlg.Pages.ToList();
        bool bw = dlg.BlackAndWhite, ocr = dlg.RecogniseText;
        var output = dlg.Output;

        string? dest = null;
        if (output == Dialogs.ScanOutput.NewPdf)
        {
            dest = AskSavePath("Save scanned PDF", "PDF Files (*.pdf)|*.pdf", $"Scan {DateTime.Now:yyyy-MM-dd HHmm}.pdf");
            if (dest == null) return;
        }

        string scanPdf = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"pdfedit-scan-{Guid.NewGuid():N}.pdf");
        string? ocrPdf = null;
        try
        {
            IsLoading = true;
            StatusText = $"Making PDF from {pages.Count} scanned page(s)…";
            PdfToolsService.CreatePdfFromScans(pages, scanPdf, bw);   // WPF encoders: stay on the UI thread

            if (ocr)
            {
                if (!OcrService.IsAvailable)
                    ToastService.Instance.Warning("Windows has no OCR language installed — the scan was saved without searchable text.");
                else
                {
                    var byPage = new Dictionary<int, List<OcrWord>>();
                    for (int i = 0; i < pages.Count; i++)
                    {
                        StatusText = $"OCR: page {i + 1} of {pages.Count}…";
                        var (w, h) = PdfToolsService.ScanPageSize(pages[i]);
                        var (_, words) = await OcrService.RecognizeAsync(pages[i].Image, w, h);
                        byPage[i + 1] = words;
                    }
                    ocrPdf = System.IO.Path.ChangeExtension(scanPdf, ".ocr.pdf");
                    string src = scanPdf, ocrDest = ocrPdf;
                    await Task.Run(() => PdfToolsService.AddInvisibleTextLayer(src, ocrDest, byPage));
                }
            }
            string result = ocrPdf ?? scanPdf;

            if (output == Dialogs.ScanOutput.NewPdf)
            {
                System.IO.File.Copy(result, dest!, overwrite: true);
                await LoadDocumentAsync(dest!);
                ToastService.Instance.Success($"Scanned {pages.Count} page(s){(ocrPdf != null ? " — searchable" : "")}.");
            }
            else if (_currentFilePath != null && _document != null)
            {
                SaveDocumentState();
                int after = output == Dialogs.ScanOutput.AfterCurrentPage ? _currentPageIndex : _document.PageCount - 1;
                string cur = _currentFilePath, tmp = cur + ".scan.tmp";
                await Task.Run(() => _formService.InsertPdfAt(cur, result, tmp, after));
                System.IO.File.Copy(tmp, cur, overwrite: true);
                System.IO.File.Delete(tmp);
                await LoadDocumentAsync(cur);
                CurrentPageIndex = after + 1;
                ToastService.Instance.Success($"Added {pages.Count} scanned page(s).");
            }
            StatusText = $"Scanned {pages.Count} page(s).";
        }
        catch (Exception ex) { Dialogs.AppDialog.ShowError("Could not make the PDF from the scan.", ex); }
        finally
        {
            IsLoading = false;
            foreach (var f in new[] { scanPdf, ocrPdf })
                if (f != null) try { System.IO.File.Delete(f); } catch { }
        }
    }

    // ── Request e-signatures ──────────────────────────────────────────────────

    /// <summary>
    /// Acrobat sends the document through its cloud service; here the document is saved, the
    /// signer's email is opened in your mail app with instructions, and the file is shown in
    /// Explorer so it can be attached. The recipient signs it with Fill &amp; Sign.
    /// </summary>
    private async Task RequestSignaturesAsync()
    {
        if (_currentFilePath == null) return;

        if (ModifiedFieldNames.Count > 0 || ModifiedFieldBounds.Count > 0 || DeletedFieldNames.Count > 0)
        {
            if (!Dialogs.AppDialog.ShowConfirm("Save your changes before sending the document for signature?", "Request e-signatures"))
                return;
            SaveCommand.Execute(null);
        }

        var dlg = new Dialogs.InputDialog("Request e-signatures",
            "Signer's email address (separate several with ';'):", string.Empty);
        if (dlg.ShowDialog() != true || string.IsNullOrWhiteSpace(dlg.InputText)) return;

        string file = System.IO.Path.GetFileName(_currentFilePath);
        string subject = Uri.EscapeDataString($"Please sign: {file}");
        string body = Uri.EscapeDataString(
            $"Hello,\n\nPlease review and sign the attached document \"{file}\".\n\n" +
            "Open it in PdfEdit (or any PDF reader), use Fill & Sign → Sign to add your signature, save, and send it back.\n\nThank you.");
        try
        {
            Process.Start(new ProcessStartInfo($"mailto:{dlg.InputText.Trim()}?subject={subject}&body={body}") { UseShellExecute = true });
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{_currentFilePath}\"") { UseShellExecute = true });
            StatusText = "Email opened — attach the highlighted file and send it to the signer.";
            ToastService.Instance.Success("Signature request email opened.");
        }
        catch (Exception ex) { Dialogs.AppDialog.ShowError("Could not open your email app.", ex); }
        await Task.CompletedTask;
    }
}
