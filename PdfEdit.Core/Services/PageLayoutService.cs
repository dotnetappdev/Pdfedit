using System.IO;
using iText.IO.Source;
using iText.Kernel.Geom;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas;
using iText.Kernel.Pdf.Canvas.Parser.Util;
using iText.Kernel.Pdf.Xobject;

namespace PdfEdit.Services;

/// <summary>How pages are placed on a new page size.</summary>
public enum ResizeFit
{
    /// <summary>Scale to fit inside the new size, keeping proportions (nothing is cut off).</summary>
    Fit,
    /// <summary>Keep the content's size and centre it (bigger pages get a border, smaller ones are cut).</summary>
    Centre,
}

/// <summary>Options for pages per sheet (N-up).</summary>
public sealed record NUpOptions(int Columns, int Rows, float SheetWidth, float SheetHeight,
                                bool Borders = false, float Margin = 18f, float Gap = 8f, bool ColumnsFirst = false);

/// <summary>
/// Page layout tools PDFgear and Acrobat have: resize pages (A4 ↔ Letter, add margins), several
/// pages per sheet, booklets for printing, and convert to greyscale. Pure iText, no UI.
/// </summary>
public static class PageLayoutService
{
    /// <summary>Common paper sizes, portrait, in points.</summary>
    public static readonly IReadOnlyList<(string Name, float Width, float Height)> PaperSizes = new[]
    {
        ("A4", 595.28f, 841.89f), ("Letter", 612f, 792f), ("Legal", 612f, 1008f), ("A3", 841.89f, 1190.55f),
        ("A5", 419.53f, 595.28f), ("Tabloid", 792f, 1224f), ("Executive", 522f, 756f), ("B5", 498.9f, 708.66f),
    };

    // ── Resize pages ─────────────────────────────────────────────────────────

    /// <summary>
    /// Gives the chosen pages a new size (in points, as they are seen on screen), scaling or
    /// centring their content, with an optional <paramref name="margin"/> all round. Done in place,
    /// so form fields, links and comments keep working: their boxes move with the content. With
    /// <paramref name="keepOrientation"/>, landscape pages stay landscape.
    /// </summary>
    public static int ResizePages(string src, string dest, float width, float height, ResizeFit fit,
                                  float margin = 0, Func<int, bool>? includePage = null, bool keepOrientation = true)
    {
        using var doc = new PdfDocument(new PdfReader(src), new PdfWriter(dest));
        int changed = 0;
        for (int n = 1; n <= doc.GetNumberOfPages(); n++)
        {
            if (includePage != null && !includePage(n)) continue;
            var page = doc.GetPage(n);
            var box = page.GetCropBox();

            // Width / height are what the reader sees; a page turned 90° stores them the other way.
            bool turned = page.GetRotation() % 180 != 0;
            float seenW = width, seenH = height;
            bool pageLandscape = turned ? box.GetHeight() > box.GetWidth() : box.GetWidth() > box.GetHeight();
            if (keepOrientation && pageLandscape != (seenW > seenH)) (seenW, seenH) = (seenH, seenW);
            float w = turned ? seenH : seenW, h = turned ? seenW : seenH;

            float availW = Math.Max(1, w - 2 * margin), availH = Math.Max(1, h - 2 * margin);
            float s = fit == ResizeFit.Fit ? Math.Min(availW / box.GetWidth(), availH / box.GetHeight()) : 1f;
            float tx = (w - box.GetWidth() * s) / 2 - box.GetX() * s;
            float ty = (h - box.GetHeight() * s) / 2 - box.GetY() * s;

            // q … cm before the old content, Q after (written raw: they pair across two streams).
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            page.NewContentStreamBefore().GetOutputStream().WriteBytes(System.Text.Encoding.ASCII.GetBytes(
                string.Format(inv, "q {0} 0 0 {0} {1} {2} cm\n", s, tx, ty)));
            page.NewContentStreamAfter().GetOutputStream().WriteBytes(System.Text.Encoding.ASCII.GetBytes("\nQ\n"));

            var newBox = new Rectangle(0, 0, w, h);
            page.SetMediaBox(newBox);
            page.SetCropBox(newBox);
            page.GetPdfObject().Remove(PdfName.TrimBox);
            page.GetPdfObject().Remove(PdfName.BleedBox);
            page.GetPdfObject().Remove(PdfName.ArtBox);

            foreach (var annot in page.GetAnnotations())
                TransformAnnotation(annot.GetPdfObject(), s, tx, ty);
            changed++;
        }
        return changed;
    }

