using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Data;
using iText.Kernel.Pdf.Canvas.Parser.Listener;
using PdfEdit.Models;
using PdfMatrix = iText.Kernel.Geom.Matrix;
using PdfVector = iText.Kernel.Geom.Vector;

namespace PdfEdit.Services;

/// <summary>
/// Best-effort reconstruction of a PDF page's content as editable Design elements
/// (text lines, images, form-field placeholders) instead of a flat page image.
/// This is inherently lossy: a PDF content stream has no concept of "paragraphs" or
/// "text boxes" — it only has individual glyph-placement operators. Text is regrouped
/// into lines by baseline proximity, which works well for simple layouts (forms,
/// single-column documents) and less well for multi-column or heavily kerned text.
/// </summary>
public static class PdfToDesignImportService
{
    public static List<DesignElement> ExtractPageAsElements(
        string pdfPath, int pageIndex, double pageHeightPt, IEnumerable<FormFieldInfo> formFieldsOnPage)
    {
        var result = new List<DesignElement>();
        int z = 0;

        using (var reader = new PdfReader(pdfPath))
        using (var pdf = new PdfDocument(reader))
        {
            if (pageIndex < 0 || pageIndex >= pdf.GetNumberOfPages()) return result;
            var page = pdf.GetPage(pageIndex + 1);

            var listener = new ImportListener();
            var processor = new PdfCanvasProcessor(listener);
            try { processor.ProcessPageContent(page); }
            catch { /* malformed content stream — return whatever was collected before it failed */ }

            foreach (var img in listener.Images)
            {
                var bmp = DecodeToBitmap(img.Bytes);
                if (bmp == null) continue;
                result.Add(new ImageDesignElement
                {
                    X = img.X,
                    Y = pageHeightPt - img.Y - img.H,
                    Width = Math.Max(4, img.W),
                    Height = Math.Max(4, img.H),
                    Bitmap = bmp,
                    ZOrder = z++
                });
            }

            foreach (var line in GroupIntoLines(listener.TextChunks))
            {
                result.Add(new TextDesignElement
                {
                    X = line.X,
                    Y = pageHeightPt - line.Y,   // line.Y is the top edge in PDF (y-up) space
                    Width = Math.Max(20, line.Width + 4),
                    Height = Math.Max(10, line.Height),
                    Text = line.Text,
                    FontFamily = line.FontFamily,
                    FontSize = line.FontSize,
                    Bold = line.Bold,
                    Italic = line.Italic,
                    Color = line.Color,
                    Wrap = false,
                    ZOrder = z++
                });
            }
        }

        // Existing AcroForm fields become editable field placeholders, drawn on top.
        foreach (var f in formFieldsOnPage)
        {
            var kind = f.FieldType switch
            {
                FieldType.Text        => f.IsMultiline ? FormFieldKind.Memo : FormFieldKind.Text,
                FieldType.Checkbox    => FormFieldKind.Checkbox,
                FieldType.RadioButton => FormFieldKind.Radio,
                FieldType.ComboBox    => FormFieldKind.ComboBox,
                FieldType.ListBox     => FormFieldKind.ComboBox,
                FieldType.Signature   => FormFieldKind.Signature,
                _                     => FormFieldKind.Text
            };
            result.Add(new FormFieldDesignElement(kind)
            {
                X = f.Left,
                Y = pageHeightPt - f.Bottom - f.Height,
                Width = Math.Max(10, f.Width),
                Height = Math.Max(10, f.Height),
                FieldName = f.DisplayName,
                Label = f.DisplayName,
                // The field's own page position already stands in for a caption — don't
                // duplicate it as a separate printed label.
                LabelPosition = FieldLabelPosition.None,
                Required = f.IsRequired,
                OptionsCsv = string.Join(", ", f.Options),
                FontSizePt = f.FontSize,
                TextAlign = f.Alignment switch
                {
                    FieldAlignment.Center => System.Windows.TextAlignment.Center,
                    FieldAlignment.Right  => System.Windows.TextAlignment.Right,
                    _                     => System.Windows.TextAlignment.Left,
                },
                SourceFieldName = f.Name,
                SourceWidgetIndex = f.WidgetIndex,
                SourcePageNumber = f.PageNumber,
                ZOrder = z++
            });
        }

        return result;
    }

    // ── Content-stream listener ─────────────────────────────────────────────

    private sealed class TextChunk
    {
        public string Text = "";
        public float BaselineY;
        public float StartX, EndX;
        public float FontSize;
        public string FontFamily = "Segoe UI";
        public bool Bold, Italic;
        public Color Color = Colors.Black;
    }

