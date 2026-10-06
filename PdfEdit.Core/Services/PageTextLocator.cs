using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Data;
using iText.Kernel.Pdf.Canvas.Parser.Listener;

namespace PdfEdit.Services;

/// <summary>A run of text on the page with its box in PDF points (bottom-left origin).</summary>
public sealed record TextChunk(string Text, double X0, double X1, double Bottom, double Top)
{
    public double MidY => (Bottom + Top) / 2;
}

/// <summary>Text runs with their positions, used to name detected fields after their printed labels.</summary>
public static class PageTextLocator
{
    private sealed class Listener : IEventListener
    {
        public readonly List<TextChunk> Chunks = new();
        public void EventOccurred(IEventData data, EventType type)
        {
            if (type != EventType.RENDER_TEXT || data is not TextRenderInfo info) return;
            string t = info.GetText();
            if (string.IsNullOrWhiteSpace(t)) return;
            var b = info.GetBaseline();
            var a = info.GetAscentLine();
            double x0 = Math.Min(b.GetStartPoint().Get(0), b.GetEndPoint().Get(0));
            double x1 = Math.Max(b.GetStartPoint().Get(0), b.GetEndPoint().Get(0));
            double bottom = b.GetStartPoint().Get(1), top = Math.Max(a.GetStartPoint().Get(1), bottom + 4);
            // Merge with the previous run when it continues the same word / line.
            if (Chunks.Count > 0)
            {
                var p = Chunks[^1];
                if (Math.Abs(p.Bottom - bottom) < 1.5 && x0 - p.X1 < 3.5 && x0 >= p.X1 - 1)
                {
                    Chunks[^1] = p with { Text = p.Text + (x0 - p.X1 > 1.2 ? " " : "") + t, X1 = x1, Top = Math.Max(p.Top, top) };
                    return;
                }
            }
            Chunks.Add(new TextChunk(t, x0, x1, bottom, top));
        }
        public ICollection<EventType> GetSupportedEvents() => new[] { EventType.RENDER_TEXT };
    }

    public static List<TextChunk> GetChunks(string path, int pageNumber)
    {
        using var pdf = new PdfDocument(new PdfReader(path));
        var l = new Listener();
        new PdfCanvasProcessor(l).ProcessPageContent(pdf.GetPage(pageNumber));
        return l.Chunks.Select(c => c with { Text = c.Text.Trim() }).Where(c => c.Text.Length > 0).ToList();
    }

    /// <summary>
    /// A field name from the label next to a field (PDF points): the words on the same line to the
    /// left, else the words just above. Check boxes also take the word to their right ("Yes").
    /// </summary>
    public static string? LabelFor(List<TextChunk> chunks, double left, double bottom, double right, double top, bool checkBox)
    {
        double mid = (bottom + top) / 2, h = Math.Max(6, top - bottom);
        bool SameLine(TextChunk c) => Math.Abs(c.MidY - mid) < Math.Max(6, h * 0.6);

        var leftOnLine = chunks.Where(c => SameLine(c) && c.X1 <= left + 3 && c.X1 > left - 260).OrderByDescending(c => c.X1).ToList();
        string? leftLabel = null;
        if (leftOnLine.Count > 0)
        {
            // The nearest phrase: chunks joined right-to-left while the gaps stay small.
            var parts = new List<TextChunk> { leftOnLine[0] };
            foreach (var c in leftOnLine.Skip(1))
            {
                if (parts[^1].X0 - c.X1 > 14) break;
                parts.Add(c);
            }
            parts.Reverse();
            leftLabel = string.Join(" ", parts.Select(p => p.Text));
        }
        if (checkBox)
        {
            var rightWord = chunks.Where(c => SameLine(c) && c.X0 >= right - 2 && c.X0 < right + 60).OrderBy(c => c.X0).FirstOrDefault();
            if (rightWord != null)
            {
                // "Yes [ ]  No [ ]": the word before the box belongs to the previous box.
                string? row = chunks.Where(c => SameLine(c) && c.X1 < left - 30).OrderByDescending(c => c.X1).FirstOrDefault()?.Text;
                return Clean(row != null ? $"{row} {rightWord.Text}" : rightWord.Text);
            }
            if (leftLabel != null)
            {
                string? row = chunks.Where(c => SameLine(c) && c.X1 < leftOnLine[0].X0 - 20).OrderByDescending(c => c.X1).FirstOrDefault()?.Text;
                return Clean(row != null && leftLabel.Length <= 4 ? $"{row} {leftLabel}" : leftLabel);
            }
        }
        if (leftLabel != null) return Clean(leftLabel);
        var above = chunks.Where(c => c.Bottom >= top - 2 && c.Bottom < top + 18 && c.X1 > left && c.X0 < right).OrderBy(c => c.Bottom).ThenBy(c => c.X0).FirstOrDefault();
        return above != null ? Clean(above.Text) : null;
    }

    private static string? Clean(string s)
    {
        s = s.Replace("*", "").Replace(":", "").Replace("_", " ").Trim();
        while (s.Contains("  ")) s = s.Replace("  ", " ");
        if (s.Length > 40) s = s[..40].Trim();
        return s.Length == 0 ? null : s;
    }
}
