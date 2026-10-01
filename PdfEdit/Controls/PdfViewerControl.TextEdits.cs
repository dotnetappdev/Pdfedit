using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using PdfEdit.Models;

namespace PdfEdit.Controls;

/// <summary>
/// Acrobat's text-edit comments: <b>Insert text</b> (click where text is missing → a blue caret
/// with the text to insert) and <b>Replace text</b> (drag over the words to replace → struck
/// through, with a caret holding the replacement). Both show in the Comments panel.
/// </summary>
public partial class PdfViewerControl
{
    private Point _replaceStart;
    private Rectangle? _replacePreview;

    private static readonly Brush TextEditBrush = MakeFrozen(Color.FromRgb(0x15, 0x65, 0xC0));
    private static Brush MakeFrozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

    /// <summary>Insert text: one click places the caret and asks for the text.</summary>
    private void PlaceInsertTextMark(Point posOnCanvas)
    {
        if (_vm?.Document == null) return;
        var dlg = new Dialogs.InputDialog("Insert Text", "Text to insert here:", "") { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() != true || string.IsNullOrWhiteSpace(dlg.InputText)) return;

        double pageH = _vm.Document.PageSizes[_vm.CurrentPageIndex].Height;
        const double sizePt = 10;
        var mark = new TextEditMark
        {
            Kind = TextEditKind.Insert,
            Left = posOnCanvas.X / Scale - sizePt / 2,
            Bottom = pageH - posOnCanvas.Y / Scale - sizePt * 0.8,
            Width = sizePt,
            Height = sizePt,
            Comment = new CommentInfo { Note = dlg.InputText.Trim() },
        };
        _vm.AddTextEditMark(mark);
        PlaceTextEditVisual(mark, pageH);
        _vm.StatusText = $"Insert text: \"{mark.Comment.Note}\" — see it in the Comments panel.";
    }

