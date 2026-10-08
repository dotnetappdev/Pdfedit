using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Annot;
using PdfEdit.Models;

namespace PdfEdit.Blazor.Services;

/// <summary>A comment or mark already in the PDF, for the Comments panel and the annotation summary.</summary>
public sealed record PdfAnnotationItem(int PageNumber, int Index, string Kind, string Author, string Text, DateTime? Modified)
{
    public double Left { get; init; }
    public double Bottom { get; init; }
    public double Width { get; init; }
    public double Height { get; init; }
    public string Colour { get; init; } = "";
    /// <summary>PdfEdit's id for its own annotations (/NM "pdfedit:&lt;id&gt;"), else null.</summary>
    public string? Id { get; init; }
}

/// <summary>
/// One of PdfEdit's own annotations (/NM "pdfedit:&lt;id&gt;") read back so it can be edited again on the
/// page, in the PDF's coordinates. Kind is Text, Mark, Stamp, Ink, Highlight, Note, Rectangle, Ellipse,
/// Line, Arrow, Distance, Polygon, Area, Polyline, Perimeter, Cloud, Callout, Insert, Replace, Signature or Picture.
/// </summary>
public sealed record OwnAnnotation(string Id, int PageNumber, string Kind)
{
    public double Left { get; init; }
    public double Bottom { get; init; }
    public double Width { get; init; }
    public double Height { get; init; }
    public string Text { get; init; } = "";
    public string? Subtitle { get; init; }
    public string Colour { get; init; } = "#000000";
    public double FontSize { get; init; } = 12;
    public bool Bold { get; init; }
    public bool Italic { get; init; }
    public double CharSpacing { get; init; }
    /// <summary>/Rotate: how far the content is turned anticlockwise on the unrotated page.</summary>
    public int Rotate { get; init; }
    public double LineWidth { get; init; } = 2;
    public double Opacity { get; init; } = 1;
    public HighlightKind Markup { get; init; } = HighlightKind.Highlight;
    public List<PointD>? Points { get; init; }
    /// <summary>A signature's or picture's image (PNG, or JPEG for photos).</summary>
    public byte[]? Image { get; init; }
    /// <summary>A callout's tip.</summary>
    public PointD? Tip { get; init; }
    /// <summary>A measurement's unit (in, mm, cm or pt).</summary>
    public string Unit { get; init; } = "in";
    public string Author { get; init; } = "";
    public bool Locked { get; init; }
    /// <summary>The review thread: note, replies, status and checkmark (read from the PDF's reply annotations).</summary>
    public CommentInfo Comment { get; init; } = new();
}

/// <summary>Lists the annotations already saved in a PDF (not form fields or links).</summary>
public static class PdfAnnotationReader
{
    private static readonly HashSet<string> Marks = ["✓", "✕", "○", "—", "●"];

