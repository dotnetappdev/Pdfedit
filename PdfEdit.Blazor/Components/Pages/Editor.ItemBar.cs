using PdfEdit.Blazor.Components.Editor;
using PdfEdit.Models;

namespace PdfEdit.Blazor.Components.Pages;

/// <summary>
/// What the floating toolbar over a selected page item does — the Windows app's annotation toolbar:
/// smaller / larger, delete, rotate, character spacing, fit, swap mark and the colour swatches.
/// </summary>
public partial class Editor
{
    public static readonly string[] MarkGlyphs = ["✓", "✕", "○", "—", "●"];

    /// <summary>The toolbar's quick colours (black, dark blue, dark red, dark green, grey).</summary>
    public static readonly (string Hex, string Name)[] SwatchColors =
        [("#000000", "Black"), ("#1A237E", "Dark Blue"), ("#B71C1C", "Dark Red"), ("#1B5E20", "Dark Green"), ("#757575", "Gray")];

    /// <summary>Text and callouts change font size; everything else grows or shrinks about its centre.</summary>
    public void ResizeItem(PageItem item, int step)
    {
        if (item.Kind is ItemKind.Text or ItemKind.Callout)
        {
            item.FontSize = Math.Clamp(item.FontSize + step, 4, 144);
            if (SelectedItemId == item.Id) _fontSize = (int)Math.Round(item.FontSize);
            GrowText(item);
            return;
        }
        if (item.Kind == ItemKind.Note) return;
        double factor = step > 0 ? 1.15 : 1 / 1.15;
        var (pw, ph) = ViewSize(item.Page);
        var (oldLeft, oldTop, oldWidth, oldHeight) = (item.Left, item.Top, item.Width, item.Height);
        double w = Math.Clamp(oldWidth * factor, 6, pw), h = Math.Clamp(oldHeight * factor, 6, ph);
        item.Left = Math.Clamp(oldLeft - (w - oldWidth) / 2, 0, pw - w);
        item.Top = Math.Clamp(oldTop - (h - oldHeight) / 2, 0, ph - h);
        item.Width = w;
        item.Height = h;
        if (item.Kind == ItemKind.Mark) item.FontSize = Math.Min(w, h);
        MoveSketchPoints(item, oldLeft, oldTop, oldWidth, oldHeight);
    }

    public static bool CanRotate(PageItem item) => item.Kind is ItemKind.Text or ItemKind.Mark or ItemKind.Stamp;

    /// <summary>Turns text, a mark or a stamp 90° clockwise; the box turns with it.</summary>
    public void RotateItem(PageItem item)
    {
        if (!CanRotate(item)) return;
        item.Rotation = (item.Rotation + 90) % 360;
        var (pw, ph) = ViewSize(item.Page);
        double cx = item.Left + item.Width / 2, cy = item.Top + item.Height / 2;
        (item.Width, item.Height) = (Math.Min(item.Height, pw), Math.Min(item.Width, ph));
        item.Left = Math.Clamp(cx - item.Width / 2, 0, pw - item.Width);
        item.Top = Math.Clamp(cy - item.Height / 2, 0, ph - item.Height);
        Status($"Rotated to {item.Rotation}°");
    }

    /// <summary>Cycles a mark through ✓ ✕ ○ — ●.</summary>
    public static void SwapMark(PageItem item)
    {
        int i = Array.IndexOf(MarkGlyphs, item.Text);
        item.Text = MarkGlyphs[(i + 1) % MarkGlyphs.Length];
    }

    public static bool HasColor(PageItem item) =>
        item.Kind is not (ItemKind.Signature or ItemKind.Picture or ItemKind.Note or ItemKind.Redact);

    public void SetItemColor(PageItem item, string hex)
    {
        item.Color = hex;
        if (item.Kind is ItemKind.Text or ItemKind.Callout && SelectedItemId == item.Id) _textColor = hex;
    }

    public void SetItemFit(PageItem item, TextFit fit)
    {
        item.Fit = fit;
        GrowText(item);
    }

    /// <summary>
    /// Sizes a text box to its text, as its Fit says: Auto fits the box to the lines, Wrap keeps the
    /// width and grows taller, Fixed leaves it (unless <paramref name="now"/>, the Fit menu's "Fit box to text now").
    /// </summary>
    public void GrowText(PageItem item, bool now = false)
    {
        if (item.Kind != ItemKind.Text || (item.Fit == TextFit.Fixed && !now)) return;
        var (pw, ph) = ViewSize(item.Page);
        var lines = item.Text.Split('\n');
        double charW = item.FontSize * 0.55 + item.CharSpacing, lineH = item.FontSize * 1.3;
        bool q = item.QuarterTurn;
        double along = q ? item.Height : item.Width;   // the length of a line on the page
        int rows = lines.Length;
        if (item.Fit == TextFit.Wrap && !now)
            rows = lines.Sum(l => Math.Max(1, (int)Math.Ceiling((l.Length * charW + 6) / Math.Max(10, along))));
        else
            along = Math.Max(20, lines.Max(l => l.Length) * charW + 6);
        double across = rows * lineH + 2;               // the stack of lines

        if (!q)
        {
            item.Width = Math.Clamp(along, 20, pw - item.Left);
            item.Height = Math.Clamp(across, 6, ph - item.Top);
            return;
        }
        double bottom = item.Top + item.Height;
        item.Height = Math.Clamp(along, 20, item.Rotation == 270 ? bottom : ph - item.Top);
        item.Width = Math.Clamp(across, 6, pw - item.Left);
        if (item.Rotation == 270) item.Top = bottom - item.Height;   // reads upwards: grows up the page
    }
}
