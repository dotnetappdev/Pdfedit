using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace PdfEdit.Services;

/// <summary>
/// Turns Word, Excel, PowerPoint and OpenDocument files into PDFs, Acrobat's "Create PDF from
/// file". Uses Microsoft Office when it's installed (Word exports a tagged PDF with heading
/// bookmarks), otherwise LibreOffice. Office is driven in the background and closed afterwards.
/// </summary>
public static class OfficeConversionService
{
    public static readonly string[] WordExtensions = { ".doc", ".docx", ".docm", ".dot", ".dotx", ".rtf", ".odt", ".wpd" };
    public static readonly string[] ExcelExtensions = { ".xls", ".xlsx", ".xlsm", ".xlsb", ".ods", ".csv" };
    public static readonly string[] PowerPointExtensions = { ".ppt", ".pptx", ".pptm", ".pps", ".ppsx", ".odp" };
    /// <summary>Text, Markdown and web pages: always converted by PdfEdit itself.</summary>
    public static readonly string[] TextExtensions = { ".txt", ".md", ".markdown", ".html", ".htm" };

    public const string FileFilter =
        "Documents|*.doc;*.docx;*.docm;*.dot;*.dotx;*.rtf;*.odt;*.wpd;*.xls;*.xlsx;*.xlsm;*.xlsb;*.ods;*.csv;*.ppt;*.pptx;*.pptm;*.pps;*.ppsx;*.odp;*.txt;*.md;*.markdown;*.html;*.htm" +
        "|Text, Markdown and web pages|*.txt;*.md;*.markdown;*.html;*.htm" +
        "|Word documents|*.doc;*.docx;*.docm;*.rtf;*.odt|Excel workbooks|*.xls;*.xlsx;*.xlsm;*.xlsb;*.ods;*.csv|PowerPoint presentations|*.ppt;*.pptx;*.pptm;*.pps;*.ppsx;*.odp|All files|*.*";

    /// <summary>Files PdfEdit can convert itself, without Office or LibreOffice.</summary>
    public static readonly string[] BuiltInExtensions = { ".docx", ".docm", ".dotx" };

    public static bool CanConvert(string path) =>
        TextExtensions.Contains(Path.GetExtension(path).ToLowerInvariant())
        || AvailableConverter() != null || BuiltInExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    public static bool IsOfficeFile(string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        return WordExtensions.Contains(ext) || ExcelExtensions.Contains(ext) || PowerPointExtensions.Contains(ext) || TextExtensions.Contains(ext);
    }

    /// <summary>What can do the conversion on this PC, for messages.</summary>
    public static string? AvailableConverter()
    {
        if (Type.GetTypeFromProgID("Word.Application") != null) return "Microsoft Office";
        if (FindLibreOffice() != null) return "LibreOffice";
        return null;
    }

    /// <summary>A free name for the PDF next to the source ("Report.pdf", "Report (2).pdf" …).</summary>
    public static string OutputPathFor(string source)
    {
        string dir = Path.GetDirectoryName(source)!, name = Path.GetFileNameWithoutExtension(source);
        string dest = Path.Combine(dir, name + ".pdf");
        for (int i = 2; File.Exists(dest); i++) dest = Path.Combine(dir, $"{name} ({i}).pdf");
        return dest;
    }

    /// <summary>Converts one file. Runs Office on its own STA thread; throws with a readable message on failure.</summary>
    public static void Convert(string source, string dest)
    {
        string ext = Path.GetExtension(source).ToLowerInvariant();
        if (TextExtensions.Contains(ext)) { DocumentConvertService.ToPdf(source, dest); return; }
        string? progId = WordExtensions.Contains(ext) ? "Word.Application"
                       : ExcelExtensions.Contains(ext) ? "Excel.Application"
                       : PowerPointExtensions.Contains(ext) ? "PowerPoint.Application" : null;
        if (progId == null) throw new NotSupportedException($"PdfEdit can't convert {ext} files.");

        // Word forms: PdfEdit's own converter turns content controls, form fields and ____ blanks into
        // fillable PDF fields (Word's own PDF export makes them flat).
        if (BuiltInExtensions.Contains(ext) && DocxToPdfService.HasFormFields(source))
        {
            try { DocxToPdfService.Convert(source, dest); return; }
            catch { /* fall back to Office / LibreOffice below */ }
        }

        Exception? officeError = null;
        if (Type.GetTypeFromProgID(progId) is { } type)
        {
            try
            {
                RunSta(() => ConvertWithOffice(type, progId, source, dest));
                if (File.Exists(dest)) return;
            }
            catch (Exception ex) { officeError = ex; }
        }

        string? soffice = FindLibreOffice();
        if (soffice != null)
        {
            ConvertWithLibreOffice(soffice, source, dest);
            return;
        }
        // No Office: PdfEdit's own Word converter.
        if (BuiltInExtensions.Contains(ext))
        {
            DocxToPdfService.Convert(source, dest);
            return;
        }
        if (officeError != null) throw new InvalidOperationException($"Microsoft Office couldn't convert the file: {officeError.Message}", officeError);
        throw new InvalidOperationException(
            "PdfEdit converts Word (.docx) files itself; other Office files need Microsoft Office or the free LibreOffice (libreoffice.org) installed on this PC.");
    }