    /// <summary>
    /// PdfEdit's own annotations that the page can show as editable items: text, marks, stamps, ink,
    /// highlights / underlines, sticky notes, shapes, lines, measurements, clouds, callouts and the
    /// insert / replace text marks, and placed signatures and pictures.
    /// </summary>
    public static List<OwnAnnotation> ReadOwn(string path)
    {
        var list = new List<OwnAnnotation>();
        try
        {
            using var doc = new PdfDocument(new PdfReader(path));
            var threads = ReadThreads(doc);
            for (int p = 1; p <= doc.GetNumberOfPages(); p++)
            {
                foreach (var a in doc.GetPage(p).GetAnnotations())
                {
                    var o = a.GetPdfObject();
                    if (PdfEdit.Services.PdfFormService.TrackedId(o) is not { } id) continue;
                    // Replies, review states and a Replace's caret belong to the comment they answer.
                    if (o.ContainsKey(PdfName.IRT)) continue;
                    string intent = o.GetAsName(new PdfName("IT"))?.GetValue() ?? "";
                    bool cloudy = o.ContainsKey(new PdfName("BE"));
                    var r = a.GetRectangle()?.ToRectangle();
                    if (r == null) continue;
                    string contents = a.GetContents()?.ToUnicodeString() ?? "";
                    string colour = Hex(o.GetAsArray(PdfName.C));
                    string author = a is PdfMarkupAnnotation m ? m.GetText()?.ToUnicodeString() ?? "" : "";
                    int rotate = o.GetAsNumber(PdfName.Rotate)?.IntValue() ?? 0;
                    double width = o.GetAsDictionary(PdfName.BS)?.GetAsNumber(PdfName.W)?.DoubleValue() ?? 2;
                    var own = new OwnAnnotation(id, p, "") with
                    {
                        Left = r.GetX(), Bottom = r.GetY(), Width = r.GetWidth(), Height = r.GetHeight(),
                        Colour = colour.Length > 0 ? colour : "#000000", Rotate = rotate, Author = author, LineWidth = width,
                        Opacity = o.GetAsNumber(PdfName.CA)?.DoubleValue() ?? 1,
                        Locked = (a.GetFlags() & PdfAnnotation.LOCKED) != 0,
                        Comment = Thread(id, author, o, threads),
                    };
                    var inner = Inner(o, r);
                    // Drawings and markup keep their comment note in /Contents (text, notes and stamps show theirs).
                    string sub = a.GetSubtype()?.GetValue() ?? "";
                    if (intent.Length == 0 && sub is "Ink" or "Highlight" or "Underline" or "StrikeOut" or "Squiggly" or "Square"
                        or "Circle" or "Line" or "Polygon" or "PolyLine")
                        own.Comment.Note = contents;
                    switch (a.GetSubtype()?.GetValue())
                    {
                        case "FreeText" when intent == "FreeTextCallout":
                            var cl = o.GetAsArray(PdfName.CL);
                            list.Add(own with
                            {
                                Kind = "Callout", Text = contents, Left = inner.L, Bottom = inner.B, Width = inner.W, Height = inner.H,
                                Tip = cl is { } c && c.Size() >= 2 ? new PointD(c.GetAsNumber(0).DoubleValue(), c.GetAsNumber(1).DoubleValue()) : null,
                            });
                            break;
                        case "FreeText" when intent.Length > 0:
                            continue;
                        case "FreeText":
                            var da = ParseDa(o.GetAsString(PdfName.DA)?.ToUnicodeString());
                            bool mark = Marks.Contains(contents.Trim());
                            list.Add(own with
                            {
                                Kind = mark ? "Mark" : "Text", Text = contents, FontSize = da.Size, Bold = da.Bold, Italic = da.Italic,
                                CharSpacing = da.Spacing, Colour = da.Colour ?? own.Colour,
                            });
                            break;
                        case "Stamp" when contents is "Signature" or "Picture":
                            // A placed signature or picture: its image comes back out of the stamp's appearance.
                            if (ImageOf(o) is not { } image) continue;
                            list.Add(own with { Kind = contents, Image = image });
                            break;
                        case "Stamp":
                            if (contents.Length == 0) continue;
                            var lines = contents.Split('\n', 2);
                            list.Add(own with { Kind = "Stamp", Text = lines[0], Subtitle = lines.Length > 1 ? lines[1] : null });
                            break;
                        case "Ink":
                            var ink = o.GetAsArray(PdfName.InkList);
                            if (ink == null || ink.Size() != 1 || ink.GetAsArray(0) is not { } pts || pts.Size() < 4) continue;
                            var points = new List<PointD>();
                            for (int k = 0; k + 1 < pts.Size(); k += 2)
                                points.Add(new PointD(pts.GetAsNumber(k).DoubleValue(), pts.GetAsNumber(k + 1).DoubleValue()));
                            list.Add(own with { Kind = "Ink", Points = points });
                            break;
                        case "StrikeOut" when intent == "StrikeOutTextEdit":
                            list.Add(own with { Kind = "Replace", Text = contents });
                            break;
                        case "Caret":
                            list.Add(own with { Kind = "Insert", Text = contents });
                            break;
                        case "Line":
                            var l = o.GetAsArray(PdfName.L);
                            if (l == null || l.Size() < 4) continue;
                            bool arrow = o.GetAsArray(PdfName.LE)?.GetAsName(1)?.GetValue() is "OpenArrow" or "ClosedArrow";
                            list.Add(own with
                            {
                                Kind = intent == "LineDimension" ? "Distance" : arrow ? "Arrow" : "Line", Unit = UnitOf(contents),
                                Points = [new PointD(l.GetAsNumber(0).DoubleValue(), l.GetAsNumber(1).DoubleValue()),
                                          new PointD(l.GetAsNumber(2).DoubleValue(), l.GetAsNumber(3).DoubleValue())],
                            });
                            break;
                        case "Polygon" or "PolyLine":
                            var vs = o.GetAsArray(PdfName.Vertices);
                            if (vs == null || vs.Size() < 4 || o.GetAsArray(PdfName.IC) != null) continue;
                            var verts = new List<PointD>();
                            for (int k = 0; k + 1 < vs.Size(); k += 2)
                                verts.Add(new PointD(vs.GetAsNumber(k).DoubleValue(), vs.GetAsNumber(k + 1).DoubleValue()));
                            bool closed = a.GetSubtype()!.GetValue() == "Polygon";
                            list.Add(own with
                            {
                                Kind = closed ? (intent == "PolygonDimension" ? "Area" : "Polygon") : (intent == "PolyLineDimension" ? "Perimeter" : "Polyline"),
                                Points = verts, Unit = UnitOf(contents),
                            });
                            break;
                        case "Square" when cloudy:
                            if (o.GetAsArray(PdfName.IC) != null) continue;
                            list.Add(own with { Kind = "Cloud", Left = inner.L, Bottom = inner.B, Width = inner.W, Height = inner.H });
                            break;
                        case "Highlight" or "Underline" or "StrikeOut" or "Squiggly":
                            list.Add(own with
                            {
                                Kind = "Highlight",
                                Colour = colour.Length > 0 ? colour : "#FFFF00",
                                Markup = a.GetSubtype()!.GetValue() switch
                                {
                                    "Underline" => HighlightKind.Underline,
                                    "StrikeOut" => HighlightKind.Strikethrough,
                                    "Squiggly" => HighlightKind.Squiggly,
                                    _ => HighlightKind.Highlight,
                                },
                            });
                            break;
                        case "Text":
                            list.Add(own with { Kind = "Note", Text = contents });
                            break;
                        case "Square" or "Circle":
                            if (o.GetAsArray(PdfName.IC) != null || intent.Length > 0) continue;   // filled toolbox shapes stay as drawn
                            list.Add(own with { Kind = a.GetSubtype()!.GetValue() == "Circle" ? "Ellipse" : "Rectangle" });
                            break;
                    }
                }
            }
        }
        catch { /* unreadable: nothing editable */ }
        return list;
    }

