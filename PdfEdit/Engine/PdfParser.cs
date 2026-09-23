using System.Text;

namespace PdfEdit.Engine;

/// <summary>
/// Parses a PDF binary file into an object graph.
/// Supports traditional xref tables, cross-reference streams (PDF 1.5+),
/// and object streams (ObjStm).
/// </summary>
internal sealed class PdfParser
{
    private readonly byte[]      _data;
    private readonly PdfLexer    _lex;

    // objectNumber → byte offset in file (or -1 if in an object stream)
    private readonly Dictionary<int, long>               _xref      = new();
    // objectNumber → (streamObjNum, indexInStream)
    private readonly Dictionary<int, (int stream, int idx)> _xrefStm = new();
    // object cache
    private readonly Dictionary<int, PdfObject>          _cache     = new();
    // decoded object streams cache
    private readonly Dictionary<int, PdfObject[]>        _objStmCache = new();

    private PdfDictionary? _trailer;

    // ── entry point ──────────────────────────────────────────────────────────

    public static PdfParser Load(byte[] data)
    {
        var p = new PdfParser(data);
        p.ReadXref();
        return p;
    }

    private PdfParser(byte[] data)
    {
        _data = data;
        _lex  = new PdfLexer(data);
    }

    // ── xref reading ─────────────────────────────────────────────────────────

    private void ReadXref()
    {
        // Find startxref near the end of the file
        int startxrefPos = PdfLexer.SearchBackward(_data, "startxref");
        if (startxrefPos < 0) throw new InvalidDataException("PDF: startxref not found");
        _lex.Position = startxrefPos + 9;
        _lex.SkipWs();
        if (_lex.ReadObject() is not PdfInteger startOff)
            throw new InvalidDataException("PDF: invalid startxref");

        ReadXrefAt((int)startOff.Value);
    }

    private void ReadXrefAt(int offset)
    {
        _lex.Position = offset;
        _lex.SkipWs();

        // Check for xref stream (PDF 1.5+) or traditional table
        int savedPos = _lex.Position;
        var first = _lex.ReadObject();
        if (first is PdfInteger iObj)
        {
            // "N G obj << /Type /XRef ... >> stream" — cross-reference stream
            var gen = _lex.ReadObject();
            _lex.ReadObject(); // "obj" keyword
            _lex.SkipWs();
            if (_lex.ReadObject() is PdfDictionary xrefDict &&
                xrefDict.GetName("Type") == "XRef")
            {
                _lex.SkipWs();
                // read keyword "stream"
                _lex.ReadObject();
                int length = (int)xrefDict.GetInt("Length");
                byte[] raw = _lex.ReadStreamBytes(length);
                var stm = new PdfStream(xrefDict, raw);
                byte[] decoded = PdfStreamFilter.Decode(stm);
                ParseXrefStream(xrefDict, decoded);

                if (xrefDict.Get("Prev") is PdfInteger prev)
                    ReadXrefAt((int)prev.Value);
                return;
            }
            _lex.Position = savedPos;
        }

        // Traditional xref table
        _lex.Position = savedPos;
        ParseTraditionalXref();
    }

    private void ParseTraditionalXref()
    {
        string line = _lex.ReadLine().Trim();
        if (line != "xref") return;

        while (true)
        {
            _lex.SkipWs();
            int savedPos = _lex.Position;
            var tok = _lex.ReadObject();
            if (tok is PdfName kw && kw.Value == "trailer") break;
            if (tok is not PdfInteger firstObj) break;
            if (_lex.ReadObject() is not PdfInteger count) break;
            int first = (int)firstObj.Value, cnt = (int)count.Value;
            for (int i = 0; i < cnt; i++)
            {
                string entry = _lex.ReadLine().Trim();
                var parts = entry.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 3) continue;
                long off = long.Parse(parts[0]);
                char type = parts[2][0];
                int objNum = first + i;
                if (type == 'n' && !_xref.ContainsKey(objNum))
                    _xref[objNum] = off;
            }
        }