    private sealed class ImageHit
    {
        public byte[] Bytes = Array.Empty<byte>();
        public float X, Y, W, H;
    }

    private sealed class ImportListener : IEventListener
    {
        public readonly List<TextChunk> TextChunks = new();
        public readonly List<ImageHit> Images = new();

        public void EventOccurred(IEventData data, EventType type)
        {
            switch (type)
            {
                case EventType.RENDER_TEXT:
                    OnText((TextRenderInfo)data);
                    break;
                case EventType.RENDER_IMAGE:
                    OnImage((ImageRenderInfo)data);
                    break;
            }
        }

        private void OnText(TextRenderInfo info)
        {
            string text = info.GetText();
            if (string.IsNullOrEmpty(text)) return;

            var start = info.GetBaseline().GetStartPoint();
            var end = info.GetBaseline().GetEndPoint();

            var rgb = SafeColorValue(info.GetFillColor());
            byte r, g, b;
            if (rgb.Length == 4)
            {
                // DeviceCMYK
                r = ToByte((1 - rgb[0]) * (1 - rgb[3]));
                g = ToByte((1 - rgb[1]) * (1 - rgb[3]));
                b = ToByte((1 - rgb[2]) * (1 - rgb[3]));
            }
            else if (rgb.Length >= 3) { r = ToByte(rgb[0]); g = ToByte(rgb[1]); b = ToByte(rgb[2]); }
            else if (rgb.Length == 1) { r = g = b = ToByte(rgb[0]); }
            else { r = g = b = 0; }

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
                Color = Color.FromRgb(r, g, b)
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

        private static float[] SafeColorValue(iText.Kernel.Colors.Color? c)
        {
            try { return c?.GetColorValue() ?? Array.Empty<float>(); }
            catch { return Array.Empty<float>(); }
        }

        private static byte ToByte(float v) => (byte)Math.Clamp(v * 255f, 0, 255);

        private void OnImage(ImageRenderInfo info)
        {
            try
            {
                var img = info.GetImage();
                if (img == null) return;
                var bytes = img.GetImageBytes(true);
                var ctm = info.GetImageCtm();
                float w = ctm.Get(PdfMatrix.I11);
                float h = ctm.Get(PdfMatrix.I22);
                float x = ctm.Get(PdfMatrix.I31);
                float y = ctm.Get(PdfMatrix.I32);
                if (Math.Abs(w) < 1 || Math.Abs(h) < 1) return;
                Images.Add(new ImageHit { Bytes = bytes, X = x, Y = Math.Min(y, y + h), W = Math.Abs(w), H = Math.Abs(h) });
            }
            catch { /* unsupported image encoding (e.g. JBIG2/CCITT) — skip this image */ }
        }

        public ICollection<EventType> GetSupportedEvents() => new[] { EventType.RENDER_TEXT, EventType.RENDER_IMAGE };
    }

    /// <summary>Strips a PDF font subset prefix ("ABCDEF+Arial-BoldMT" → "Arial-BoldMT") and the
    /// PostScript style suffix, leaving a best-guess family name for WPF to resolve or fall back on.</summary>
    private static string CleanFontName(string raw)
    {
        var name = raw;
        int plus = name.IndexOf('+');
        if (plus == 6) name = name[(plus + 1)..];
        int dash = name.IndexOf('-');
        if (dash > 0) name = name[..dash];
        return string.IsNullOrWhiteSpace(name) ? "Segoe UI" : name;
    }

    private static BitmapImage? DecodeToBitmap(byte[] bytes)
    {
        try
        {
            using var ms = new MemoryStream(bytes);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch { return null; }
    }

    // ── Line grouping ────────────────────────────────────────────────────────

    private sealed class LineAcc
    {
        public string Text = "";
        public float MinX = float.MaxValue, MaxX = float.MinValue;
        public float BaselineY;
        public float MaxFontSize;
        public string FontFamily = "Segoe UI";
        public bool Bold, Italic;
        public Color Color = Colors.Black;
    }

    private static IEnumerable<(string Text, double X, double Y, double Width, double Height, double FontSize, string FontFamily, bool Bold, bool Italic, Color Color)>
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

        static (string, double, double, double, double, double, string, bool, bool, Color)? Emit(LineAcc l)
        {
            string text = l.Text.Trim();
            if (text.Length == 0) return null;
            // WPF places the baseline roughly one ascent (~0.9 em) below the text box top
            double topY = l.BaselineY + l.MaxFontSize * 0.9;
            double height = l.MaxFontSize * 1.25;
            return (text, l.MinX, topY, l.MaxX - l.MinX, height, l.MaxFontSize, l.FontFamily, l.Bold, l.Italic, l.Color);
        }
    }
}
