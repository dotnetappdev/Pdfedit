using System.IO;
using System.Net.Http;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace PdfEdit.Services;

/// <summary>
/// Text recognition for scans and image-only PDFs, with two engines:
/// <list type="bullet">
/// <item><b>Windows OCR</b> (Windows.Media.Ocr) — built into Windows 10/11, used when a Windows OCR
/// language is installed.</item>
/// <item><b>Tesseract</b> (bundled, Apache-2.0) — works on any PC with no Windows language pack.
/// English ships with PdfEdit; other languages download on demand (Settings → OCR).</item>
/// </list>
/// "Automatic" uses Windows OCR when it can and Tesseract otherwise, so OCR always works.
/// </summary>
public static class OcrService
{
    // ── Engine choice ────────────────────────────────────────────────────────

    public static bool WindowsOcrAvailable
    {
        get
        {
            try { return OcrEngine.TryCreateFromUserProfileLanguages() != null; }
            catch { return false; }
        }
    }

    public static bool TesseractAvailable => InstalledTesseractLanguages().Count > 0;

    /// <summary>True when either engine can run (Tesseract ships with English, so normally always).</summary>
    public static bool IsAvailable => WindowsOcrAvailable || TesseractAvailable;

    /// <summary>The engine "Automatic" / the setting resolves to right now.</summary>
    public static string ActiveEngineName => UseWindows() ? "Windows OCR" : "Tesseract";

    private static bool UseWindows() => AppSettings.Current.OcrEngine switch
    {
        "Windows" => WindowsOcrAvailable || !TesseractAvailable,
        "Tesseract" => !TesseractAvailable && WindowsOcrAvailable,
        _ => WindowsOcrAvailable,   // Automatic
    };

    /// <summary>
    /// Recognises the words in a page image and maps them to PDF points (origin bottom-left)
    /// using the page size.
    /// </summary>
    public static Task<(string Text, List<OcrWord> Words)> RecognizeAsync(
        BitmapSource page, double pageWidthPt, double pageHeightPt)
    {
        if (UseWindows()) return RecognizeWindowsAsync(page, pageWidthPt, pageHeightPt);
        if (TesseractAvailable) return RecognizeTesseractAsync(page, pageWidthPt, pageHeightPt);
        throw new InvalidOperationException(
            "No OCR engine is available. Download a Tesseract language in Settings → OCR, or install a Windows OCR language there.");
    }

    // ── Windows OCR ──────────────────────────────────────────────────────────

    private static async Task<(string, List<OcrWord>)> RecognizeWindowsAsync(BitmapSource page, double pageWidthPt, double pageHeightPt)
    {
        var engine = OcrEngine.TryCreateFromUserProfileLanguages()
            ?? throw new InvalidOperationException("Windows has no OCR language installed.");

        // Keep within the engine's limit (it rejects very large images).
        BitmapSource src = page;
        double max = OcrEngine.MaxImageDimension;
        if (src.PixelWidth > max || src.PixelHeight > max)
        {
            double k = max / Math.Max(src.PixelWidth, src.PixelHeight);
            src = new TransformedBitmap(src, new ScaleTransform(k, k));
        }

        var bgra = new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
        int w = bgra.PixelWidth, h = bgra.PixelHeight, stride = w * 4;
        var pixels = new byte[stride * h];
        bgra.CopyPixels(pixels, stride, 0);

        var writer = new DataWriter();
        writer.WriteBytes(pixels);
        using var bitmap = SoftwareBitmap.CreateCopyFromBuffer(writer.DetachBuffer(), BitmapPixelFormat.Bgra8, w, h, BitmapAlphaMode.Premultiplied);

        var result = await engine.RecognizeAsync(bitmap);

        double sx = pageWidthPt / w, sy = pageHeightPt / h;
        var words = new List<OcrWord>();
        foreach (var line in result.Lines)
            foreach (var word in line.Words)
            {
                var r = word.BoundingRect;
                words.Add(new OcrWord(word.Text, r.X * sx, pageHeightPt - (r.Y + r.Height) * sy, r.Width * sx, r.Height * sy));
            }
        return (string.Join(Environment.NewLine, result.Lines.Select(l => l.Text)), words);
    }

