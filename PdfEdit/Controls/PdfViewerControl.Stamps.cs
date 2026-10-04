using System.Globalization;
using System.Windows;
using System.Windows.Media;
using PdfEdit.Models;
using PdfEdit.Services;

namespace PdfEdit.Controls;

/// <summary>
/// Rubber stamps like Acrobat's: a rounded, double-bordered box in the stamp's colour with the
/// title (and for dynamic stamps a "By … at …" line). Placed with the Stamp tool, then selected,
/// moved, resized (corner handle or the toolbar's A / A), rotated and deleted like other marks.
/// </summary>
public partial class PdfViewerControl
{
    /// <summary>Marks and stamps are drawings: their text is hidden and not editable in place.</summary>
    private static bool IsDrawn(FreeTextAnnotation? ann) => ann != null && (ann.IsStamp || IsMarkGlyph(ann.Text));

    /// <summary>The stamp's drawing, laid out for its box size (PDF points) and stretched to fit.</summary>
    internal static Brush StampBrush(FreeTextAnnotation ann)
    {
        double w = Math.Max(10, ann.Width), h = Math.Max(6, ann.Height);
        if (IsQuarterTurn(ann.RotationAngle)) (w, h) = (h, w);
        Color color;
        try { color = (Color)ColorConverter.ConvertFromString(ann.FontColor); }
        catch { color = Color.FromRgb(0x6A, 0x1B, 0x9A); }

        var group = new DrawingGroup();
        using (var dc = group.Open())
        {
            // Transparent frame: the box (not the ink) defines the drawing's bounds.
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));

            double outer = Math.Max(1.2, Math.Min(w, h) * 0.07);
            var pen = new Pen(new SolidColorBrush(color), outer);
            var thin = new Pen(new SolidColorBrush(color), Math.Max(0.6, outer * 0.4));
            double r = Math.Min(w, h) * 0.18;
            var fill = new SolidColorBrush(Color.FromArgb(22, color.R, color.G, color.B));
            dc.DrawRoundedRectangle(fill, pen, new Rect(outer / 2, outer / 2, w - outer, h - outer), r, r);
            double inset = outer * 2.1;
            if (w > inset * 4 && h > inset * 4)
                dc.DrawRoundedRectangle(null, thin, new Rect(inset, inset, w - inset * 2, h - inset * 2), r * 0.7, r * 0.7);

            var typeface = new Typeface(new FontFamily("Arial"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
            bool hasSub = !string.IsNullOrWhiteSpace(ann.StampSubtitle);
            double innerW = w - inset * 2 - outer * 2;
            double titleH = hasSub ? h * 0.42 : h * 0.55;

            var title = Fit(ann.Text, typeface, titleH, innerW, color);
            double titleY = hasSub ? h * 0.14 : (h - title.Height) / 2;
            dc.DrawText(title, new Point((w - title.WidthIncludingTrailingWhitespace) / 2, titleY));

            if (hasSub)
            {
                var subFace = new Typeface(new FontFamily("Arial"), FontStyles.Italic, FontWeights.Normal, FontStretches.Normal);
                var sub = Fit(ann.StampSubtitle!, subFace, h * 0.2, innerW, color);
                dc.DrawText(sub, new Point((w - sub.WidthIncludingTrailingWhitespace) / 2, titleY + title.Height + h * 0.03));
            }
        }
        group.Freeze();
        var brush = new DrawingBrush(group) { Stretch = Stretch.Fill };
        brush.Freeze();
        return brush;
    }

    // Largest text (up to maxH tall) that fits maxW.
    private static FormattedText Fit(string text, Typeface face, double maxH, double maxW, Color color)
    {
        var brush = new SolidColorBrush(color);
        double size = Math.Max(1, maxH / 1.15);
        var ft = Make(size);
        if (ft.WidthIncludingTrailingWhitespace > maxW && maxW > 1)
            ft = Make(Math.Max(1, size * maxW / ft.WidthIncludingTrailingWhitespace));
        return ft;

        FormattedText Make(double em) => new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, face, em, brush, 1.0);
    }

    /// <summary>Stamp tool: places the chosen stamp centred on the click (undoable).</summary>
    private void PlaceRubberStampAnnotation(Point posOnCanvas)
    {
        if (_vm?.Document == null) return;
        int pageNum = _vm.CurrentPageIndex + 1;
        if (pageNum < 1 || pageNum > _vm.Document.PageSizes.Count) return;
        double pageH = _vm.Document.PageSizes[pageNum - 1].Height;

        var def = _vm.SelectedStampDefinition
                  ?? new StampDefinition(StampCatalog.CustomCategory, _vm.SelectedStamp, StampCatalog.ColorFor(_vm.SelectedStamp));
        string? subtitle = def.MakeSubtitle(DateTime.Now);

        // Size from the title length, like Acrobat's stamps (points).
        double hPt = subtitle != null ? 46 : 34;
        double wPt = Math.Clamp(def.Title.Length * 12.5 + 34, 90, 330);
        if (subtitle != null) wPt = Math.Max(wPt, subtitle.Length * 4.6 + 30);

        var ann = new FreeTextAnnotation
        {
            PageNumber = pageNum,
            Left = posOnCanvas.X / Scale - wPt / 2,
            Bottom = pageH - posOnCanvas.Y / Scale - hPt / 2,
            Width = wPt,
            Height = hPt,
            Text = def.Title,
            StampSubtitle = subtitle,
            IsStamp = true,
            FontSize = 18,
            FontFamily = "Arial",
            IsBold = true,
            FontColor = def.Color,
            TextAlignment = TextAlignment.Center,
        };
        _vm.FreeTextAnnotations.Add(ann);
        PlaceAnnotationVisual(ann, pageH);

        var captured = ann;
        _vm.PushUndo(
            undo: () => { _vm.FreeTextAnnotations.Remove(captured); RefreshPage(); },
            redo: () => { _vm.FreeTextAnnotations.Add(captured); RefreshPage(); });

        _vm.StatusText = $"'{def.Title}' stamp placed — drag to move, corner to resize, Del to delete.";
    }
}
