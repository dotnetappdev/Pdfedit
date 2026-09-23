using System.Text;

namespace PdfEdit.Engine;

internal sealed class PdfLexer
{
    private readonly byte[] _data;
    private int _pos;

    public int  Position { get => _pos; set => _pos = value; }
    public bool AtEnd    => _pos >= _data.Length;

    public PdfLexer(byte[] data, int startPos = 0) { _data = data; _pos = startPos; }

    // ── whitespace / comments ────────────────────────────────────────────────

    private static bool IsWs(byte b)  => b is 0 or 9 or 10 or 12 or 13 or 32;
    private static bool IsDelim(byte b) =>
        b is (byte)'(' or (byte)')' or (byte)'<' or (byte)'>' or
            (byte)'[' or (byte)']' or (byte)'{' or (byte)'}' or
            (byte)'/' or (byte)'%';

    public void SkipWs()
    {
        while (_pos < _data.Length)
        {
            byte b = _data[_pos];
            if (IsWs(b)) { _pos++; continue; }
            if (b == '%') { while (_pos < _data.Length && _data[_pos] is not 10 and not 13) _pos++; continue; }
            break;
        }
    }

    // ── token reading ────────────────────────────────────────────────────────

    // Returns the next PdfObject (or null at end).  Does not resolve indirect refs.
    public PdfObject? ReadObject()
    {
        SkipWs();
        if (_pos >= _data.Length) return null;

        byte b = _data[_pos];

        // << dict start — handled by caller
        if (b == '<' && Peek2() == '<') return ReadDict();
        if (b == '<') return ReadHexString();
        if (b == '(') return ReadLiteralString();
        if (b == '/') return ReadName();
        if (b == '[') return ReadArray();
        if (b == ']') { _pos++; return null; }   // array end sentinel (caller handles)
        if (b == '>') return null;

        if (b == '-' || b == '+' || b == '.' || (b >= '0' && b <= '9'))
            return ReadNumber();

        return ReadKeyword();
    }

    // ── dict ─────────────────────────────────────────────────────────────────

    public PdfDictionary ReadDict()
    {
        _pos += 2; // skip <<
        var dict = new PdfDictionary();
        while (_pos < _data.Length)
        {
            SkipWs();
            if (_pos + 1 < _data.Length && _data[_pos] == '>' && _data[_pos + 1] == '>')
            {
                _pos += 2; break;
            }
            if (ReadObject() is not PdfName key) break;
            var val = ReadObject() ?? PdfNull.Instance;
            dict.Items[key.Value] = val;
        }
        return dict;
    }

    // ── array ────────────────────────────────────────────────────────────────

    private PdfArray ReadArray()
    {
        _pos++; // skip [
        var arr = new PdfArray();
        while (_pos < _data.Length)
        {
            SkipWs();
            if (_pos < _data.Length && _data[_pos] == ']') { _pos++; break; }
            var obj = ReadObject();
            if (obj == null) break;
            // If it looks like an indirect ref N G R, collapse it
            if (obj is PdfInteger iObj)
            {
                int savedPos = _pos;
                SkipWs();
                if (ReadObject() is PdfInteger gen)
                {
                    SkipWs();
                    int kw = _pos;
                    if (ReadObject() is { } kObj && kObj.ToString() == "R")
                    {
                        arr.Items.Add(new PdfIndirectRef((int)iObj.Value, (int)gen.Value));
                        continue;
                    }
                    _pos = kw;
                }
                _pos = savedPos;
            }
            arr.Items.Add(obj);
        }
        return arr;
    }

    // ── strings ──────────────────────────────────────────────────────────────

    private PdfString ReadHexString()
    {
        _pos++; // skip <
        var sb = new StringBuilder();
        while (_pos < _data.Length && _data[_pos] != '>')
        {
            byte c = _data[_pos++];
            if (!IsWs(c)) sb.Append((char)c);
        }
        if (_pos < _data.Length) _pos++; // skip >
        string hex = sb.ToString();
        if (hex.Length % 2 != 0) hex += "0";
        var bytes = new byte[hex.Length / 2];
        for (int i = 0; i < bytes.Length; i++)
            bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
        return new PdfString(bytes);
    }