    /// <summary>Moves an annotation's box and points by x' = s·x + tx, y' = s·y + ty.</summary>
    private static void TransformAnnotation(PdfDictionary a, float s, float tx, float ty)
    {
        if (a.GetAsArray(PdfName.Rect) is { } rect && rect.Size() == 4)
        {
            var r = rect.ToRectangle();
            a.Put(PdfName.Rect, new PdfArray(new[] { r.GetLeft() * s + tx, r.GetBottom() * s + ty, r.GetRight() * s + tx, r.GetTop() * s + ty }));
        }
        foreach (var key in new[] { PdfName.QuadPoints, PdfName.Vertices, PdfName.L, PdfName.CL })
            if (a.GetAsArray(key) is { } pts) a.Put(key, MapPoints(pts, s, tx, ty));
        if (a.GetAsArray(PdfName.InkList) is { } ink)
        {
            var mapped = new PdfArray();
            for (int i = 0; i < ink.Size(); i++)
                mapped.Add(ink.GetAsArray(i) is { } stroke ? MapPoints(stroke, s, tx, ty) : ink.Get(i));
            a.Put(PdfName.InkList, mapped);
        }
    }

    private static PdfArray MapPoints(PdfArray pts, float s, float tx, float ty)
    {
        var o = new PdfArray();
        for (int i = 0; i < pts.Size(); i++)
        {
            var num = pts.GetAsNumber(i);
            if (num == null) { o.Add(pts.Get(i)); continue; }
            o.Add(new PdfNumber(num.FloatValue() * s + (i % 2 == 0 ? tx : ty)));
        }
        return o;
    }

    // ── Pages per sheet (N-up) ───────────────────────────────────────────────

    /// <summary>
    /// A new PDF with <see cref="NUpOptions.Columns"/> × <see cref="NUpOptions.Rows"/> pages on each
    /// sheet, in reading order, each scaled to fit its cell. Returns the number of sheets.
    /// </summary>
    public static int NUp(string src, string dest, NUpOptions o)
    {
        using var source = new PdfDocument(new PdfReader(src));
        using var output = new PdfDocument(new PdfWriter(dest));
        int per = Math.Max(1, o.Columns * o.Rows), total = source.GetNumberOfPages();
        float cellW = (o.SheetWidth - 2 * o.Margin - (o.Columns - 1) * o.Gap) / o.Columns;
        float cellH = (o.SheetHeight - 2 * o.Margin - (o.Rows - 1) * o.Gap) / o.Rows;

        int sheets = 0;
        for (int first = 1; first <= total; first += per)
        {
            var sheet = output.AddNewPage(new PageSize(o.SheetWidth, o.SheetHeight));
            var canvas = new PdfCanvas(sheet);
            sheets++;
            for (int k = 0; k < per && first + k <= total; k++)
            {
                int col = o.ColumnsFirst ? k / o.Rows : k % o.Columns;
                int row = o.ColumnsFirst ? k % o.Rows : k / o.Columns;
                var cell = new Rectangle(o.Margin + col * (cellW + o.Gap),
                                         o.SheetHeight - o.Margin - (row + 1) * cellH - row * o.Gap, cellW, cellH);
                PlacePage(source, first + k, output, canvas, cell);
                if (o.Borders)
                    canvas.SaveState().SetLineWidth(0.5f).SetStrokeColorGray(0.6f)
                          .Rectangle(cell.GetX(), cell.GetY(), cell.GetWidth(), cell.GetHeight()).Stroke().RestoreState();
            }
        }
        if (sheets == 0) output.AddNewPage(new PageSize(o.SheetWidth, o.SheetHeight));
        return sheets;
    }

