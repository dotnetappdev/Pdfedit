using iText.IO.Font.Constants;
using iText.IO.Image;
using iText.Kernel.Colors;
using iText.Kernel.Font;
using iText.Kernel.Geom;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas;
using iText.Kernel.Pdf.Extgstate;
using PdfEdit.Models;

namespace PdfEdit.Services;

/// <summary>
/// Watermarks / backgrounds like Acrobat's Edit PDF → Watermark: text (e.g. a diagonal DRAFT) or an
/// image, behind the page content (under the text and form fields) or on top, centred, at the top,
/// at the bottom or tiled, on all / the current / a range of pages. Each watermark goes in its own
/// content stream, marked as a pagination artifact, so <see cref="Remove"/> can take it out again.
/// </summary>
public static class WatermarkService
{
    private static readonly PdfName Marker = new("PdfEditWatermark");

    public static void Apply(string inputPath, string outputPath, WatermarkOptions opt) =>
        Apply(inputPath, outputPath, opt, currentPage: 1);

    public static int Apply(string inputPath, string outputPath, WatermarkOptions opt, int currentPage)
    {
        using var pdf = new PdfDocument(new PdfReader(inputPath), new PdfWriter(outputPath));
        var pages = SelectPages(opt, currentPage, pdf.GetNumberOfPages());

        var color = ParseColor(opt.Color);
        PdfFont? font = string.IsNullOrEmpty(opt.ImagePath) ? CreateFont(opt.FontName) : null;
        ImageData? image = string.IsNullOrEmpty(opt.ImagePath) ? null : ImageDataFactory.Create(opt.ImagePath);

        foreach (int p in pages)
        {
            var page = pdf.GetPage(p);
            var box = page.GetCropBox();
            int rot = ((page.GetRotation() % 360) + 360) % 360;
            bool swap = rot is 90 or 270;
            // Size and angle as the page is seen on screen (a rotated page is shown turned).
            float viewW = swap ? box.GetHeight() : box.GetWidth();
            float viewH = swap ? box.GetWidth() : box.GetHeight();
            double viewAngle = opt.AngleFor(viewW, viewH);
            double angle = (viewAngle + rot) * Math.PI / 180;   // into unrotated page space

            var stream = opt.Behind ? page.NewContentStreamBefore() : page.NewContentStreamAfter();
            stream.Put(Marker, PdfBoolean.TRUE);
            var canvas = new PdfCanvas(stream, page.GetResources(), pdf);

            var props = new PdfDictionary();
            props.Put(PdfName.Type, PdfName.Pagination);
            props.Put(PdfName.Subtype, PdfName.Watermark);
            canvas.BeginMarkedContent(PdfName.Artifact, props);
            canvas.SaveState();
            // Multiply: the watermark can only darken what's under it, so black text stays black and
            // readable even when the watermark is on top (it tints the white paper around the text).
            canvas.SetExtGState(new PdfExtGState().SetFillOpacity(opt.Opacity).SetStrokeOpacity(opt.Opacity)
                                                  .SetBlendMode(PdfExtGState.BM_MULTIPLY));

            // Item size (unrotated) in points.
            float itemW, itemH;
            if (image != null)
            {
                itemW = Math.Clamp(opt.ImageScale, 0.05f, 1f) * viewW;
                itemH = itemW * image.GetHeight() / Math.Max(1, image.GetWidth());
            }
            else
            {
                itemW = font!.GetWidth(opt.Text, opt.FontSize);
                itemH = opt.FontSize * 0.72f;   // cap height-ish
                // Text that would run off the page (in whichever direction it runs) shrinks to fit.
                double across = WatermarkOptions.LineAcross(viewW, viewH, viewAngle);
                if (opt.Position != WatermarkPosition.Tiled && itemW > across * 0.9)
                {
                    float k = (float)(across * 0.9 / itemW);
                    itemW *= k; itemH *= k;
                }
            }
            float fontSize = image == null ? itemH / 0.72f : 0;

            // Centres in view space (origin bottom-left of what is seen), then mapped to page space.
            var centres = new List<(float X, float Y)>();
            float margin = Math.Max(18, viewH * 0.04f);
            switch (opt.Position)
            {
                case WatermarkPosition.Top:    centres.Add((viewW / 2, viewH - margin - itemH / 2)); break;
                case WatermarkPosition.Bottom: centres.Add((viewW / 2, margin + itemH / 2)); break;
                case WatermarkPosition.Tiled:
                {
                    float stepX = itemW + Math.Max(40, itemH * 2), stepY = itemH * 4 + 40;
                    int row = 0;
                    for (float y = stepY / 2; y < viewH + stepY; y += stepY, row++)
                        for (float x = (row % 2 == 0 ? 0 : stepX / 2); x < viewW + stepX; x += stepX)
                            centres.Add((x, y));
                    break;
                }
                default: centres.Add((viewW / 2, viewH / 2)); break;
            }

            double cos = Math.Cos(angle), sin = Math.Sin(angle);
            foreach (var (vx, vy) in centres)
            {
                var (px, py) = ViewToPage(vx, vy, rot, box);
                canvas.SaveState();
                canvas.ConcatMatrix(cos, sin, -sin, cos, px, py);
                if (image != null)
                    canvas.AddImageFittedIntoRectangle(image, new Rectangle(-itemW / 2, -itemH / 2, itemW, itemH), false);
                else
                {
                    canvas.BeginText()
                          .SetFontAndSize(font!, fontSize)
                          .SetFillColor(color)
                          .SetTextMatrix(1, 0, 0, 1, -itemW / 2, -itemH / 2)
                          .ShowText(opt.Text)
                          .EndText();
                }
                canvas.RestoreState();
            }

            canvas.RestoreState();
            canvas.EndMarkedContent();
        }
        return pages.Count;
    }