    // ── Tesseract ────────────────────────────────────────────────────────────

    private static readonly SemaphoreSlim TesseractLock = new(1, 1);   // the engine is single-threaded
    private static Tesseract.TesseractEngine? _engine;
    private static string? _engineLangs;

    /// <summary>Where language data lives: the user's folder (downloads) — English is copied in from the app.</summary>
    public static string UserTessdataDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PdfEdit", "tessdata");

    private static string AppTessdataDir => Path.Combine(AppContext.BaseDirectory, "tessdata");

    /// <summary>Tesseract language codes available (e.g. "eng", "deu").</summary>
    public static List<string> InstalledTesseractLanguages()
    {
        var langs = new HashSet<string>();
        foreach (var dir in new[] { AppTessdataDir, UserTessdataDir })
            if (Directory.Exists(dir))
                foreach (var f in Directory.GetFiles(dir, "*.traineddata"))
                    langs.Add(Path.GetFileNameWithoutExtension(f));
        langs.Remove("osd");
        return langs.OrderBy(l => l).ToList();
    }

    /// <summary>The languages to recognise ("eng", "eng+deu" …), limited to installed ones.</summary>
    private static string TesseractLanguages()
    {
        var installed = InstalledTesseractLanguages();
        var wanted = (AppSettings.Current.OcrLanguages ?? "eng").Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(installed.Contains).ToList();
        if (wanted.Count == 0) wanted.Add(installed.Contains("eng") ? "eng" : installed[0]);
        return string.Join("+", wanted);
    }

    /// <summary>Puts every installed language in the user folder (Tesseract reads a single folder).</summary>
    private static string EnsureDataDir()
    {
        Directory.CreateDirectory(UserTessdataDir);
        if (Directory.Exists(AppTessdataDir))
            foreach (var f in Directory.GetFiles(AppTessdataDir, "*.traineddata"))
            {
                string dest = Path.Combine(UserTessdataDir, Path.GetFileName(f));
                if (!File.Exists(dest)) File.Copy(f, dest);
            }
        return UserTessdataDir;
    }

    private static async Task<(string, List<OcrWord>)> RecognizeTesseractAsync(BitmapSource page, double pageWidthPt, double pageHeightPt)
    {
        // PNG bytes for Leptonica (made on the calling thread: WPF bitmaps)
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(page));
        byte[] png;
        using (var ms = new MemoryStream()) { enc.Save(ms); png = ms.ToArray(); }
        int w = page.PixelWidth, h = page.PixelHeight;

