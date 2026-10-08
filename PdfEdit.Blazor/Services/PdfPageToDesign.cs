using iText.Kernel.Geom;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Data;
using iText.Kernel.Pdf.Canvas.Parser.Listener;
using PdfEdit.Models;
using PdfMatrix = iText.Kernel.Geom.Matrix;
using PdfVector = iText.Kernel.Geom.Vector;

namespace PdfEdit.Blazor.Services;

/// <summary>
/// Best-effort reconstruction of a PDF page as editable Design items (the Windows app's
/// PdfToDesignImportService): text lines, pictures, simple boxes and rules, and the page's form
/// fields. Lossy by nature: a content stream has no paragraphs or text boxes, only glyph placements,
/// so text is regrouped into lines by baseline, which suits forms and single-column pages best.
/// </summary>
public static class PdfPageToDesign
{
    /// <summary>At most this many boxes and rules come in, so a page of fine artwork stays usable.</summary>
    private const int MaxShapes = 400;

    /// <summary>
    /// The content of page <paramref name="pageNumber"/> (1-based) as design items, in points from
    /// the page's top-left, plus the page size. Pass the page's <paramref name="fields"/> (from
    /// PdfDocumentInfo.FormFields) to bring them in as field placeholders too.
    /// </summary>
    public static (List<DesignItem> Items, double PageWidth, double PageHeight) ExtractPage(
        string pdfPath, int pageNumber, IEnumerable<FormFieldInfo>? fields = null)
    {
        var items = new List<DesignItem>();
        using var pdf = new PdfDocument(new PdfReader(pdfPath));
        if (pageNumber < 1 || pageNumber > pdf.GetNumberOfPages()) return (items, 595, 842);
        var page = pdf.GetPage(pageNumber);
        var box = page.GetPageSize();
        double left = box.GetLeft(), bottom = box.GetBottom(), pageW = box.GetWidth(), pageH = box.GetHeight();
        double Top(double pdfY) => pageH - (pdfY - bottom);

        var listener = new ImportListener();
        try { new PdfCanvasProcessor(listener).ProcessPageContent(page); }
        catch { /* malformed content stream: keep whatever was collected before it failed */ }

        // Boxes, rules and pictures, in the order the page paints them, beneath the text.
        foreach (var g in listener.Graphics)
        {
            if (g.Picture is { } pic)
            {
                items.Add(new DesignItem
                {
                    Type = "image", X = g.X - left, Y = Top(g.Y + g.H),
                    W = Math.Max(4, g.W), H = Math.Max(4, g.H), Signature = Convert.ToBase64String(pic),
                });
            }
            else if (g.ShapeType == "Line")
            {
                double w = Math.Max(1, g.W), h = Math.Max(1, g.H);   // centred on the page's line when padded out
                items.Add(new DesignItem
                {
                    Type = "shape", ShapeType = "Line", X = g.X - left - (w - g.W) / 2, Y = Top(g.Y + g.H) - (h - g.H) / 2, W = w, H = h,
                    FillColor = "#00FFFFFF", StrokeColor = g.Stroke, StrokeThick = Math.Max(0.5, g.Thick), FlipY = g.FlipY,
                });
            }
            else
            {
                items.Add(new DesignItem
                {
                    Type = "shape", ShapeType = "Rectangle", X = g.X - left, Y = Top(g.Y + g.H), W = Math.Max(1, g.W), H = Math.Max(1, g.H),
                    FillColor = g.Fill ?? "#00FFFFFF", StrokeColor = g.Stroke ?? "#00FFFFFF", StrokeThick = Math.Max(0.5, g.Thick),
                });
            }
        }

        foreach (var line in GroupIntoLines(listener.TextChunks))
        {
            items.Add(new DesignItem
            {
                Type = "text",
                X = line.X - left,
                Y = Top(line.Y),    // line.Y is the top edge in PDF (y-up) space
                W = Math.Max(20, line.Width + 4),
                H = Math.Max(10, line.Height),
                Text = line.Text,
                FontFamily = line.FontFamily,
                FontSize = Math.Max(6, line.FontSize),
                Bold = line.Bold,
                Italic = line.Italic,
                Color = line.Color,
                BgColor = "#00FFFFFF",
                Alignment = "Left",
                Wrap = false,
            });
        }

        // Existing form fields become field placeholders, drawn on top.
        foreach (var f in fields ?? [])
        {
            if (f.PageNumber != 0 && f.PageNumber != pageNumber) continue;
            var kind = f.FieldType switch
            {
                FieldType.Text => f.IsMultiline ? "Memo" : "Text",
                FieldType.Checkbox => "Checkbox",
                FieldType.RadioButton => "Radio",
                FieldType.ComboBox or FieldType.ListBox => "ComboBox",
                FieldType.Signature => "Signature",
                _ => "Text",
            };
            items.Add(new DesignItem
            {
                Type = "field", FieldKind = kind,
                X = f.Left - left,
                Y = Top(f.Bottom + f.Height),
                W = Math.Max(10, f.Width),
                H = Math.Max(10, f.Height),
                FieldName = f.DisplayName,
                Label = f.DisplayName,
                // The field's own place on the page already stands in for a caption: no printed label.
                LabelPosition = "None", LabelOffset = 6,
                Required = f.IsRequired,
                Wrap = true,
                OptionsCsv = string.Join(", ", f.Options),
                FontSize = f.FontSize,
                Alignment = f.Alignment switch { FieldAlignment.Center => "Center", FieldAlignment.Right => "Right", _ => "Left" },
                ExportValue = string.IsNullOrEmpty(f.ExportValue) ? "Yes" : f.ExportValue,
                Value = f.Value,
            });
        }

        return (items, pageW, pageH);
    }

