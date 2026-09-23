using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PdfEdit.Engine;

/// <summary>
/// Interprets a PDF page content stream and renders it to a WPF DrawingContext.
/// Covers the common operator subset needed for business/form PDFs.
/// </summary>
internal sealed class PdfContentRenderer
{
    private readonly DrawingContext    _dc;
    private readonly PdfParser         _parser;
    private readonly PdfDictionary?    _resources;
    private readonly double            _pageH;   // page height in pts (for Y flip)
    private readonly double            _scale;   // pts → pixels

    private readonly Stack<PdfGraphicsState> _gsStack = new();
    private PdfGraphicsState _gs = new();

    // Current path segments
    private StreamGeometry? _pathGeom;
    private StreamGeometryContext? _pathCtx;
    private Point _currentPoint;
    private bool  _pathOpen;

    // Font cache
    private readonly Dictionary<string, PdfFont> _fontCache = new();
    private PdfFont? _currentFont;

    // ── ctor ─────────────────────────────────────────────────────────────────

    public PdfContentRenderer(DrawingContext dc, PdfParser parser, PdfDictionary? resources,
                               double pageHeightPts, double scale)
    {
        _dc        = dc;
        _parser    = parser;
        _resources = resources;
        _pageH     = pageHeightPts;
        _scale     = scale;

        // PDF coords: origin bottom-left, Y up. WPF: origin top-left, Y down.
        // Initial CTM flips Y and scales.
        _gs.Ctm = new Matrix(scale, 0, 0, -scale, 0, pageHeightPts * scale);
    }

    // ── entry point ──────────────────────────────────────────────────────────

    public void Render(byte[] contentBytes)
    {
        var lex = new PdfLexer(contentBytes);
        var operands = new List<PdfObject>();

        while (!lex.AtEnd)
        {
            lex.SkipWs();
            if (lex.AtEnd) break;

            var obj = lex.ReadObject();
            if (obj == null) break;

            // A PdfName that is NOT preceded by '/' is a keyword/operator
            // We distinguish: PdfName = operator (starts with alphabetic/etc), not '/'
            // Actually our lexer wraps keywords as PdfName too — check if it's an operator
            if (obj is PdfName opName && IsOperator(opName.Value))
            {
                ExecuteOp(opName.Value, operands, contentBytes, ref lex);
                operands.Clear();
            }
            else
            {
                operands.Add(obj);
            }
        }
    }

    // Operators do NOT start with '/' in a content stream (those are names used as operands).
    // They are alphabetic strings like "BT", "q", "cm", "Tf", etc.
    // Our lexer returns keywords as PdfName with no '/' — distinguish them from
    // actual /Name operands: a content stream name operand starts with '/' but we strip it.
    // The lexer already reads '/' names as PdfName; keywords become PdfName too.
    // We treat a token as an operator when it's not a number, string, array, dict, or indirect ref.
    private static bool IsOperator(string v) =>
        v.Length > 0 && v[0] != '/' && !char.IsDigit(v[0]) && v[0] != '-' && v[0] != '.' && v[0] != '+';

    // ── operator dispatch ─────────────────────────────────────────────────────

