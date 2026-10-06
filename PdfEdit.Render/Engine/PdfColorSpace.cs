using System.Globalization;
using PdfEdit.Render.Drawing;

namespace PdfEdit.Render.Engine;

/// <summary>
/// Converts colour values in any PDF colour space (device, CIE-based, ICCBased,
/// Indexed, Separation, DeviceN) to WPF sRGB colours.
/// </summary>
internal static class PdfColorSpace
{
    // ── device colours ────────────────────────────────────────────────────────

    public static RgbaColor Gray(double g)
    {
        byte b = ToByte(g);
        return RgbaColor.FromRgb(b, b, b);
    }

    public static RgbaColor Rgb(double r, double g, double b) =>
        RgbaColor.FromRgb(ToByte(r), ToByte(g), ToByte(b));

    public static RgbaColor Cmyk(double c, double m, double y, double k) =>
        RgbaColor.FromRgb(
            ToByte((1 - Clamp01(c)) * (1 - Clamp01(k))),
            ToByte((1 - Clamp01(m)) * (1 - Clamp01(k))),
            ToByte((1 - Clamp01(y)) * (1 - Clamp01(k))));

    private static double Clamp01(double v) => v < 0 ? 0 : v > 1 ? 1 : v;
    private static byte ToByte(double v) => (byte)Math.Round(Clamp01(v) * 255);

    // ── generic conversion ────────────────────────────────────────────────────

    /// <summary>Returns the family name of a colour space object (e.g. "DeviceRGB", "ICCBased").</summary>
    public static string Family(PdfObject? cs, PdfParser parser)
    {
        cs = cs == null ? null : parser.Resolve(cs);
        return cs switch
        {
            PdfName n                                    => n.Value,
            PdfArray a when a.Count > 0 && a[0] is PdfName n => n.Value,
            _                                            => "DeviceGray"
        };
    }

    /// <summary>Number of colour components a value in this space has.</summary>
    public static int ComponentCount(PdfObject? cs, PdfParser parser)
    {
        cs = cs == null ? null : parser.Resolve(cs);
        switch (Family(cs, parser))
        {
            case "DeviceRGB": case "RGB": case "CalRGB": case "Lab": return 3;
            case "DeviceCMYK": case "CMYK": return 4;
            case "ICCBased":
                if (cs is PdfArray a && a.Count > 1 && parser.Resolve(a[1]) is PdfStream s)
                    return (int)s.Dict.GetInt("N", 3);
                return 3;
            case "DeviceN":
                return cs is PdfArray dn && dn.Count > 1 && parser.Resolve(dn[1]) is PdfArray names ? names.Count : 1;
            default: return 1;   // Gray, CalGray, Indexed, Separation
        }
    }