    /// <summary>Replies and review states, by the id of the comment they answer.</summary>
    private static Dictionary<string, List<PdfDictionary>> ReadThreads(PdfDocument doc)
    {
        var threads = new Dictionary<string, List<PdfDictionary>>();
        for (int p = 1; p <= doc.GetNumberOfPages(); p++)
            foreach (var a in doc.GetPage(p).GetAnnotations())
            {
                var o = a.GetPdfObject();
                if (o.GetAsDictionary(PdfName.IRT) is not { } parent || PdfEdit.Services.PdfFormService.TrackedId(parent) is not { } pid) continue;
                if (!PdfName.Text.Equals(o.GetAsName(PdfName.Subtype))) continue;   // a Replace's caret, not a reply
                (threads.TryGetValue(pid, out var l) ? l : threads[pid] = new()).Add(o);
            }
        return threads;
    }

    /// <summary>The comment's thread as PdfEdit keeps it: author, dates, note, replies, status and checkmark.</summary>
    private static CommentInfo Thread(string id, string author, PdfDictionary o, Dictionary<string, List<PdfDictionary>> threads)
    {
        DateTime? Date(PdfDictionary d, PdfName key)
        {
            var s = d.GetAsString(key)?.ToUnicodeString();
            if (s == null) return null;
            try { return PdfDate.Decode(s); } catch { return null; }
        }
        var c = new CommentInfo
        {
            Id = id, Author = author,
            Created = Date(o, PdfName.CreationDate) ?? Date(o, PdfName.M) ?? DateTime.Now,
            Modified = Date(o, PdfName.M) ?? DateTime.Now,
        };
        foreach (var r in threads.GetValueOrDefault(id) ?? [])
        {
            string model = r.GetAsString(new PdfName("StateModel"))?.ToUnicodeString() ?? "";
            string state = r.GetAsString(new PdfName("State"))?.ToUnicodeString() ?? "";
            if (model == "Review" && Enum.TryParse<CommentStatus>(state, out var status)) c.Status = status;
            else if (model == "Marked") c.Checked = state == "Marked";
            else c.Replies.Add(new CommentReply
            {
                Author = r.GetAsString(PdfName.T)?.ToUnicodeString() ?? "",
                Text = r.GetAsString(PdfName.Contents)?.ToUnicodeString() ?? "",
                Created = Date(r, PdfName.M) ?? DateTime.Now,
            });
        }
        return c;
    }