    private void ExecuteOp(string op, List<PdfObject> ops, byte[] bytes, ref PdfLexer lex)
    {
        switch (op)
        {
            // ── Graphics state ─────────────────────────────────────────────
            case "q":  _gsStack.Push(_gs); _gs = _gs.Clone(); break;
            case "Q":  if (_gsStack.Count > 0) _gs = _gsStack.Pop(); break;
            case "cm": SetCtm(ops); break;
            case "w":  _gs.LineWidth = GetReal(ops, 0, 1); break;
            case "J":  _gs.LineCap  = (PenLineCap)(int)GetReal(ops, 0, 0); break;
            case "j":  _gs.LineJoin = (PenLineJoin)(int)GetReal(ops, 0, 0); break;
            case "M":  _gs.MiterLimit = GetReal(ops, 0, 10); break;
            case "d":  SetDash(ops); break;
            case "ri": break; // rendering intent — ignore
            case "i":  break; // flatness — ignore
            case "gs": ApplyExtGState(ops); break;

            // ── Path construction ─────────────────────────────────────────
            case "m":  BeginPath(); MoveTo(ops); break;
            case "l":  LineTo(ops); break;
            case "c":  CurveTo(ops, false, false); break;
            case "v":  CurveTo(ops, true,  false); break;
            case "y":  CurveTo(ops, false, true); break;
            case "h":  ClosePath(); break;
            case "re": DrawRect(ops); break;

            // ── Path painting ──────────────────────────────────────────────
            case "S":  StrokePath(); break;
            case "s":  ClosePath(); StrokePath(); break;
            case "f":  case "F": FillPath(false); break;
            case "f*": FillPath(true); break;
            case "B":  FillAndStroke(false); break;
            case "B*": FillAndStroke(true); break;
            case "b":  ClosePath(); FillAndStroke(false); break;
            case "b*": ClosePath(); FillAndStroke(true); break;
            case "n":  _pathGeom = null; _pathCtx = null; _pathOpen = false; break;

            // ── Clipping ───────────────────────────────────────────────────
            case "W":  case "W*": /* clipping — ignore for now */ _pathGeom = null; break;

            // ── Color (stroke) ────────────────────────────────────────────
            case "CS": break; // set stroke color space name — handled by SC/SCN
            case "SC": case "SCN": _gs.StrokeColor = ParseColor(ops, "stroke"); break;
            case "G":  _gs.StrokeColor = Gray(GetReal(ops, 0, 0)); break;
            case "RG": _gs.StrokeColor = Rgb(ops, 0); break;
            case "K":  _gs.StrokeColor = Cmyk(ops, 0); break;

            // ── Color (fill) ──────────────────────────────────────────────
            case "cs": break;
            case "sc": case "scn": _gs.FillColor = ParseColor(ops, "fill"); break;
            case "g":  _gs.FillColor = Gray(GetReal(ops, 0, 0)); break;
            case "rg": _gs.FillColor = Rgb(ops, 0); break;
            case "k":  _gs.FillColor = Cmyk(ops, 0); break;

            // ── Text ────────────────────────────────────────────────────────
            case "BT": BeginText(); break;
            case "ET": EndText(); break;
            case "Tf": SetFont(ops); break;
            case "Td": MoveText(ops, false); break;
            case "TD": MoveText(ops, true); break;
            case "Tm": SetTextMatrix(ops); break;
            case "T*": NextLine(); break;
            case "Tc": _gs.Text.CharSpacing = GetReal(ops, 0, 0); break;
            case "Tw": _gs.Text.WordSpacing = GetReal(ops, 0, 0); break;
            case "Tz": _gs.Text.HorizScale  = GetReal(ops, 0, 100); break;
            case "TL": _gs.Text.Leading     = GetReal(ops, 0, 0); break;
            case "Tr": _gs.Text.RenderMode  = (int)GetReal(ops, 0, 0); break;
            case "Ts": _gs.Text.Rise        = GetReal(ops, 0, 0); break;
            case "Tj": ShowString(ops); break;
            case "TJ": ShowStringArray(ops); break;
            case "'":  NextLine(); ShowString(ops); break;
            case "\"": _gs.Text.WordSpacing = GetReal(ops, 0, 0);
                        _gs.Text.CharSpacing = GetReal(ops, 1, 0);
                        NextLine(); ShowStringArray(ops.Skip(2).ToList()); break;

            // ── XObjects ─────────────────────────────────────────────────
            case "Do": DrawXObject(ops); break;

            // ── Inline images ──────────────────────────────────────────────
            case "BI": DrawInlineImage(bytes, ref lex); break;

            // ── Marked content (ignore content, just bookkeeping) ──────────
            case "BMC": case "BDC": case "EMC": case "MP": case "DP": break;

            // ── Compatibility ──────────────────────────────────────────────
            case "BX": case "EX": break;
        }
    }

    // ── CTM ──────────────────────────────────────────────────────────────────

    private void SetCtm(List<PdfObject> ops)
    {
        if (ops.Count < 6) return;
        double a = GetReal(ops, 0), b = GetReal(ops, 1),
               c = GetReal(ops, 2), d = GetReal(ops, 3),
               e = GetReal(ops, 4), f = GetReal(ops, 5);
        var m = new Matrix(a, b, c, d, e, f);
        _gs.Ctm = Matrix.Multiply(m, _gs.Ctm);
    }

