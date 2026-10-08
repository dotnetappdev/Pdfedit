using Microsoft.AspNetCore.Components.Forms;
using PdfEdit.Blazor.Services;

namespace PdfEdit.Blazor.Components.Editor;

/// <summary>An uploaded file read into memory, so it can still be read after its file picker has gone.</summary>
public sealed class BufferedFile : IBrowserFile
{
    private readonly byte[] _data;

    private BufferedFile(IBrowserFile source, byte[] data)
    {
        Name = source.Name;
        LastModified = source.LastModified;
        ContentType = source.ContentType;
        _data = data;
    }

    public static async Task<IBrowserFile> ReadAsync(IBrowserFile file)
    {
        if (file is BufferedFile) return file;
        await using var s = file.OpenReadStream(PdfDocumentStore.MaxUploadBytes);
        using var m = new MemoryStream();
        await s.CopyToAsync(m);
        return new BufferedFile(file, m.ToArray());
    }

    public string Name { get; }
    public DateTimeOffset LastModified { get; }
    public long Size => _data.Length;
    public string ContentType { get; }

    public Stream OpenReadStream(long maxAllowedSize = 512000, CancellationToken cancellationToken = default)
    {
        if (_data.Length > maxAllowedSize) throw new IOException($"The file exceeds the {maxAllowedSize} byte limit.");
        return new MemoryStream(_data, writable: false);
    }
}