    // ── Content-stream listener ─────────────────────────────────────────────

    private sealed class TextChunk
    {
        public string Text = "";
        public float BaselineY;
        public float StartX, EndX;
        public float FontSize;
        public string FontFamily = "Helvetica";
        public bool Bold, Italic;
        public string Color = "#FF000000";
    }

    /// <summary>A picture (Picture set) or a box / rule, in PDF space (y up, Y = bottom edge).</summary>
    private sealed class Graphic
    {
        public byte[]? Picture;
        public string? ShapeType, Fill, Stroke;
        public double X, Y, W, H, Thick;
        public bool FlipY;
    }

    private sealed class ImportListener : IEventListener
    {
        public readonly List<TextChunk> TextChunks = new();
        public readonly List<Graphic> Graphics = new();
        private int _shapes;

        public void EventOccurred(IEventData data, EventType type)
        {
            switch (type)
            {
                case EventType.RENDER_TEXT: OnText((TextRenderInfo)data); break;
                case EventType.RENDER_IMAGE: OnImage((ImageRenderInfo)data); break;
                case EventType.RENDER_PATH: OnPath((PathRenderInfo)data); break;
            }
        }

        public ICollection<EventType> GetSupportedEvents() => [EventType.RENDER_TEXT, EventType.RENDER_IMAGE, EventType.RENDER_PATH];

        private void OnText(TextRenderInfo info)
        {
            string text = info.GetText();
            if (string.IsNullOrEmpty(text)) return;

            var start = info.GetBaseline().GetStartPoint();
            var end = info.GetBaseline().GetEndPoint();

            var fontNames = info.GetFont()?.GetFontProgram()?.GetFontNames();
            string rawName = fontNames?.GetFontName() ?? "Helvetica";
            bool bold = (fontNames?.GetFontWeight() ?? 400) >= 600
                || rawName.Contains("Bold", StringComparison.OrdinalIgnoreCase);
            bool italic = (fontNames?.GetStyle()?.Contains("Italic", StringComparison.OrdinalIgnoreCase) ?? false)
                || rawName.Contains("Italic", StringComparison.OrdinalIgnoreCase)
                || rawName.Contains("Oblique", StringComparison.OrdinalIgnoreCase);

            TextChunks.Add(new TextChunk
            {
                Text = text,
                BaselineY = start.Get(PdfVector.I2),
                StartX = start.Get(PdfVector.I1),
                EndX = end.Get(PdfVector.I1),
                FontSize = EffectiveFontSize(info),
                FontFamily = CleanFontName(rawName),
                Bold = bold,
                Italic = italic,
                Color = Hex(info.GetFillColor()) ?? "#FF000000",
            });
        }

        /// <summary>
        /// Tf only gives the nominal size; many producers use "1 Tf" and scale via the text
        /// matrix (e.g. "12 0 0 12 x y Tm"). The on-page size is Tf × the vertical scale of Tm × CTM.
        /// </summary>
        private static float EffectiveFontSize(TextRenderInfo info)
        {
            float size = info.GetFontSize();
            try
            {
                var m = info.GetTextMatrix().Multiply(info.GetGraphicsState().GetCtm());
                float sy = (float)Math.Sqrt(m.Get(PdfMatrix.I21) * m.Get(PdfMatrix.I21) + m.Get(PdfMatrix.I22) * m.Get(PdfMatrix.I22));
                if (sy > 0) size *= sy;
            }
            catch { }
            return size;
        }