    /// <summary>
    /// A booklet for printing double-sided (flip on the short edge), folding and stapling: two pages
    /// side by side on each landscape sheet, in the order that reads correctly once folded. Blank
    /// pages are added to make the count a multiple of four. Returns the number of printed sides.
    /// </summary>
    public static int Booklet(string src, string dest, float sheetWidth, float sheetHeight)
    {
        using var source = new PdfDocument(new PdfReader(src));
        using var output = new PdfDocument(new PdfWriter(dest));
        float sw = Math.Max(sheetWidth, sheetHeight), sh = Math.Min(sheetWidth, sheetHeight);   // landscape
        int n = source.GetNumberOfPages(), padded = (n + 3) / 4 * 4;
        var left = new Rectangle(0, 0, sw / 2, sh);
        var right = new Rectangle(sw / 2, 0, sw / 2, sh);

        int sides = 0;
        for (int s = 0; s < padded / 4; s++)
        {
            // Front: last-unused on the left, first-unused on the right; back: the next two inside.
            foreach (var (l, r) in new[] { (padded - 2 * s, 2 * s + 1), (2 * s + 2, padded - 2 * s - 1) })
            {
                var canvas = new PdfCanvas(output.AddNewPage(new PageSize(sw, sh)));
                if (l <= n) PlacePage(source, l, output, canvas, left);
                if (r <= n) PlacePage(source, r, output, canvas, right);
                sides++;
            }
        }
        if (sides == 0) output.AddNewPage(new PageSize(sw, sh));
        return sides;
    }

    /// <summary>Draws page <paramref name="number"/> of <paramref name="source"/> centred in <paramref name="cell"/>, as large as fits.</summary>
    private static void PlacePage(PdfDocument source, int number, PdfDocument output, PdfCanvas canvas, Rectangle cell)
    {
        var page = source.GetPage(number);
        var box = page.GetCropBox();
        int rot = ((page.GetRotation() % 360) + 360) % 360;
        bool turned = rot % 180 != 0;
        float pw = turned ? box.GetHeight() : box.GetWidth(), ph = turned ? box.GetWidth() : box.GetHeight();
        float s = Math.Min(cell.GetWidth() / pw, cell.GetHeight() / ph);
        float x = cell.GetX() + (cell.GetWidth() - pw * s) / 2, y = cell.GetY() + (cell.GetHeight() - ph * s) / 2;

        PdfFormXObject form = page.CopyAsFormXObject(output);
        form.SetBBox(new PdfArray(box));
        // Map the crop box's corner to (0,0), turn as /Rotate says, then scale and place.
        float bx = box.GetX(), by = box.GetY(), bw = box.GetWidth(), bh = box.GetHeight();
        var m = rot switch
        {
            90  => new AffineTransform(0, -s, s, 0, x - by * s, y + (bx + bw) * s),
            180 => new AffineTransform(-s, 0, 0, -s, x + (bx + bw) * s, y + (by + bh) * s),
            270 => new AffineTransform(0, s, -s, 0, x + (by + bh) * s, y - bx * s),
            _   => new AffineTransform(s, 0, 0, s, x - bx * s, y - by * s),
        };
        var v = new double[6];
        m.GetMatrix(v);
        canvas.SaveState()
              .Rectangle(x, y, pw * s, ph * s).Clip().EndPath()
              .AddXObjectWithTransformationMatrix(form, (float)v[0], (float)v[1], (float)v[2], (float)v[3], (float)v[4], (float)v[5])
              .RestoreState();
    }

    // ── Greyscale ────────────────────────────────────────────────────────────

