using System.IO;
using iText.IO.Source;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser.Util;
using iText.Kernel.Pdf.Xobject;

namespace PdfEdit.Services;

/// <summary>
/// A picture drawn on a page: which one it is (<see cref="Index"/>: the n-th picture drawn in the
/// page's own content), its box in PDF points (origin bottom-left) and its pixel size.
/// </summary>
public sealed record PageImageInfo(int PageNumber, int Index, string Name, double Left, double Bottom, double Width, double Height,
                                   int PixelWidth, int PixelHeight, bool Rotated);

/// <summary>
/// Edit the pictures already in a PDF, like PDFgear's and Acrobat's Edit PDF: move, resize,
/// replace, delete or save one. Only the one drawing command for that picture is changed, so the
/// rest of the page is untouched. Pictures inside form XObjects or inline images aren't listed.
/// </summary>
public static class ImageEditService
{
    /// <summary>An affine matrix [a b c d e f] in double precision (PDF row-vector convention).</summary>
    private readonly record struct M(double A, double B, double C, double D, double E, double F)
    {
        public static readonly M Identity = new(1, 0, 0, 1, 0, 0);
        /// <summary>this × o: apply this, then o.</summary>
        public M Times(M o) => new(A * o.A + B * o.C, A * o.B + B * o.D, C * o.A + D * o.C, C * o.B + D * o.D,
                                   E * o.A + F * o.C + o.E, E * o.B + F * o.D + o.F);
        public M Inverse()
        {
            double det = A * D - B * C;
            if (Math.Abs(det) < 1e-12) throw new InvalidOperationException("This picture is squashed flat and can't be edited.");
            return new(D / det, -B / det, -C / det, A / det, (C * F - D * E) / det, (B * E - A * F) / det);
        }
        public (double X, double Y) Apply(double x, double y) => (x * A + y * C + E, x * B + y * D + F);
    }

    private sealed record Placement(int OpIndex, PdfName Name, M Ctm);

    /// <summary>The pictures drawn on <paramref name="pageNumber"/>, in drawing order.</summary>
    public static List<PageImageInfo> GetImages(string pdfPath, int pageNumber)
    {
        using var doc = new PdfDocument(new PdfReader(pdfPath));
        if (pageNumber < 1 || pageNumber > doc.GetNumberOfPages()) return new();
        var page = doc.GetPage(pageNumber);
        var result = new List<PageImageInfo>();
        int index = 0;
        foreach (var p in Placements(page, out _))
        {
            var box = Bounds(p.Ctm);
            var img = page.GetResources().GetResource(PdfName.XObject)?.GetAsStream(p.Name);
            int pw = img?.GetAsNumber(PdfName.Width)?.IntValue() ?? 0, ph = img?.GetAsNumber(PdfName.Height)?.IntValue() ?? 0;
            double scale = Math.Max(Math.Abs(p.Ctm.A) + Math.Abs(p.Ctm.B), Math.Abs(p.Ctm.C) + Math.Abs(p.Ctm.D));
            bool rotated = Math.Abs(p.Ctm.B) > scale * 1e-4 || Math.Abs(p.Ctm.C) > scale * 1e-4;
            result.Add(new PageImageInfo(pageNumber, index++, p.Name.GetValue(), box.X, box.Y, box.W, box.H, pw, ph, rotated));
        }
        return result;
    }

    /// <summary>Moves and / or resizes picture <paramref name="index"/> so its box becomes the given one (PDF points).</summary>
    public static void MoveResize(string src, string dest, int pageNumber, int index, double left, double bottom, double width, double height)
    {
        Edit(src, dest, pageNumber, index, (ops, p, _, _) =>
        {
            var old = Bounds(p.Ctm);
            // Page-space change P: old box → new box. The extra cm X before Do must satisfy X·CTM = CTM·P.
            double sx = width / Math.Max(0.01, old.W), sy = height / Math.Max(0.01, old.H);
            var pMatrix = new M(sx, 0, 0, sy, left - old.X * sx, bottom - old.Y * sy);
            var x = p.Ctm.Times(pMatrix).Times(p.Ctm.Inverse());
            Wrap(ops, p.OpIndex, x, p.Name);
        });
    }

