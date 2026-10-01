using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PdfEdit.Models;
using PdfEdit.Services;

namespace PdfEdit.Controls;

/// <summary>
/// Acrobat Fill &amp; Sign on flat forms: a PDF whose boxes are only drawn on the page (no AcroForm
/// fields) can still be filled — clicking inside a drawn box places text lined up in that box, a
/// tick in a small square, or a signature fitted to the box.
/// </summary>
public partial class PdfViewerControl
{
    // Grey-scale copy of the rendered page, rebuilt when the page image changes.
    private BitmapSource? _lumSource;
    private byte[]? _lum;
    private int _lumW, _lumH;

    private bool EnsureLuminance()
    {
        if (PageImage.Source is not BitmapSource src) return false;
        if (ReferenceEquals(src, _lumSource) && _lum != null) return true;
        try
        {
            var gray = new FormatConvertedBitmap(src, PixelFormats.Gray8, null, 0);
            int w = gray.PixelWidth, h = gray.PixelHeight;
            var buf = new byte[w * h];
            gray.CopyPixels(buf, w, 0);
            (_lumSource, _lum, _lumW, _lumH) = (src, buf, w, h);
            return true;
        }
        catch { _lum = null; return false; }
    }

    /// <summary>The inside of the drawn box under <paramref name="posOnCanvas"/> (canvas DIPs), if any.</summary>
    private Rect? DetectBoxAt(Point posOnCanvas)
    {
        if (!EnsureLuminance() || _lumSource == null || _lum == null) return null;
        double sx = _lumW / _lumSource.Width, sy = _lumH / _lumSource.Height;
        // Boxes up to ~7 in wide and ~2.5 in tall; bigger outlines are panels, not form boxes.
        int maxW = (int)(500 * Scale * sx), maxH = (int)(180 * Scale * sy);
        var box = BoxDetector.Find(_lum, _lumW, _lumH,
            (int)(posOnCanvas.X * sx), (int)(posOnCanvas.Y * sy), maxW, maxH);
        if (box is not { } b) return null;
        return new Rect(b.Left / sx, b.Top / sy, (b.Right - b.Left + 1) / sx, (b.Bottom - b.Top + 1) / sy);
    }

    private bool IsCheckBoxSized(Rect box)
    {
        double wPt = box.Width / Scale, hPt = box.Height / Scale;
        return wPt <= 28 && hPt <= 28 && wPt / hPt is > 0.6 and < 1.6;
    }

    /// <summary>
    /// Select / Fill tools on a page area with no form field: fill the drawn box under the click
    /// like Acrobat does (text box → type; small square → ✓). Returns false if there is no box.
    /// </summary>
    private bool TryFillDrawnBox(Point posOnPage)
    {
        if (DetectBoxAt(posOnPage) is not { } box) return false;
        FinalizeAnnotationBox();
        if (FocusExistingTextIn(box)) return true;
        if (IsCheckBoxSized(box))
            PlaceStampAnnotation(posOnPage, "✓", "#1A1A1A", box);
        else
            PlaceNewAnnotationBox(posOnPage, false, null, box);
        return true;
    }

    /// <summary>
    /// The page image has no annotations in it, so a box that already holds typed text still
    /// looks empty: clicking it again edits that text instead of stacking a second one on top.
    /// </summary>
    private bool FocusExistingTextIn(Rect box)
    {
        if (_vm?.Document == null) return false;
        int page = _vm.CurrentPageIndex + 1;
        double pageH = _vm.Document.PageSizes[page - 1].Height;
        foreach (var (ann, tb) in _annotationBoxes)
        {
            if (ann.PageNumber != page) continue;
            var r = new Rect(ann.Left * Scale, (pageH - ann.Bottom - ann.Height) * Scale, ann.Width * Scale, ann.Height * Scale);
            var c = new Point(r.X + r.Width / 2, r.Y + r.Height / 2);
            if (!box.Contains(c)) continue;
            tb.Focus();
            System.Windows.Input.Keyboard.Focus(tb);
            tb.CaretIndex = tb.Text.Length;
            return true;
        }
        return false;
    }
}