    /// <summary>
    /// Converts the PDF to shades of grey: text and drawing colours are rewritten as grey, and
    /// pictures are passed to <paramref name="toGreyImage"/> (which returns the grey image's bytes,
    /// or null to leave it). Text stays selectable. Returns how many pages were changed.
    /// </summary>
    public static int Greyscale(string src, string dest, Func<byte[], byte[]?>? toGreyImage)
    {
        using var doc = new PdfDocument(new PdfReader(src), new PdfWriter(dest));
        var done = new HashSet<PdfIndirectReference>();
        int changed = 0;
        for (int n = 1; n <= doc.GetNumberOfPages(); n++)
        {
            var page = doc.GetPage(n);
            var res = page.GetResources();
            byte[] bytes;
            try { bytes = page.GetContentBytes(); } catch { continue; }
            var grey = RewriteColours(bytes, res);
            if (grey != null)
            {
                var stream = new PdfStream(grey);
                stream.MakeIndirect(doc);
                page.GetPdfObject().Put(PdfName.Contents, stream);
                changed++;
            }
            GreyResources(doc, res.GetPdfObject(), toGreyImage, done, depth: 0);
            foreach (var annot in page.GetAnnotations())
                if (annot.GetPdfObject().GetAsDictionary(PdfName.AP)?.GetAsStream(PdfName.N) is { } ap)
                    GreyForm(doc, ap, toGreyImage, done, 0);
        }
        return changed;
    }

    private static void GreyResources(PdfDocument doc, PdfDictionary? res, Func<byte[], byte[]?>? toGreyImage,
                                      HashSet<PdfIndirectReference> done, int depth)
    {
        if (res?.GetAsDictionary(PdfName.XObject) is not { } xobjects || depth > 8) return;
        foreach (var name in xobjects.KeySet().ToList())
        {
            if (xobjects.GetAsStream(name) is not { } xs) continue;
            var reference = xs.GetIndirectReference();
            if (reference != null && !done.Add(reference)) continue;

            var subtype = xs.GetAsName(PdfName.Subtype);
            if (PdfName.Form.Equals(subtype)) GreyForm(doc, xs, toGreyImage, done, depth + 1);
            else if (PdfName.Image.Equals(subtype) && toGreyImage != null)
            {
                var replacement = GreyImage(doc, xs, toGreyImage);
                if (replacement != null) xobjects.Put(name, replacement);
            }
        }
    }

    private static void GreyForm(PdfDocument doc, PdfStream form, Func<byte[], byte[]?>? toGreyImage,
                                 HashSet<PdfIndirectReference> done, int depth)
    {
        var resDict = form.GetAsDictionary(PdfName.Resources);
        try
        {
            var grey = RewriteColours(form.GetBytes(), new PdfResources(resDict ?? new PdfDictionary()));
            if (grey != null) form.SetData(grey);
        }
        catch { /* unreadable stream: leave it */ }
        GreyResources(doc, resDict, toGreyImage, done, depth);
    }

    private static PdfStream? GreyImage(PdfDocument doc, PdfStream img, Func<byte[], byte[]?> toGreyImage)
    {
        try
        {
            if (img.GetAsBool(PdfName.ImageMask) == true) return null;
            var cs = img.Get(PdfName.ColorSpace);
            if (PdfName.DeviceGray.Equals(cs) || PdfName.CalGray.Equals(cs)) return null;
            if (img.GetAsNumber(PdfName.BitsPerComponent)?.IntValue() == 1) return null;

            byte[] encoded = new PdfImageXObject(img).GetImageBytes(true);
            if (toGreyImage(encoded) is not { } greyBytes) return null;
            var grey = new PdfImageXObject(iText.IO.Image.ImageDataFactory.Create(greyBytes));
            var obj = grey.GetPdfObject();
            if (img.Get(PdfName.SMask) is { } smask) obj.Put(PdfName.SMask, smask);
            if (img.Get(PdfName.Mask) is { } mask) obj.Put(PdfName.Mask, mask);
            obj.MakeIndirect(doc);
            return obj;
        }
        catch { return null; }   // JBIG2, JPX or an unusual format: keep the original
    }

