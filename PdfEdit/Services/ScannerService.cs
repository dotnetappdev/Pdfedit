using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;
using NTwain;
using NTwain.Data;

namespace PdfEdit.Services;

public enum ScanDriver { Windows, Twain, Wia }
public enum ScanColorMode { Color, Gray, BlackWhite }
public enum ScanPaperSource { Flatbed, Feeder, Duplex }
public enum ScanFileType { Pdf, Png, Jpeg, Tiff, Bmp }

/// <summary>
/// A scanner, reached through the Windows scanner API (what the Windows Scan app lists), TWAIN or
/// WIA Automation. Windows-API scanners show their plain name, as in Windows Scan.
/// </summary>
public sealed record ScanDevice(string Name, ScanDriver Driver, string Id)
{
    public string Display => Driver switch
    {
        ScanDriver.Twain => $"{Name}  (TWAIN)",
        ScanDriver.Wia => $"{Name}  (WIA)",
        _ => Name,
    };
    public override string ToString() => Display;
}

/// <summary>What a scanner can do; unknown (TWAIN / WIA) means everything is offered.</summary>
public sealed class ScanCapabilities
{
    public bool Known { get; set; }
    public bool Flatbed { get; set; } = true;
    public bool Feeder { get; set; } = true;
    public bool Duplex { get; set; } = true;
    public List<ScanColorMode> ColorModes { get; } = new() { ScanColorMode.Color, ScanColorMode.Gray, ScanColorMode.BlackWhite };
    public List<ScanFileType> FileTypes { get; } = new() { ScanFileType.Pdf, ScanFileType.Png, ScanFileType.Jpeg, ScanFileType.Tiff, ScanFileType.Bmp };
    public int MinDpi { get; set; } = 50;
    public int MaxDpi { get; set; } = 1200;
    public bool CanPreview { get; set; } = true;
    public bool CanAutoCrop { get; set; }
    /// <summary>Scan bed size in inches (empty when unknown).</summary>
    public System.Windows.Size MaxArea { get; set; } = System.Windows.Size.Empty;
}

public sealed class ScanSettings
{
    public int Dpi { get; set; } = 200;
    public ScanColorMode Color { get; set; } = ScanColorMode.Color;
    public ScanPaperSource Source { get; set; } = ScanPaperSource.Flatbed;
    public string PaperSize { get; set; } = "Auto";      // Auto, A4, A5, Letter, Legal
    public int Brightness { get; set; }                   // -100 … 100
    public int Contrast { get; set; }                     // -100 … 100
    public bool ShowDriverUi { get; set; }                // the scanner's own dialog
    public bool SinglePage { get; set; }                  // preview: one page only
    public bool AutoCrop { get; set; }                    // let the scanner find the document's edges
    /// <summary>Area to scan in inches from the bed's top-left corner (null = page size / whole bed).</summary>
    public System.Windows.Rect? Region { get; set; }
}

/// <summary>One scanned page and the resolution it was scanned at.</summary>
public sealed record ScannedPage(BitmapSource Image, double Dpi);

/// <summary>
/// Scanning from TWAIN scanners (via NTwain and the TWAIN DSM that scanner drivers install) and
/// WIA scanners (built into Windows — many newer scanners only ship WIA drivers). Pages are
/// returned as they arrive, so the dialog can show each one while a feeder is still running.
/// </summary>
public static partial class ScannerService
{
    // ── Devices ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Every scanner, the way Windows Scan lists them (Windows scanner API) plus any TWAIN-only
    /// scanners. WIA Automation entries for a scanner the Windows API already found are left out —
    /// they are the same device. Call on the UI thread: TWAIN needs its window handle.
    /// </summary>
    public static async Task<(List<ScanDevice> Devices, string? Note)> ListDevicesAsync(IntPtr hwnd)
    {
        var windows = new List<ScanDevice>();
        string? winNote = null;
        try { windows = await ListWindowsDevicesAsync(); }
        catch (Exception ex) { winNote = "Windows scanner service unavailable: " + ex.Message; }

        var others = ListTwainAndWiaDevices(hwnd, out var note);
        var names = new HashSet<string>(windows.Select(d => d.Name), StringComparer.OrdinalIgnoreCase);
        var list = windows.Concat(others.Where(d => d.Driver == ScanDriver.Twain || !names.Contains(d.Name))).ToList();
        // A missing TWAIN driver isn't worth a warning when Windows already found scanners.
        if (windows.Count > 0) note = null;
        return (list, winNote == null ? note : note == null ? winNote : winNote + "\n" + note);
    }