    /// <summary>Takes picture <paramref name="index"/> off the page.</summary>
    public static void Delete(string src, string dest, int pageNumber, int index) =>
        Edit(src, dest, pageNumber, index, (ops, p, _, _) => ops[p.OpIndex] = new List<PdfObject>());

    /// <summary>
    /// Puts a new picture (<paramref name="imageBytes"/>: PNG, JPEG, …) in the same place as picture
    /// <paramref name="index"/>, fitted inside its box without stretching.
    /// </summary>
    public static void Replace(string src, string dest, int pageNumber, int index, byte[] imageBytes)
    {
        Edit(src, dest, pageNumber, index, (ops, p, page, doc) =>
        {
            var data = iText.IO.Image.ImageDataFactory.Create(imageBytes);
            var xobj = new PdfImageXObject(data);
            var name = page.GetResources().AddImage(xobj);

            // Fit the new picture's proportions inside the old box (as seen on the page).
            var old = Bounds(p.Ctm);
            double aspect = data.GetWidth() / Math.Max(1.0, data.GetHeight());
            double w = old.W, h = w / aspect;
            if (h > old.H) { h = old.H; w = h * aspect; }
            double left = old.X + (old.W - w) / 2, bottom = old.Y + (old.H - h) / 2;
            // Draw the new picture upright in that box: CTM' = [w 0 0 h left bottom]; X = CTM' · CTM⁻¹.
            var x = new M(w, 0, 0, h, left, bottom).Times(p.Ctm.Inverse());
            Wrap(ops, p.OpIndex, x, name);
        });
    }

    /// <summary>The picture's own file (JPEG, PNG …) and a suitable extension, for Save image.</summary>
    public static (byte[] Bytes, string Extension) GetImageFile(string pdfPath, int pageNumber, int index)
    {
        using var doc = new PdfDocument(new PdfReader(pdfPath));
        var page = doc.GetPage(pageNumber);
        var p = Placements(page, out _).ElementAtOrDefault(index) ?? throw new ArgumentOutOfRangeException(nameof(index));
        var stream = page.GetResources().GetResource(PdfName.XObject).GetAsStream(p.Name);
        var img = new PdfImageXObject(stream);
        return (img.GetImageBytes(true), "." + img.IdentifyImageFileExtension());
    }

    // ── Content stream work ──────────────────────────────────────────────────

    private static void Edit(string src, string dest, int pageNumber, int index,
                             Action<List<List<PdfObject>>, Placement, PdfPage, PdfDocument> change)
    {
        using var doc = new PdfDocument(new PdfReader(src), new PdfWriter(dest));
        var page = doc.GetPage(pageNumber);
        var placements = Placements(page, out var ops);
        var p = placements.ElementAtOrDefault(index)
                ?? throw new InvalidOperationException("That picture isn't on the page any more. Reopen the PDF and try again.");
        change(ops, p, page, doc);

        using var ms = new MemoryStream();
        using (var output = new PdfOutputStream(ms))
        {
            foreach (var op in ops)
            {
                if (op.Count == 0) continue;
                for (int i = 0; i < op.Count; i++)
                {
                    output.Write(op[i]);
                    output.WriteBytes(i == op.Count - 1 ? "\n"u8.ToArray() : " "u8.ToArray());
                }
            }
            output.Flush();
        }
        var stream = new PdfStream(ms.ToArray());
        stream.MakeIndirect(doc);
        page.GetPdfObject().Put(PdfName.Contents, stream);
    }