    // ── Path construction ─────────────────────────────────────────────────────

    private void BeginPath()
    {
        if (_pathCtx != null) { try { _pathCtx.Close(); } catch { } _pathCtx = null; }
        _pathGeom = new StreamGeometry();
        _pathCtx  = _pathGeom.Open();
        _pathOpen = false;
    }

    private void EnsurePath()
    {
        if (_pathGeom == null) BeginPath();
    }

    private Point TransformPoint(double x, double y)
    {
        var p = _gs.Ctm.Transform(new Point(x, y));
        return p;
    }

    private void MoveTo(List<PdfObject> ops)
    {
        EnsurePath();
        _currentPoint = TransformPoint(GetReal(ops, 0), GetReal(ops, 1));
        _pathCtx!.BeginFigure(_currentPoint, isFilled: true, isClosed: false);
        _pathOpen = true;
    }

    private void LineTo(List<PdfObject> ops)
    {
        EnsurePath();
        var p = TransformPoint(GetReal(ops, 0), GetReal(ops, 1));
        _pathCtx!.LineTo(p, isStroked: true, isSmoothJoin: false);
        _currentPoint = p;
    }

    private void CurveTo(List<PdfObject> ops, bool v, bool y)
    {
        EnsurePath();
        // c: (x1,y1) (x2,y2) (x3,y3)
        // v: current_pt (x2,y2) (x3,y3)
        // y: (x1,y1) (x3,y3) (x3,y3)
        Point p1, p2, p3;
        if (v)
        {
            p1 = _currentPoint;
            p2 = TransformPoint(GetReal(ops, 0), GetReal(ops, 1));
            p3 = TransformPoint(GetReal(ops, 2), GetReal(ops, 3));
        }
        else if (y)
        {
            p1 = TransformPoint(GetReal(ops, 0), GetReal(ops, 1));
            p3 = TransformPoint(GetReal(ops, 2), GetReal(ops, 3));
            p2 = p3;
        }
        else
        {
            p1 = TransformPoint(GetReal(ops, 0), GetReal(ops, 1));
            p2 = TransformPoint(GetReal(ops, 2), GetReal(ops, 3));
            p3 = TransformPoint(GetReal(ops, 4), GetReal(ops, 5));
        }
        _pathCtx!.BezierTo(p1, p2, p3, isStroked: true, isSmoothJoin: false);
        _currentPoint = p3;
    }

    private void ClosePath()
    {
        // Close the current sub-path
        if (_pathCtx != null && _pathOpen)
            _pathCtx.LineTo(_currentPoint, isStroked: true, isSmoothJoin: false);
    }

    private void DrawRect(List<PdfObject> ops)
    {
        if (ops.Count < 4) return;
        double x = GetReal(ops, 0), y = GetReal(ops, 1);
        double w = GetReal(ops, 2), h = GetReal(ops, 3);
        BeginPath();
        var tl = TransformPoint(x,     y);
        var tr = TransformPoint(x + w, y);
        var br = TransformPoint(x + w, y + h);
        var bl = TransformPoint(x,     y + h);
        _pathCtx!.BeginFigure(tl, isFilled: true, isClosed: true);
        _pathCtx.LineTo(tr, true, false);
        _pathCtx.LineTo(br, true, false);
        _pathCtx.LineTo(bl, true, false);
        _pathOpen = true;
    }

    // ── Path painting ─────────────────────────────────────────────────────────

    private Geometry? FinalizeGeom(bool evenOdd)
    {
        if (_pathGeom == null) return null;
        try { _pathCtx?.Close(); } catch { }
        _pathCtx = null;
        _pathGeom.FillRule = evenOdd ? FillRule.EvenOdd : FillRule.Nonzero;
        var g = _pathGeom;
        _pathGeom = null;
        _pathOpen = false;
        return g;
    }