    /// <summary>Converts component values to a colour, or null for spaces we cannot paint (Pattern).</summary>
    public static RgbaColor? ToColor(PdfObject cs, double[] c, PdfParser parser, int depth = 0)
    {
        if (depth > 8) return null;
        cs = parser.Resolve(cs);
        string family = Family(cs, parser);
        var arr = cs as PdfArray;

        switch (family)
        {
            case "DeviceGray": case "G": case "CalGray":
                return Gray(At(c, 0));
            case "DeviceRGB": case "RGB": case "CalRGB":
                return Rgb(At(c, 0), At(c, 1), At(c, 2));
            case "DeviceCMYK": case "CMYK":
                return Cmyk(At(c, 0), At(c, 1), At(c, 2), At(c, 3));

            case "Lab":
                return LabToRgb(arr, c, parser);

            case "ICCBased":
            {
                // We don't apply ICC profiles; use the declared Alternate or the component count.
                if (arr != null && arr.Count > 1 && parser.Resolve(arr[1]) is PdfStream s)
                {
                    var alt = s.Dict.Get("Alternate");
                    if (alt != null) return ToColor(alt, c, parser, depth + 1);
                    return s.Dict.GetInt("N", 3) switch
                    {
                        1 => Gray(At(c, 0)),
                        4 => Cmyk(At(c, 0), At(c, 1), At(c, 2), At(c, 3)),
                        _ => Rgb(At(c, 0), At(c, 1), At(c, 2)),
                    };
                }
                return c.Length switch { 1 => Gray(c[0]), 4 => Cmyk(c[0], c[1], c[2], c[3]), _ => Rgb(At(c, 0), At(c, 1), At(c, 2)) };
            }

            case "Indexed": case "I":
            {
                if (arr == null || arr.Count < 4) return null;
                var baseCs = arr[1];
                int hival  = (int)GetNum(parser.Resolve(arr[2]));
                byte[] lut = LookupBytes(parser.Resolve(arr[3]));
                int n      = ComponentCount(baseCs, parser);
                int idx    = Math.Clamp((int)Math.Round(At(c, 0)), 0, Math.Max(0, hival));
                var comps  = new double[n];
                for (int i = 0; i < n; i++)
                {
                    int p = idx * n + i;
                    comps[i] = p < lut.Length ? lut[p] / 255.0 : 0;
                }
                // Lab bases store raw bytes mapped onto their Range; approximate with 0..100 / -128..127
                if (Family(baseCs, parser) == "Lab")
                    comps = new[] { comps[0] * 100, comps[1] * 255 - 128, comps[2] * 255 - 128 };
                return ToColor(baseCs, comps, parser, depth + 1);
            }

            case "Separation":
            case "DeviceN":
            {
                if (arr == null || arr.Count < 4) return Gray(1 - At(c, 0));
                if (family == "Separation" && arr[1] is PdfName sepName)
                {
                    if (sepName.Value == "None") return RgbaColor.Transparent;
                    if (sepName.Value == "All")  return Gray(1 - At(c, 0));
                }
                var alt = arr[2];
                var fn  = parser.Resolve(arr[3]);
                var outp = PdfFunction.Evaluate(fn, c, parser);
                if (outp == null)
                    return Gray(1 - At(c, 0));   // unknown tint transform: treat tint as ink coverage
                return ToColor(alt, outp, parser, depth + 1);
            }

            case "Pattern":
                // Uncoloured patterns carry their colour in the underlying space
                if (arr != null && arr.Count > 1 && c.Length > 0) return ToColor(arr[1], c, parser, depth + 1);
                return null;

            default:
                return c.Length switch { 1 => Gray(c[0]), 3 => Rgb(c[0], c[1], c[2]), 4 => Cmyk(c[0], c[1], c[2], c[3]), _ => null };
        }
    }

    /// <summary>The initial colour set by cs/CS (PDF spec 8.6.8).</summary>
    public static RgbaColor InitialColor(PdfObject? cs, PdfParser parser)
    {
        if (cs == null) return RgbaColor.Black;
        string fam = Family(cs, parser);
        double[] init = fam switch
        {
            "Separation"                  => new[] { 1.0 },
            "DeviceN"                     => Enumerable.Repeat(1.0, ComponentCount(cs, parser)).ToArray(),
            "DeviceCMYK" or "CMYK"        => new[] { 0.0, 0, 0, 1 },
            "Lab"                         => new[] { 0.0, 0, 0 },
            "Indexed" or "I"              => new[] { 0.0 },
            "Pattern"                     => Array.Empty<double>(),
            _                             => new double[ComponentCount(cs, parser)],
        };
        return ToColor(cs, init, parser) ?? RgbaColor.Black;
    }

    // ── CIE L*a*b* ────────────────────────────────────────────────────────────

    private static RgbaColor LabToRgb(PdfArray? arr, double[] c, PdfParser parser)
    {
        double xw = 0.9642, yw = 1.0, zw = 0.8249;   // D50 default
        if (arr != null && arr.Count > 1 && parser.ResolveDict(arr[1]) is { } d && d.GetArray("WhitePoint") is { Count: >= 3 } wp)
        {
            xw = GetNum(wp[0]); yw = GetNum(wp[1]); zw = GetNum(wp[2]);
        }

        double L = At(c, 0), a = At(c, 1), b = At(c, 2);
        double fy = (L + 16) / 116.0;
        double fx = fy + a / 500.0;
        double fz = fy - b / 200.0;
        static double G(double x) => x >= 6.0 / 29 ? x * x * x : 108.0 / 841 * (x - 4.0 / 29);
        double X = xw * G(fx), Y = yw * G(fy), Z = zw * G(fz);

        // XYZ (D50) → linear sRGB (Bradford-adapted to D65)
        double r =  3.1338561 * X - 1.6168667 * Y - 0.4906146 * Z;
        double g = -0.9787684 * X + 1.9161415 * Y + 0.0334540 * Z;
        double bl = 0.0719453 * X - 0.2289914 * Y + 1.4052427 * Z;
        return Rgb(Gamma(r), Gamma(g), Gamma(bl));
    }

