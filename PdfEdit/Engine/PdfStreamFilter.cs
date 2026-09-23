using System.IO;
using System.IO.Compression;

namespace PdfEdit.Engine;

/// <summary>Decodes a PDF stream's filter chain (FlateDecode, DCTDecode, etc.).</summary>
internal static class PdfStreamFilter
{
    public static byte[] Decode(PdfStream stream)
    {
        byte[] data = stream.RawData;

        var filterObj = stream.Dict.Get("Filter");
        if (filterObj == null) return data;

        // Filter can be a single name or an array of names
        var filters = filterObj is PdfArray arr
            ? arr.Items.OfType<PdfName>().Select(n => n.Value).ToList()
            : filterObj is PdfName sn ? new List<string> { sn.Value } : new List<string>();

        var paramsObj = stream.Dict.Get("DecodeParms");
        var paramsList = paramsObj is PdfArray pa
            ? pa.Items.Select(p => p as PdfDictionary).ToList()
            : paramsObj is PdfDictionary pd
                ? new List<PdfDictionary?> { pd }
                : Enumerable.Repeat<PdfDictionary?>(null, filters.Count).ToList();

        while (paramsList.Count < filters.Count) paramsList.Add(null);

        for (int fi = 0; fi < filters.Count; fi++)
        {
            string filter = filters[fi];
            PdfDictionary? parms = paramsList[fi];
            data = filter switch
            {
                "FlateDecode" or "Fl"          => DecodeFlateDecode(data, parms),
                "LZWDecode"   or "LZW"         => DecodeLzw(data, parms),
                "ASCIIHexDecode" or "AHx"      => DecodeAsciiHex(data),
                "ASCII85Decode" or "A85"        => DecodeAscii85(data),
                "RunLengthDecode" or "RL"       => DecodeRunLength(data),
                "DCTDecode" or "DCT"            => data,  // JPEG passthrough
                "JPXDecode"                     => data,  // JPEG2000 passthrough
                "CCITTFaxDecode" or "CCF"       => data,  // passthrough (not rendered)
                _                               => data
            };
        }
        return data;
    }

    // ── FlateDecode ──────────────────────────────────────────────────────────

    private static byte[] DecodeFlateDecode(byte[] data, PdfDictionary? parms)
    {
        // PDF's FlateDecode uses zlib header (RFC 1950) — skip the 2-byte zlib header
        int offset = (data.Length >= 2 && (data[0] & 0x0F) == 8) ? 2 : 0;
        using var input  = new MemoryStream(data, offset, data.Length - offset);
        using var deflate = new DeflateStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        deflate.CopyTo(output);
        byte[] raw = output.ToArray();

        if (parms != null)
            raw = ApplyPredictor(raw, parms);
        return raw;
    }

    private static byte[] ApplyPredictor(byte[] data, PdfDictionary parms)
    {
        int predictor = (int)parms.GetInt("Predictor", 1);
        if (predictor == 1) return data;

        int colors   = (int)parms.GetInt("Colors",      1);
        int bpc      = (int)parms.GetInt("BitsPerComponent", 8);
        int columns  = (int)parms.GetInt("Columns",     1);

        if (predictor == 2)
            return ApplyTiffPredictor(data, colors, bpc, columns);

        // PNG predictors: 10-15
        if (predictor >= 10)
            return ApplyPngPredictor(data, colors, bpc, columns);

        return data;
    }

    private static byte[] ApplyTiffPredictor(byte[] data, int colors, int bpc, int columns)
    {
        // Horizontal differencing — only handles bpc==8 for simplicity
        if (bpc != 8) return data;
        int stride = columns * colors;
        var result = new byte[data.Length];
        for (int row = 0; row < data.Length / stride; row++)
        {
            int rowOff = row * stride;
            for (int c = 0; c < colors; c++) result[rowOff + c] = data[rowOff + c];
            for (int x = colors; x < stride; x++)
                result[rowOff + x] = (byte)(data[rowOff + x] + result[rowOff + x - colors]);
        }
        return result;
    }

    private static byte[] ApplyPngPredictor(byte[] data, int colors, int bpc, int columns)
    {
        int bpp = Math.Max(1, colors * bpc / 8);
        int stride = columns * colors * bpc / 8;
        int rowStride = stride + 1; // +1 for filter byte
        int rows = data.Length / rowStride;
        var result = new byte[rows * stride];
        var prev = new byte[stride];

        for (int row = 0; row < rows; row++)
        {
            int srcOff = row * rowStride;
            int dstOff = row * stride;
            byte filter = data[srcOff];
            var cur = new byte[stride];
            Array.Copy(data, srcOff + 1, cur, 0, stride);

            for (int x = 0; x < stride; x++)
            {
                byte a = x >= bpp ? cur[x - bpp]  : (byte)0;
                byte b = prev[x];
                byte c = x >= bpp ? prev[x - bpp] : (byte)0;
                cur[x] = filter switch
                {
                    0 => cur[x],
                    1 => (byte)(cur[x] + a),
                    2 => (byte)(cur[x] + b),
                    3 => (byte)(cur[x] + (a + b) / 2),
                    4 => (byte)(cur[x] + PaethPredictor(a, b, c)),
                    _ => cur[x]
                };
            }
            Array.Copy(cur, 0, result, dstOff, stride);
            prev = cur;
        }
        return result;
    }

