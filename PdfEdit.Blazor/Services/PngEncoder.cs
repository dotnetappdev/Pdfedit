using System.Buffers.Binary;
using System.IO.Compression;

namespace PdfEdit.Blazor.Services;

/// <summary>
/// Turns the BGRA pixels PdfEdit.Render produces into a PNG: 24-bit RGB for pages (rendered on
/// white, so there's no transparency to keep), or 32-bit RGBA with <c>alpha</c> for signatures and
/// pictures that are see-through. Small and dependency-free, so the site needs no imaging library.
/// </summary>
public static class PngEncoder
{
    private static readonly byte[] Signature = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly uint[] CrcTable = BuildCrcTable();

    public static byte[] FromBgra(byte[] bgra, int width, int height, bool alpha = false)
    {
        int channels = alpha ? 4 : 3;
        using var output = new MemoryStream();
        output.Write(Signature);

        Span<byte> ihdr = stackalloc byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr, width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr[4..], height);
        ihdr[8] = 8;    // bits per channel
        ihdr[9] = (byte)(alpha ? 6 : 2);    // colour type: RGBA or RGB
        ihdr[10] = 0;   // compression: deflate
        ihdr[11] = 0;   // filter method
        ihdr[12] = 0;   // no interlace
        WriteChunk(output, "IHDR", ihdr);

        using (var raw = new MemoryStream())
        {
            using (var z = new ZLibStream(raw, CompressionLevel.Fastest, leaveOpen: true))
            {
                var row = new byte[1 + width * channels];   // filter byte 0 (None), then RGB(A)
                for (int y = 0; y < height; y++)
                {
                    int src = y * width * 4;
                    for (int x = 0, dst = 1; x < width; x++, src += 4, dst += channels)
                    {
                        row[dst] = bgra[src + 2];
                        row[dst + 1] = bgra[src + 1];
                        row[dst + 2] = bgra[src];
                        if (alpha) row[dst + 3] = bgra[src + 3];
                    }
                    z.Write(row);
                }
            }
            WriteChunk(output, "IDAT", raw.GetBuffer().AsSpan(0, (int)raw.Length));
        }

        WriteChunk(output, "IEND", ReadOnlySpan<byte>.Empty);
        return output.ToArray();
    }

    private static void WriteChunk(Stream s, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> buf = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(buf, data.Length);
        s.Write(buf);

        Span<byte> typeBytes = stackalloc byte[4];
        for (int i = 0; i < 4; i++) typeBytes[i] = (byte)type[i];
        s.Write(typeBytes);
        s.Write(data);

        uint crc = Crc(0xFFFFFFFFu, typeBytes);
        crc = Crc(crc, data) ^ 0xFFFFFFFFu;
        BinaryPrimitives.WriteUInt32BigEndian(buf, crc);
        s.Write(buf);
    }

    private static uint Crc(uint crc, ReadOnlySpan<byte> data)
    {
        foreach (var b in data) crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc;
    }

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[n] = c;
        }
        return table;
    }
}