    private static double Gamma(double v)
    {
        v = Clamp01(v);
        return v <= 0.0031308 ? 12.92 * v : 1.055 * Math.Pow(v, 1 / 2.4) - 0.055;
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private static double At(double[] c, int i) => i < c.Length ? c[i] : 0;

    internal static double GetNum(PdfObject? o) =>
        o switch { PdfReal r => r.Value, PdfInteger i => i.Value, _ => 0 };

    private static byte[] LookupBytes(PdfObject o) => o switch
    {
        PdfString s => s.Bytes,
        PdfStream st => PdfStreamFilter.Decode(st),
        _ => Array.Empty<byte>()
    };
}

/// <summary>Evaluates PDF functions (types 0, 2, 3 and 4) used by tint transforms.</summary>
internal static class PdfFunction
{
    public static double[]? Evaluate(PdfObject? fnObj, double[] input, PdfParser parser, int depth = 0)
    {
        if (fnObj == null || depth > 8) return null;
        fnObj = parser.Resolve(fnObj);

        // An array of functions: each yields one output from the same input
        if (fnObj is PdfArray fnArr)
        {
            var outs = new List<double>();
            foreach (var f in fnArr.Items)
            {
                var r = Evaluate(f, input, parser, depth + 1);
                if (r == null) return null;
                outs.AddRange(r);
            }
            return outs.ToArray();
        }

        PdfDictionary? dict = fnObj switch { PdfStream s => s.Dict, PdfDictionary d => d, _ => null };
        if (dict == null) return null;

        var domain = Nums(dict.GetArray("Domain"));
        var range  = Nums(dict.GetArray("Range"));
        var x = new double[input.Length];
        for (int i = 0; i < x.Length; i++)
            x[i] = domain.Length >= 2 * i + 2 ? Math.Clamp(input[i], domain[2 * i], domain[2 * i + 1]) : input[i];

        double[]? result = dict.GetInt("FunctionType", -1) switch
        {
            0 => fnObj is PdfStream s0 ? Sampled(s0, x, domain, parser) : null,
            2 => Exponential(dict, x),
            3 => Stitching(dict, x, domain, parser, depth),
            4 => fnObj is PdfStream s4 ? PostScript(s4, x) : null,
            _ => null
        };

        if (result != null && range.Length >= 2)
            for (int i = 0; i < result.Length && 2 * i + 1 < range.Length; i++)
                result[i] = Math.Clamp(result[i], range[2 * i], range[2 * i + 1]);
        return result;
    }

    private static double[] Nums(PdfArray? a) =>
        a == null ? Array.Empty<double>() : a.Items.Select(PdfColorSpace.GetNum).ToArray();

    // Type 2: C0 + x^N × (C1 − C0)
    private static double[] Exponential(PdfDictionary d, double[] x)
    {
        var c0 = Nums(d.GetArray("C0")); if (c0.Length == 0) c0 = new[] { 0.0 };
        var c1 = Nums(d.GetArray("C1")); if (c1.Length == 0) c1 = new[] { 1.0 };
        double n = d.GetReal("N", 1);
        double t = Math.Pow(x.Length > 0 ? x[0] : 0, n);
        var r = new double[Math.Min(c0.Length, c1.Length)];
        for (int i = 0; i < r.Length; i++) r[i] = c0[i] + t * (c1[i] - c0[i]);
        return r;
    }

    // Type 3: pick a sub-function by the Bounds, then remap into its Encode interval
    private static double[]? Stitching(PdfDictionary d, double[] x, double[] domain, PdfParser parser, int depth)
    {
        var fns = d.GetArray("Functions");
        if (fns == null || fns.Count == 0) return null;
        var bounds = Nums(d.GetArray("Bounds"));
        var encode = Nums(d.GetArray("Encode"));
        double v = x.Length > 0 ? x[0] : 0;
        double d0 = domain.Length >= 2 ? domain[0] : 0, d1 = domain.Length >= 2 ? domain[1] : 1;

        int k = 0;
        while (k < bounds.Length && v >= bounds[k]) k++;
        k = Math.Min(k, fns.Count - 1);
        double lo = k == 0 ? d0 : bounds[k - 1];
        double hi = k < bounds.Length ? bounds[k] : d1;
        double e0 = encode.Length > 2 * k ? encode[2 * k] : 0, e1 = encode.Length > 2 * k + 1 ? encode[2 * k + 1] : 1;
        double mapped = hi == lo ? e0 : e0 + (v - lo) * (e1 - e0) / (hi - lo);
        return Evaluate(fns[k], new[] { mapped }, parser, depth + 1);
    }

