using System.Diagnostics;
using System.Globalization;
using PdfEdit.Services;

namespace PdfEdit.Blazor.Services;

/// <summary>
/// OCR on the server with the Tesseract command-line program (the same engine the Windows app
/// bundles). Install it on the server — e.g. <c>apt install tesseract-ocr</c> (plus
/// tesseract-ocr-deu etc. for more languages) — or point PdfEdit:Ocr:TesseractPath at it.
/// </summary>
public sealed class OcrEngine(IConfiguration config, ILogger<OcrEngine> log)
{
    private readonly string _exe = config["PdfEdit:Ocr:TesseractPath"] is { Length: > 0 } p ? p : "tesseract";
    private readonly string? _tessdata = config["PdfEdit:Ocr:TessdataDir"];
    private List<string>? _languages;
    private readonly SemaphoreSlim _gate = new(Math.Max(1, Environment.ProcessorCount / 2));

    /// <summary>Installed languages (codes such as eng, deu), or empty when Tesseract isn't installed.</summary>
    public async Task<IReadOnlyList<string>> LanguagesAsync()
    {
        if (_languages != null) return _languages;
        try
        {
            var (code, output, _) = await RunAsync(["--list-langs"], TimeSpan.FromSeconds(15), CancellationToken.None);
            _languages = code == 0
                ? output.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0 && !l.Contains(' ') && l != "osd").ToList()
                : [];
        }
        catch (Exception ex)
        {
            log.LogInformation("Tesseract isn't available: {Message}", ex.Message);
            _languages = [];
        }
        return _languages;
    }

    public async Task<bool> IsAvailableAsync() => (await LanguagesAsync()).Count > 0;

    /// <summary>
    /// Reads the words on a page image. Positions come back in PDF points (bottom-left origin) for
    /// a page of <paramref name="pageWidthPt"/> × <paramref name="pageHeightPt"/>.
    /// </summary>
    public async Task<(string Text, List<OcrWord> Words)> RecognizeAsync(byte[] png, int pixelWidth, int pixelHeight,
        double pageWidthPt, double pageHeightPt, string languages, CancellationToken ct = default)
    {
        var temp = Path.Combine(Path.GetTempPath(), $"pdfedit-ocr-{Guid.NewGuid():N}.png");
        await File.WriteAllBytesAsync(temp, png, ct);
        await _gate.WaitAsync(ct);
        try
        {
            var args = new List<string> { temp, "stdout", "-l", string.IsNullOrWhiteSpace(languages) ? "eng" : languages, "--psm", "3" };
            if (!string.IsNullOrEmpty(_tessdata)) { args.Add("--tessdata-dir"); args.Add(_tessdata); }
            args.Add("tsv");
            var (code, tsv, err) = await RunAsync(args, TimeSpan.FromMinutes(3), ct);
            if (code != 0) throw new InvalidOperationException("OCR failed: " + err.Trim());
            return ParseTsv(tsv, pageWidthPt / pixelWidth, pageHeightPt / pixelHeight, pageHeightPt);
        }
        finally
        {
            _gate.Release();
            try { File.Delete(temp); } catch { }
        }
    }

    /// <summary>
    /// A searchable copy of <paramref name="src"/>: upright pages without text are read and get an
    /// invisible text layer (the batch OCR step). Copies the file unchanged when nothing was read.
    /// </summary>
    public async Task MakeSearchableAsync(string src, string dest, string languages, CancellationToken ct)
    {
        if (!await IsAvailableAsync()) throw new InvalidOperationException("OCR isn't set up on this server.");
        using var renderer = new PdfEdit.Render.PdfiumRenderEngine();
        await renderer.LoadAsync(src);
        var byPage = new Dictionary<int, List<OcrWord>>();
        for (int i = 0; i < renderer.PageCount; i++)
        {
            ct.ThrowIfCancellationRequested();
            if (renderer.GetPageRotation(i) != 0) continue;
            if (PdfTextExtractorService.GetPageText(src, i + 1).Trim().Length > 20) continue;
            var (w, h) = renderer.GetPageSizeInPoints(i);
            var page = await renderer.RenderPageAsync(i, 1.0, 3.0 * 72 / 96);   // about 216 dpi
            var png = PngEncoder.FromBgra(page.Pixels ?? [], page.PixelWidth, page.PixelHeight);
            var (_, words) = await RecognizeAsync(png, page.PixelWidth, page.PixelHeight, w, h, languages, ct);
            if (words.Count > 0) byPage[i + 1] = words;
        }
        if (byPage.Count == 0) File.Copy(src, dest, overwrite: true);
        else await Task.Run(() => PdfToolsService.AddInvisibleTextLayer(src, dest, byPage), ct);
    }

    /// <summary>Tesseract's TSV: level 5 rows are words with their pixel boxes and confidence.</summary>
    internal static (string Text, List<OcrWord> Words) ParseTsv(string tsv, double sx, double sy, double pageHeightPt)
    {
        var words = new List<OcrWord>();
        var text = new System.Text.StringBuilder();
        int lastLine = -1, lastBlock = -1;
        foreach (var line in tsv.Split('\n').Skip(1))
        {
            var c = line.TrimEnd('\r').Split('\t');
            if (c.Length < 12 || c[0] != "5") continue;
            var word = c[11].Trim();
            if (word.Length == 0) continue;
            if (!double.TryParse(c[10], NumberStyles.Float, CultureInfo.InvariantCulture, out var conf) || conf < 30) continue;   // noise / specks
            int block = int.Parse(c[2]), ln = int.Parse(c[4]) + block * 1000;
            int left = int.Parse(c[6]), top = int.Parse(c[7]), w = int.Parse(c[8]), h = int.Parse(c[9]);
            words.Add(new OcrWord(word, left * sx, pageHeightPt - (top + h) * sy, w * sx, h * sy));
            if (lastLine >= 0) text.Append(ln != lastLine ? (block != lastBlock ? "\n\n" : "\n") : " ");
            text.Append(word);
            lastLine = ln; lastBlock = block;
        }
        return (text.ToString(), words);
    }

    private async Task<(int Code, string Output, string Error)> RunAsync(IEnumerable<string> args, TimeSpan timeout, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(_exe)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("Couldn't start Tesseract.");
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        var stdout = p.StandardOutput.ReadToEndAsync(cts.Token);
        var stderr = p.StandardError.ReadToEndAsync(cts.Token);
        try { await p.WaitForExitAsync(cts.Token); }
        catch (OperationCanceledException) { try { p.Kill(true); } catch { } throw; }
        return (p.ExitCode, await stdout, await stderr);
    }
}