        private void OnImage(ImageRenderInfo info)
        {
            try
            {
                var img = info.GetImage();
                if (img == null) return;
                var ctm = info.GetImageCtm();
                float w = ctm.Get(PdfMatrix.I11), h = ctm.Get(PdfMatrix.I22);
                float x = ctm.Get(PdfMatrix.I31), y = ctm.Get(PdfMatrix.I32);
                if (Math.Abs(w) < 1 || Math.Abs(h) < 1) return;
                // iText hands back JPEGs as they are and decodes plain (Flate / raw) pictures to PNG;
                // JPEG 2000, JBIG2 and CCITT pictures come back in formats the web canvas can't show.
                var bytes = img.GetImageBytes(true);
                if (!IsPng(bytes) && !IsJpeg(bytes)) return;
                Graphics.Add(new Graphic { Picture = bytes, X = Math.Min(x, x + w), Y = Math.Min(y, y + h), W = Math.Abs(w), H = Math.Abs(h) });
            }
            catch { /* unsupported picture encoding: skip it */ }
        }

        /// <summary>Axis-aligned rectangles and straight lines; curves and other outlines are left out.</summary>
        private void OnPath(PathRenderInfo info)
        {
            try
            {
                int op = info.GetOperation();
                if (op == PathRenderInfo.NO_OP || info.IsPathModifiesClippingPath() || _shapes >= MaxShapes) return;
                bool fill = (op & PathRenderInfo.FILL) != 0, stroke = (op & PathRenderInfo.STROKE) != 0;
                string? fillHex = fill ? Hex(info.GetFillColor()) : null;
                string? strokeHex = stroke ? Hex(info.GetStrokeColor()) : null;
                if (fillHex == null && strokeHex == null) return;   // patterns, shadings, etc.
                var ctm = info.GetCtm();
                double thick = info.GetLineWidth() * Math.Sqrt(Math.Abs(ctm.Get(PdfMatrix.I11) * ctm.Get(PdfMatrix.I22) - ctm.Get(PdfMatrix.I12) * ctm.Get(PdfMatrix.I21)));

                foreach (var sub in info.GetPath().GetSubpaths())
                {
                    if (_shapes >= MaxShapes) return;
                    var segments = sub.GetSegments();
                    if (segments.Count == 0 || segments.Any(s => s is not Line)) continue;
                    var pts = new List<(double X, double Y)>();
                    foreach (var seg in segments)
                        foreach (var p in seg.GetBasePoints())
                        {
                            var v = new PdfVector((float)p.GetX(), (float)p.GetY(), 1).Cross(ctm);
                            var pt = ((double)v.Get(0), (double)v.Get(1));
                            if (pts.Count == 0 || Math.Abs(pts[^1].Item1 - pt.Item1) > 0.01 || Math.Abs(pts[^1].Item2 - pt.Item2) > 0.01) pts.Add(pt);
                        }
                    if (pts.Count < 2) continue;
                    double x0 = pts.Min(p => p.X), x1 = pts.Max(p => p.X), y0 = pts.Min(p => p.Y), y1 = pts.Max(p => p.Y);

                    if (pts.Count == 2 && !sub.IsClosed() && strokeHex != null)
                    {
                        // A single straight line: a rule, a border, an underline.
                        var (a, b) = (pts[0], pts[1]);
                        if (a.X > b.X) (a, b) = (b, a);
                        Graphics.Add(new Graphic
                        {
                            ShapeType = "Line", Stroke = strokeHex, Thick = thick,
                            X = x0, Y = y0, W = x1 - x0, H = y1 - y0, FlipY = b.Y > a.Y,   // y up: a rising line runs bottom-left to top-right
                        });
                        _shapes++;
                    }
                    else if (IsBox(pts, x0, x1, y0, y1) && (x1 - x0 >= 0.5 || y1 - y0 >= 0.5))
                    {
                        // A filled sliver is a rule: keep it as a filled box, as the page draws it.
                        Graphics.Add(new Graphic
                        {
                            ShapeType = "Rectangle", Fill = fillHex, Stroke = strokeHex, Thick = stroke ? thick : 0.5,
                            X = x0, Y = y0, W = x1 - x0, H = y1 - y0,
                        });
                        _shapes++;
                    }
                }
            }
            catch { /* odd path: skip it */ }
        }

        /// <summary>Every corner lies on the bounding box's edges and there are 4 of them (an "re" or a drawn box).</summary>
        private static bool IsBox(List<(double X, double Y)> pts, double x0, double x1, double y0, double y1)
        {
            var corners = pts.Count == 5 && Near(pts[0], pts[4]) ? pts.Take(4).ToList() : pts;
            if (corners.Count != 4) return false;
            static bool Close(double a, double b) => Math.Abs(a - b) < 0.5;
            return corners.All(p => (Close(p.X, x0) || Close(p.X, x1)) && (Close(p.Y, y0) || Close(p.Y, y1)));
        }