    private Pen MakePen()
    {
        var color = Color.FromArgb((byte)(_gs.AlphaStroke * _gs.StrokeColor.A),
            _gs.StrokeColor.R, _gs.StrokeColor.G, _gs.StrokeColor.B);
        var pen = new Pen(new SolidColorBrush(color), _gs.LineWidth * _scale);
        pen.StartLineCap = pen.EndLineCap = _gs.LineCap;
        pen.LineJoin = _gs.LineJoin;
        pen.MiterLimit = _gs.MiterLimit;
        if (_gs.DashArray != null && _gs.DashArray.Length > 0)
            pen.DashStyle = new DashStyle(_gs.DashArray.Select(d => d), _gs.DashPhase);
        pen.Freeze();
        return pen;
    }

    private Brush MakeFillBrush()
    {
        var color = Color.FromArgb((byte)(_gs.AlphaFill * _gs.FillColor.A),
            _gs.FillColor.R, _gs.FillColor.G, _gs.FillColor.B);
        var b = new SolidColorBrush(color);
        b.Freeze();
        return b;
    }

    private void StrokePath()
    {
        var geom = FinalizeGeom(_gs.EvenOddFill);
        if (geom != null) _dc.DrawGeometry(null, MakePen(), geom);
    }

    private void FillPath(bool evenOdd)
    {
        var geom = FinalizeGeom(evenOdd);
        if (geom != null) _dc.DrawGeometry(MakeFillBrush(), null, geom);
    }

    private void FillAndStroke(bool evenOdd)
    {
        var geom = FinalizeGeom(evenOdd);
        if (geom != null) _dc.DrawGeometry(MakeFillBrush(), MakePen(), geom);
    }

    // ── Color helpers ─────────────────────────────────────────────────────────

    private static Color Gray(double g)
    {
        byte b = (byte)(g * 255);
        return Color.FromRgb(b, b, b);
    }

    private static Color Rgb(List<PdfObject> ops, int startIdx)
    {
        return Color.FromRgb(
            (byte)(GetReal(ops, startIdx,     0) * 255),
            (byte)(GetReal(ops, startIdx + 1, 0) * 255),
            (byte)(GetReal(ops, startIdx + 2, 0) * 255));
    }

    private static Color Cmyk(List<PdfObject> ops, int startIdx)
    {
        double c = GetReal(ops, startIdx,     0);
        double m = GetReal(ops, startIdx + 1, 0);
        double y = GetReal(ops, startIdx + 2, 0);
        double k = GetReal(ops, startIdx + 3, 0);
        return Color.FromRgb(
            (byte)((1 - Math.Min(1, c + k)) * 255),
            (byte)((1 - Math.Min(1, m + k)) * 255),
            (byte)((1 - Math.Min(1, y + k)) * 255));
    }

    private Color ParseColor(List<PdfObject> ops, string mode)
    {
        return ops.Count switch
        {
            1 => Gray(GetReal(ops, 0)),
            3 => Rgb(ops, 0),
            4 => Cmyk(ops, 0),
            _ => mode == "stroke" ? _gs.StrokeColor : _gs.FillColor
        };
    }

    // ── Extended graphics state ────────────────────────────────────────────────

    private void SetDash(List<PdfObject> ops)
    {
        if (ops.Count < 2) return;
        if (ops[0] is PdfArray arr)
        {
            _gs.DashArray = arr.Items.Select(o => o is PdfReal r ? r.Value : o is PdfInteger i ? (double)i.Value : 0).ToArray();
            _gs.DashPhase = GetReal(ops, 1, 0);
        }
    }

    private void ApplyExtGState(List<PdfObject> ops)
    {
        if (ops.Count == 0 || _resources == null) return;
        string name = (ops[0] as PdfName)?.Value ?? string.Empty;
        var extGs = _parser.ResolveDict(_parser.ResolveDict(_resources.Get("ExtGState"))?.Get(name));
        if (extGs == null) return;
        double ca = extGs.GetReal("ca", -1);
        double CA = extGs.GetReal("CA", -1);
        if (ca >= 0) _gs.AlphaFill   = ca;
        if (CA >= 0) _gs.AlphaStroke = CA;
        double lw = extGs.GetReal("LW", -1);
        if (lw >= 0) _gs.LineWidth = lw;
    }