    /// <summary>What the scanner supports (sources, colour modes, resolutions, file types).</summary>
    public static async Task<ScanCapabilities> GetCapabilitiesAsync(ScanDevice device)
    {
        if (device.Driver != ScanDriver.Windows) return new ScanCapabilities();
        try { return await GetWindowsCapabilitiesAsync(device); }
        catch { return new ScanCapabilities(); }
    }

    private static List<ScanDevice> ListTwainAndWiaDevices(IntPtr hwnd, out string? note)
    {
        var list = new List<ScanDevice>();
        note = null;
        try
        {
            if (!PlatformInfo.Current.IsSupported || !PlatformInfo.Current.DsmExists)
                note = "TWAIN driver manager (TWAINDSM.dll) not found — install your scanner's TWAIN driver to use TWAIN. WIA scanners still work.";
            else
            {
                var session = NewSession();
                if (session.Open(new WpfMessageLoopHook(hwnd)) == ReturnCode.Success)
                {
                    try { list.AddRange(session.GetSources().Select(s => new ScanDevice(s.Name, ScanDriver.Twain, s.Name))); }
                    finally { session.Close(); }
                }
            }
        }
        catch (Exception ex) { note = "TWAIN unavailable: " + ex.Message; }

        try { list.AddRange(ListWiaDevices()); }
        catch (Exception ex) { note = (note == null ? "" : note + "\n") + "WIA unavailable: " + ex.Message; }
        return list;
    }

    private static TwainSession NewSession()
    {
        var appId = TWIdentity.CreateFromAssembly(DataGroups.Image, Assembly.GetExecutingAssembly());
        return new TwainSession(appId);
    }

    // ── Scan ─────────────────────────────────────────────────────────────────

    /// <summary>Scans with the device's driver; <paramref name="onPage"/> is called (on the UI thread) per page.</summary>
    public static Task<int> ScanAsync(ScanDevice device, ScanSettings settings, IntPtr hwnd,
                                      Action<ScannedPage> onPage, CancellationToken ct, Action<string>? onStatus = null)
    {
        if (device.Driver == ScanDriver.Windows && settings.ShowDriverUi)
        {
            // The Windows scanner API has no driver dialog — use the same scanner's WIA dialog.
            try
            {
                var wia = ListWiaDevices().FirstOrDefault(d => string.Equals(d.Name, device.Name, StringComparison.OrdinalIgnoreCase));
                if (wia != null) return ScanWiaAsync(wia, settings, onPage, ct);
            }
            catch { /* no WIA — scan with the settings in the dialog */ }
        }
        return device.Driver switch
        {
            ScanDriver.Twain => ScanTwainAsync(device, settings, hwnd, onPage, ct),
            ScanDriver.Wia => ScanWiaAsync(device, settings, onPage, ct),
            _ => ScanWindowsAsync(device, settings, onPage, onStatus, ct),
        };
    }