    private PdfString ReadLiteralString()
    {
        _pos++; // skip (
        var result = new List<byte>();
        int depth = 1;
        while (_pos < _data.Length && depth > 0)
        {
            byte c = _data[_pos++];
            if (c == '\\' && _pos < _data.Length)
            {
                byte esc = _data[_pos++];
                switch (esc)
                {
                    case (byte)'n': result.Add(10); break;
                    case (byte)'r': result.Add(13); break;
                    case (byte)'t': result.Add(9);  break;
                    case (byte)'b': result.Add(8);  break;
                    case (byte)'f': result.Add(12); break;
                    case (byte)'(': result.Add((byte)'('); break;
                    case (byte)')': result.Add((byte)')'); break;
                    case (byte)'\\': result.Add((byte)'\\'); break;
                    case 10: case 13: break;
                    default:
                        if (esc >= '0' && esc <= '7')
                        {
                            int oct = esc - '0';
                            for (int i = 0; i < 2 && _pos < _data.Length && _data[_pos] >= '0' && _data[_pos] <= '7'; i++)
                                oct = oct * 8 + (_data[_pos++] - '0');
                            result.Add((byte)(oct & 0xFF));
                        }
                        else result.Add(esc);
                        break;
                }
            }
            else if (c == '(') { depth++; result.Add(c); }
            else if (c == ')') { depth--; if (depth > 0) result.Add(c); }
            else result.Add(c);
        }
        return new PdfString(result.ToArray());
    }

    // ── name ─────────────────────────────────────────────────────────────────

    private PdfName ReadName()
    {
        _pos++; // skip /
        var sb = new StringBuilder();
        while (_pos < _data.Length)
        {
            byte c = _data[_pos];
            if (IsWs(c) || IsDelim(c)) break;
            if (c == '#' && _pos + 2 < _data.Length)
            {
                _pos++;
                string h = new string(new[] { (char)_data[_pos], (char)_data[_pos + 1] });
                sb.Append((char)Convert.ToByte(h, 16));
                _pos += 2;
            }
            else { sb.Append((char)c); _pos++; }
        }
        return new PdfName(sb.ToString());
    }

    // ── numbers ──────────────────────────────────────────────────────────────

    private PdfObject ReadNumber()
    {
        int start = _pos;
        bool real = false;
        if (_pos < _data.Length && (_data[_pos] == '-' || _data[_pos] == '+')) _pos++;
        while (_pos < _data.Length)
        {
            byte c = _data[_pos];
            if (c == '.') { real = true; _pos++; }
            else if (c >= '0' && c <= '9') _pos++;
            else break;
        }
        string text = Encoding.ASCII.GetString(_data, start, _pos - start);
        if (real)
        {
            double.TryParse(text, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double d);
            return new PdfReal(d);
        }
        long.TryParse(text, out long iv);
        return new PdfInteger(iv);
    }

    // ── keyword ──────────────────────────────────────────────────────────────

    private PdfObject ReadKeyword()
    {
        int start = _pos;
        while (_pos < _data.Length && !IsWs(_data[_pos]) && !IsDelim(_data[_pos]))
            _pos++;
        string kw = Encoding.ASCII.GetString(_data, start, _pos - start);
        return kw switch
        {
            "true"  => PdfBoolean.True,
            "false" => PdfBoolean.False,
            "null"  => PdfNull.Instance,
            _       => new PdfName(kw)      // treat unknown keywords as names
        };
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private byte Peek2() => _pos + 1 < _data.Length ? _data[_pos + 1] : 0;

    public string ReadLine()
    {
        int start = _pos;
        while (_pos < _data.Length && _data[_pos] is not 10 and not 13) _pos++;
        string line = Encoding.ASCII.GetString(_data, start, _pos - start);
        if (_pos < _data.Length && _data[_pos] == 13) _pos++;
        if (_pos < _data.Length && _data[_pos] == 10) _pos++;
        return line;
    }

    // Advance past 'stream' keyword's newline, then read length bytes.
    public byte[] ReadStreamBytes(int length)
    {
        if (_pos < _data.Length && _data[_pos] == 13) _pos++;
        if (_pos < _data.Length && _data[_pos] == 10) _pos++;
        int avail = Math.Min(length, _data.Length - _pos);
        var result = new byte[avail];
        Array.Copy(_data, _pos, result, 0, avail);
        _pos += avail;
        return result;
    }

    // Search backward from the end for a token.
    public static int SearchBackward(byte[] data, string token)
    {
        byte[] tok = Encoding.ASCII.GetBytes(token);
        for (int i = data.Length - tok.Length; i >= 0; i--)
        {
            bool match = true;
            for (int j = 0; j < tok.Length && match; j++)
                match = data[i + j] == tok[j];
            if (match) return i;
        }
        return -1;
    }
}