        await TesseractLock.WaitAsync();
        try
        {
            return await Task.Run(() =>
            {
                string langs = TesseractLanguages();
                if (_engine == null || _engineLangs != langs)
                {
                    _engine?.Dispose();
                    _engine = null;
                    try
                    {
                        _engine = new Tesseract.TesseractEngine(EnsureDataDir(), langs, Tesseract.EngineMode.Default);
                    }
                    catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException or TypeInitializationException
                                               || ex.InnerException is DllNotFoundException)
                    {
                        throw new InvalidOperationException(
                            "The Tesseract OCR engine couldn't start — the Microsoft Visual C++ Redistributable (x64) is missing. " +
                            "Install it from https://aka.ms/vs/17/release/vc_redist.x64.exe, or install a Windows OCR language in Settings → OCR.", ex);
                    }
                    _engineLangs = langs;
                }

                using var pix = Tesseract.Pix.LoadFromMemory(png);
                using var result = _engine.Process(pix, Tesseract.PageSegMode.Auto);
                string text = result.GetText() ?? string.Empty;

                double sx = pageWidthPt / w, sy = pageHeightPt / h;
                var words = new List<OcrWord>();
                using var it = result.GetIterator();
                it.Begin();
                do
                {
                    if (it.IsAtBeginningOf(Tesseract.PageIteratorLevel.Block) && it.BlockType == Tesseract.PolyBlockType.Unknown) continue;
                    string? word = it.GetText(Tesseract.PageIteratorLevel.Word)?.Trim();
                    if (string.IsNullOrEmpty(word)) continue;
                    if (it.GetConfidence(Tesseract.PageIteratorLevel.Word) < 30) continue;   // noise / specks
                    if (!it.TryGetBoundingBox(Tesseract.PageIteratorLevel.Word, out var r)) continue;
                    words.Add(new OcrWord(word, r.X1 * sx, pageHeightPt - r.Y2 * sy, r.Width * sx, r.Height * sy));
                }
                while (it.Next(Tesseract.PageIteratorLevel.Word));
                return (text.Trim(), words);
            });
        }
        finally { TesseractLock.Release(); }
    }

    // ── Language downloads (Tesseract) ───────────────────────────────────────

    /// <summary>Common Tesseract languages offered in Settings (code, name).</summary>
    public static readonly (string Code, string Name)[] CommonLanguages =
    {
        ("eng", "English"), ("deu", "German"), ("fra", "French"), ("spa", "Spanish"), ("ita", "Italian"),
        ("por", "Portuguese"), ("nld", "Dutch"), ("pol", "Polish"), ("swe", "Swedish"), ("dan", "Danish"),
        ("nor", "Norwegian"), ("fin", "Finnish"), ("ces", "Czech"), ("ron", "Romanian"), ("hun", "Hungarian"),
        ("tur", "Turkish"), ("gle", "Irish"), ("cym", "Welsh"), ("rus", "Russian"), ("ukr", "Ukrainian"),
        ("ell", "Greek"), ("ara", "Arabic"), ("heb", "Hebrew"), ("hin", "Hindi"), ("chi_sim", "Chinese (Simplified)"),
        ("chi_tra", "Chinese (Traditional)"), ("jpn", "Japanese"), ("kor", "Korean"), ("vie", "Vietnamese"), ("tha", "Thai"),
    };

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(5) };

    /// <summary>Downloads a Tesseract language (tessdata_fast, Apache-2.0) into the user folder.</summary>
    public static async Task DownloadTesseractLanguageAsync(string code, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        string url = $"https://raw.githubusercontent.com/tesseract-ocr/tessdata_fast/main/{code}.traineddata";
        Directory.CreateDirectory(UserTessdataDir);
        string dest = Path.Combine(UserTessdataDir, code + ".traineddata"), tmp = dest + ".part";
        using (var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            resp.EnsureSuccessStatusCode();
            long total = resp.Content.Headers.ContentLength ?? -1, read = 0;
            await using var src = await resp.Content.ReadAsStreamAsync(ct);
            await using var file = File.Create(tmp);
            var buf = new byte[81920];
            int n;
            while ((n = await src.ReadAsync(buf, ct)) > 0)
            {
                await file.WriteAsync(buf.AsMemory(0, n), ct);
                read += n;
                if (total > 0) progress?.Report((double)read / total);
            }
        }
        File.Move(tmp, dest, overwrite: true);
        _engineLangs = null;   // reload with the new language next time
    }

    // ── Windows OCR language install ─────────────────────────────────────────

    /// <summary>
    /// Installs the Windows OCR capability for the user's language (needs administrator approval —
    /// Windows asks). Returns false if the user declined.
    /// </summary>
    public static async Task<bool> InstallWindowsOcrLanguageAsync()
    {
        string tag = System.Globalization.CultureInfo.CurrentUICulture.Name;   // e.g. en-GB
        if (string.IsNullOrEmpty(tag)) tag = "en-US";
        string script =
            $"$c = Get-WindowsCapability -Online | Where-Object {{ $_.Name -like 'Language.OCR*{tag}*' }}; " +
            "if (-not $c) { $c = Get-WindowsCapability -Online | Where-Object { $_.Name -like 'Language.OCR*en-US*' } }; " +
            "$c | Add-WindowsCapability -Online";
        var psi = new System.Diagnostics.ProcessStartInfo("powershell.exe",
            $"-NoProfile -ExecutionPolicy Bypass -Command \"{script}\"")
        {
            UseShellExecute = true,
            Verb = "runas",                       // UAC prompt
            WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden,
        };
        try
        {
            using var p = System.Diagnostics.Process.Start(psi);
            if (p == null) return false;
            await p.WaitForExitAsync();
            return p.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception) { return false; }   // UAC declined
    }
}