    // TWAIN runs on the UI thread (it needs the window's message loop); events come back on it.
    private static Task<int> ScanTwainAsync(ScanDevice device, ScanSettings s, IntPtr hwnd,
                                            Action<ScannedPage> onPage, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<int>();
        var session = NewSession();
        session.SynchronizationContext = SynchronizationContext.Current;
        int pages = 0;
        Exception? error = null;

        void Finish()
        {
            try { session.CurrentSource?.Close(); } catch { }
            try { session.Close(); } catch { }
            if (error != null && pages == 0) tcs.TrySetException(error);
            else if (ct.IsCancellationRequested && pages == 0) tcs.TrySetCanceled();
            else tcs.TrySetResult(pages);
        }

        session.TransferReady += (_, e) =>
        {
            if (ct.IsCancellationRequested) e.CancelAll = true;
            else if (s.SinglePage && pages > 0) e.CancelAll = true;
        };
        session.DataTransferred += (_, e) =>
        {
            if (e.NativeData == IntPtr.Zero) return;
            try
            {
                using var stream = e.GetNativeImageStream();
                if (stream == null) return;
                var frame = BitmapFrame.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                frame.Freeze();
                pages++;
                onPage(new ScannedPage(frame, ImageDpi(frame, s.Dpi)));
            }
            catch (Exception ex) { error = ex; }
        };
        session.TransferError += (_, e) => error = e.Exception ?? new InvalidOperationException($"Scanner error ({e.ReturnCode}).");
        session.SourceDisabled += (_, _) => Finish();

        try
        {
            if (session.Open(new WpfMessageLoopHook(hwnd)) != ReturnCode.Success)
                throw new InvalidOperationException("Could not open the TWAIN driver manager.");
            var source = session.GetSources().FirstOrDefault(x => x.Name == device.Id)
                ?? throw new InvalidOperationException($"Scanner \"{device.Name}\" was not found.");
            if (source.Open() != ReturnCode.Success)
                throw new InvalidOperationException($"Could not open \"{device.Name}\" — is it switched on and connected?");

            ApplyTwainSettings(source, s);

            var mode = s.ShowDriverUi ? SourceEnableMode.ShowUI : SourceEnableMode.NoUI;
            if (source.Enable(mode, false, hwnd) != ReturnCode.Success)
                throw new InvalidOperationException($"\"{device.Name}\" would not start scanning.");
            ct.Register(() => { /* the next TransferReady cancels the job */ });
        }
        catch (Exception ex)
        {
            error = ex;
            Finish();
        }
        return tcs.Task;
    }

    private static void ApplyTwainSettings(DataSource src, ScanSettings s)
    {
        var caps = src.Capabilities;
        // Each setting is best effort: scanners support different subsets.
        Try(() => { if (caps.ICapPixelType.CanSet) caps.ICapPixelType.SetValue(s.Color switch
        {
            ScanColorMode.Gray => PixelType.Gray,
            ScanColorMode.BlackWhite => PixelType.BlackWhite,
            _ => PixelType.RGB,
        }); });
        Try(() => { if (caps.ICapXResolution.CanSet) caps.ICapXResolution.SetValue(s.Dpi); });
        Try(() => { if (caps.ICapYResolution.CanSet) caps.ICapYResolution.SetValue(s.Dpi); });
        bool feeder = s.Source != ScanPaperSource.Flatbed;
        Try(() => { if (caps.CapFeederEnabled.CanSet) caps.CapFeederEnabled.SetValue(feeder ? BoolType.True : BoolType.False); });
        Try(() => { if (feeder && caps.CapAutoFeed.CanSet) caps.CapAutoFeed.SetValue(BoolType.True); });
        Try(() => { if (caps.CapDuplexEnabled.CanSet) caps.CapDuplexEnabled.SetValue(s.Source == ScanPaperSource.Duplex ? BoolType.True : BoolType.False); });
        Try(() => { if (caps.CapXferCount.CanSet) caps.CapXferCount.SetValue(s.SinglePage ? 1 : -1); });
        Try(() =>
        {
            // A region picked on the preview (inches from the top-left of the bed).
            if (s.Region is not { } r || !caps.ICapFrames.CanSet) return;
            if (caps.ICapUnits.CanSet) caps.ICapUnits.SetValue(Unit.Inches);
            caps.ICapFrames.SetValue(new TWFrame
            {
                Left = (float)r.Left, Top = (float)r.Top, Right = (float)r.Right, Bottom = (float)r.Bottom,
            });
        });
        Try(() =>
        {
            if (s.Region != null || !caps.ICapSupportedSizes.CanSet || s.PaperSize == "Auto") return;
            caps.ICapSupportedSizes.SetValue(s.PaperSize switch
            {
                "A4" => SupportedSize.A4,
                "A5" => SupportedSize.A5,
                "Letter" => SupportedSize.USLetter,
                "Legal" => SupportedSize.USLegal,
                _ => SupportedSize.None,
            });
        });
        // TWAIN brightness / contrast run -1000…1000
        Try(() => { if (s.Brightness != 0 && caps.ICapBrightness.CanSet) caps.ICapBrightness.SetValue(s.Brightness * 10); });
        Try(() => { if (s.Contrast != 0 && caps.ICapContrast.CanSet) caps.ICapContrast.SetValue(s.Contrast * 10); });
        Try(() => { if (caps.CapIndicators.CanSet) caps.CapIndicators.SetValue(BoolType.True); });
    }