    /// <summary>Replaces operator <paramref name="opIndex"/> (a Do) with "q X cm /Name Do Q".</summary>
    private static void Wrap(List<List<PdfObject>> ops, int opIndex, M x, PdfName name)
    {
        // Full precision: iText's own number writer keeps only two decimals.
        static PdfLiteral N(double v) => new(Math.Abs(v) < 1e-9 ? "0" : v.ToString("0.#########", System.Globalization.CultureInfo.InvariantCulture));
        ops[opIndex] = new List<PdfObject>
        {
            new PdfLiteral("q\n"), N(x.A), N(x.B), N(x.C), N(x.D), N(x.E), N(x.F),
            new PdfLiteral("cm\n"), name, new PdfLiteral("Do\nQ"),
        };
    }

    /// <summary>
    /// Every operator of the page's content (operands then the operator) and, for each image drawn
    /// with Do at page level, where it is and its transformation matrix.
    /// </summary>
    private static List<Placement> Placements(PdfPage page, out List<List<PdfObject>> ops)
    {
        ops = new List<List<PdfObject>>();
        var result = new List<Placement>();
        var resources = page.GetResources();
        var xobjects = resources.GetResource(PdfName.XObject);
        var tokenizer = new PdfTokenizer(new RandomAccessFileOrArray(new RandomAccessSourceFactory().CreateSource(page.GetContentBytes())));
        var parser = new PdfCanvasParser(tokenizer, resources);
        var stack = new Stack<M>();
        var ctm = M.Identity;
        var operands = new List<PdfObject>();

        while (parser.Parse(operands).Count > 0)
        {
            var op = operands.ToList();
            string name = op[^1].ToString();
            switch (name)
            {
                case "q": stack.Push(ctm); break;
                case "Q": if (stack.Count > 0) ctm = stack.Pop(); break;
                case "cm" when op.Count == 7 && op.Take(6).All(o => o is PdfNumber):
                    var m = new M(((PdfNumber)op[0]).GetValue(), ((PdfNumber)op[1]).GetValue(), ((PdfNumber)op[2]).GetValue(),
                                  ((PdfNumber)op[3]).GetValue(), ((PdfNumber)op[4]).GetValue(), ((PdfNumber)op[5]).GetValue());
                    ctm = m.Times(ctm);
                    break;
                case "Do" when op.Count == 2 && op[0] is PdfName xname
                               && PdfName.Image.Equals(xobjects?.GetAsStream(xname)?.GetAsName(PdfName.Subtype)):
                    result.Add(new Placement(ops.Count, xname, ctm));
                    break;
                case "EI":
                    // An inline image: write it back exactly as it was (BI … ID data EI).
                    op = new List<PdfObject> { InlineImageLiteral((PdfStream)op[0]) };
                    break;
            }
            ops.Add(op);
        }
        return result;
    }

    private static PdfLiteral InlineImageLiteral(PdfStream inline)
    {
        using var ms = new MemoryStream();
        using (var o = new PdfOutputStream(ms))
        {
            o.WriteBytes("BI\n"u8.ToArray());
            foreach (var key in inline.KeySet())
            {
                if (key.Equals(PdfName.Length)) continue;
                o.Write(key); o.WriteSpace(); o.Write(inline.Get(key, false)); o.WriteNewLine();
            }
            o.WriteBytes("ID\n"u8.ToArray());
            o.WriteBytes(inline.GetBytes(false));
            o.WriteBytes("\nEI"u8.ToArray());
            o.Flush();
        }
        return new PdfLiteral(ms.ToArray());
    }

    /// <summary>The page-space box of the unit square under <paramref name="m"/>.</summary>
    private static (double X, double Y, double W, double H) Bounds(M m)
    {
        var pts = new[] { m.Apply(0, 0), m.Apply(1, 0), m.Apply(0, 1), m.Apply(1, 1) };
        double x0 = pts.Min(p => p.X), x1 = pts.Max(p => p.X), y0 = pts.Min(p => p.Y), y1 = pts.Max(p => p.Y);
        return (x0, y0, x1 - x0, y1 - y0);
    }
}