    /// <summary>
    /// Rewrites colour operators as grey (rg/RG, k/K, and sc/scn in RGB or CMYK colour spaces).
    /// Returns null when there's nothing to change or the stream has inline images (left as is).
    /// </summary>
    private static byte[]? RewriteColours(byte[] content, PdfResources resources)
    {
        var tokenizer = new PdfTokenizer(new RandomAccessFileOrArray(new RandomAccessSourceFactory().CreateSource(content)));
        var parser = new PdfCanvasParser(tokenizer, resources);
        using var ms = new MemoryStream();
        using var output = new PdfOutputStream(ms);
        var operands = new List<PdfObject>();
        int fillN = 0, strokeN = 0;      // components to fold into grey after cs / CS (0 = leave alone)
        bool any = false;

        while (parser.Parse(operands).Count > 0)
        {
            string op = operands[^1].ToString();
            int count = operands.Count - 1;
            List<PdfObject>? replaced = null;
            string? newOp = null;

            switch (op)
            {
                case "EI":
                    return null;   // inline image: keep the stream as it was
                case "rg" or "RG" when count == 3 && Numbers(operands, 3) is { } rgb:
                    replaced = [new PdfNumber(Luma(rgb))];
                    newOp = op == "rg" ? "g" : "G";
                    break;
                case "k" or "K" when count == 4 && Numbers(operands, 4) is { } cmyk:
                    replaced = [new PdfNumber(CmykGrey(cmyk))];
                    newOp = op == "k" ? "g" : "G";
                    break;
                case "cs" or "CS" when count == 1 && operands[0] is PdfName csName:
                    int comps = Components(csName, resources);
                    if (op == "cs") fillN = comps; else strokeN = comps;
                    if (comps is 3 or 4) replaced = [PdfName.DeviceGray];
                    break;
                case "sc" or "scn" or "SC" or "SCN":
                    int want = op is "sc" or "scn" ? fillN : strokeN;
                    if (want is 3 or 4 && count == want && Numbers(operands, want) is { } c)
                        replaced = [new PdfNumber(want == 3 ? Luma(c) : CmykGrey(c))];
                    break;
            }

            if (replaced != null) any = true;
            var args = replaced ?? operands.Take(count).ToList();
            foreach (var a in args) { output.Write(a); output.WriteSpace(); }
            output.WriteBytes(System.Text.Encoding.ASCII.GetBytes(newOp ?? op));
            output.WriteNewLine();
        }
        output.Flush();
        return any ? ms.ToArray() : null;
    }

    private static float[]? Numbers(List<PdfObject> operands, int n)
    {
        var v = new float[n];
        for (int i = 0; i < n; i++)
        {
            if (operands[i] is not PdfNumber num) return null;
            v[i] = num.FloatValue();
        }
        return v;
    }

    private static float Luma(float[] rgb) => Math.Clamp(0.299f * rgb[0] + 0.587f * rgb[1] + 0.114f * rgb[2], 0, 1);

    private static float CmykGrey(float[] c) => Math.Clamp(1 - Math.Min(1, 0.3f * c[0] + 0.59f * c[1] + 0.11f * c[2] + c[3]), 0, 1);

    /// <summary>3 for RGB-like colour spaces, 4 for CMYK-like, 0 for anything else (left as is).</summary>
    private static int Components(PdfName name, PdfResources resources)
    {
        if (PdfName.DeviceRGB.Equals(name)) return 3;
        if (PdfName.DeviceCMYK.Equals(name)) return 4;
        var cs = resources.GetResource(PdfName.ColorSpace)?.Get(name);
        if (cs is PdfArray arr && arr.Size() >= 2)
        {
            var kind = arr.GetAsName(0);
            if (PdfName.CalRGB.Equals(kind)) return 3;
            if (PdfName.ICCBased.Equals(kind) && arr.GetAsStream(1)?.GetAsNumber(PdfName.N)?.IntValue() is int n and (3 or 4))
                return n;
        }
        if (cs is PdfName direct && !direct.Equals(name)) return Components(direct, resources);
        return 0;
    }
}