    // ── Text ──────────────────────────────────────────────────────────────────

    private void BeginText()
    {
        _gs.Text.Tm  = Matrix.Identity;
        _gs.Text.Tlm = Matrix.Identity;
    }

    private void EndText() { }

    private void SetFont(List<PdfObject> ops)
    {
        if (ops.Count < 2) return;
        string name = (ops[0] as PdfName)?.Value ?? "F1";
        double size = GetReal(ops, 1, 12);
        _gs.Text.FontName = name;
        _gs.Text.FontSize = size;
        _currentFont = ResolveFont(name);
    }

    private PdfFont? ResolveFont(string name)
    {
        if (_fontCache.TryGetValue(name, out var cached)) return cached;
        if (_resources == null) return null;
        var dict = _parser.GetFont(_resources, name);
        if (dict == null) return null;
        var font = PdfFont.FromDictionary(dict, _parser);
        _fontCache[name] = font;
        return font;
    }

    private void MoveText(List<PdfObject> ops, bool setLeading)
    {
        double tx = GetReal(ops, 0), ty = GetReal(ops, 1);
        if (setLeading) _gs.Text.Leading = -ty;
        var m = new Matrix(1, 0, 0, 1, tx, ty);
        _gs.Text.Tlm = Matrix.Multiply(m, _gs.Text.Tlm);
        _gs.Text.Tm  = _gs.Text.Tlm;
    }

    private void SetTextMatrix(List<PdfObject> ops)
    {
        if (ops.Count < 6) return;
        var m = new Matrix(GetReal(ops,0), GetReal(ops,1), GetReal(ops,2),
                           GetReal(ops,3), GetReal(ops,4), GetReal(ops,5));
        _gs.Text.Tm  = m;
        _gs.Text.Tlm = m;
    }

    private void NextLine()
    {
        var m = new Matrix(1, 0, 0, 1, 0, -_gs.Text.Leading);
        _gs.Text.Tlm = Matrix.Multiply(m, _gs.Text.Tlm);
        _gs.Text.Tm  = _gs.Text.Tlm;
    }

    private void ShowString(List<PdfObject> ops)
    {
        if (ops.Count == 0 || _currentFont == null) return;
        byte[] bytes = (ops[0] as PdfString)?.Bytes ?? Array.Empty<byte>();
        RenderTextBytes(bytes);
    }

    private void ShowStringArray(List<PdfObject> ops)
    {
        if (ops.Count == 0 || _currentFont == null) return;
        var arr = (ops[0] as PdfArray) ?? (ops.Count > 0 ? new PdfArray() : null);
        if (arr == null) { ShowString(ops); return; }

        foreach (var item in arr.Items)
        {
            if (item is PdfString ps) RenderTextBytes(ps.Bytes);
            else if (item is PdfInteger ki) AdjustTextPosition(-ki.Value * _gs.Text.FontSize / 1000.0);
            else if (item is PdfReal   kr) AdjustTextPosition(-kr.Value  * _gs.Text.FontSize / 1000.0);
        }
    }

    private void AdjustTextPosition(double tx)
    {
        var m = new Matrix(1, 0, 0, 1, tx, 0);
        _gs.Text.Tm = Matrix.Multiply(m, _gs.Text.Tm);
    }