    private static void Try(Action a) { try { a(); } catch { } }

    // ── WIA (late-bound COM: WIA Automation ships with Windows) ─────────────

    private const string WiaFormatPng = "{B96B3CAF-0728-11D3-9D7B-0000F81EF32E}";
    private const int WiaPaperEmpty = unchecked((int)0x80210003);

    private static dynamic NewWia(string progId) =>
        Activator.CreateInstance(Type.GetTypeFromProgID(progId)
            ?? throw new InvalidOperationException("Windows Image Acquisition (WIA) is not available."))!;

    private static IEnumerable<ScanDevice> ListWiaDevices()
    {
        var list = new List<ScanDevice>();
        dynamic manager = NewWia("WIA.DeviceManager");
        foreach (dynamic info in manager.DeviceInfos)
        {
            if ((int)info.Type != 1) continue;   // 1 = scanner
            string name = (string)info.Properties["Name"].get_Value();
            list.Add(new ScanDevice(name, ScanDriver.Wia, (string)info.DeviceID));
        }
        return list;
    }

    // WIA blocks while it scans, so it runs on its own STA thread; pages are posted back.
    private static Task<int> ScanWiaAsync(ScanDevice device, ScanSettings s, Action<ScannedPage> onPage, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<int>();
        var ui = SynchronizationContext.Current;
        var thread = new Thread(() =>
        {
            int pages = 0;
            try
            {
                dynamic manager = NewWia("WIA.DeviceManager");
                dynamic? info = null;
                foreach (dynamic d in manager.DeviceInfos)
                    if ((string)d.DeviceID == device.Id) { info = d; break; }
                if (info == null) throw new InvalidOperationException($"Scanner \"{device.Name}\" was not found.");
                dynamic dev = info.Connect();

                if (s.ShowDriverUi)
                {
                    // The scanner's own WIA dialog (Windows' scan dialog with preview) for this device.
                    dynamic dlg = NewWia("WIA.CommonDialog");
                    dynamic? items = dlg.ShowSelectItems(dev, IntentFor(s.Color), 0, true, true, false);
                    if (items != null)
                        foreach (dynamic it in items)
                        {
                            dynamic? img = dlg.ShowTransfer(it, WiaFormatPng, false);
                            if (img != null) Deliver(img);
                        }
                }
                else
                {
                    // Paper source: 1 = feeder, 2 = flatbed, 4 = duplex (WIA_DPS_DOCUMENT_HANDLING_SELECT)
                    SetWiaProp(dev.Properties, 3088, s.Source switch
                    {
                        ScanPaperSource.Feeder => 1, ScanPaperSource.Duplex => 1 | 4, _ => 2,
                    });
                    dynamic item = dev.Items[1];
                    SetWiaProp(item.Properties, 6146, IntentFor(s.Color));     // colour intent
                    SetWiaProp(item.Properties, 6147, s.Dpi);                    // horizontal DPI
                    SetWiaProp(item.Properties, 6148, s.Dpi);                    // vertical DPI
                    if (s.Brightness != 0) SetWiaProp(item.Properties, 6154, s.Brightness * 10);
                    if (s.Contrast != 0) SetWiaProp(item.Properties, 6155, s.Contrast * 10);
                    // Scan area in pixels at the chosen dpi: X/Y position 6149/6150, width/height 6151/6152
                    var area = s.Region ?? (PaperInches(s.PaperSize) is { } p ? new System.Windows.Rect(0, 0, p.Width, p.Height) : (System.Windows.Rect?)null);
                    if (area is { } a && s.Source == ScanPaperSource.Flatbed)
                    {
                        SetWiaProp(item.Properties, 6149, (int)(a.X * s.Dpi));
                        SetWiaProp(item.Properties, 6150, (int)(a.Y * s.Dpi));
                        SetWiaProp(item.Properties, 6151, (int)(a.Width * s.Dpi));
                        SetWiaProp(item.Properties, 6152, (int)(a.Height * s.Dpi));
                    }

                    bool feeder = s.Source != ScanPaperSource.Flatbed;
                    do
                    {
                        if (ct.IsCancellationRequested) break;
                        try
                        {
                            Deliver(item.Transfer(WiaFormatPng));
                        }
                        catch (COMException ex) when (ex.HResult == WiaPaperEmpty)
                        {
                            if (pages == 0) throw new InvalidOperationException("The document feeder is empty.");
                            break;
                        }
                    }
                    while (feeder && !s.SinglePage);
                }
                tcs.TrySetResult(pages);
            }
            catch (Exception ex) { if (pages > 0) tcs.TrySetResult(pages); else tcs.TrySetException(ex); }

            void Deliver(dynamic image)
            {
                byte[] bytes = (byte[])image.FileData.get_BinaryData();
                var frame = BitmapFrame.Create(new MemoryStream(bytes), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                frame.Freeze();
                pages++;
                var page = new ScannedPage(frame, ImageDpi(frame, s.Dpi));
                if (ui != null) ui.Post(_ => onPage(page), null); else onPage(page);
            }
        }) { IsBackground = true, Name = "WIA scan" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return tcs.Task;
    }

    private static int IntentFor(ScanColorMode c) => c switch
    {
        ScanColorMode.Gray => 2,
        ScanColorMode.BlackWhite => 4,
        _ => 1,
    };

    private static void SetWiaProp(dynamic props, int id, object value)
    {
        try
        {
            foreach (dynamic p in props)
                if ((int)p.PropertyID == id) { p.set_Value(value); return; }
        }
        catch { /* not supported by this scanner */ }
    }

    // ── Sizes, cropping and saving ───────────────────────────────────────────

    /// <summary>The image's own dpi, unless it's missing or the 96 dpi placeholder.</summary>
    internal static double ImageDpi(BitmapSource img, double fallback) =>
        img.DpiX > 1 && Math.Abs(img.DpiX - 96) > 0.5 ? img.DpiX : fallback;

    /// <summary>Page size in inches (portrait), or null for "Auto".</summary>
    public static System.Windows.Size? PaperInches(string paper) => paper switch
    {
        "A4" => new System.Windows.Size(8.27, 11.69),
        "A5" => new System.Windows.Size(5.83, 8.27),
        "Letter" => new System.Windows.Size(8.5, 11),
        "Legal" => new System.Windows.Size(8.5, 14),
        _ => null,
    };

    /// <summary>
    /// Crops a scanned page to <paramref name="region"/> (inches from the bed's top-left) when the
    /// driver ignored the requested scan area and sent the whole bed. Pages that already match are
    /// returned unchanged.
    /// </summary>
    public static ScannedPage FitToRegion(ScannedPage page, System.Windows.Rect region)
    {
        double dpi = page.Dpi > 1 ? page.Dpi : 200;
        int w = page.Image.PixelWidth, h = page.Image.PixelHeight;
        double expectW = region.Width * dpi, expectH = region.Height * dpi;
        if (w <= expectW * 1.05 && h <= expectH * 1.05) return page;   // the scanner did it
        var px = new System.Windows.Int32Rect(
            (int)Math.Clamp(region.X * dpi, 0, w - 1), (int)Math.Clamp(region.Y * dpi, 0, h - 1), 0, 0);
        px.Width = (int)Math.Clamp(Math.Min(expectW, w), 1, w - px.X);
        px.Height = (int)Math.Clamp(Math.Min(expectH, h), 1, h - px.Y);
        return Crop(page, px);
    }

    /// <summary>Crops a page to a rectangle in image pixels.</summary>
    public static ScannedPage Crop(ScannedPage page, System.Windows.Int32Rect px)
    {
        var cropped = new CroppedBitmap(page.Image, px);
        cropped.Freeze();
        return page with { Image = cropped };
    }

    public static string Extension(ScanFileType t) => t switch
    {
        ScanFileType.Png => ".png",
        ScanFileType.Jpeg => ".jpg",
        ScanFileType.Tiff => ".tif",
        ScanFileType.Bmp => ".bmp",
        _ => ".pdf",
    };

    public static string FileFilter(ScanFileType t) => t switch
    {
        ScanFileType.Png => "PNG image (*.png)|*.png",
        ScanFileType.Jpeg => "JPEG image (*.jpg)|*.jpg;*.jpeg",
        ScanFileType.Tiff => "TIFF image (*.tif)|*.tif;*.tiff",
        ScanFileType.Bmp => "Bitmap image (*.bmp)|*.bmp",
        _ => "PDF Files (*.pdf)|*.pdf",
    };

    /// <summary>
    /// Saves scanned pages as image files. TIFF keeps every page in one file; other types write
    /// one file per page ("name.png", "name (2).png", …). Returns the files written.
    /// </summary>
    public static List<string> SaveImages(IReadOnlyList<ScannedPage> pages, string path, ScanFileType type, ScanColorMode color)
    {
        var written = new List<string>();
        if (type == ScanFileType.Tiff)
        {
            var tiff = new TiffBitmapEncoder
            {
                Compression = color == ScanColorMode.BlackWhite ? TiffCompressOption.Ccitt4 : TiffCompressOption.Lzw,
            };
            foreach (var p in pages) tiff.Frames.Add(BitmapFrame.Create(Prepare(p, type, color)));
            using (var fs = File.Create(path)) tiff.Save(fs);
            written.Add(path);
            return written;
        }

        string dir = Path.GetDirectoryName(path) ?? ".", name = Path.GetFileNameWithoutExtension(path), ext = Path.GetExtension(path);
        for (int i = 0; i < pages.Count; i++)
        {
            string file = i == 0 ? path : Path.Combine(dir, $"{name} ({i + 1}){ext}");
            BitmapEncoder enc = type switch
            {
                ScanFileType.Jpeg => new JpegBitmapEncoder { QualityLevel = 90 },
                ScanFileType.Bmp => new BmpBitmapEncoder(),
                _ => new PngBitmapEncoder(),
            };
            enc.Frames.Add(BitmapFrame.Create(Prepare(pages[i], type, color)));
            using (var fs = File.Create(file)) enc.Save(fs);
            written.Add(file);
        }
        return written;
    }

    // Converts to the colour mode picked (scanners don't always honour it) and keeps the scan dpi.
    private static BitmapSource Prepare(ScannedPage p, ScanFileType type, ScanColorMode color)
    {
        BitmapSource img = p.Image;
        var target = color switch
        {
            ScanColorMode.BlackWhite when type != ScanFileType.Jpeg => System.Windows.Media.PixelFormats.BlackWhite,
            ScanColorMode.BlackWhite or ScanColorMode.Gray => System.Windows.Media.PixelFormats.Gray8,
            _ => System.Windows.Media.PixelFormats.Bgr24,
        };
        if (img.Format != target) img = new FormatConvertedBitmap(img, target, null, 0);

        double dpi = p.Dpi > 1 ? p.Dpi : 200;
        if (Math.Abs(img.DpiX - dpi) > 0.5)
        {
            int stride = (img.PixelWidth * img.Format.BitsPerPixel + 7) / 8;
            var buffer = new byte[stride * img.PixelHeight];
            img.CopyPixels(buffer, stride, 0);
            img = BitmapSource.Create(img.PixelWidth, img.PixelHeight, dpi, dpi, img.Format, img.Palette, buffer, stride);
        }
        img.Freeze();
        return img;
    }
}