    /// <summary>
    /// The picture in a signature or picture stamp's appearance, as an image file: rebuilt as a PNG with
    /// its transparency (the PDF keeps the colours and the see-through mask apart), or the JPEG as stored.
    /// </summary>
    private static byte[]? ImageOf(PdfDictionary annot)
    {
        try
        {
            var ap = annot.GetAsDictionary(PdfName.AP)?.GetAsStream(PdfName.N);
            var xobjects = ap?.GetAsDictionary(PdfName.Resources)?.GetAsDictionary(PdfName.XObject);
            if (xobjects == null) return null;
            foreach (var key in xobjects.KeySet())
            {
                if (xobjects.GetAsStream(key) is not { } stream || !PdfName.Image.Equals(stream.GetAsName(PdfName.Subtype))) continue;
                return Decode(stream);
            }
        }
        catch { /* not an image we can read: the stamp stays part of the page */ }
        return null;
    }

    private static byte[]? Decode(PdfStream image)
    {
        var xobject = new iText.Kernel.Pdf.Xobject.PdfImageXObject(image);
        int w = (int)xobject.GetWidth(), h = (int)xobject.GetHeight();
        var filter = image.Get(PdfName.Filter);
        bool jpeg = PdfName.DCTDecode.Equals(filter) || filter is PdfArray fa && fa.Contains(PdfName.DCTDecode);
        int bpc = image.GetAsNumber(PdfName.BitsPerComponent)?.IntValue() ?? 8;
        int channels = Channels(image.Get(PdfName.ColorSpace));
        if (jpeg || channels == 0 || bpc != 8 || w <= 0 || h <= 0)
            return xobject.GetImageBytes(true);   // as stored (JPEG), or iText's PNG of other colour spaces

        byte[] px = image.GetBytes(true);
        if (px.Length < w * h * channels) return xobject.GetImageBytes(true);
        byte[]? alpha = null;
        if (image.GetAsStream(PdfName.SMask) is { } mask
            && mask.GetAsNumber(PdfName.Width)?.IntValue() == w && mask.GetAsNumber(PdfName.Height)?.IntValue() == h)
        {
            alpha = mask.GetBytes(true);
            if (alpha.Length < w * h) alpha = null;
        }
        var bgra = new byte[w * h * 4];
        for (int i = 0, n = w * h; i < n; i++)
        {
            byte r = px[i * channels], g = channels == 3 ? px[i * 3 + 1] : r, b = channels == 3 ? px[i * 3 + 2] : r;
            bgra[i * 4] = b; bgra[i * 4 + 1] = g; bgra[i * 4 + 2] = r;
            bgra[i * 4 + 3] = alpha?[i] ?? 255;
        }
        return PngEncoder.FromBgra(bgra, w, h, alpha: alpha != null);
    }

    /// <summary>
    /// Colour channels for the colour spaces a picture's pixels can be copied from as they are: device
    /// RGB and grey, and their calibrated and ICC forms (pictures with a colour profile — from Safari,
    /// macOS or a photo editor — are stored as CalRGB or ICCBased). 0 for anything else.
    /// </summary>
    private static int Channels(PdfObject? cs)
    {
        if (cs is PdfName name) return PdfName.DeviceRGB.Equals(name) ? 3 : PdfName.DeviceGray.Equals(name) ? 1 : 0;
        if (cs is not PdfArray array || array.Size() < 1) return 0;
        var family = array.GetAsName(0);
        if (PdfName.CalRGB.Equals(family)) return 3;
        if (PdfName.CalGray.Equals(family)) return 1;
        if (PdfName.ICCBased.Equals(family) && array.GetAsStream(1)?.GetAsNumber(PdfName.N)?.IntValue() is int n and (1 or 3)) return n;
        return 0;
    }