    private void RenderTextBytes(byte[] bytes)
    {
        if (_currentFont?.GlyphTypeface == null) { RenderTextFallback(bytes); return; }

        var gt = _currentFont.GlyphTypeface;
        double fontSize = _gs.Text.FontSize;
        double hScale   = _gs.Text.HorizScale / 100.0;

        foreach (byte b in bytes)
        {
            char ch = _currentFont.DecodeChar(b);
            if (!gt.CharacterToGlyphMap.TryGetValue(ch, out ushort glyphIdx)) glyphIdx = 0;

            // Text rendering matrix = Tm × CTM, but we have a Y-flip in CTM
            // Text user-space point in PDF: (Tm.OffsetX, Tm.OffsetY)
            // Transform via CTM to screen space
            double tx = _gs.Text.Tm.OffsetX;
            double ty = _gs.Text.Tm.OffsetY + _gs.Text.Rise;
            var screenPt = _gs.Ctm.Transform(new Point(tx, ty));

            // The font size in screen pixels: fontSize * CTM scale
            double scaleX = Math.Sqrt(_gs.Ctm.M11 * _gs.Ctm.M11 + _gs.Ctm.M21 * _gs.Ctm.M21);
            double pixelSize = fontSize * scaleX;

            if (_gs.Text.RenderMode != 3 && pixelSize >= 1 && glyphIdx > 0)
            {
                // Build a single-glyph GlyphRun
                var origin = new Point(screenPt.X, screenPt.Y);
                try
                {
                    var glyphRun = new GlyphRun(
                        glyphTypeface:    gt,
                        bidiLevel:        0,
                        isSideways:       false,
                        renderingEmSize:  pixelSize,
                        pixelsPerDip:     1.0,
                        glyphIndices:     new[] { glyphIdx },
                        baselineOrigin:   origin,
                        advanceWidths:    new[] { gt.AdvanceWidths[glyphIdx] * pixelSize },
                        glyphOffsets:     null,
                        characters:       new[] { ch },
                        deviceFontName:   null,
                        clusterMap:       null,
                        caretStops:       null,
                        language:         System.Windows.Markup.XmlLanguage.GetLanguage("en-us"));

                    Color fillColor = _gs.FillColor;
                    var brush = new SolidColorBrush(fillColor); brush.Freeze();
                    _dc.DrawGlyphRun(brush, glyphRun);
                }
                catch { RenderTextCharFallback(ch, screenPt, pixelSize); }
            }

            // Advance Tm
            double charW = _currentFont.GetCharWidth(b, fontSize) * hScale;
            if (b == 32) charW += _gs.Text.WordSpacing * hScale;
            charW += _gs.Text.CharSpacing * hScale;
            AdjustTextPosition(charW);
        }
    }

    private void RenderTextFallback(byte[] bytes)
    {
        if (_currentFont == null) return;
        double fontSize = _gs.Text.FontSize;
        double hScale   = _gs.Text.HorizScale / 100.0;
        double scaleX   = Math.Sqrt(_gs.Ctm.M11 * _gs.Ctm.M11 + _gs.Ctm.M21 * _gs.Ctm.M21);
        double pixelSize = fontSize * scaleX;

        var sb = new System.Text.StringBuilder();
        foreach (byte b in bytes) sb.Append(_currentFont.DecodeChar(b));

        double tx = _gs.Text.Tm.OffsetX;
        double ty = _gs.Text.Tm.OffsetY + _gs.Text.Rise;
        var screenPt = _gs.Ctm.Transform(new Point(tx, ty));

        var ft = new System.Windows.Media.FormattedText(
            sb.ToString(),
            System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface(_currentFont.WpfFamilyName),
            Math.Max(1, pixelSize),
            new SolidColorBrush(_gs.FillColor),
            1.0);
        _dc.DrawText(ft, new Point(screenPt.X, screenPt.Y - pixelSize));

        // Advance text position by full string width estimate
        double totalW = bytes.Sum(b =>
        {
            double w = _currentFont.GetCharWidth(b, fontSize) * hScale;
            if (b == 32) w += _gs.Text.WordSpacing * hScale;
            return w + _gs.Text.CharSpacing * hScale;
        });
        AdjustTextPosition(totalW);
    }

    private void RenderTextCharFallback(char ch, Point screenPt, double pixelSize)
    {
        if (_currentFont == null) return;
        var ft = new System.Windows.Media.FormattedText(
            ch.ToString(),
            System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface(_currentFont.WpfFamilyName),
            Math.Max(1, pixelSize),
            new SolidColorBrush(_gs.FillColor),
            1.0);
        _dc.DrawText(ft, new Point(screenPt.X, screenPt.Y - pixelSize));
    }

    // ── XObjects ──────────────────────────────────────────────────────────────

    private void DrawXObject(List<PdfObject> ops)
    {
        if (ops.Count == 0 || _resources == null) return;
        string name = (ops[0] as PdfName)?.Value ?? string.Empty;
        var stm = _parser.GetXObject(_resources, name);
        if (stm == null) return;

        string subtype = stm.Dict.GetName("Subtype") ?? string.Empty;
        if (subtype == "Image")    DrawImageXObject(stm);
        else if (subtype == "Form") DrawFormXObject(stm);
    }