        // Read trailer dict
        _lex.SkipWs();
        if (_lex.ReadObject() is PdfDictionary td)
        {
            _trailer ??= td;
            if (td.Get("Prev") is PdfInteger prev)
                ReadXrefAt((int)prev.Value);
        }
    }

    private void ParseXrefStream(PdfDictionary dict, byte[] data)
    {
        _trailer ??= dict;

        var sizeObj  = dict.Get("Size");
        var indexObj = dict.GetArray("Index");
        var wArr     = dict.GetArray("W");
        if (wArr == null || wArr.Count < 3) return;

        int w0 = (int)((wArr[0] as PdfInteger)?.Value ?? 0);
        int w1 = (int)((wArr[1] as PdfInteger)?.Value ?? 0);
        int w2 = (int)((wArr[2] as PdfInteger)?.Value ?? 0);
        int entrySize = w0 + w1 + w2;
        if (entrySize == 0) return;

        // Parse Index pairs
        var sections = new List<(int First, int Count)>();
        if (indexObj != null && indexObj.Count >= 2)
        {
            for (int i = 0; i + 1 < indexObj.Count; i += 2)
            {
                int f = (int)((indexObj[i]     as PdfInteger)?.Value ?? 0);
                int c = (int)((indexObj[i + 1] as PdfInteger)?.Value ?? 0);
                sections.Add((f, c));
            }
        }
        else
        {
            int size = (int)((sizeObj as PdfInteger)?.Value ?? 0);
            sections.Add((0, size));
        }

        int pos = 0;
        foreach (var (first, count) in sections)
        {
            for (int i = 0; i < count; i++)
            {
                if (pos + entrySize > data.Length) break;
                long t = ReadInt(data, pos,      w0, 1);
                long f = ReadInt(data, pos + w0,  w1, 0);
                long g = ReadInt(data, pos + w0 + w1, w2, 0);
                pos += entrySize;
                int objNum = first + i;
                if (t == 1 && !_xref.ContainsKey(objNum))    _xref[objNum]    = f;
                else if (t == 2 && !_xrefStm.ContainsKey(objNum)) _xrefStm[objNum] = ((int)f, (int)g);
            }
        }
    }

    private static long ReadInt(byte[] data, int off, int w, long def)
    {
        if (w == 0) return def;
        long v = 0;
        for (int i = 0; i < w; i++) v = (v << 8) | data[off + i];
        return v;
    }

    // ── object resolution ────────────────────────────────────────────────────

    public PdfObject Resolve(PdfObject obj)
    {
        if (obj is not PdfIndirectRef r) return obj;
        return GetObject(r.ObjNum);
    }

    public PdfObject GetObject(int objNum)
    {
        if (_cache.TryGetValue(objNum, out var cached)) return cached;

        if (_xref.TryGetValue(objNum, out long offset))
        {
            var obj = ParseObjectAt((int)offset);
            _cache[objNum] = obj;
            return obj;
        }

        if (_xrefStm.TryGetValue(objNum, out var stmRef))
        {
            var objs = GetObjectStream(stmRef.stream);
            if (stmRef.idx < objs.Length)
            {
                _cache[objNum] = objs[stmRef.idx];
                return objs[stmRef.idx];
            }
        }

        return PdfNull.Instance;
    }

    private PdfObject ParseObjectAt(int offset)
    {
        var lex = new PdfLexer(_data, offset);
        lex.SkipWs();
        lex.ReadObject(); // obj number
        lex.ReadObject(); // generation
        lex.ReadObject(); // "obj" keyword

        var val = lex.ReadObject() ?? PdfNull.Instance;

        // Resolve as stream if dict is followed by 'stream'
        if (val is PdfDictionary dict)
        {
            lex.SkipWs();
            int savedPos = lex.Position;
            var next = lex.ReadObject();
            if (next is PdfName kw && kw.Value == "stream")
            {
                int length = (int)ResolveInt(dict, "Length");
                byte[] raw = lex.ReadStreamBytes(length);
                return new PdfStream(dict, raw);
            }
            lex.Position = savedPos;
        }

        return val;
    }

    private long ResolveInt(PdfDictionary dict, string key)
    {
        var v = dict.Get(key);
        if (v is PdfIndirectRef r) v = GetObject(r.ObjNum);
        return v switch { PdfInteger i => i.Value, PdfReal re => (long)re.Value, _ => 0 };
    }

    private PdfObject[] GetObjectStream(int stmObjNum)
    {
        if (_objStmCache.TryGetValue(stmObjNum, out var cached)) return cached;

        var stmObj = GetObject(stmObjNum);
        if (stmObj is not PdfStream stm) return Array.Empty<PdfObject>();

        byte[] data = PdfStreamFilter.Decode(stm);
        int n    = (int)stm.Dict.GetInt("N");
        int first = (int)stm.Dict.GetInt("First");

        // Read the N pairs (objNum offset) from header, then parse objects
        var headerLex = new PdfLexer(data);
        var offsets = new (int ObjNum, int Offset)[n];
        for (int i = 0; i < n; i++)
        {
            headerLex.SkipWs();
            int num = (int)((headerLex.ReadObject() as PdfInteger)?.Value ?? 0);
            int off = (int)((headerLex.ReadObject() as PdfInteger)?.Value ?? 0);
            offsets[i] = (num, first + off);
        }

        var result = new PdfObject[n];
        for (int i = 0; i < n; i++)
        {
            var objLex = new PdfLexer(data, offsets[i].Offset);
            result[i] = objLex.ReadObject() ?? PdfNull.Instance;
        }

        _objStmCache[stmObjNum] = result;
        return result;
    }

    // ── page tree ────────────────────────────────────────────────────────────

    public List<PdfDictionary> GetPages()
    {
        var root  = Resolve(_trailer?.Get("Root") ?? PdfNull.Instance) as PdfDictionary;
        var pages = Resolve(root?.Get("Pages") ?? PdfNull.Instance)   as PdfDictionary;
        if (pages == null) return new List<PdfDictionary>();

        var result = new List<PdfDictionary>();
        CollectPages(pages, result);
        return result;
    }

    private void CollectPages(PdfDictionary node, List<PdfDictionary> result)
    {
        string? type = node.GetName("Type");
        if (type == "Page") { result.Add(node); return; }

        var kids = node.GetArray("Kids");
        if (kids == null) return;
        foreach (var kid in kids.Items)
        {
            if (Resolve(kid) is PdfDictionary page)
                CollectPages(page, result);
        }
    }

    // ── content stream bytes ──────────────────────────────────────────────────

    /// <summary>Returns the concatenated decoded content stream bytes for a page.</summary>
    public byte[] GetPageContent(PdfDictionary page)
    {
        var contentObj = Resolve(page.Get("Contents") ?? PdfNull.Instance);
        if (contentObj is PdfStream single)
            return PdfStreamFilter.Decode(single);

        if (contentObj is PdfArray arr)
        {
            using var ms = new System.IO.MemoryStream();
            foreach (var item in arr.Items)
            {
                if (Resolve(item) is PdfStream stm)
                {
                    var decoded = PdfStreamFilter.Decode(stm);
                    ms.Write(decoded);
                    ms.WriteByte((byte)' ');
                }
            }
            return ms.ToArray();
        }
        return Array.Empty<byte>();
    }

    /// <summary>Resolves the resource dictionary for a page (may inherit from parent).</summary>
    public PdfDictionary? GetResources(PdfDictionary page)
    {
        var res = page.Get("Resources");
        if (res == null)
        {
            // Inherit from parent
            if (Resolve(page.Get("Parent") ?? PdfNull.Instance) is PdfDictionary parent)
                return GetResources(parent);
            return null;
        }
        return Resolve(res) as PdfDictionary;
    }

    /// <summary>Decodes and returns an XObject stream by resource name.</summary>
    public PdfStream? GetXObject(PdfDictionary resources, string name)
    {
        var xobjDict = Resolve(resources.Get("XObject") ?? PdfNull.Instance) as PdfDictionary;
        if (xobjDict == null) return null;
        return Resolve(xobjDict.Get(name) ?? PdfNull.Instance) as PdfStream;
    }

    /// <summary>Returns a font dictionary by resource name.</summary>
    public PdfDictionary? GetFont(PdfDictionary resources, string name)
    {
        var fontDict = Resolve(resources.Get("Font") ?? PdfNull.Instance) as PdfDictionary;
        if (fontDict == null) return null;
        return Resolve(fontDict.Get(name) ?? PdfNull.Instance) as PdfDictionary;
    }

    /// <summary>Resolve helper — walks an indirect ref if needed.</summary>
    public PdfDictionary? ResolveDict(PdfObject? obj)
    {
        if (obj == null) return null;
        return Resolve(obj) as PdfDictionary;
    }

    /// <summary>Returns the decoded byte array for a stream object reference.</summary>
    public byte[] DecodeStream(PdfObject? obj)
    {
        if (Resolve(obj ?? PdfNull.Instance) is PdfStream stm)
            return PdfStreamFilter.Decode(stm);
        return Array.Empty<byte>();
    }
}