    // A point seen on the (possibly /Rotate'd) page → unrotated page coordinates.
    private static (double X, double Y) ViewToPage(float vx, float vy, int rot, Rectangle box) => rot switch
    {
        90  => (box.GetRight() - vy, box.GetBottom() + vx),   // shown turned clockwise
        180 => (box.GetRight() - vx, box.GetTop() - vy),
        270 => (box.GetLeft() + vy, box.GetTop() - vx),
        _   => (box.GetLeft() + vx, box.GetBottom() + vy),
    };

    /// <summary>Removes watermarks added by PdfEdit. Returns how many pages had one.</summary>
    public static int Remove(string inputPath, string outputPath)
    {
        int pagesTouched = 0;
        using var pdf = new PdfDocument(new PdfReader(inputPath), new PdfWriter(outputPath));
        for (int p = 1; p <= pdf.GetNumberOfPages(); p++)
        {
            var dict = pdf.GetPage(p).GetPdfObject();
            var contents = dict.Get(PdfName.Contents);
            bool removed = false;
            if (contents is PdfArray arr)
            {
                for (int i = arr.Size() - 1; i >= 0; i--)
                    if (arr.GetAsStream(i) is { } st && st.ContainsKey(Marker)) { arr.Remove(i); removed = true; }
                if (removed) arr.SetModified();
            }
            else if (contents is PdfStream single && single.ContainsKey(Marker))
            {
                dict.Put(PdfName.Contents, new PdfStream());
                removed = true;
            }
            if (removed) { pagesTouched++; dict.SetModified(); }
        }
        return pagesTouched;
    }

    /// <summary>True when the PDF has a watermark added by PdfEdit.</summary>
    public static bool HasWatermark(string path)
    {
        try
        {
            using var pdf = new PdfDocument(new PdfReader(path));
            for (int p = 1; p <= pdf.GetNumberOfPages(); p++)
            {
                var contents = pdf.GetPage(p).GetPdfObject().Get(PdfName.Contents);
                if (contents is PdfArray arr)
                {
                    for (int i = 0; i < arr.Size(); i++)
                        if (arr.GetAsStream(i)?.ContainsKey(Marker) == true) return true;
                }
                else if (contents is PdfStream s && s.ContainsKey(Marker)) return true;
            }
        }
        catch { }
        return false;
    }

    private static List<int> SelectPages(WatermarkOptions opt, int currentPage, int count)
    {
        if (opt.Pages == WatermarkPages.Current) return new() { Math.Clamp(currentPage, 1, count) };
        if (opt.Pages == WatermarkPages.Range && ParseRange(opt.PageRange, count) is { Count: > 0 } r) return r;
        return Enumerable.Range(1, count).ToList();
    }

    /// <summary>"1-3, 5, 8-" → page numbers (1-based, within the document).</summary>
    public static List<int> ParseRange(string text, int count)
    {
        var set = new SortedSet<int>();
        foreach (var part in (text ?? "").Split(',', ';'))
        {
            var t = part.Trim();
            if (t.Length == 0) continue;
            var ab = t.Split('-');
            if (ab.Length == 1 && int.TryParse(ab[0], out int one)) { if (one >= 1 && one <= count) set.Add(one); }
            else if (ab.Length == 2)
            {
                int a = int.TryParse(ab[0].Trim(), out var x) ? x : 1;
                int b = int.TryParse(ab[1].Trim(), out var y) ? y : count;
                for (int i = Math.Max(1, a); i <= Math.Min(count, b); i++) set.Add(i);
            }
        }
        return set.ToList();
    }

    private static readonly HashSet<string> StandardFontNames = new()
    {
        StandardFonts.HELVETICA, StandardFonts.HELVETICA_BOLD, StandardFonts.HELVETICA_OBLIQUE, StandardFonts.HELVETICA_BOLDOBLIQUE,
        StandardFonts.TIMES_ROMAN, StandardFonts.TIMES_BOLD, StandardFonts.TIMES_ITALIC, StandardFonts.TIMES_BOLDITALIC,
        StandardFonts.COURIER, StandardFonts.COURIER_BOLD, StandardFonts.COURIER_OBLIQUE, StandardFonts.COURIER_BOLDOBLIQUE,
    };

    private static PdfFont CreateFont(string name) =>
        PdfFontFactory.CreateFont(StandardFontNames.Contains(name) ? name : StandardFonts.HELVETICA_BOLD);

    private static DeviceRgb ParseColor(string hex)
    {
        hex = (hex ?? "").TrimStart('#');
        if (hex.Length == 8) hex = hex[2..]; // strip alpha
        if (hex.Length != 6) return new DeviceRgb(0.5f, 0.5f, 0.5f);
        try
        {
            float r = Convert.ToInt32(hex[0..2], 16) / 255f;
            float g = Convert.ToInt32(hex[2..4], 16) / 255f;
            float b = Convert.ToInt32(hex[4..6], 16) / 255f;
            return new DeviceRgb(r, g, b);
        }
        catch { return new DeviceRgb(0.5f, 0.5f, 0.5f); }
    }
}
