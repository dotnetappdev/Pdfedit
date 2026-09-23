namespace PdfEdit.Engine;

internal abstract class PdfObject { }

internal sealed class PdfNull : PdfObject
{
    public static readonly PdfNull Instance = new();
}

internal sealed class PdfBoolean : PdfObject
{
    public bool Value { get; }
    public PdfBoolean(bool v) => Value = v;
    public static readonly PdfBoolean True  = new(true);
    public static readonly PdfBoolean False = new(false);
}

internal sealed class PdfInteger : PdfObject
{
    public long Value { get; }
    public PdfInteger(long v) => Value = v;
}

internal sealed class PdfReal : PdfObject
{
    public double Value { get; }
    public PdfReal(double v) => Value = v;
}

internal sealed class PdfString : PdfObject
{
    public byte[] Bytes { get; }
    public PdfString(byte[] bytes) => Bytes = bytes;
    public string ToLatin1() => System.Text.Encoding.Latin1.GetString(Bytes);
    public override string ToString() => ToLatin1();
}

internal sealed class PdfName : PdfObject
{
    public string Value { get; }
    public PdfName(string v) => Value = v;
    public override string ToString() => "/" + Value;
    public override bool Equals(object? obj) => obj is PdfName n && n.Value == Value;
    public override int GetHashCode() => Value.GetHashCode();
}

internal sealed class PdfArray : PdfObject
{
    public List<PdfObject> Items { get; } = new();
    public int Count => Items.Count;
    public PdfObject this[int i] => Items[i];
}

internal sealed class PdfDictionary : PdfObject
{
    public Dictionary<string, PdfObject> Items { get; } = new();

    public PdfObject?     Get(string key)             => Items.TryGetValue(key, out var v) ? v : null;
    public string?        GetName(string key)          => Get(key) is PdfName n ? n.Value : null;
    public PdfArray?      GetArray(string key)         => Get(key) as PdfArray;
    public PdfDictionary? GetDict(string key)          => Get(key) as PdfDictionary;

    public long   GetInt(string key, long def = 0) =>
        Get(key) switch { PdfInteger i => i.Value, PdfReal r => (long)r.Value, _ => def };

    public double GetReal(string key, double def = 0.0) =>
        Get(key) switch { PdfReal r => r.Value, PdfInteger i => (double)i.Value, _ => def };

    public bool HasType(string t) => GetName("Type") == t;
}

internal sealed class PdfStream : PdfObject
{
    public PdfDictionary Dict    { get; }
    public byte[]        RawData { get; }
    public PdfStream(PdfDictionary dict, byte[] raw) { Dict = dict; RawData = raw; }
}

internal sealed class PdfIndirectRef : PdfObject
{
    public int ObjNum { get; }
    public int GenNum { get; }
    public PdfIndirectRef(int obj, int gen) { ObjNum = obj; GenNum = gen; }
}