    private void DrawImageXObject(PdfStream stm)
    {
        int w    = (int)stm.Dict.GetInt("Width",  stm.Dict.GetInt("W"));
        int h    = (int)stm.Dict.GetInt("Height", stm.Dict.GetInt("H"));
        int bpc  = (int)stm.Dict.GetInt("BitsPerComponent", stm.Dict.GetInt("BPC", 8));
        string cs = stm.Dict.GetName("ColorSpace") ?? stm.Dict.GetName("CS") ?? "DeviceRGB";
        string filterName = GetFilterName(stm);

        BitmapSource? bitmap = null;
        byte[] raw = PdfStreamFilter.Decode(stm);

        if (filterName is "DCTDecode" or "DCT" || IsJpeg(stm.RawData))
        {
            // JPEG — feed the original compressed bytes directly to BitmapImage
            bitmap = LoadJpegBitmap(stm.RawData);
        }
        else
        {
            bitmap = RawToBitmap(raw, w, h, bpc, cs);
        }

        if (bitmap == null) return;

        // The image occupies a unit square [0,0]-[1,1] in user space,
        // stretched by the CTM.  We draw at the CTM-transformed origin.
        // CTM column vectors give the four corners.
        var origin  = _gs.Ctm.Transform(new Point(0, 0));
        var xAxis   = _gs.Ctm.Transform(new Point(1, 0));
        var yAxis   = _gs.Ctm.Transform(new Point(0, 1));

        double imgW = (xAxis - origin).Length;
        double imgH = (yAxis - origin).Length;

        if (imgW < 1 || imgH < 1) return;

        _dc.PushTransform(new MatrixTransform(_gs.Ctm));
        _dc.DrawImage(bitmap, new Rect(0, 0, 1, 1));
        _dc.Pop();
    }

    private static string GetFilterName(PdfStream stm)
    {
        var f = stm.Dict.Get("Filter") ?? stm.Dict.Get("F");
        if (f is PdfName n) return n.Value;
        if (f is PdfArray a && a.Count > 0 && a[0] is PdfName fn) return fn.Value;
        return string.Empty;
    }

    private static bool IsJpeg(byte[] data) =>
        data.Length >= 2 && data[0] == 0xFF && data[1] == 0xD8;