    // Type 0: sampled lookup (nearest sample; multi-input via linear indexing)
    private static double[]? Sampled(PdfStream s, double[] x, double[] domain, PdfParser parser)
    {
        var size   = Nums(s.Dict.GetArray("Size")).Select(v => (int)v).ToArray();
        int bps    = (int)s.Dict.GetInt("BitsPerSample", 8);
        var range  = Nums(s.Dict.GetArray("Range"));
        var encode = Nums(s.Dict.GetArray("Encode"));
        var decode = Nums(s.Dict.GetArray("Decode"));
        int m = x.Length, n = range.Length / 2;
        if (size.Length < m || n == 0) return null;
        byte[] data = PdfStreamFilter.Decode(s);

        long index = 0, stride = 1;
        for (int i = 0; i < m; i++)
        {
            double d0 = domain.Length > 2 * i ? domain[2 * i] : 0, d1 = domain.Length > 2 * i + 1 ? domain[2 * i + 1] : 1;
            double e0 = encode.Length > 2 * i ? encode[2 * i] : 0, e1 = encode.Length > 2 * i + 1 ? encode[2 * i + 1] : size[i] - 1;
            double e = d1 == d0 ? e0 : e0 + (x[i] - d0) * (e1 - e0) / (d1 - d0);
            int ei = Math.Clamp((int)Math.Round(e), 0, size[i] - 1);
            index += ei * stride;
            stride *= size[i];
        }

        var r = new double[n];
        double maxV = Math.Pow(2, bps) - 1;
        for (int j = 0; j < n; j++)
        {
            long bitPos = (index * n + j) * bps;
            long raw = 0;
            for (int b = 0; b < bps; b++)
            {
                long bp = bitPos + b;
                int byteIdx = (int)(bp >> 3);
                int bit = byteIdx < data.Length ? (data[byteIdx] >> (7 - (int)(bp & 7))) & 1 : 0;
                raw = (raw << 1) | (long)bit;
            }
            double r0 = decode.Length > 2 * j ? decode[2 * j] : range[2 * j];
            double r1 = decode.Length > 2 * j + 1 ? decode[2 * j + 1] : range[2 * j + 1];
            r[j] = r0 + raw * (r1 - r0) / maxV;
        }
        return r;
    }

    // Type 4: PostScript calculator
    private static double[]? PostScript(PdfStream s, double[] x)
    {
        string code = System.Text.Encoding.Latin1.GetString(PdfStreamFilter.Decode(s));
        int pos = 0;
        var program = ParseBlock(code, ref pos);
        if (program == null) return null;
        var stack = new List<double>(x);
        try { Execute(program, stack); } catch { return null; }
        return stack.ToArray();
    }

    private static List<object>? ParseBlock(string code, ref int pos)
    {
        // Skip to the opening brace
        while (pos < code.Length && code[pos] != '{') pos++;
        if (pos >= code.Length) return null;
        pos++;
        var items = new List<object>();
        while (pos < code.Length)
        {
            char ch = code[pos];
            if (char.IsWhiteSpace(ch)) { pos++; continue; }
            if (ch == '{') { var sub = ParseBlock(code, ref pos); if (sub != null) items.Add(sub); continue; }
            if (ch == '}') { pos++; return items; }
            int start = pos;
            while (pos < code.Length && !char.IsWhiteSpace(code[pos]) && code[pos] != '{' && code[pos] != '}') pos++;
            string tok = code[start..pos];
            if (double.TryParse(tok, NumberStyles.Float, CultureInfo.InvariantCulture, out double num)) items.Add(num);
            else items.Add(tok);
        }
        return items;
    }