        private static bool Near((double X, double Y) a, (double X, double Y) b) => Math.Abs(a.X - b.X) < 0.5 && Math.Abs(a.Y - b.Y) < 0.5;
    }

    /// <summary>"#FFRRGGBB" for gray, RGB and CMYK colours; null for patterns and others.</summary>
    private static string? Hex(iText.Kernel.Colors.Color? c)
    {
        float[] v;
        try { v = c?.GetColorValue() ?? []; }
        catch { return null; }
        static byte B(float f) => (byte)Math.Clamp(f * 255f, 0, 255);
        return v.Length switch
        {
            1 => DesignColor.ToHex(255, B(v[0]), B(v[0]), B(v[0])),
            3 => DesignColor.ToHex(255, B(v[0]), B(v[1]), B(v[2])),
            4 => DesignColor.ToHex(255, B((1 - v[0]) * (1 - v[3])), B((1 - v[1]) * (1 - v[3])), B((1 - v[2]) * (1 - v[3]))),
            _ => null,
        };
    }

    private static bool IsPng(byte[] b) => b.Length > 8 && b[0] == 0x89 && b[1] == (byte)'P' && b[2] == (byte)'N' && b[3] == (byte)'G';
    private static bool IsJpeg(byte[] b) => b.Length > 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF;

    /// <summary>Strips a PDF font subset prefix ("ABCDEF+Arial-BoldMT" → "Arial-BoldMT") and the
    /// PostScript style suffix, leaving a best-guess family name.</summary>
    private static string CleanFontName(string raw)
    {
        var name = raw;
        int plus = name.IndexOf('+');
        if (plus == 6) name = name[(plus + 1)..];
        int dash = name.IndexOf('-');
        if (dash > 0) name = name[..dash];
        return string.IsNullOrWhiteSpace(name) ? "Helvetica" : name;
    }

    // ── Line grouping ────────────────────────────────────────────────────────

    private sealed class LineAcc
    {
        public string Text = "";
        public float MinX = float.MaxValue, MaxX = float.MinValue;
        public float BaselineY;
        public float MaxFontSize;
        public string FontFamily = "Helvetica";
        public bool Bold, Italic;
        public string Color = "#FF000000";
    }

    private static IEnumerable<(string Text, double X, double Y, double Width, double Height, double FontSize, string FontFamily, bool Bold, bool Italic, string Color)>
        GroupIntoLines(List<TextChunk> chunks)
    {
        // 1. Bucket chunks by baseline
        var rows = new List<(float Baseline, List<TextChunk> Chunks)>();
        foreach (var c in chunks)
        {
            float tolerance = Math.Max(2f, c.FontSize * 0.3f);
            int idx = rows.FindIndex(r => Math.Abs(r.Baseline - c.BaselineY) < tolerance);
            if (idx < 0) rows.Add((c.BaselineY, new List<TextChunk> { c }));
            else rows[idx].Chunks.Add(c);
        }

        // 2. Within a row, order left→right (content-stream order is arbitrary) and split
        //    at wide gaps so separate labels on one baseline ("City … State … Zip") stay apart.
        foreach (var (baseline, rowChunks) in rows.OrderByDescending(r => r.Baseline))
        {
            LineAcc? line = null;
            foreach (var c in rowChunks.OrderBy(c => c.StartX))
            {
                float gap = line == null ? 0 : c.StartX - line.MaxX;
                if (line == null || gap > Math.Max(c.FontSize, line.MaxFontSize) * 1.5f)
                {
                    if (line != null) { var done = Emit(line); if (done != null) yield return done.Value; }
                    line = new LineAcc { BaselineY = baseline, FontFamily = c.FontFamily, Bold = c.Bold, Italic = c.Italic, Color = c.Color };
                }
                else if (gap > c.FontSize * 0.15f && !line.Text.EndsWith(' ') && !c.Text.StartsWith(' '))
                {
                    line.Text += " ";
                }
                line.Text += c.Text;
                line.MinX = Math.Min(line.MinX, c.StartX);
                line.MaxX = Math.Max(line.MaxX, c.EndX);
                line.MaxFontSize = Math.Max(line.MaxFontSize, c.FontSize);
            }
            if (line != null) { var last = Emit(line); if (last != null) yield return last.Value; }
        }

        static (string, double, double, double, double, double, string, bool, bool, string)? Emit(LineAcc l)
        {
            string text = l.Text.Trim();
            if (text.Length == 0) return null;
            // The baseline sits roughly one ascent (~0.9 em) below the text box top
            double topY = l.BaselineY + l.MaxFontSize * 0.9;
            double height = l.MaxFontSize * 1.25;
            return (text, l.MinX, topY, l.MaxX - l.MinX, height, l.MaxFontSize, l.FontFamily, l.Bold, l.Italic, l.Color);
        }
    }
}