    private static byte PaethPredictor(byte a, byte b, byte c)
    {
        int p  = a + b - c;
        int pa = Math.Abs(p - a);
        int pb = Math.Abs(p - b);
        int pc = Math.Abs(p - c);
        if (pa <= pb && pa <= pc) return a;
        if (pb <= pc)             return b;
        return c;
    }

    // ── LZW ─────────────────────────────────────────────────────────────────

    private static byte[] DecodeLzw(byte[] data, PdfDictionary? parms)
    {
        // Basic LZW used in older PDFs — EarlyChange=1 (default) means code size increases one step early
        int earlyChange = (int)(parms?.GetInt("EarlyChange", 1) ?? 1);
        var dict  = new List<byte[]>();
        var result = new MemoryStream();

        void ResetDict()
        {
            dict.Clear();
            for (int i = 0; i < 256; i++) dict.Add(new[] { (byte)i });
            dict.Add(Array.Empty<byte>()); // 256 = clear
            dict.Add(Array.Empty<byte>()); // 257 = EOD
        }
        ResetDict();

        int bitOff = 0, codeSize = 9;
        byte[]? prev = null;

        while (true)
        {
            int code = ReadBits(data, bitOff, codeSize);
            bitOff += codeSize;
            if (code == 256) { ResetDict(); codeSize = 9; prev = null; continue; }
            if (code == 257 || bitOff > data.Length * 8) break;

            byte[] entry;
            if (code < dict.Count) entry = dict[code];
            else if (prev != null) entry = prev.Concat(new[] { prev[0] }).ToArray();
            else break;

            result.Write(entry, 0, entry.Length);
            if (prev != null) dict.Add(prev.Concat(new[] { entry[0] }).ToArray());
            prev = entry;
            int nextSize = dict.Count + earlyChange;
            if (nextSize >= (1 << codeSize) && codeSize < 12) codeSize++;
        }

        byte[] raw = result.ToArray();
        if (parms != null) raw = ApplyPredictor(raw, parms);
        return raw;
    }

    private static int ReadBits(byte[] data, int bitOff, int count)
    {
        int result = 0;
        for (int i = 0; i < count; i++)
        {
            int byteIdx = (bitOff + i) / 8;
            int bitIdx  = 7 - (bitOff + i) % 8;
            if (byteIdx < data.Length && ((data[byteIdx] >> bitIdx) & 1) == 1)
                result |= 1 << (count - 1 - i);
        }
        return result;
    }

    // ── ASCII85 ───────────────────────────────────────────────────────────────

    private static byte[] DecodeAscii85(byte[] data)
    {
        var result = new MemoryStream();
        int acc = 0, count = 0;
        foreach (byte b in data)
        {
            if (b is (byte)'~') break;
            if (b == (byte)'z') { result.Write(new byte[4]); continue; }
            if (b < 33 || b > 117) continue;
            acc = acc * 85 + (b - 33);
            count++;
            if (count == 5)
            {
                result.Write(new[] { (byte)(acc >> 24), (byte)(acc >> 16), (byte)(acc >> 8), (byte)acc });
                acc = 0; count = 0;
            }
        }
        if (count > 0)
        {
            for (int i = count; i < 5; i++) acc = acc * 85 + 84;
            for (int i = 0; i < count - 1; i++) result.WriteByte((byte)(acc >> (24 - i * 8)));
        }
        return result.ToArray();
    }

    // ── ASCIIHex ─────────────────────────────────────────────────────────────

    private static byte[] DecodeAsciiHex(byte[] data)
    {
        var sb = new System.Text.StringBuilder();
        foreach (byte b in data)
        {
            if (b == '>') break;
            char c = (char)b;
            if (c is not ' ' and not '\r' and not '\n') sb.Append(c);
        }
        string hex = sb.ToString();
        if (hex.Length % 2 != 0) hex += "0";
        var result = new byte[hex.Length / 2];
        for (int i = 0; i < result.Length; i++)
            result[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
        return result;
    }

    // ── RunLength ────────────────────────────────────────────────────────────

    private static byte[] DecodeRunLength(byte[] data)
    {
        var result = new MemoryStream();
        int i = 0;
        while (i < data.Length)
        {
            int len = (sbyte)data[i++];
            if (len == -128) break;
            if (len >= 0)
            {
                int count = len + 1;
                result.Write(data, i, Math.Min(count, data.Length - i));
                i += count;
            }
            else
            {
                int count = 1 - len;
                byte rep = i < data.Length ? data[i++] : (byte)0;
                for (int j = 0; j < count; j++) result.WriteByte(rep);
            }
        }
        return result.ToArray();
    }
}