    /// <summary>The box inside an annotation's /Rect that /RD describes (a cloud's or callout's own box).</summary>
    private static (double L, double B, double W, double H) Inner(PdfDictionary o, iText.Kernel.Geom.Rectangle r)
    {
        var rd = o.GetAsArray(new PdfName("RD"));
        if (rd == null || rd.Size() < 4) return (r.GetX(), r.GetY(), r.GetWidth(), r.GetHeight());
        double dl = rd.GetAsNumber(0).DoubleValue(), db = rd.GetAsNumber(1).DoubleValue(),
               dr = rd.GetAsNumber(2).DoubleValue(), dt = rd.GetAsNumber(3).DoubleValue();
        return (r.GetX() + dl, r.GetY() + db, Math.Max(1, r.GetWidth() - dl - dr), Math.Max(1, r.GetHeight() - db - dt));
    }

    /// <summary>The unit at the end of a measurement's label ("2.35 in", "4.10 sq cm").</summary>
    private static string UnitOf(string label)
    {
        var last = label.Trim().Split(' ').LastOrDefault() ?? "";
        return last is "in" or "mm" or "cm" or "pt" ? last : "in";
    }

    /// <summary>Font size, bold / italic, character spacing and colour from a /DA string ("/Helv 12.0 Tf 1.50 Tc 0 0 0 rg").</summary>
    private static (double Size, bool Bold, bool Italic, double Spacing, string? Colour) ParseDa(string? da)
    {
        double size = 12, spacing = 0;
        bool bold = false, italic = false;
        string? colour = null;
        if (string.IsNullOrWhiteSpace(da)) return (size, bold, italic, spacing, colour);
        var t = da.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        double N(int i) => i >= 0 && i < t.Length && double.TryParse(t[i], System.Globalization.NumberStyles.Float, inv, out var v) ? v : double.NaN;
        for (int i = 0; i < t.Length; i++)
        {
            switch (t[i])
            {
                case "Tf":
                    if (!double.IsNaN(N(i - 1)) && N(i - 1) > 0) size = N(i - 1);
                    string font = i >= 2 ? t[i - 2] : "";
                    bold = font.Contains("Bold"); italic = font.Contains("Oblique") || font.Contains("Italic");
                    break;
                case "Tc":
                    if (!double.IsNaN(N(i - 1))) spacing = N(i - 1);
                    break;
                case "rg" when i >= 3 && !double.IsNaN(N(i - 3)) && !double.IsNaN(N(i - 2)) && !double.IsNaN(N(i - 1)):
                    int C(double v) => (int)Math.Round(Math.Clamp(v, 0, 1) * 255);
                    colour = $"#{C(N(i - 3)):X2}{C(N(i - 2)):X2}{C(N(i - 1)):X2}";
                    break;
                case "g" when i >= 1 && !double.IsNaN(N(i - 1)):
                    int G = (int)Math.Round(Math.Clamp(N(i - 1), 0, 1) * 255);
                    colour = $"#{G:X2}{G:X2}{G:X2}";
                    break;
            }
        }
        return (size, bold, italic, spacing, colour);
    }

    private static readonly HashSet<string> Skip = ["Widget", "Link", "Popup"];

    public static List<PdfAnnotationItem> Read(string path)
    {
        var list = new List<PdfAnnotationItem>();
        try
        {
            using var doc = new PdfDocument(new PdfReader(path));
            for (int p = 1; p <= doc.GetNumberOfPages(); p++)
            {
                int i = 0;
                foreach (var a in doc.GetPage(p).GetAnnotations())
                {
                    var kind = a.GetSubtype()?.GetValue() ?? "Annotation";
                    if (Skip.Contains(kind)) { i++; continue; }
                    string author = a is PdfMarkupAnnotation m ? m.GetText()?.ToUnicodeString() ?? "" : "";
                    string text = a.GetContents()?.ToUnicodeString() ?? "";
                    DateTime? modified = null;
                    var date = a.GetDate()?.ToUnicodeString();
                    if (date != null) try { modified = PdfDate.Decode(date); } catch { /* not a PDF date */ }
                    var r = a.GetRectangle()?.ToRectangle();
                    list.Add(new PdfAnnotationItem(p, i, Friendly(kind), author, text, modified)
                    {
                        Left = r?.GetX() ?? 0, Bottom = r?.GetY() ?? 0, Width = r?.GetWidth() ?? 0, Height = r?.GetHeight() ?? 0,
                        Colour = Hex(a.GetPdfObject().GetAsArray(PdfName.C)),
                        Id = PdfEdit.Services.PdfFormService.TrackedId(a.GetPdfObject()),
                    });
                    i++;
                }
            }
        }
        catch { /* unreadable: no comments */ }
        return list;
    }

