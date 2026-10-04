using System.IO;
using System.IO.Compression;
using System.Xml.Linq;
using iText.IO.Font;
using iText.IO.Image;
using iText.Kernel.Colors;
using iText.Kernel.Font;
using iText.Kernel.Geom;
using iText.Kernel.Pdf;
using iText.Layout;
using iText.Layout.Borders;
using iText.Layout.Element;
using iText.Layout.Properties;

namespace PdfEdit.Services;

/// <summary>
/// Word (.docx) to PDF without Microsoft Office: reads the document's XML and lays it out with
/// iText. Covers what most documents use: headings and styles, bold / italic / underline /
/// strike / colour / highlight / size / font, alignment, spacing and indents, bulleted and numbered
/// lists, tables (with merged columns), pictures, hyperlinks, tabs, line and page breaks, and the
/// page size and margins. Headers, footers, text boxes and footnotes are left out.
/// </summary>
public static class DocxToPdfService
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly XNamespace WP = "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing";
    private static readonly XNamespace Rel = "http://schemas.openxmlformats.org/package/2006/relationships";

    public static void Convert(string docxPath, string pdfPath)
    {
        using var zip = ZipFile.OpenRead(docxPath);
        var ctx = new Context(zip);
        var body = ctx.Doc.Root?.Element(W + "body") ?? throw new InvalidDataException("This doesn't look like a Word document.");

        // Page size and margins from the (last) section; twips → points.
        var sect = body.Element(W + "sectPr");
        var pgSz = sect?.Element(W + "pgSz");
        var pgMar = sect?.Element(W + "pgMar");
        float pw = Twips(pgSz?.Attribute(W + "w"), 12240), ph = Twips(pgSz?.Attribute(W + "h"), 15840);
        if (pgSz?.Attribute(W + "orient")?.Value == "landscape" && pw < ph) (pw, ph) = (ph, pw);

        using var pdf = new PdfDocument(new PdfWriter(pdfPath));
        pdf.GetDocumentInfo().SetTitle(ctx.Title ?? System.IO.Path.GetFileNameWithoutExtension(docxPath));
        using var doc = new Document(pdf, new PageSize(pw, ph));
        doc.SetMargins(Twips(pgMar?.Attribute(W + "top"), 1440), Twips(pgMar?.Attribute(W + "right"), 1440),
                       Twips(pgMar?.Attribute(W + "bottom"), 1440), Twips(pgMar?.Attribute(W + "left"), 1440));
        ctx.ContentWidth = pw - doc.GetLeftMargin() - doc.GetRightMargin();

        foreach (var el in body.Elements())
        {
            if (el.Name == W + "p") AddParagraph(doc, ctx, el);
            else if (el.Name == W + "tbl") doc.Add(BuildTable(ctx, el, ctx.ContentWidth));
            else if (el.Name == W + "sdt") foreach (var inner in el.Element(W + "sdtContent")?.Elements() ?? Enumerable.Empty<XElement>())
            {
                if (inner.Name == W + "p") AddParagraph(doc, ctx, inner);
                else if (inner.Name == W + "tbl") doc.Add(BuildTable(ctx, inner, ctx.ContentWidth));
            }
        }
    }

    private static float Twips(XAttribute? a, float fallbackTwips) =>
        (a != null && float.TryParse(a.Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : fallbackTwips) / 20f;

    // ── Paragraphs ──────────────────────────────────────────────────────────

    private static void AddParagraph(Document doc, Context ctx, XElement p)
    {
        var pPr = p.Element(W + "pPr");
        if (pPr?.Element(W + "pageBreakBefore") is { } pbb && On(pbb)) doc.Add(new AreaBreak(AreaBreakType.NEXT_PAGE));

        // Split the paragraph at page breaks inside it.
        var pieces = new List<List<XElement>> { new() };
        foreach (var child in p.Elements())
        {
            if (child.Name == W + "r" && child.Elements(W + "br").Any(b => b.Attribute(W + "type")?.Value == "page"))
            {
                pieces[^1].Add(child);
                pieces.Add(new());
            }
            else pieces[^1].Add(child);
        }
        for (int i = 0; i < pieces.Count; i++)
        {
            if (i > 0) doc.Add(new AreaBreak(AreaBreakType.NEXT_PAGE));
            var para = BuildParagraph(ctx, pPr, pieces[i], ctx.ContentWidth);
            if (para != null) doc.Add(para);
        }
    }

    private static IBlockElement? BuildParagraph(Context ctx, XElement? pPr, IEnumerable<XElement> children, float width)
    {
        var style = ctx.GetParaStyle(pPr);
        var para = new Paragraph().SetMargin(0).SetMultipliedLeading(style.Leading);
        para.SetMarginTop(style.SpaceBefore).SetMarginBottom(style.SpaceAfter);
        para.SetTextAlignment(style.Align);
        float indent = style.IndentLeft;

        // Lists: a bullet or number in front.
        var numPr = pPr?.Element(W + "numPr") ?? style.NumPr;
        if (numPr != null)
        {
            string numId = numPr.Element(W + "numId")?.Attribute(W + "val")?.Value ?? "";
            int lvl = int.TryParse(numPr.Element(W + "ilvl")?.Attribute(W + "val")?.Value, out var l) ? l : 0;
            if (numId != "0")
            {
                string label = ctx.NextListLabel(numId, lvl);
                if (indent <= 0) indent = 18 * (lvl + 1);
                para.Add(new Text(label + "  ").SetFont(ctx.Font(style.Run.FontName, false, false)).SetFontSize(style.Run.Size));
                para.SetFirstLineIndent(-14);
            }
        }
        if (indent > 0) para.SetMarginLeft(indent);
        if (style.IndentRight > 0) para.SetMarginRight(style.IndentRight);
        if (style.FirstLine != 0 && numPr == null) para.SetFirstLineIndent(style.FirstLine);

        bool any = false;
        foreach (var child in children)
        {
            if (child.Name == W + "r") any |= AddRun(ctx, para, child, style.Run, null, width - indent);
            else if (child.Name == W + "hyperlink")
            {
                string? url = child.Attribute(R + "id") is { } id ? ctx.Link(id.Value) : null;
                foreach (var r in child.Elements(W + "r")) any |= AddRun(ctx, para, r, style.Run, url, width - indent);
            }
            else if (child.Name == W + "smartTag" || child.Name == W + "ins" || child.Name == W + "fldSimple" || child.Name == W + "sdt")
                foreach (var r in child.Descendants(W + "r")) any |= AddRun(ctx, para, r, style.Run, null, width - indent);
        }
        if (!any) para.Add(new Text(" ").SetFontSize(style.Run.Size));   // empty line keeps its height
        return para;
    }

    private static bool AddRun(Context ctx, Paragraph para, XElement r, RunStyle baseStyle, string? url, float width)
    {
        var rs = ctx.GetRunStyle(r.Element(W + "rPr"), baseStyle);
        if (rs.Hidden) return false;
        bool added = false;
        foreach (var e in r.Elements())
        {
            if (e.Name == W + "t") { para.Add(MakeText(ctx, e.Value, rs, url)); added = true; }
            else if (e.Name == W + "tab") { para.Add(new Tab()); added = true; }
            else if (e.Name == W + "br" && e.Attribute(W + "type")?.Value != "page") { para.Add(new Text("\n")); added = true; }
            else if (e.Name == W + "noBreakHyphen") { para.Add(MakeText(ctx, "-", rs, url)); added = true; }
            else if (e.Name == W + "sym" && e.Attribute(W + "char") is { } ch && int.TryParse(ch.Value, System.Globalization.NumberStyles.HexNumber, null, out int code))
            { para.Add(MakeText(ctx, code >= 0xF000 ? "•" : char.ConvertFromUtf32(code), rs, url)); added = true; }
            else if (e.Name == W + "drawing" && Picture(ctx, e, width) is { } img) { para.Add(img); added = true; }
        }
        return added;
    }

    private static ILeafElement MakeText(Context ctx, string s, RunStyle rs, string? url)
    {
        if (rs.Caps) s = s.ToUpperInvariant();
        var t = new Text(s).SetFont(ctx.Font(rs.FontName, rs.Bold, rs.Italic)).SetFontSize(rs.Size);
        if (rs.Bold && !ctx.HasStyle(rs.FontName, true, false)) t.SimulateBold();
        if (rs.Italic && !ctx.HasStyle(rs.FontName, false, true)) t.SimulateItalic();
        if (rs.Color != null) t.SetFontColor(rs.Color);
        if (rs.Underline || url != null) t.SetUnderline();
        if (rs.Strike) t.SetLineThrough();
        if (rs.Highlight != null) t.SetBackgroundColor(rs.Highlight);
        if (rs.Super) t.SetTextRise(rs.Size * 0.33f).SetFontSize(rs.Size * 0.65f);
        if (rs.Sub) t.SetTextRise(-rs.Size * 0.15f).SetFontSize(rs.Size * 0.65f);
        if (url != null)
        {
            t.SetFontColor(new DeviceRgb(0x05, 0x63, 0xC1));
            t.SetAction(iText.Kernel.Pdf.Action.PdfAction.CreateURI(url));
        }
        return t;
    }

    private static Image? Picture(Context ctx, XElement drawing, float maxWidth)
    {
        var blip = drawing.Descendants(A + "blip").FirstOrDefault();
        string? id = blip?.Attribute(R + "embed")?.Value;
        if (id == null || ctx.Media(id) is not { } bytes) return null;
        try
        {
            var img = new Image(ImageDataFactory.Create(bytes));
            var extent = drawing.Descendants(WP + "extent").FirstOrDefault();
            if (extent != null && long.TryParse(extent.Attribute("cx")?.Value, out long cx) && long.TryParse(extent.Attribute("cy")?.Value, out long cy) && cx > 0 && cy > 0)
            {
                float w = cx / 12700f, h = cy / 12700f;
                if (w > maxWidth) { h *= maxWidth / w; w = maxWidth; }
                img.ScaleAbsolute(w, h);
            }
            else img.SetAutoScaleWidth(true);
            return img;
        }
        catch { return null; }   // EMF/WMF and other formats iText can't draw
    }

    // ── Tables ──────────────────────────────────────────────────────────────

    private static Table BuildTable(Context ctx, XElement tbl, float width)
    {
        var grid = tbl.Element(W + "tblGrid")?.Elements(W + "gridCol").Select(g => Twips(g.Attribute(W + "w"), 1440)).ToArray() ?? Array.Empty<float>();
        int cols = grid.Length > 0 ? grid.Length
            : tbl.Elements(W + "tr").Select(tr => tr.Elements(W + "tc").Sum(tc => Span(tc))).DefaultIfEmpty(1).Max();
        if (grid.Length == 0) grid = Enumerable.Repeat(width / cols, cols).ToArray();
        float total = grid.Sum();
        if (total > width) grid = grid.Select(g => g * width / total).ToArray();

        bool borders = tbl.Element(W + "tblPr")?.Element(W + "tblBorders") is not { } b
            || b.Elements().Any(e => e.Attribute(W + "val")?.Value is not ("nil" or "none"));
        var table = new Table(UnitValue.CreatePointArray(grid)).SetMarginBottom(8);
        table.SetFixedLayout();
        foreach (var tr in tbl.Elements(W + "tr"))
        {
            int col = 0;
            foreach (var tc in tr.Elements(W + "tc"))
            {
                int span = Math.Min(Span(tc), cols - col);
                if (span <= 0) break;
                bool continueMerge = tc.Element(W + "tcPr")?.Element(W + "vMerge") is { } vm && vm.Attribute(W + "val")?.Value is null or "continue";
                var cell = new Cell(1, span).SetPadding(4);
                cell.SetBorder(borders ? new SolidBorder(new DeviceRgb(0x99, 0x99, 0x99), 0.5f) : Border.NO_BORDER);
                if (continueMerge) cell.SetBorderTop(Border.NO_BORDER);
                if (tc.Element(W + "tcPr")?.Element(W + "shd")?.Attribute(W + "fill")?.Value is { } fill && Hex(fill) is { } shade) cell.SetBackgroundColor(shade);
                float cellWidth = grid.Skip(col).Take(span).Sum() - 8;
                if (!continueMerge)
                    foreach (var el in tc.Elements())
                    {
                        if (el.Name == W + "p") { if (BuildParagraph(ctx, el.Element(W + "pPr"), el.Elements(), cellWidth) is { } bp) cell.Add(bp); }
                        else if (el.Name == W + "tbl") cell.Add(BuildTable(ctx, el, cellWidth));
                    }
                table.AddCell(cell);
                col += span;
            }
            for (; col < cols; col++) table.AddCell(new Cell().SetBorder(Border.NO_BORDER));
        }
        return table;

        static int Span(XElement tc) => int.TryParse(tc.Element(W + "tcPr")?.Element(W + "gridSpan")?.Attribute(W + "val")?.Value, out int s) ? Math.Max(1, s) : 1;
    }

    // ── Styles, numbering, fonts, parts ─────────────────────────────────────

    private sealed record RunStyle(string FontName, float Size, bool Bold, bool Italic, bool Underline, bool Strike,
        Color? Color, Color? Highlight, bool Caps, bool Hidden, bool Super, bool Sub);

    private sealed record ParaStyle(RunStyle Run, TextAlignment Align, float SpaceBefore, float SpaceAfter, float Leading,
        float IndentLeft, float IndentRight, float FirstLine, XElement? NumPr);

    private static bool On(XElement e) => e.Attribute(W + "val")?.Value is null or "1" or "true" or "on";

    private static Color? Hex(string? v)
    {
        if (v == null || v == "auto" || v.Length != 6) return null;
        try { return new DeviceRgb(System.Convert.ToInt32(v[..2], 16), System.Convert.ToInt32(v[2..4], 16), System.Convert.ToInt32(v[4..], 16)); }
        catch { return null; }
    }

    private static Color? HighlightColor(string? v) => v switch
    {
        "yellow" => new DeviceRgb(255, 255, 0), "green" => new DeviceRgb(0, 255, 0), "cyan" => new DeviceRgb(0, 255, 255),
        "magenta" => new DeviceRgb(255, 0, 255), "blue" => new DeviceRgb(0, 0, 255), "red" => new DeviceRgb(255, 0, 0),
        "lightGray" => new DeviceRgb(211, 211, 211), "darkGray" => new DeviceRgb(169, 169, 169), _ => null,
    };

    private sealed class Context
    {
        private readonly ZipArchive _zip;
        private readonly Dictionary<string, XElement> _styles = new();
        private readonly Dictionary<string, string> _rels = new();
        private readonly Dictionary<string, PdfFont> _fonts = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _fontFiles = new(StringComparer.OrdinalIgnoreCase);
        private readonly XDocument? _numbering;
        private readonly Dictionary<(string, int), int> _counters = new();
        private readonly RunStyle _defaultRun;
        private readonly XElement? _defaultPPr;

        public XDocument Doc { get; }
        public string? Title { get; }
        public float ContentWidth { get; set; }

        public Context(ZipArchive zip)
        {
            _zip = zip;
            Doc = Load("word/document.xml") ?? throw new InvalidDataException("This doesn't look like a Word document.");
            var styles = Load("word/styles.xml");
            foreach (var s in styles?.Root?.Elements(W + "style") ?? Enumerable.Empty<XElement>())
                if (s.Attribute(W + "styleId")?.Value is { } id) _styles[id] = s;
            _numbering = Load("word/numbering.xml");
            foreach (var r in Load("word/_rels/document.xml.rels")?.Root?.Elements(Rel + "Relationship") ?? Enumerable.Empty<XElement>())
                if (r.Attribute("Id")?.Value is { } id) _rels[id] = r.Attribute("Target")?.Value ?? "";
            Title = Load("docProps/core.xml")?.Descendants().FirstOrDefault(e => e.Name.LocalName == "title")?.Value is { Length: > 0 } t ? t : null;

            var defaults = styles?.Root?.Element(W + "docDefaults");
            var baseRun = new RunStyle("Calibri", 11, false, false, false, false, null, null, false, false, false, false);
            _defaultRun = ApplyRPr(defaults?.Element(W + "rPrDefault")?.Element(W + "rPr"), baseRun);
            _defaultPPr = defaults?.Element(W + "pPrDefault")?.Element(W + "pPr");
            // The "Normal" style applies to every paragraph without its own style.
            if (_styles.Values.FirstOrDefault(s => s.Attribute(W + "default")?.Value == "1" && s.Attribute(W + "type")?.Value == "paragraph") is { } normal)
                _defaultRun = ApplyRPr(normal.Element(W + "rPr"), _defaultRun);
        }

        private XDocument? Load(string name)
        {
            var e = _zip.GetEntry(name);
            if (e == null) return null;
            using var s = e.Open();
            return XDocument.Load(s);
        }

        public byte[]? Media(string relId)
        {
            if (!_rels.TryGetValue(relId, out var target)) return null;
            var e = _zip.GetEntry("word/" + target.TrimStart('/').Replace("../", "")) ?? _zip.GetEntry(target.TrimStart('/'));
            if (e == null) return null;
            using var s = e.Open();
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            return ms.ToArray();
        }

        public string? Link(string relId) => _rels.TryGetValue(relId, out var t) && Uri.IsWellFormedUriString(t, UriKind.Absolute) ? t : null;

        /// <summary>A style and the styles it's based on, nearest last.</summary>
        private IEnumerable<XElement> Chain(string? styleId)
        {
            var chain = new List<XElement>();
            for (int guard = 0; styleId != null && guard < 10 && _styles.TryGetValue(styleId, out var s); guard++)
            {
                chain.Insert(0, s);
                styleId = s.Element(W + "basedOn")?.Attribute(W + "val")?.Value;
            }
            return chain;
        }

        public ParaStyle GetParaStyle(XElement? pPr)
        {
            string? styleId = pPr?.Element(W + "pStyle")?.Attribute(W + "val")?.Value;
            var run = _defaultRun;
            var pprs = new List<XElement?> { _defaultPPr };
            foreach (var s in Chain(styleId))
            {
                run = ApplyRPr(s.Element(W + "rPr"), run);
                pprs.Add(s.Element(W + "pPr"));
            }
            pprs.Add(pPr);
            // Headings Word marks only by name still get heading sizes if the style has none.
            if (styleId != null && styleId.StartsWith("Heading", StringComparison.OrdinalIgnoreCase) && !_styles.ContainsKey(styleId)
                && int.TryParse(styleId[7..], out int level))
                run = run with { Bold = true, Size = level switch { 1 => 16, 2 => 13, 3 => 12, _ => 11 } };
            if (styleId == "Title" && !_styles.ContainsKey("Title")) run = run with { Size = 28 };

            var align = TextAlignment.LEFT;
            float before = 0, after = 0, leading = 1.08f, left = 0, right = 0, first = 0;
            XElement? numPr = null;
            foreach (var p in pprs.Where(p => p != null))
            {
                switch (p!.Element(W + "jc")?.Attribute(W + "val")?.Value)
                {
                    case "center": align = TextAlignment.CENTER; break;
                    case "right" or "end": align = TextAlignment.RIGHT; break;
                    case "both" or "distribute": align = TextAlignment.JUSTIFIED; break;
                    case "left" or "start": align = TextAlignment.LEFT; break;
                }
                if (p.Element(W + "spacing") is { } sp)
                {
                    if (sp.Attribute(W + "before") is { } b) before = Twips(b, 0);
                    if (sp.Attribute(W + "after") is { } a) after = Twips(a, 0);
                    if (sp.Attribute(W + "line") is { } ln && float.TryParse(ln.Value, out var lv) && sp.Attribute(W + "lineRule")?.Value is null or "auto")
                        leading = Math.Clamp(lv / 240f * 1.08f, 0.8f, 3f);
                }
                if (p.Element(W + "ind") is { } ind)
                {
                    if ((ind.Attribute(W + "left") ?? ind.Attribute(W + "start")) is { } l) left = Twips(l, 0);
                    if ((ind.Attribute(W + "right") ?? ind.Attribute(W + "end")) is { } r) right = Twips(r, 0);
                    if (ind.Attribute(W + "firstLine") is { } f) first = Twips(f, 0);
                    if (ind.Attribute(W + "hanging") is { } h) first = -Twips(h, 0);
                }
                if (p.Element(W + "numPr") is { } np) numPr = np;
            }
            return new ParaStyle(run, align, before, after, leading, left, right, first, numPr);
        }

        public RunStyle GetRunStyle(XElement? rPr, RunStyle baseStyle)
        {
            var run = baseStyle;
            if (rPr?.Element(W + "rStyle")?.Attribute(W + "val")?.Value is { } rs)
                foreach (var s in Chain(rs)) run = ApplyRPr(s.Element(W + "rPr"), run);
            return ApplyRPr(rPr, run);
        }

        private RunStyle ApplyRPr(XElement? rPr, RunStyle r)
        {
            if (rPr == null) return r;
            if (rPr.Element(W + "rFonts") is { } f && (f.Attribute(W + "ascii") ?? f.Attribute(W + "hAnsi"))?.Value is { } font) r = r with { FontName = font };
            else if (rPr.Element(W + "rFonts")?.Attribute(W + "asciiTheme")?.Value is { } theme)
                r = r with { FontName = theme.StartsWith("major") ? "Calibri Light" : "Calibri" };
            if (rPr.Element(W + "sz")?.Attribute(W + "val")?.Value is { } sz && float.TryParse(sz, out var half)) r = r with { Size = half / 2 };
            if (rPr.Element(W + "b") is { } b) r = r with { Bold = On(b) };
            if (rPr.Element(W + "i") is { } i) r = r with { Italic = On(i) };
            if (rPr.Element(W + "u") is { } u) r = r with { Underline = u.Attribute(W + "val")?.Value is not ("none" or null) || u.Attribute(W + "val") == null };
            if (rPr.Element(W + "strike") is { } st) r = r with { Strike = On(st) };
            if (rPr.Element(W + "color")?.Attribute(W + "val")?.Value is { } c) r = r with { Color = Hex(c) };
            if (rPr.Element(W + "highlight")?.Attribute(W + "val")?.Value is { } hl) r = r with { Highlight = HighlightColor(hl) };
            if (rPr.Element(W + "shd")?.Attribute(W + "fill")?.Value is { } shd && Hex(shd) is { } sc) r = r with { Highlight = sc };
            if (rPr.Element(W + "caps") is { } caps) r = r with { Caps = On(caps) };
            if (rPr.Element(W + "vanish") is { } v) r = r with { Hidden = On(v) };
            if (rPr.Element(W + "vertAlign")?.Attribute(W + "val")?.Value is { } va) r = r with { Super = va == "superscript", Sub = va == "subscript" };
            return r;
        }

        /// <summary>The bullet or number for the next item of a list.</summary>
        public string NextListLabel(string numId, int level)
        {
            var num = _numbering?.Root?.Elements(W + "num").FirstOrDefault(n => n.Attribute(W + "numId")?.Value == numId);
            string? absId = num?.Element(W + "abstractNumId")?.Attribute(W + "val")?.Value;
            var abs = _numbering?.Root?.Elements(W + "abstractNum").FirstOrDefault(a => a.Attribute(W + "abstractNumId")?.Value == absId);
            var lvl = abs?.Elements(W + "lvl").FirstOrDefault(l => l.Attribute(W + "ilvl")?.Value == level.ToString());
            string fmt = lvl?.Element(W + "numFmt")?.Attribute(W + "val")?.Value ?? "bullet";
            string text = lvl?.Element(W + "lvlText")?.Attribute(W + "val")?.Value ?? "%1.";

            // Count this item; deeper levels restart.
            var key = (numId, level);
            _counters[key] = _counters.TryGetValue(key, out var n) ? n + 1 : (int.TryParse(lvl?.Element(W + "start")?.Attribute(W + "val")?.Value, out var st) ? st : 1);
            foreach (var k in _counters.Keys.Where(k => k.Item1 == numId && k.Item2 > level).ToList()) _counters.Remove(k);

            if (fmt == "bullet") return level % 3 switch { 0 => "•", 1 => "◦", _ => "▪" };
            if (fmt == "none") return "";
            string result = text;
            for (int l = 0; l <= level; l++)
            {
                int value = _counters.TryGetValue((numId, l), out var c) ? c : 1;
                var lf = abs?.Elements(W + "lvl").FirstOrDefault(x => x.Attribute(W + "ilvl")?.Value == l.ToString())?.Element(W + "numFmt")?.Attribute(W + "val")?.Value ?? "decimal";
                result = result.Replace($"%{l + 1}", Format(value, lf));
            }
            return result;
        }

        private static string Format(int n, string fmt) => fmt switch
        {
            "lowerLetter" => Letters(n).ToLowerInvariant(),
            "upperLetter" => Letters(n),
            "lowerRoman" => Roman(n).ToLowerInvariant(),
            "upperRoman" => Roman(n),
            _ => n.ToString(),
        };

        private static string Letters(int n) { string s = ""; for (; n > 0; n = (n - 1) / 26) s = (char)('A' + (n - 1) % 26) + s; return s; }

        private static string Roman(int n)
        {
            var map = new[] { (1000, "M"), (900, "CM"), (500, "D"), (400, "CD"), (100, "C"), (90, "XC"), (50, "L"), (40, "XL"), (10, "X"), (9, "IX"), (5, "V"), (4, "IV"), (1, "I") };
            var sb = new System.Text.StringBuilder();
            foreach (var (v, s) in map) while (n >= v) { sb.Append(s); n -= v; }
            return sb.ToString();
        }

        // Font files for the common Office fonts: regular, bold, italic, bold italic.
        private static readonly Dictionary<string, string[]> KnownFonts = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Calibri"] = new[] { "calibri.ttf", "calibrib.ttf", "calibrii.ttf", "calibriz.ttf" },
            ["Calibri Light"] = new[] { "calibril.ttf", "calibrib.ttf", "calibrili.ttf", "calibriz.ttf" },
            ["Arial"] = new[] { "arial.ttf", "arialbd.ttf", "ariali.ttf", "arialbi.ttf" },
            ["Times New Roman"] = new[] { "times.ttf", "timesbd.ttf", "timesi.ttf", "timesbi.ttf" },
            ["Courier New"] = new[] { "cour.ttf", "courbd.ttf", "couri.ttf", "courbi.ttf" },
            ["Verdana"] = new[] { "verdana.ttf", "verdanab.ttf", "verdanai.ttf", "verdanaz.ttf" },
            ["Tahoma"] = new[] { "tahoma.ttf", "tahomabd.ttf", "tahoma.ttf", "tahomabd.ttf" },
            ["Georgia"] = new[] { "georgia.ttf", "georgiab.ttf", "georgiai.ttf", "georgiaz.ttf" },
            ["Segoe UI"] = new[] { "segoeui.ttf", "segoeuib.ttf", "segoeuii.ttf", "segoeuiz.ttf" },
            ["Cambria"] = new[] { "cambria.ttc,0", "cambriab.ttf", "cambriai.ttf", "cambriaz.ttf" },
            ["Trebuchet MS"] = new[] { "trebuc.ttf", "trebucbd.ttf", "trebucit.ttf", "trebucbi.ttf" },
            ["Garamond"] = new[] { "GARA.TTF", "GARABD.TTF", "GARAIT.TTF", "GARABD.TTF" },
            ["Aptos"] = new[] { "aptos.ttf", "aptos-bold.ttf", "aptos-italic.ttf", "aptos-bold-italic.ttf" },
        };

        private static string FontsDir => Environment.GetFolderPath(Environment.SpecialFolder.Fonts);

        private string? FileFor(string name, bool bold, bool italic)
        {
            if (!KnownFonts.TryGetValue(name, out var files)) return null;
            string f = files[(bold ? 1 : 0) + (italic ? 2 : 0)];
            return File.Exists(System.IO.Path.Combine(FontsDir, f.Split(',')[0])) ? System.IO.Path.Combine(FontsDir, f) : null;
        }

        public bool HasStyle(string name, bool bold, bool italic) => FileFor(name, bold, italic) != null && FileFor(name, false, false) != FileFor(name, bold, italic);

        public PdfFont Font(string name, bool bold, bool italic)
        {
            string key = $"{name}|{bold}|{italic}";
            if (_fonts.TryGetValue(key, out var cached)) return cached;
            PdfFont font;
            string? file = FileFor(name, bold, italic) ?? FileFor(name, false, false) ?? FileFor("Calibri", bold, italic) ?? FileFor("Arial", bold, italic);
            try
            {
                font = file != null
                    ? PdfFontFactory.CreateFont(file, PdfEncodings.IDENTITY_H, PdfFontFactory.EmbeddingStrategy.PREFER_EMBEDDED)
                    : PdfFontFactory.CreateFont(iText.IO.Font.Constants.StandardFonts.HELVETICA);
            }
            catch { font = PdfFontFactory.CreateFont(iText.IO.Font.Constants.StandardFonts.HELVETICA); }
            _fonts[key] = font;
            return font;
        }
    }
}