    private void BeginReplaceText(Point posOnCanvas)
    {
        _replaceStart = posOnCanvas;
        _replacePreview = new Rectangle
        {
            Stroke = TextEditBrush, StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 3, 2 },
            Fill = new SolidColorBrush(Color.FromArgb(30, 0x15, 0x65, 0xC0)), IsHitTestVisible = false,
        };
        Canvas.SetLeft(_replacePreview, posOnCanvas.X);
        Canvas.SetTop(_replacePreview, posOnCanvas.Y);
        AnnotationCanvas.Children.Add(_replacePreview);
        CaptureMouse();
    }

    private bool UpdateReplaceText(Point pos)
    {
        if (_replacePreview == null) return false;
        Canvas.SetLeft(_replacePreview, Math.Min(pos.X, _replaceStart.X));
        Canvas.SetTop(_replacePreview, Math.Min(pos.Y, _replaceStart.Y));
        _replacePreview.Width = Math.Abs(pos.X - _replaceStart.X);
        _replacePreview.Height = Math.Abs(pos.Y - _replaceStart.Y);
        return true;
    }

    private bool EndReplaceText(Point pos)
    {
        if (_replacePreview == null) return false;
        AnnotationCanvas.Children.Remove(_replacePreview);
        _replacePreview = null;
        ReleaseMouseCapture();
        if (_vm?.Document == null) return true;

        var r = new Rect(_replaceStart, pos);
        if (r.Width < 4) { _vm.StatusText = "Replace text: drag across the words to replace."; return true; }
        if (r.Height < 6) r.Inflate(0, (8 - r.Height) / 2);   // a sideways drag along one line

        var dlg = new Dialogs.InputDialog("Replace Text", "Replace the struck-through text with:", "") { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() != true) return true;

        double pageH = _vm.Document.PageSizes[_vm.CurrentPageIndex].Height;
        var mark = new TextEditMark
        {
            Kind = TextEditKind.Replace,
            Left = r.X / Scale,
            Bottom = pageH - r.Bottom / Scale,
            Width = r.Width / Scale,
            Height = r.Height / Scale,
            Color = "#C62828",
            Comment = new CommentInfo { Note = dlg.InputText?.Trim() ?? string.Empty },
        };
        _vm.AddTextEditMark(mark);
        PlaceTextEditVisual(mark, pageH);
        _vm.StatusText = $"Replace text with \"{mark.Comment.Note}\" — see it in the Comments panel.";
        return true;
    }

    private void BuildTextEditOverlay()
    {
        if (_vm?.Document == null) return;
        double pageH = _vm.Document.PageSizes[_vm.CurrentPageIndex].Height;
        foreach (var mark in _vm.GetTextEditMarksForCurrentPage().ToList())
            PlaceTextEditVisual(mark, pageH);
    }

    /// <summary>Caret (insert) or strike-through + caret (replace), with the text as tooltip.</summary>
    private void PlaceTextEditVisual(TextEditMark mark, double pageH)
    {
        double x = mark.Left * Scale, y = (pageH - mark.Bottom - mark.Height) * Scale;
        double w = mark.Width * Scale, h = mark.Height * Scale;
        var brush = new SolidColorBrush(ParseColor(mark.Color));
        var host = new Canvas { Width = Math.Max(w, 10), Height = Math.Max(h, 10), Background = Brushes.Transparent, Tag = mark };

        Path Caret(double cx, double baseY, double size) => new()
        {
            Data = Geometry.Parse(FormattableString.Invariant(
                $"M {cx - size / 2},{baseY} L {cx},{baseY - size} L {cx + size / 2},{baseY}")),
            Stroke = brush, StrokeThickness = Math.Max(1.2, size / 6), StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
        };

        if (mark.Kind == TextEditKind.Insert)
        {
            host.Children.Add(Caret(w / 2, h, Math.Max(8, h)));
        }
        else
        {
            host.Children.Add(new Line { X1 = 0, Y1 = h / 2, X2 = w, Y2 = h / 2, Stroke = brush, StrokeThickness = Math.Max(1, h / 10) });
            host.Children.Add(Caret(w, h + Math.Max(8, h * 0.6) * 0.6, Math.Max(8, h * 0.6)));
        }

        host.ToolTip = mark.Kind == TextEditKind.Insert
            ? $"Insert: {mark.Comment.Note}\n{mark.Comment.Author} · {mark.Comment.Created:g}"
            : $"Replace with: {mark.Comment.Note}\n{mark.Comment.Author} · {mark.Comment.Created:g}";

        var menu = new ContextMenu();
        var edit = new MenuItem { Header = "Edit text…" };
        edit.Click += (_, _) =>
        {
            var dlg = new Dialogs.InputDialog(mark.Kind == TextEditKind.Insert ? "Insert Text" : "Replace Text",
                "Text:", mark.Comment.Note) { Owner = Window.GetWindow(this) };
            if (dlg.ShowDialog() == true)
            {
                mark.Comment.Note = dlg.InputText?.Trim() ?? string.Empty;
                mark.Comment.Modified = DateTime.Now;
                _vm?.NotifyCommentsChanged();
                RefreshPage();
            }
        };
        var open = new MenuItem { Header = "Show in Comments panel" };
        open.Click += (_, _) => _vm?.ShowCommentsPanel();
        var del = new MenuItem { Header = "Delete" };
        del.Click += (_, _) => { AnnotationCanvas.Children.Remove(host); _vm?.RemoveTextEditMark(mark); };
        menu.Items.Add(edit);
        menu.Items.Add(open);
        menu.Items.Add(new Separator());
        menu.Items.Add(del);
        host.ContextMenu = menu;

        MakeDraggable(host, (dx, dy) => { mark.Left += dx; mark.Bottom += dy; });
        Canvas.SetLeft(host, x);
        Canvas.SetTop(host, y);
        AnnotationCanvas.Children.Add(host);
    }
}