    /// <summary>The PDF's comments as PdfEdit's annotation models (for XFDF export).</summary>
    public static (List<HighlightAnnotation> Highlights, List<StickyNoteAnnotation> Notes, List<FreeTextAnnotation> Texts, List<ShapeAnnotation> Shapes) ReadModels(string path)
    {
        var highlights = new List<HighlightAnnotation>();
        var notes = new List<StickyNoteAnnotation>();
        var texts = new List<FreeTextAnnotation>();
        var shapes = new List<ShapeAnnotation>();
        using var doc = new PdfDocument(new PdfReader(path));
        for (int p = 1; p <= doc.GetNumberOfPages(); p++)
        {
            foreach (var a in doc.GetPage(p).GetAnnotations())
            {
                var r = a.GetRectangle()?.ToRectangle();
                if (r == null) continue;
                string colour = Hex(a.GetPdfObject().GetAsArray(PdfName.C)), contents = a.GetContents()?.ToUnicodeString() ?? "";
                string author = a is PdfMarkupAnnotation m ? m.GetText()?.ToUnicodeString() ?? "" : "";
                var comment = new CommentInfo { Author = author, Note = contents };
                switch (a.GetSubtype()?.GetValue())
                {
                    case "Highlight" or "Underline" or "StrikeOut" or "Squiggly":
                        highlights.Add(new HighlightAnnotation
                        {
                            PageNumber = p, Left = r.GetX(), Bottom = r.GetY(), Width = r.GetWidth(), Height = r.GetHeight(),
                            Color = colour.Length > 0 ? colour : "#FFFF00",
                            Opacity = a.GetPdfObject().GetAsNumber(PdfName.CA)?.FloatValue() ?? 1f,
                            Kind = a.GetSubtype()!.GetValue() switch
                            {
                                "Underline" => HighlightKind.Underline,
                                "StrikeOut" => HighlightKind.Strikethrough,
                                "Squiggly" => HighlightKind.Squiggly,
                                _ => HighlightKind.Highlight,
                            },
                            Comment = comment,
                        });
                        break;
                    case "Text":
                        notes.Add(new StickyNoteAnnotation { PageNumber = p, Left = r.GetX(), Bottom = r.GetY(), Text = contents, Author = author, Comment = comment });
                        break;
                    case "FreeText":
                        texts.Add(new FreeTextAnnotation
                        {
                            PageNumber = p, Left = r.GetX(), Bottom = r.GetY(), Width = r.GetWidth(), Height = r.GetHeight(),
                            Text = contents, Comment = comment,
                        });
                        break;
                    case "Square" or "Circle" or "Line":
                        shapes.Add(new ShapeAnnotation
                        {
                            PageNumber = p, X1 = r.GetX(), Y1 = r.GetY(), X2 = r.GetX() + r.GetWidth(), Y2 = r.GetY() + r.GetHeight(),
                            Kind = a.GetSubtype()!.GetValue() switch { "Circle" => ShapeKind.Ellipse, "Line" => ShapeKind.Line, _ => ShapeKind.Rectangle },
                            StrokeColor = colour.Length > 0 ? colour : "#C62828", Comment = comment,
                        });
                        break;
                }
            }
        }
        return (highlights, notes, texts, shapes);
    }

    private static string Hex(PdfArray? c)
    {
        if (c == null || c.Size() < 3) return "";
        int Ch(int i) => (int)Math.Round(Math.Clamp(c.GetAsNumber(i)?.FloatValue() ?? 0, 0, 1) * 255);
        return $"#{Ch(0):X2}{Ch(1):X2}{Ch(2):X2}";
    }

    private static string Friendly(string kind) => kind switch
    {
        "Text" => "Sticky note",
        "FreeText" => "Text",
        "Highlight" => "Highlight",
        "Underline" => "Underline",
        "StrikeOut" => "Strikethrough",
        "Squiggly" => "Squiggly",
        "Ink" => "Drawing",
        "Square" => "Rectangle",
        "Circle" => "Ellipse",
        "Line" => "Line",
        "Stamp" => "Stamp",
        "Redact" => "Redaction",
        _ => kind,
    };
}