    private static BitmapSource? LoadJpegBitmap(byte[] jpegBytes)
    {
        try
        {
            using var ms = new MemoryStream(jpegBytes);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.StreamSource = ms;
            bmp.CacheOption  = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch { return null; }
    }

    private static BitmapSource? RawToBitmap(byte[] data, int w, int h, int bpc, string cs)
    {
        if (w <= 0 || h <= 0) return null;
        try
        {
            int components = cs switch { "DeviceRGB" or "RGB" => 3, "DeviceCMYK" or "CMYK" => 4, _ => 1 };
            int stride = w * 4; // BGRA32 output
            var pixels  = new byte[stride * h];

            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int src = (y * w + x) * components * bpc / 8;
                int dst = (y * w + x) * 4;
                if (dst + 3 >= pixels.Length) break;

                byte r = 0, g = 0, b = 0, a = 255;
                if (components == 3 && src + 2 < data.Length)
                {
                    r = data[src]; g = data[src + 1]; b = data[src + 2];
                }
                else if (components == 4 && src + 3 < data.Length)
                {
                    double dc = data[src] / 255.0, dm = data[src+1] / 255.0,
                           dy = data[src+2] / 255.0, dk = data[src+3] / 255.0;
                    r = (byte)((1 - Math.Min(1, dc + dk)) * 255);
                    g = (byte)((1 - Math.Min(1, dm + dk)) * 255);
                    b = (byte)((1 - Math.Min(1, dy + dk)) * 255);
                }
                else if (components == 1 && src < data.Length)
                {
                    r = g = b = data[src];
                }
                pixels[dst] = b; pixels[dst+1] = g; pixels[dst+2] = r; pixels[dst+3] = a;
            }

            var wb = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgra32, null);
            wb.WritePixels(new Int32Rect(0, 0, w, h), pixels, stride, 0);
            wb.Freeze();
            return wb;
        }
        catch { return null; }
    }

    private void DrawFormXObject(PdfStream stm)
    {
        // Recursively render the form XObject in its own graphics state
        byte[] content = PdfStreamFilter.Decode(stm);
        var res = _parser.ResolveDict(stm.Dict.Get("Resources")) ?? _resources;

        _gsStack.Push(_gs);
        _gs = _gs.Clone();

        // Apply optional Matrix from the Form XObject
        var matrix = stm.Dict.GetArray("Matrix");
        if (matrix?.Count >= 6)
        {
            var m = new Matrix(
                GetArrReal(matrix, 0), GetArrReal(matrix, 1),
                GetArrReal(matrix, 2), GetArrReal(matrix, 3),
                GetArrReal(matrix, 4), GetArrReal(matrix, 5));
            _gs.Ctm = Matrix.Multiply(m, _gs.Ctm);
        }

        var sub = new PdfContentRenderer(_dc, _parser, res, _pageH, _scale);
        sub._gsStack.Clear();
        // Copy our current CTM into the sub-renderer's initial CTM
        sub._gs.Ctm = _gs.Ctm;
        // Re-render with sub-renderer's own state
        var subContent = new PdfContentRenderer(_dc, _parser, res, _pageH, _scale);
        subContent._gs.Ctm = _gs.Ctm;
        subContent.Render(content);

        _gs = _gsStack.Pop();
    }

    // ── Inline images ─────────────────────────────────────────────────────────

    private void DrawInlineImage(byte[] contentBytes, ref PdfLexer lex)
    {
        // BI already consumed. Read image parameters until ID, then raw data until EI.
        var dict = new PdfDictionary();
        while (!lex.AtEnd)
        {
            lex.SkipWs();
            var key = lex.ReadObject();
            if (key is PdfName kn && kn.Value == "ID") break;
            if (key is not PdfName keyName) continue;
            var val = lex.ReadObject() ?? PdfNull.Instance;
            dict.Items[keyName.Value] = val;
        }
        // skip one byte (space after ID)
        if (!lex.AtEnd) lex.Position++;

        // Read until EI
        int start = lex.Position;
        while (lex.Position + 2 < contentBytes.Length)
        {
            if (contentBytes[lex.Position]   == 'E' &&
                contentBytes[lex.Position+1] == 'I' &&
                (lex.Position == 0 || contentBytes[lex.Position-1] == '\n' || contentBytes[lex.Position-1] == '\r' || contentBytes[lex.Position-1] == ' '))
            { break; }
            lex.Position++;
        }
        byte[] imgData = contentBytes[start..lex.Position];
        lex.Position += 2; // skip EI

        int w   = (int)(dict.GetInt("W", dict.GetInt("Width",  1)));
        int h   = (int)(dict.GetInt("H", dict.GetInt("Height", 1)));
        int bpc = (int)(dict.GetInt("BPC", dict.GetInt("BitsPerComponent", 8)));
        string cs = dict.GetName("CS") ?? dict.GetName("ColorSpace") ?? "DeviceRGB";

        var stm = new PdfStream(dict, imgData);
        byte[] decoded = PdfStreamFilter.Decode(stm);
        var bitmap = IsJpeg(imgData) ? LoadJpegBitmap(imgData) : RawToBitmap(decoded, w, h, bpc, cs);
        if (bitmap != null)
        {
            _dc.PushTransform(new MatrixTransform(_gs.Ctm));
            _dc.DrawImage(bitmap, new Rect(0, 0, 1, 1));
            _dc.Pop();
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static double GetReal(List<PdfObject> ops, int idx, double def = 0.0)
    {
        if (idx >= ops.Count) return def;
        return ops[idx] switch { PdfReal r => r.Value, PdfInteger i => i.Value, _ => def };
    }

    private static double GetArrReal(PdfArray arr, int idx)
        => arr[idx] switch { PdfReal r => r.Value, PdfInteger i => i.Value, _ => 0.0 };
}
