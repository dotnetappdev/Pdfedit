using PdfEdit.Render.Drawing;

namespace PdfEdit.Render.Engine;

/// <summary>
/// Interprets a PDF page content stream and draws it on an <see cref="IDrawingSurface"/>.
/// Covers the common operator subset needed for business/form PDFs.
/// </summary>
internal sealed class PdfContentRenderer
{
    private readonly IDrawingSurface   _dc;
    private readonly IDrawingBackend   _backend;
    private readonly PdfParser         _parser;
    private readonly PdfDictionary?    _resources;
    private readonly double            _pageH;   // page height in pts (for Y flip)
    private readonly double            _scale;   // pts → pixels

    private readonly Stack<PdfGraphicsState> _gsStack = new();
    private PdfGraphicsState _gs = new();

    // Current path segments
    private PathData? _pathGeom;
    private Point2D _currentPoint;
    private Point2D _subpathStart;
    private bool  _pathOpen;

    // Clip requested by W / W* — takes effect after the next path-painting operator
    private bool _pendingClip;
    private bool _pendingClipEvenOdd;

    /// <summary>Skip painting text (it still advances, so layout is unaffected).</summary>
    public bool SkipText   { get; init; }
    /// <summary>Skip painting image XObjects and inline images.</summary>
    public bool SkipImages { get; init; }

    // Font cache
    private readonly Dictionary<string, PdfFont> _fontCache = new();
    private PdfFont? _currentFont;

    // ── ctor ─────────────────────────────────────────────────────────────────

    public PdfContentRenderer(IDrawingSurface dc, IDrawingBackend backend, PdfParser parser, PdfDictionary? resources,
                               double pageHeightPts, double scale)
    {
        _dc        = dc;
        _backend   = backend;
        _parser    = parser;
        _resources = resources;
        _pageH     = pageHeightPts;
        _scale     = scale;

        // PDF coords: origin bottom-left, Y up. Device: origin top-left, Y down.
        // Initial CTM flips Y and scales.
        _gs.Ctm = new Matrix2D(scale, 0, 0, -scale, 0, pageHeightPts * scale);
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

            int before = lex.Position;
            var obj = lex.ReadObject();
            if (obj == null)
            {
                // Stray delimiter such as ']' or '>' — skip it rather than stopping the page
                if (lex.Position == before) lex.Position++;
                continue;
            }

            // Bare keywords are operators; /Names (PdfName) are operands
            if (obj is PdfKeyword opName)
            {
                // One malformed operator must not abort the rest of the page
                try { ExecuteOp(opName.Value, operands, contentBytes, ref lex); }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[PdfRender] {opName.Value}: {ex.Message}"); }
                operands.Clear();
            }
            else
            {
                operands.Add(obj);
            }
        }

