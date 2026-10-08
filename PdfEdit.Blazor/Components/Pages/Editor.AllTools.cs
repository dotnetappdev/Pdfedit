using Microsoft.JSInterop;
using PdfEdit.Blazor.Components.Editor;
using PdfEdit.Models;
using ToolKind = PdfEdit.Blazor.Components.Editor.Tool;

namespace PdfEdit.Blazor.Components.Pages;

/// <summary>
/// The Windows app's "All tools" pane (like Acrobat's): every feature in one list, grouped by what
/// you want to do, with a search box. Scanning from a scanner is the camera here.
/// </summary>
public partial class Editor
{
    public sealed record ToolAction(string Label, string? Tip, Func<Task> Run, bool NeedsDoc = true);
    public sealed record ToolCategory(string Title, string Icon, string Description, List<ToolAction> Actions);

    private List<ToolCategory>? _allTools;

    public List<ToolCategory> AllTools => _allTools ??= BuildAllTools();

    private List<ToolCategory> BuildAllTools()
    {
        ToolAction Run(string label, Func<Task> run, string? tip = null, bool needsDoc = true) => new(label, tip, run, needsDoc);
        ToolAction Act(string label, Action run, string? tip = null, bool needsDoc = true) => new(label, tip, () => { run(); return Task.CompletedTask; }, needsDoc);
        // Tools that act on the page bring the PDF view to the front.
        ToolAction Tool(string label, ToolKind tool, string? tip = null) => Act(label, () => { if (DesignMode) SetDesignMode(false); if (PrepareMode && tool < ToolKind.FieldText) TogglePrepareMode(); SetTool(tool); }, tip);
        ToolAction Dlg(string label, DialogKind kind, string? tip = null, bool needsDoc = true) => Act(label, () => ShowDialog(kind), tip, needsDoc);

        return
        [
            new("Convert & save as", "bi-arrow-left-right", "Convert to Word, Excel, HTML, ePub, images or PDF/A",
            [
                Run("Microsoft Word (.docx)", () => ExportAsync(ExportFormat.Word), "Text of every page as a Word document"),
                Run("Microsoft Excel (.xlsx)", () => ExportAsync(ExportFormat.Excel), "Tables and columns rebuilt as rows and cells, numbers as numbers"),
                Run("Microsoft PowerPoint (.pptx)", () => ExportAsync(ExportFormat.PowerPoint), "One slide per page"),
                Run("All images", () => ExportAsync(ExportFormat.Pictures), "Every picture in the PDF, in a ZIP"),
                Run("Plain text (.txt)", () => ExportAsync(ExportFormat.Text)),
                Run("Web page (.html)", () => ExportAsync(ExportFormat.Html), "Text with its headings, lists and paragraphs"),
                Run("Markdown (.md)", () => ExportAsync(ExportFormat.Markdown)),
                Run("E-book (.epub)", () => ExportAsync(ExportFormat.Epub), "One chapter per page, for e-readers"),
                Run("Images — all pages (PNG)", () => ExportAsync(ExportFormat.Images)),
                Run("Image — current page", ExportPageImageAsync),
                Tool("Snapshot of an area (copy / save picture)", ToolKind.Snapshot, "Drag a box round any area, then copy it or save it as PNG"),
                Run("PDF/A (archival)", PdfAAsync),
                Run("Form data", ExportDataAsync),
                Run("Comments (XFDF)", ExportXfdfAsync),
                Run("Comment summary (CSV)", AnnotationSummaryAsync),
            ]),
            new("Edit content", "bi-pencil", "Add text, watermarks, headers, page numbers",
            [
                Tool("Add text", ToolKind.Text, "Click on the page to type"),
                Tool("Edit images (move, resize, replace, delete)", ToolKind.EditImages, "Select a picture already in the PDF"),
                Run("Edit page on the Design canvas", ImportPdfPageAsync, "Move and restyle the page's text, images and fields"),
                Dlg("Watermark", DialogKind.Watermark),
                Dlg("Header & footer", DialogKind.HeaderFooter),
                Dlg("Page numbers", DialogKind.PageNumbers),
                Dlg("Bates numbering", DialogKind.Bates),
                Dlg("Add bookmark", DialogKind.AddBookmark),
                Dlg("Crop pages", DialogKind.Crop),
                Dlg("Resize pages (A4, Letter, margins)…", DialogKind.Resize, "Change the paper size; content is scaled to fit"),
                Run("Convert to greyscale", GreyscaleAsync, "Text, drawings and pictures in shades of grey"),
                Dlg("Find & replace in fields", DialogKind.FindReplace),
                Dlg("Document properties", DialogKind.Properties),
            ]),
            new("New PDF", "bi-file-earmark-plus", "Blank, from Office files, images or a design",
            [
                Run("Blank PDF", NewBlankAsync, needsDoc: false),
                Dlg("From images", DialogKind.Combine, "One page per image", needsDoc: false),
                Dlg("From Word, Excel or PowerPoint", DialogKind.Combine, "Converted on the server", needsDoc: false),
                Dlg("From text, Markdown or HTML", DialogKind.Combine, "Headings, lists, tables and pictures are kept", needsDoc: false),
                Dlg("From a Google Docs link", DialogKind.GoogleLink, "Google Docs, Sheets, Slides, or a Word file on Drive", needsDoc: false),
                Act("Design a PDF (Design canvas)", NewDesignPage, needsDoc: false),
                Run("Export design as PDF", ExportDesignAsync, needsDoc: false),
            ]),
            new("Cloud", "bi-cloud", "Google Drive and OneDrive",
            [
                Run("Import from Google Drive or OneDrive", () => ShowCloudAsync(save: false), "PDFs, Word files and Google Docs, brought in as PDFs", needsDoc: false),
                Run("Save to Google Drive or OneDrive", () => ShowCloudAsync(save: true)),
            ]),
            new("Merge & compare", "bi-intersect", "Merge PDFs and images into one PDF",
            [
                Dlg("Combine files (PDFs & images)", DialogKind.Combine, needsDoc: false),
                Dlg("Merge PDFs into this one", DialogKind.Merge),
                Dlg("Insert pages from a PDF", DialogKind.InsertPdf),
                Dlg("Compare two PDFs (text)", DialogKind.CompareUpload),
                Dlg("Compare two PDFs (visual)", DialogKind.CompareUpload, "Colours what was added, removed and changed on each page"),
            ]),
            new("Pages", "bi-files", "Rotate, insert, move, delete, extract, split",
            [
                Act("Show / hide page thumbnails", () => _showLeft = !_showLeft, needsDoc: false),
                Run("Slide show (full screen)", StartSlideShowAsync),
                Act("Two-page view on / off", () => TwoPages = !TwoPages),
                Act("Night mode on / off", () => NightMode = !NightMode),
                Run("Auto-scroll on / off", ToggleAutoScrollAsync, "Hands-free reading; scroll yourself to stop"),
                Run("Rotate page right", () => RotateAsync(90)),
                Run("Rotate page left", () => RotateAsync(-90)),
                Run("Rotate all pages right", () => RotateAllAsync(90)),
                Run("Insert blank page after", () => InsertBlankAsync(before: false)),
                Run("Insert blank page before", () => InsertBlankAsync(before: true)),
                Run("Duplicate page", DuplicatePageAsync),
                Run("Move page up", () => MovePageAsync(-1)),
                Run("Move page down", () => MovePageAsync(1)),
                Run("Delete page", DeleteThisPageAsync),
                Dlg("Delete page range…", DialogKind.DeleteRange),
                Run("Extract page", ExtractThisPageAsync),
                Dlg("Extract page range…", DialogKind.ExtractRange),
                Run("Split PDF", SplitAsync),
                Dlg("Pages per sheet / booklet…", DialogKind.NUp, "A new PDF with several pages on each sheet, or a booklet to fold"),
            ]),
            new("AI assistant", "bi-stars", "Chat about the document, fill forms with AI",
            [
                Act("Open AI Assistant", () => ShowRight(RightTab.AI), needsDoc: false),
                Act("Translate the PDF (keep layout)", ShowTranslate, "A translated copy, paragraph by paragraph in place"),
                Dlg("Ask across several PDFs", DialogKind.AskAcross, "Answers cite the file and page", needsDoc: false),
                Run("Mind map of this document", MindMapAsync),
                Dlg("Design a form with AI", DialogKind.DesignAi, "Describe the form; the AI lays out fillable fields on the Design canvas", needsDoc: false),
                Run("Summary outline", AiOutlineAsync),
                Run("Fill the form with AI", AiFillFromChatAsync, "Uses what you typed in the AI chat box"),
                Run("Smart fill from the document", () => RunAiPresetAsync("smartfill")),
                Run("Analyse contract", () => RunAiPresetAsync("contract")),
                Run("Extract key data", () => RunAiPresetAsync("extract")),
            ]),
            new("Summaries & reading", "bi-book", "Summarise the document with AI",
            [
                Run("Summarise this document", () => RunAiPresetAsync("summarize")),
                Run("Read this page aloud", () => ReadAloudAsync(false)),
                Run("Read to the end aloud", () => ReadAloudAsync(true)),
                Run("Stop reading", StopReadingAsync),
                Dlg("Document statistics", DialogKind.Statistics),
            ]),
            new("Signatures", "bi-pen", "Send for signature or sign yourself",
            [
                Run("Send for signature (email)", RequestSignaturesAsync, "Downloads the PDF and opens an email to the signer to attach it to"),
                Run("Sign yourself", SignatureToolAsync),
                Act("Sign with a certificate (Digital ID)…", () => { PrepareCertSign(); ShowDialog(DialogKind.CertSign); }, "A digital signature that proves who signed and that nothing changed"),
                Run("Check signatures", CheckSignaturesAsync),
                Run("Check required fields", CheckRequired),
            ]),
            new("Batch & automation", "bi-collection-play", "Run steps on many PDFs at once",
            [
                Act("Batch process files…", ShowBatch, "OCR, compress, watermark, flatten, number, protect… many files at once", needsDoc: false),
                Run("Bulk fill from spreadsheet…", ShowBulkFillAsync, "One filled copy of this form per CSV / Excel row"),
                Dlg("Search PDFs in a folder…", DialogKind.SearchFolder, "Find text in every PDF in a folder you choose", needsDoc: false),
            ]),
            new("Scan & recognise text", "bi-camera", "Make scanned pages searchable",
            [
                Dlg("Scan with your camera…", DialogKind.ScanCamera, "Photos of paper pages (or pictures) made into a PDF, searchable if you like", needsDoc: false),
                Dlg("Recognise text (make searchable)", DialogKind.Ocr, "OCR every scanned page"),
                Run("Clean up scans", ShowCleanupAsync, "Remove blank pages, straighten crooked pages, split two-page spreads"),
                Dlg("Create PDF from scans / images", DialogKind.Combine, needsDoc: false),
            ]),
            new("Security & privacy", "bi-shield-lock", "Passwords and permissions",
            [
                Dlg("Protect with password", DialogKind.Protect),
                Run("Remove password", RemovePasswordAsync),
                Dlg("Remove hidden information", DialogKind.Sanitize, "Metadata, scripts, attachments, comments"),
                Run("Check accessibility", AccessibilityCheckAsync, "Screen reader and keyboard checks, with fixes"),
                Run("Flatten form & save", SaveFlattenedAsync, "Make field values part of the page so they can't be edited"),
            ]),
            new("Redaction", "bi-eraser-fill", "Permanently remove sensitive content",
            [
                Tool("Mark areas to redact", ToolKind.Redact),
                Run("Apply redactions", CommitAsync),
                Run("Find personal information (AI)", () => RunAiPresetAsync("pii")),
            ]),
            new("Reduce file size", "bi-file-zip", "Reduce file size", [Run("Compress PDF", CompressAsync)]),
            new("Form builder", "bi-ui-checks-grid", "Add, move and edit form fields",
            [
                Run("Detect fields automatically", DetectFieldsAsync, "Turn a flat form's boxes, squares and blank lines into fillable fields"),
                Act("Edit fields (move / resize / align)", () => { if (!PrepareMode) TogglePrepareMode(); }),
                Tool("Add text field", ToolKind.FieldText),
                Tool("Add checkbox", ToolKind.FieldCheckbox),
                Tool("Add radio button", ToolKind.FieldRadio),
                Tool("Add dropdown", ToolKind.FieldCombo),
                Tool("Add list box", ToolKind.FieldList),
                Tool("Add signature field", ToolKind.FieldSignature),
                Tool("Add date field", ToolKind.FieldDate),
                Run("Check required fields", CheckRequired),
                Act("Clear all fields", ClearAllFields),
                Dlg("Import form data", DialogKind.ImportData),
            ]),
        ];
    }

    /// <summary>Send for signature: downloads the PDF and opens an email to attach it to (as the Windows app does).</summary>
    private async Task RequestSignaturesAsync()
    {
        if (Doc == null) return;
        await SaveAsync();
        string file = Doc.FileName;
        string subject = Uri.EscapeDataString($"Please sign: {file}");
        string body = Uri.EscapeDataString(
            $"Hello,\n\nPlease review and sign the attached document \"{file}\".\n\n" +
            "Open it in PdfEdit (or any PDF reader), use Fill & Sign → Sign to add your signature, save, and send it back.\n\nThank you.");
        await JS.InvokeVoidAsync("pdfedit.openInNewTab", $"mailto:?subject={subject}&body={body}");
        Toast("The PDF was downloaded and an email opened — attach the file and send it to the signer.", "success");
    }
}
