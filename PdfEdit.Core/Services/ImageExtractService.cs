using System.IO;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Xobject;

namespace PdfEdit.Services;

/// <summary>Saves every picture in a PDF to a folder (Acrobat's Export all images).</summary>
public static class ImageExtractService
{
    /// <returns>How many images were saved, and how many couldn't be decoded.</returns>
    public static (int Saved, int Skipped) ExtractAll(string pdfPath, string folder, int minSize = 16)
    {
        Directory.CreateDirectory(folder);
        string baseName = Path.GetFileNameWithoutExtension(pdfPath);
        using var pdf = new PdfDocument(new PdfReader(pdfPath));
        var seen = new HashSet<PdfIndirectReference>();
        int saved = 0, skipped = 0;

        for (int p = 1; p <= pdf.GetNumberOfPages(); p++)
        {
            int onPage = 0;
            Walk(pdf.GetPage(p).GetResources()?.GetPdfObject(), 0);

            void Walk(PdfDictionary? resources, int depth)
            {
                var xobjects = resources?.GetAsDictionary(PdfName.XObject);
                if (xobjects == null || depth > 8) return;
                foreach (var key in xobjects.KeySet())
                {
                    if (xobjects.GetAsStream(key) is not { } stream) continue;
                    var reference = stream.GetIndirectReference();
                    if (reference != null && !seen.Add(reference)) continue;
                    var subtype = stream.GetAsName(PdfName.Subtype);
                    if (PdfName.Form.Equals(subtype)) { Walk(stream.GetAsDictionary(PdfName.Resources), depth + 1); continue; }
                    if (!PdfName.Image.Equals(subtype)) continue;
                    int w = stream.GetAsNumber(PdfName.Width)?.IntValue() ?? 0, h = stream.GetAsNumber(PdfName.Height)?.IntValue() ?? 0;
                    if (w < minSize || h < minSize) continue;
                    try
                    {
                        var img = new PdfImageXObject(stream);
                        byte[] bytes = img.GetImageBytes(true);
                        string ext = img.IdentifyImageFileExtension();
                        if (string.IsNullOrEmpty(ext)) ext = "png";
                        onPage++;
                        File.WriteAllBytes(Path.Combine(folder, $"{baseName} p{p} image {onPage}.{ext.TrimStart('.')}"), bytes);
                        saved++;
                    }
                    catch { skipped++; }   // e.g. JBIG2 or unusual colour spaces
                }
            }
        }
        return (saved, skipped);
    }
}