    private static void Execute(List<object> program, List<double> st)
    {
        double Pop() { var v = st[^1]; st.RemoveAt(st.Count - 1); return v; }
        void Push(double v) => st.Add(v);
        static double B(bool b) => b ? 1 : 0;

        for (int i = 0; i < program.Count; i++)
        {
            var item = program[i];
            if (item is double d) { Push(d); continue; }
            if (item is List<object> block)
            {
                // "{...} if" or "{...} {...} ifelse"
                if (i + 1 < program.Count && program[i + 1] is "if")
                {
                    if (Pop() != 0) Execute(block, st);
                    i++;
                }
                else if (i + 2 < program.Count && program[i + 1] is List<object> elseBlock && program[i + 2] is "ifelse")
                {
                    Execute(Pop() != 0 ? block : elseBlock, st);
                    i += 2;
                }
                continue;
            }

            double a, b;
            switch ((string)item)
            {
                case "add": b = Pop(); a = Pop(); Push(a + b); break;
                case "sub": b = Pop(); a = Pop(); Push(a - b); break;
                case "mul": b = Pop(); a = Pop(); Push(a * b); break;
                case "div": b = Pop(); a = Pop(); Push(b == 0 ? 0 : a / b); break;
                case "idiv": b = Pop(); a = Pop(); Push(b == 0 ? 0 : Math.Truncate(a / b)); break;
                case "mod": b = Pop(); a = Pop(); Push(b == 0 ? 0 : a % b); break;
                case "neg": Push(-Pop()); break;
                case "abs": Push(Math.Abs(Pop())); break;
                case "ceiling": Push(Math.Ceiling(Pop())); break;
                case "floor": Push(Math.Floor(Pop())); break;
                case "round": Push(Math.Round(Pop(), MidpointRounding.AwayFromZero)); break;
                case "truncate": case "cvi": Push(Math.Truncate(Pop())); break;
                case "cvr": break;
                case "sqrt": Push(Math.Sqrt(Pop())); break;
                case "sin": Push(Math.Sin(Pop() * Math.PI / 180)); break;
                case "cos": Push(Math.Cos(Pop() * Math.PI / 180)); break;
                case "atan": b = Pop(); a = Pop(); { double ang = Math.Atan2(a, b) * 180 / Math.PI; Push(ang < 0 ? ang + 360 : ang); } break;
                case "exp": b = Pop(); a = Pop(); Push(Math.Pow(a, b)); break;
                case "ln": Push(Math.Log(Pop())); break;
                case "log": Push(Math.Log10(Pop())); break;
                case "dup": Push(st[^1]); break;
                case "exch": b = Pop(); a = Pop(); Push(b); Push(a); break;
                case "pop": Pop(); break;
                case "copy": { int n = (int)Pop(); var top = st.Skip(st.Count - n).ToList(); st.AddRange(top); } break;
                case "index": { int n = (int)Pop(); Push(st[st.Count - 1 - n]); } break;
                case "roll":
                {
                    int j = (int)Pop(), n = (int)Pop();
                    if (n <= 0) break;
                    var seg = st.GetRange(st.Count - n, n);
                    st.RemoveRange(st.Count - n, n);
                    j = ((j % n) + n) % n;
                    st.AddRange(seg.Skip(n - j).Concat(seg.Take(n - j)));
                    break;
                }
                case "eq": b = Pop(); a = Pop(); Push(B(a == b)); break;
                case "ne": b = Pop(); a = Pop(); Push(B(a != b)); break;
                case "gt": b = Pop(); a = Pop(); Push(B(a > b)); break;
                case "ge": b = Pop(); a = Pop(); Push(B(a >= b)); break;
                case "lt": b = Pop(); a = Pop(); Push(B(a < b)); break;
                case "le": b = Pop(); a = Pop(); Push(B(a <= b)); break;
                case "and": b = Pop(); a = Pop(); Push((long)a & (long)b); break;
                case "or":  b = Pop(); a = Pop(); Push((long)a | (long)b); break;
                case "xor": b = Pop(); a = Pop(); Push((long)a ^ (long)b); break;
                case "not": a = Pop(); Push(a == 0 ? 1 : a == 1 ? 0 : ~(long)a); break;
                case "bitshift": b = Pop(); a = Pop(); Push(b >= 0 ? (long)a << (int)b : (long)a >> (int)-b); break;
                case "true": Push(1); break;
                case "false": Push(0); break;
            }
        }
    }
}