    private static void RunSta(Action action)
    {
        Exception? error = null;
        var t = new Thread(() => { try { action(); } catch (Exception ex) { error = ex; } }) { IsBackground = true };
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        if (error != null) throw error is System.Reflection.TargetInvocationException { InnerException: { } inner } ? inner : error;
    }

    private static void ConvertWithOffice(Type type, string progId, string source, string dest)
    {
        dynamic app = Activator.CreateInstance(type)!;
        try
        {
            switch (progId)
            {
                case "Word.Application":
                {
                    app.Visible = false;
                    app.DisplayAlerts = 0;                       // wdAlertsNone
                    dynamic doc = app.Documents.Open(source, false, true, false);   // FileName, ConfirmConversions, ReadOnly, AddToRecentFiles
                    try
                    {
                        // wdExportFormatPDF, no open after, print quality, whole document, from/to, content,
                        // IncludeDocProps, KeepIRM, heading bookmarks, document structure tags.
                        doc.ExportAsFixedFormat(dest, 17, false, 0, 0, 1, 1, 0, true, true, 1, true);
                    }
                    finally { doc.Close(0); Release(doc); }
                    break;
                }
                case "Excel.Application":
                {
                    app.Visible = false;
                    app.DisplayAlerts = false;
                    dynamic wb = app.Workbooks.Open(source, 0, true);   // FileName, UpdateLinks, ReadOnly
                    try { wb.ExportAsFixedFormat(0, dest); }           // xlTypePDF
                    finally { wb.Close(false); Release(wb); }
                    break;
                }
                default:
                {
                    // PowerPoint refuses Visible = false on the application; open without a window instead.
                    dynamic pres = app.Presentations.Open(source, -1, 0, 0);   // ReadOnly, Untitled, WithWindow
                    try { pres.SaveAs(dest, 32); }                              // ppSaveAsPDF
                    finally { pres.Close(); Release(pres); }
                    break;
                }
            }
        }
        finally
        {
            try { app.Quit(); } catch { }
            Release(app);
        }
    }

    private static void Release(object? o)
    {
        try { if (o != null && Marshal.IsComObject(o)) Marshal.FinalReleaseComObject(o); } catch { }
    }

    private static void ConvertWithLibreOffice(string soffice, string source, string dest)
    {
        string outDir = Path.Combine(Path.GetTempPath(), "PdfEdit-convert-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(outDir);
        try
        {
            var psi = new ProcessStartInfo(soffice)
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
            };
            foreach (var a in new[] { "--headless", "--norestore", "--convert-to", "pdf", "--outdir", outDir, source }) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi) ?? throw new InvalidOperationException("LibreOffice didn't start.");
            string err = p.StandardError.ReadToEnd();
            if (!p.WaitForExit(180_000)) { try { p.Kill(true); } catch { } throw new TimeoutException("LibreOffice took too long."); }
            string produced = Path.Combine(outDir, Path.GetFileNameWithoutExtension(source) + ".pdf");
            if (!File.Exists(produced)) throw new InvalidOperationException("LibreOffice couldn't convert the file. " + err.Trim());
            File.Copy(produced, dest, overwrite: true);
        }
        finally
        {
            try { Directory.Delete(outDir, true); } catch { }
        }
    }

    private static string? FindLibreOffice()
    {
        foreach (var root in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) })
        {
            if (string.IsNullOrEmpty(root)) continue;
            string p = Path.Combine(root, "LibreOffice", "program", "soffice.exe");
            if (File.Exists(p)) return p;
        }
        return null;
    }
}