        // Unwind any clips left by unbalanced q/Q so the drawing surface stays balanced.
        for (int i = 0; i < _gs.ClipPushes; i++) _dc.Pop();
        _gs.ClipPushes = 0;
    }

    private void RestoreState()
    {
        if (_gsStack.Count == 0) return;
        var prev = _gsStack.Pop();
        for (int i = prev.ClipPushes; i < _gs.ClipPushes; i++) _dc.Pop();
        _gs = prev;
    }

    private void ApplyPendingClip(PathData? geom)
    {
        if (!_pendingClip) return;
        _pendingClip = false;
        if (geom == null) return;
        var clip = new PathData { EvenOdd = _pendingClipEvenOdd };
        clip.Figures.AddRange(geom.Figures);
        _dc.PushClip(clip);
        _gs.ClipPushes++;
    }

    // ── operator dispatch ─────────────────────────────────────────────────────

    private void ExecuteOp(string op, List<PdfObject> ops, byte[] bytes, ref PdfLexer lex)
    {
        switch (op)
        {
            // ── Graphics state ─────────────────────────────────────────────
            case "q":  _gsStack.Push(_gs); _gs = _gs.Clone(); break;
            case "Q":  RestoreState(); break;
            case "cm": SetCtm(ops); break;
            case "w":  _gs.LineWidth = GetReal(ops, 0, 1); break;
            case "J":  _gs.LineCap  = (LineCap)(int)GetReal(ops, 0, 0); break;
            case "j":  _gs.LineJoin = (LineJoin)(int)GetReal(ops, 0, 0); break;
            case "M":  _gs.MiterLimit = GetReal(ops, 0, 10); break;
            case "d":  SetDash(ops); break;
            case "ri": break; // rendering intent — ignore
            case "i":  break; // flatness — ignore
            case "gs": ApplyExtGState(ops); break;

            // ── Path construction ─────────────────────────────────────────
            case "m":  MoveTo(ops); break;
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
            case "n":  ApplyPendingClip(FinalizeGeom(false)); break;

            // ── Clipping (applied by the next painting operator) ──────────
            case "W":  _pendingClip = true; _pendingClipEvenOdd = false; break;
            case "W*": _pendingClip = true; _pendingClipEvenOdd = true;  break;

            // ── Color (stroke) ────────────────────────────────────────────
            case "CS": _gs.StrokeColorSpace = ResolveColorSpace(ops); _gs.StrokeColor = PdfColorSpace.InitialColor(_gs.StrokeColorSpace, _parser); break;
            case "SC": case "SCN": _gs.StrokeColor = ParseColor(ops, _gs.StrokeColorSpace, _gs.StrokeColor); break;
            case "G":  _gs.StrokeColorSpace = null; _gs.StrokeColor = Gray(GetReal(ops, 0, 0)); break;
            case "RG": _gs.StrokeColorSpace = null; _gs.StrokeColor = Rgb(ops, 0); break;
            case "K":  _gs.StrokeColorSpace = null; _gs.StrokeColor = Cmyk(ops, 0); break;

            // ── Color (fill) ──────────────────────────────────────────────
            case "cs": _gs.FillColorSpace = ResolveColorSpace(ops); _gs.FillColor = PdfColorSpace.InitialColor(_gs.FillColorSpace, _parser); break;
            case "sc": case "scn": _gs.FillColor = ParseColor(ops, _gs.FillColorSpace, _gs.FillColor); break;
            case "g":  _gs.FillColorSpace = null; _gs.FillColor = Gray(GetReal(ops, 0, 0)); break;
            case "rg": _gs.FillColorSpace = null; _gs.FillColor = Rgb(ops, 0); break;
            case "k":  _gs.FillColorSpace = null; _gs.FillColor = Cmyk(ops, 0); break;

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
        var m = new Matrix2D(a, b, c, d, e, f);
        _gs.Ctm = Matrix2D.Multiply(m, _gs.Ctm);
    }

    // ── Path construction ─────────────────────────────────────────────────────

    private void BeginPath()
    {
        _pathGeom = new PathData();
        _pathOpen = false;
    }

    private void EnsurePath()
    {
        if (_pathGeom == null) BeginPath();
    }

    private Point2D TransformPoint(double x, double y) => _gs.Ctm.Transform(new Point2D(x, y));

    private void MoveTo(List<PdfObject> ops)
    {
        EnsurePath();
        _currentPoint = TransformPoint(GetReal(ops, 0), GetReal(ops, 1));
        _subpathStart = _currentPoint;
        _pathGeom!.BeginFigure(_currentPoint, isFilled: true, isClosed: false);
        _pathOpen = true;
    }

    private void LineTo(List<PdfObject> ops)
    {
        EnsurePath();
        var p = TransformPoint(GetReal(ops, 0), GetReal(ops, 1));
        _pathGeom!.LineTo(p, isStroked: true);
        _currentPoint = p;
    }

    private void CurveTo(List<PdfObject> ops, bool v, bool y)
    {
        EnsurePath();
        // c: (x1,y1) (x2,y2) (x3,y3)
        // v: current_pt (x2,y2) (x3,y3)
        // y: (x1,y1) (x3,y3) (x3,y3)
        Point2D p1, p2, p3;
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
        _pathGeom!.BezierTo(p1, p2, p3, isStroked: true);
        _currentPoint = p3;
    }

    private void ClosePath()
    {
        if (_pathGeom != null && _pathOpen)
        {
            _pathGeom.LineTo(_subpathStart, isStroked: true);
            _currentPoint = _subpathStart;
            _pathOpen = false;
        }
    }

    private void DrawRect(List<PdfObject> ops)
    {
        if (ops.Count < 4) return;
        double x = GetReal(ops, 0), y = GetReal(ops, 1);
        double w = GetReal(ops, 2), h = GetReal(ops, 3);
        EnsurePath();
        var tl = TransformPoint(x,     y);
        var tr = TransformPoint(x + w, y);
        var br = TransformPoint(x + w, y + h);
        var bl = TransformPoint(x,     y + h);
        _pathGeom!.BeginFigure(tl, isFilled: true, isClosed: true);
        _pathGeom.LineTo(tr);
        _pathGeom.LineTo(br);
        _pathGeom.LineTo(bl);
        _pathOpen = true;
    }

    // ── Path painting ─────────────────────────────────────────────────────────

    private PathData? FinalizeGeom(bool evenOdd)
    {
        if (_pathGeom == null) return null;
        _pathGeom.EvenOdd = evenOdd;
        var g = _pathGeom;
        _pathGeom = null;
        _pathOpen = false;
        return g;
    }

    private StrokeStyle MakePen()
    {
        var color = _gs.StrokeColor.WithOpacity(_gs.AlphaStroke);
        // Line width is in user space: scale it by the CTM (geometric mean of the axes).
        double ctmScale = Math.Sqrt(Math.Abs(_gs.Ctm.Determinant));
        double width = _gs.LineWidth * ctmScale;
        if (width < 1) width = 1;   // PDF: width 0 means "thinnest visible line"
        double[]? dashes = null;
        double dashOffset = 0;
        if (_gs.DashArray != null && _gs.DashArray.Length > 0 && _gs.DashArray.Any(d => d > 0))
        {
            // PDF dash lengths are user-space units; the surface takes device pixels.
            dashes = _gs.DashArray.Select(d => d * ctmScale).ToArray();
            dashOffset = _gs.DashPhase * ctmScale;
        }
        return new StrokeStyle(color, width, _gs.LineCap, _gs.LineJoin, _gs.MiterLimit, dashes, dashOffset);
    }

    private RgbaColor MakeFillBrush() => _gs.FillColor.WithOpacity(_gs.AlphaFill);

    private void StrokePath()
    {
        var geom = FinalizeGeom(_gs.EvenOddFill);
        if (geom != null) _dc.DrawPath(geom, null, MakePen());
        ApplyPendingClip(geom);
    }

    private void FillPath(bool evenOdd)
    {
        var geom = FinalizeGeom(evenOdd);
        if (geom != null) _dc.DrawPath(geom, MakeFillBrush(), null);
        ApplyPendingClip(geom);
    }

    private void FillAndStroke(bool evenOdd)
    {
        var geom = FinalizeGeom(evenOdd);
        if (geom != null) _dc.DrawPath(geom, MakeFillBrush(), MakePen());
        ApplyPendingClip(geom);
    }

    // ── Color helpers ─────────────────────────────────────────────────────────

    private static RgbaColor Gray(double g) => PdfColorSpace.Gray(g);

    private static RgbaColor Rgb(List<PdfObject> ops, int startIdx) =>
        PdfColorSpace.Rgb(GetReal(ops, startIdx), GetReal(ops, startIdx + 1), GetReal(ops, startIdx + 2));

    private static RgbaColor Cmyk(List<PdfObject> ops, int startIdx) =>
        PdfColorSpace.Cmyk(GetReal(ops, startIdx), GetReal(ops, startIdx + 1),
                           GetReal(ops, startIdx + 2), GetReal(ops, startIdx + 3));

    /// <summary>Resolves a cs/CS operand: device names directly, others via /Resources /ColorSpace.</summary>
    private PdfObject? ResolveColorSpace(List<PdfObject> ops)
    {
        if (ops.Count == 0 || ops[0] is not PdfName n) return null;
        if (n.Value is "DeviceGray" or "DeviceRGB" or "DeviceCMYK" or "Pattern" or "G" or "RGB" or "CMYK")
            return n;
        var csDict = _resources == null ? null : _parser.ResolveDict(_resources.Get("ColorSpace"));
        var cs = csDict?.Get(n.Value);
        return cs != null ? _parser.Resolve(cs) : n;
    }

    private RgbaColor ParseColor(List<PdfObject> ops, PdfObject? colorSpace, RgbaColor current)
    {
        var comps = ops.Where(o => o is PdfInteger or PdfReal)
                       .Select(o => o is PdfReal r ? r.Value : ((PdfInteger)o).Value)
                       .ToArray();
        if (comps.Length == 0) return current;   // e.g. a pattern name only — keep current color

        if (colorSpace != null)
            return PdfColorSpace.ToColor(colorSpace, comps, _parser) ?? current;

        return comps.Length switch
        {
            1 => PdfColorSpace.Gray(comps[0]),
            3 => PdfColorSpace.Rgb(comps[0], comps[1], comps[2]),
            4 => PdfColorSpace.Cmyk(comps[0], comps[1], comps[2], comps[3]),
            _ => current
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
        _gs.Text.Tm  = Matrix2D.Identity;
        _gs.Text.Tlm = Matrix2D.Identity;
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
        var font = PdfFont.FromDictionary(dict, _parser, _backend.Fonts);
        _fontCache[name] = font;
        return font;
    }

    private void MoveText(List<PdfObject> ops, bool setLeading)
    {
        double tx = GetReal(ops, 0), ty = GetReal(ops, 1);
        if (setLeading) _gs.Text.Leading = -ty;
        var m = new Matrix2D(1, 0, 0, 1, tx, ty);
        _gs.Text.Tlm = Matrix2D.Multiply(m, _gs.Text.Tlm);
        _gs.Text.Tm  = _gs.Text.Tlm;
    }

    private void SetTextMatrix(List<PdfObject> ops)
    {
        if (ops.Count < 6) return;
        var m = new Matrix2D(GetReal(ops,0), GetReal(ops,1), GetReal(ops,2),
                           GetReal(ops,3), GetReal(ops,4), GetReal(ops,5));
        _gs.Text.Tm  = m;
        _gs.Text.Tlm = m;
    }

    private void NextLine()
    {
        var m = new Matrix2D(1, 0, 0, 1, 0, -_gs.Text.Leading);
        _gs.Text.Tlm = Matrix2D.Multiply(m, _gs.Text.Tlm);
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
            double hScale = _gs.Text.HorizScale / 100.0;
            if (item is PdfString ps) RenderTextBytes(ps.Bytes);
            else if (item is PdfInteger ki) AdjustTextPosition(-ki.Value * _gs.Text.FontSize / 1000.0 * hScale);
            else if (item is PdfReal   kr) AdjustTextPosition(-kr.Value  * _gs.Text.FontSize / 1000.0 * hScale);
        }
    }

    private void AdjustTextPosition(double tx)
    {
        var m = new Matrix2D(1, 0, 0, 1, tx, 0);
        _gs.Text.Tm = Matrix2D.Multiply(m, _gs.Text.Tm);
    }

    /// <summary>
    /// Text rendering matrix per PDF 9.4.4: [Tfs·Th 0 0 Tfs 0 Trise] × Tm × CTM.
    /// Maps glyph space (1 unit = 1 em, y up) to device pixels.
    /// </summary>
    private Matrix2D TextRenderMatrix()
    {
        var t = _gs.Text;
        var m = new Matrix2D(t.FontSize * t.HorizScale / 100.0, 0, 0, t.FontSize, 0, t.Rise);
        m = Matrix2D.Multiply(m, t.Tm);
        return Matrix2D.Multiply(m, _gs.Ctm);
    }

    private RgbaColor TextBrush() => _gs.FillColor.WithOpacity(_gs.AlphaFill);

    /// <summary>
    /// Pushes a transform so that drawing a glyph run of em size <paramref name="emSize"/>
    /// at (0,0) (device y-down) lands where the PDF text rendering matrix puts it.
    /// Returns the em size to use, or 0 when the text is degenerate / too small to see.
    /// </summary>
    private double PushGlyphTransform(out bool pushed)
    {
        pushed = false;
        var trm = TextRenderMatrix();
        // Vertical em size in pixels — used as the glyph run's rendering size so
        // hinting works at the real on-screen size.
        double emSize = Math.Sqrt(trm.M21 * trm.M21 + trm.M22 * trm.M22);
        if (emSize < 0.5 || double.IsNaN(emSize)) return 0;

        // Glyph space on the surface is y-down: flip, then apply trm, all scaled down by emSize.
        var g = Matrix2D.Multiply(new Matrix2D(1 / emSize, 0, 0, -1 / emSize, 0, 0), trm);
        _dc.PushTransform(g);
        pushed = true;
        return emSize;
    }

    private void RenderTextBytes(byte[] bytes)
    {
        if (_currentFont == null) return;
        var glyphs = _currentFont.Glyphs;
        double fontSize = _gs.Text.FontSize;
        double hScale   = _gs.Text.HorizScale / 100.0;
        bool visible    = !SkipText && _gs.Text.RenderMode is not 3 and not 7;
        var brush       = visible ? TextBrush() : default;

        foreach (byte b in bytes)
        {
            char ch = _currentFont.DecodeChar(b);

            if (visible && ch != ' ')
            {
                double emSize = PushGlyphTransform(out bool pushed);
                if (pushed)
                {
                    try
                    {
                        // Stretch the substitute glyph to the PDF's advance width so
                        // words keep their original length when the font is not embedded.
                        double xStretch = 1.0;
                        if (glyphs != null && glyphs.TryGetAdvanceWidth(ch, out double sysW))
                        {
                            double pdfW = _currentFont.GetCharWidth(b, 1.0);
                            xStretch = pdfW > 0 && sysW > 0 ? Math.Clamp(pdfW / sysW, 0.6, 1.4) : 1.0;
                        }
                        _dc.DrawGlyph(glyphs, _currentFont.FamilyName, ch, emSize, brush, xStretch);
                    }
                    catch { }
                    finally { _dc.Pop(); }
                }
            }

            // Advance Tm: tx = ((w0/1000)·Tfs + Tc + Tw) · Th
            double charW = _currentFont.GetCharWidth(b, fontSize);
            charW += _gs.Text.CharSpacing;
            if (b == 32) charW += _gs.Text.WordSpacing;
            AdjustTextPosition(charW * hScale);
        }
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
        if (SkipImages) return;
        var bitmap = DecodeImage(stm);
        if (bitmap != null) PaintImage(bitmap);
    }

    /// <summary>
    /// Paints a bitmap into the current unit square. PDF images map row 0 to the top
    /// of the unit square (user y = 1), so flip vertically before applying the CTM.
    /// </summary>
    private void PaintImage(RasterImage bitmap)
    {
        var m = Matrix2D.Multiply(new Matrix2D(1, 0, 0, -1, 0, 1), _gs.Ctm);
        _dc.PushTransform(m);
        if (_gs.AlphaFill < 1) _dc.PushOpacity(_gs.AlphaFill);
        _dc.DrawImage(bitmap, 0, 0, 1, 1);
        if (_gs.AlphaFill < 1) _dc.Pop();
        _dc.Pop();
    }

    private RasterImage? DecodeImage(PdfStream stm)
    {
        var d   = stm.Dict;
        int w   = (int)(_parser.Resolve(d.Get("Width")  ?? d.Get("W") ?? PdfNull.Instance) is PdfInteger wi ? wi.Value : 0);
        int h   = (int)(_parser.Resolve(d.Get("Height") ?? d.Get("H") ?? PdfNull.Instance) is PdfInteger hi ? hi.Value : 0);
        bool isMask = (d.Get("ImageMask") ?? d.Get("IM")) is PdfBoolean { Value: true };
        int bpc = isMask ? 1 : (int)d.GetInt("BitsPerComponent", d.GetInt("BPC", 8));
        var csObj = d.Get("ColorSpace") ?? d.Get("CS");
        if (csObj != null) csObj = ResolveImageColorSpace(csObj);

        byte[] data;
        try { data = PdfStreamFilter.Decode(stm); } catch { return null; }

        var filters = GetFilterNames(stm);
        if (filters.Contains("JPXDecode")) return null;   // JPEG 2000 not supported

        byte[]? bgra;
        if (filters.Contains("DCTDecode") || filters.Contains("DCT") || IsJpeg(data))
        {
            bool needsMask = d.Get("SMask") != null || d.Get("Mask") is PdfIndirectRef or PdfStream;
            RasterImage? jpeg;
            try { jpeg = _backend.DecodeImage(data, needPixels: needsMask); } catch { return null; }
            if (jpeg == null) return null;
            if (!needsMask || jpeg.Pixels == null) return jpeg;
            // Per-pixel alpha: BGRA pixels so the mask can be applied
            w = jpeg.Width; h = jpeg.Height;
            bgra = jpeg.Pixels;
        }
        else
        {
            if (w <= 0 || h <= 0 || bpc <= 0) return null;
            bgra = isMask
                ? DecodeStencil(data, w, h, d)
                : DecodeSamples(data, w, h, bpc, csObj ?? new PdfName("DeviceGray"), d.GetArray("Decode") ?? d.GetArray("D"));
            if (bgra == null) return null;
        }

        // Soft mask (alpha channel) or explicit stencil mask
        if (!isMask)
        {
            if (_parser.Resolve(d.Get("SMask") ?? PdfNull.Instance) is PdfStream smask)
                ApplyAlphaMask(bgra, w, h, smask, invert: false);
            else if (_parser.Resolve(d.Get("Mask") ?? PdfNull.Instance) is PdfStream mask)
                ApplyAlphaMask(bgra, w, h, mask, invert: true);
        }

        return bgra.Length >= w * h * 4 ? new RasterImage(w, h, bgra) : null;
    }

    private PdfObject ResolveImageColorSpace(PdfObject cs)
    {
        cs = _parser.Resolve(cs);
        // Named colour spaces may refer to /Resources /ColorSpace entries
        if (cs is PdfName n && n.Value is not ("DeviceGray" or "DeviceRGB" or "DeviceCMYK" or "G" or "RGB" or "CMYK" or "I" or "Indexed"))
        {
            var csDict = _resources == null ? null : _parser.ResolveDict(_resources.Get("ColorSpace"));
            if (csDict?.Get(n.Value) is { } res) return _parser.Resolve(res);
        }
        return cs;
    }

    private List<string> GetFilterNames(PdfStream stm)
    {
        var f = _parser.Resolve(stm.Dict.Get("Filter") ?? stm.Dict.Get("F") ?? PdfNull.Instance);
        if (f is PdfName n) return new List<string> { n.Value };
        if (f is PdfArray a) return a.Items.Select(i => (_parser.Resolve(i) as PdfName)?.Value ?? "").ToList();
        return new List<string>();
    }

    /// <summary>Reads one packed sample of 1/2/4/8/16 bits (16-bit returns the high byte).</summary>
    private static int ReadSample(byte[] data, long bitPos, int bits)
    {
        long i = bitPos >> 3;
        if (bits >= 8) return i < data.Length ? data[i] : 0;
        if (i >= data.Length) return 0;
        int shift = 8 - bits - (int)(bitPos & 7);
        return (data[i] >> shift) & ((1 << bits) - 1);
    }

    private byte[]? DecodeSamples(byte[] data, int w, int h, int bpc, PdfObject cs, PdfArray? decodeArr)
    {
        string family = PdfColorSpace.Family(cs, _parser);
        int comps = PdfColorSpace.ComponentCount(cs, _parser);
        bool indexed = family is "Indexed" or "I";
        int maxV = bpc == 16 ? 255 : (1 << bpc) - 1;   // 16-bit reads the high byte only
        long rowBits = ((long)w * comps * bpc + 7) / 8 * 8;

        // Decode ranges (defaults: [0 1] per component, [0 2^bpc-1] for Indexed, Lab uses its Range)
        var dec = new double[comps * 2];
        for (int c = 0; c < comps; c++) { dec[2 * c] = 0; dec[2 * c + 1] = indexed ? maxV : 1; }
        if (family == "Lab")
        {
            dec[0] = 0; dec[1] = 100;
            var rng = _parser.Resolve(cs) is PdfArray { Count: > 1 } la ? _parser.ResolveDict(la[1])?.GetArray("Range") : null;
            for (int c = 1; c < 3; c++)
            {
                dec[2 * c]     = rng != null && rng.Count >= 2 * c ? PdfColorSpace.GetNum(rng[2 * c - 2]) : -100;
                dec[2 * c + 1] = rng != null && rng.Count >= 2 * c ? PdfColorSpace.GetNum(rng[2 * c - 1]) : 100;
            }
        }
        if (decodeArr != null)
            for (int i = 0; i < Math.Min(dec.Length, decodeArr.Count); i++) dec[i] = PdfColorSpace.GetNum(decodeArr[i]);

        // Palette for indexed images; memo cache for the non-device spaces
        RgbaColor[]? palette = null;
        if (indexed)
        {
            palette = new RgbaColor[maxV + 1];
            for (int i = 0; i <= maxV; i++)
                palette[i] = PdfColorSpace.ToColor(cs, new double[] { i }, _parser) ?? RgbaColor.Black;
        }
        var cache = new Dictionary<long, RgbaColor>();
        bool fastDevice = family is "DeviceGray" or "G" or "CalGray" or "DeviceRGB" or "RGB" or "CalRGB" or "DeviceCMYK" or "CMYK"
                          || (family == "ICCBased" && comps is 1 or 3 or 4);

        var px = new byte[w * h * 4];
        var vals = new double[comps];
        for (int y = 0; y < h; y++)
        {
            long bit = y * rowBits;
            for (int x = 0; x < w; x++)
            {
                RgbaColor col;
                if (indexed)
                {
                    int raw = ReadSample(data, bit, bpc); bit += bpc;
                    int idx = (int)Math.Round(dec[0] + raw * (dec[1] - dec[0]) / maxV);
                    col = palette![Math.Clamp(idx, 0, palette.Length - 1)];
                }
                else
                {
                    long key = 0;
                    for (int c = 0; c < comps; c++)
                    {
                        int raw = ReadSample(data, bit, bpc); bit += bpc;
                        key = (key << 8) ^ raw;
                        vals[c] = dec[2 * c] + raw * (dec[2 * c + 1] - dec[2 * c]) / maxV;
                    }
                    if (fastDevice)
                    {
                        col = comps switch
                        {
                            1 => PdfColorSpace.Gray(vals[0]),
                            4 => PdfColorSpace.Cmyk(vals[0], vals[1], vals[2], vals[3]),
                            _ => PdfColorSpace.Rgb(vals[0], vals[1], vals[2]),
                        };
                    }
                    else if (!cache.TryGetValue(key, out col))
                    {
                        col = PdfColorSpace.ToColor(cs, (double[])vals.Clone(), _parser) ?? RgbaColor.Black;
                        if (cache.Count < 65536) cache[key] = col;
                    }
                }
                int o = (y * w + x) * 4;
                px[o] = col.B; px[o + 1] = col.G; px[o + 2] = col.R; px[o + 3] = col.A;
            }
        }
        return px;
    }

    /// <summary>ImageMask: 1-bit stencil painted with the current fill colour.</summary>
    private byte[] DecodeStencil(byte[] data, int w, int h, PdfDictionary d)
    {
        var decode = d.GetArray("Decode") ?? d.GetArray("D");
        bool paintOnOne = decode != null && decode.Count >= 1 && PdfColorSpace.GetNum(decode[0]) == 1;
        var c = _gs.FillColor;
        long rowBits = (w + 7) / 8 * 8;
        var px = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            int s = ReadSample(data, y * rowBits + x, 1);
            if ((s == 1) != paintOnOne) continue;
            int o = (y * w + x) * 4;
            px[o] = c.B; px[o + 1] = c.G; px[o + 2] = c.R; px[o + 3] = 255;
        }
        return px;
    }

    /// <summary>
    /// Applies an SMask (gray = alpha) or a Mask stencil (sample 1 = masked out when
    /// <paramref name="invert"/>) to straight-alpha BGRA pixels, resampling if sizes differ.
    /// </summary>
    private void ApplyAlphaMask(byte[] bgra, int w, int h, PdfStream mask, bool invert)
    {
        int mw = (int)mask.Dict.GetInt("Width"), mh = (int)mask.Dict.GetInt("Height");
        if (mw <= 0 || mh <= 0) return;
        bool isStencil = mask.Dict.Get("ImageMask") is PdfBoolean { Value: true };
        int bpc = isStencil ? 1 : (int)mask.Dict.GetInt("BitsPerComponent", 8);
        byte[] md;
        try { md = PdfStreamFilter.Decode(mask); } catch { return; }
        if (GetFilterNames(mask).Contains("DCTDecode"))
        {
            RasterImage? jpeg;
            try { jpeg = _backend.DecodeImage(md, needPixels: true); } catch { return; }
            if (jpeg?.Pixels == null) return;
            // The grey level of each pixel (Rec. 601 luma) is the mask value
            var px = jpeg.Pixels;
            md = new byte[jpeg.Width * jpeg.Height];
            for (int i = 0; i < md.Length; i++)
                md[i] = (byte)((px[i * 4 + 2] * 299 + px[i * 4 + 1] * 587 + px[i * 4] * 114 + 500) / 1000);
            mw = jpeg.Width; mh = jpeg.Height;
            bpc = 8;
        }
        int maxV = bpc == 16 ? 255 : (1 << bpc) - 1;
        long rowBits = ((long)mw * bpc + 7) / 8 * 8;
        var dec = mask.Dict.GetArray("Decode");
        bool inverted = dec != null && dec.Count >= 1 && PdfColorSpace.GetNum(dec[0]) == 1;

        for (int y = 0; y < h; y++)
        {
            int my = (int)((long)y * mh / h);
            for (int x = 0; x < w; x++)
            {
                int mx = (int)((long)x * mw / w);
                int s = ReadSample(md, my * rowBits + (long)mx * bpc, bpc);
                double a = (double)s / maxV;
                if (inverted) a = 1 - a;
                if (invert) a = 1 - a;
                int o = (y * w + x) * 4;
                bgra[o + 3] = (byte)Math.Round(bgra[o + 3] * a);
            }
        }
    }

    private static bool IsJpeg(byte[] data) =>
        data.Length >= 2 && data[0] == 0xFF && data[1] == 0xD8;

    private void DrawFormXObject(PdfStream stm)
    {
        if (_formDepth >= 12) return;   // guard against self-referencing forms

        // Recursively render the form XObject with a copy of the current graphics state
        byte[] content;
        try { content = PdfStreamFilter.Decode(stm); } catch { return; }
        var res = _parser.ResolveDict(stm.Dict.Get("Resources")) ?? _resources;

        var gs = _gs.Clone();
        gs.ClipPushes = 0;

        // Apply optional Matrix from the Form XObject
        var matrix = stm.Dict.GetArray("Matrix");
        if (matrix?.Count >= 6)
        {
            var m = new Matrix2D(
                GetArrReal(matrix, 0), GetArrReal(matrix, 1),
                GetArrReal(matrix, 2), GetArrReal(matrix, 3),
                GetArrReal(matrix, 4), GetArrReal(matrix, 5));
            gs.Ctm = Matrix2D.Multiply(m, gs.Ctm);
        }

        // Clip to the form's BBox
        bool clipped = false;
        var bbox = stm.Dict.GetArray("BBox");
        if (bbox?.Count >= 4)
        {
            double x0 = GetArrReal(bbox, 0), y0 = GetArrReal(bbox, 1), x1 = GetArrReal(bbox, 2), y1 = GetArrReal(bbox, 3);
            var geo = PathData.Rectangle(
                gs.Ctm.Transform(new Point2D(x0, y0)), gs.Ctm.Transform(new Point2D(x1, y0)),
                gs.Ctm.Transform(new Point2D(x1, y1)), gs.Ctm.Transform(new Point2D(x0, y1)));
            _dc.PushClip(geo);
            clipped = true;
        }

        var sub = new PdfContentRenderer(_dc, _backend, _parser, res, _pageH, _scale)
        {
            _formDepth = _formDepth + 1, SkipText = SkipText, SkipImages = SkipImages
        };
        sub._gs = gs;
        sub.Render(content);

        if (clipped) _dc.Pop();
    }

    private int _formDepth;

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

        // Read until "EI" delimited by whitespace on both sides
        int start = lex.Position;
        while (lex.Position + 1 < contentBytes.Length)
        {
            int p = lex.Position;
            if (contentBytes[p] == 'E' && contentBytes[p + 1] == 'I' &&
                (p == 0 || IsWsByte(contentBytes[p - 1])) &&
                (p + 2 >= contentBytes.Length || IsWsByte(contentBytes[p + 2])))
            { break; }
            lex.Position++;
        }
        int end = lex.Position;
        if (end > start && IsWsByte(contentBytes[end - 1])) end--;
        byte[] imgData = contentBytes[start..end];
        lex.Position = Math.Min(contentBytes.Length, lex.Position + 2); // skip EI

        // Expand the abbreviated inline-image keys and values
        var full = new PdfDictionary();
        foreach (var (k, v) in dict.Items)
        {
            string key = k switch
            {
                "W" => "Width", "H" => "Height", "BPC" => "BitsPerComponent", "CS" => "ColorSpace",
                "D" => "Decode", "DP" => "DecodeParms", "F" => "Filter", "IM" => "ImageMask", _ => k
            };
            full.Items[key] = v switch
            {
                PdfName n  => new PdfName(ExpandInlineName(n.Value)),
                PdfArray a => ExpandInlineArray(a),
                _          => v
            };
        }

        if (SkipImages) return;
        var bitmap = DecodeImage(new PdfStream(full, imgData));
        if (bitmap != null) PaintImage(bitmap);
    }

    private static bool IsWsByte(byte b) => b is 0 or 9 or 10 or 12 or 13 or 32;

    private static string ExpandInlineName(string v) => v switch
    {
        "G" => "DeviceGray", "RGB" => "DeviceRGB", "CMYK" => "DeviceCMYK", "I" => "Indexed",
        "AHx" => "ASCIIHexDecode", "A85" => "ASCII85Decode", "LZW" => "LZWDecode", "Fl" => "FlateDecode",
        "RL" => "RunLengthDecode", "CCF" => "CCITTFaxDecode", "DCT" => "DCTDecode", _ => v
    };

    private static PdfArray ExpandInlineArray(PdfArray a)
    {
        var r = new PdfArray();
        foreach (var i in a.Items) r.Items.Add(i is PdfName n ? new PdfName(ExpandInlineName(n.Value)) : i);
        return r;
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
