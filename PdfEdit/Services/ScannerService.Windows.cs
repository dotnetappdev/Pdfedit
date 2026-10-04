using System.IO;
using System.Windows.Media.Imaging;
using WinEnum = Windows.Devices.Enumeration;
using WinPrint = Windows.Graphics.Printing;
using WinScan = Windows.Devices.Scanners;
using WinStorage = Windows.Storage;
using WinStreams = Windows.Storage.Streams;

namespace PdfEdit.Services;

/// <summary>
/// The scanner API the Windows Scan app uses (Windows.Devices.Scanners). It finds every scanner
/// Windows knows about — USB, and network scanners reached over WSD / eSCL such as most current HP,
/// Canon, Epson and Brother all-in-ones, which often have no TWAIN driver and don't show up in WIA
/// Automation — and reports what each one supports: flatbed / feeder / duplex, colour modes,
/// resolutions, file types and auto-crop.
/// </summary>
public static partial class ScannerService
{
    private static async Task<List<ScanDevice>> ListWindowsDevicesAsync()
    {
        var found = await WinEnum.DeviceInformation.FindAllAsync(WinScan.ImageScanner.GetDeviceSelector());
        return found
            .Where(d => d.IsEnabled && !string.IsNullOrWhiteSpace(d.Name))
            .Select(d => new ScanDevice(d.Name, ScanDriver.Windows, d.Id))
            .ToList();
    }

    private static async Task<ScanCapabilities> GetWindowsCapabilitiesAsync(ScanDevice device)
    {
        var scanner = await WinScan.ImageScanner.FromIdAsync(device.Id)
            ?? throw new InvalidOperationException($"Scanner \"{device.Name}\" is not available.");
        var caps = new ScanCapabilities
        {
            Flatbed = scanner.IsScanSourceSupported(WinScan.ImageScannerScanSource.Flatbed),
            Feeder = scanner.IsScanSourceSupported(WinScan.ImageScannerScanSource.Feeder),
            Known = true,
        };
        caps.Duplex = caps.Feeder && scanner.FeederConfiguration.CanScanDuplex;

        WinScan.IImageScannerSourceConfiguration? cfg =
            caps.Flatbed ? scanner.FlatbedConfiguration : caps.Feeder ? scanner.FeederConfiguration : null;
        WinScan.IImageScannerFormatConfiguration formats = (WinScan.IImageScannerFormatConfiguration?)cfg ?? scanner.AutoConfiguration;

        caps.FileTypes.Clear();
        caps.FileTypes.Add(ScanFileType.Pdf);   // PDFs are always made here, from the scanned images
        if (formats.IsFormatSupported(WinScan.ImageScannerFormat.Png)) caps.FileTypes.Add(ScanFileType.Png);
        if (formats.IsFormatSupported(WinScan.ImageScannerFormat.Jpeg)) caps.FileTypes.Add(ScanFileType.Jpeg);
        if (formats.IsFormatSupported(WinScan.ImageScannerFormat.Tiff)) caps.FileTypes.Add(ScanFileType.Tiff);
        if (formats.IsFormatSupported(WinScan.ImageScannerFormat.DeviceIndependentBitmap)) caps.FileTypes.Add(ScanFileType.Bmp);
        if (caps.FileTypes.Count == 1)   // the scanner didn't say — everything can be re-encoded anyway
            caps.FileTypes.AddRange(new[] { ScanFileType.Png, ScanFileType.Jpeg, ScanFileType.Tiff, ScanFileType.Bmp });

        if (cfg != null)
        {
            caps.ColorModes.Clear();
            if (cfg.IsColorModeSupported(WinScan.ImageScannerColorMode.Color)) caps.ColorModes.Add(ScanColorMode.Color);
            if (cfg.IsColorModeSupported(WinScan.ImageScannerColorMode.Grayscale)) caps.ColorModes.Add(ScanColorMode.Gray);
            if (cfg.IsColorModeSupported(WinScan.ImageScannerColorMode.Monochrome)) caps.ColorModes.Add(ScanColorMode.BlackWhite);
            if (caps.ColorModes.Count == 0) caps.ColorModes.Add(ScanColorMode.Color);

            caps.MinDpi = (int)Math.Floor(Math.Max(1, cfg.MinResolution.DpiX));
            caps.MaxDpi = (int)Math.Ceiling(Math.Max(caps.MinDpi, cfg.MaxResolution.DpiX));
            caps.CanAutoCrop = cfg.IsAutoCroppingModeSupported(WinScan.ImageScannerAutoCroppingMode.SingleRegion);
            caps.MaxArea = new System.Windows.Size(cfg.MaxScanArea.Width, cfg.MaxScanArea.Height);
        }
        caps.CanPreview = (caps.Flatbed && scanner.IsPreviewSupported(WinScan.ImageScannerScanSource.Flatbed))
                       || (caps.Feeder && scanner.IsPreviewSupported(WinScan.ImageScannerScanSource.Feeder));
        return caps;
    }

    private static async Task<int> ScanWindowsAsync(ScanDevice device, ScanSettings s, Action<ScannedPage> onPage,
                                                    Action<string>? onStatus, CancellationToken ct)
    {
        var scanner = await WinScan.ImageScanner.FromIdAsync(device.Id)
            ?? throw new InvalidOperationException($"Scanner \"{device.Name}\" was not found — is it switched on and connected?");

        bool flatbedOk = scanner.IsScanSourceSupported(WinScan.ImageScannerScanSource.Flatbed);
        bool feederOk = scanner.IsScanSourceSupported(WinScan.ImageScannerScanSource.Feeder);
        var source = s.Source != ScanPaperSource.Flatbed && feederOk ? WinScan.ImageScannerScanSource.Feeder
                   : flatbedOk ? WinScan.ImageScannerScanSource.Flatbed
                   : feederOk ? WinScan.ImageScannerScanSource.Feeder
                   : WinScan.ImageScannerScanSource.AutoConfigured;

        WinScan.IImageScannerSourceConfiguration? cfg = source switch
        {
            WinScan.ImageScannerScanSource.Flatbed => scanner.FlatbedConfiguration,
            WinScan.ImageScannerScanSource.Feeder => scanner.FeederConfiguration,
            _ => null,
        };
        if (cfg != null) ConfigureWindowsSource(cfg, s);
        if (source == WinScan.ImageScannerScanSource.Feeder)
        {
            var feeder = scanner.FeederConfiguration;
            Try(() => feeder.Duplex = s.Source == ScanPaperSource.Duplex && feeder.CanScanDuplex);
            Try(() => feeder.MaxNumberOfPages = s.SinglePage ? 1u : 0u);   // 0 = until the feeder is empty
            if (FeederPageSize(s.PaperSize) is { } size)
                Try(() =>
                {
                    if (feeder.IsPageSizeSupported(size, WinPrint.PrintOrientation.Portrait))
                    {
                        feeder.PageSize = size;
                        feeder.PageOrientation = WinPrint.PrintOrientation.Portrait;
                    }
                });
        }

        // A preview scan uses the driver's quick preview when it has one.
        if (s.SinglePage && source != WinScan.ImageScannerScanSource.AutoConfigured && scanner.IsPreviewSupported(source))
        {
            using var mem = new WinStreams.InMemoryRandomAccessStream();
            var preview = await scanner.ScanPreviewToStreamAsync(source, mem).AsTask(ct);
            if (preview.Succeeded)
            {
                mem.Seek(0);
                var frame = BitmapFrame.Create(mem.AsStream(), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                frame.Freeze();
                // Previews cover the whole scan bed, so their dpi follows from the bed's width.
                double dpi = cfg != null && cfg.MaxScanArea.Width > 0 ? frame.PixelWidth / cfg.MaxScanArea.Width : ImageDpi(frame, 75);
                onPage(new ScannedPage(frame, dpi));
                return 1;
            }
        }

        string folder = Path.Combine(Path.GetTempPath(), "pdfedit-scan-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var storage = await WinStorage.StorageFolder.GetFolderFromPathAsync(folder);
            var progress = new Progress<uint>(n => onStatus?.Invoke($"Scanning page {n + 1}…"));
            var result = await scanner.ScanFilesToFolderAsync(source, storage).AsTask(ct, progress);

            int pages = 0;
            foreach (var file in result.ScannedFiles.OrderBy(f => f.DateCreated))
            {
                var bytes = await File.ReadAllBytesAsync(file.Path, ct);
                var decoder = BitmapDecoder.Create(new MemoryStream(bytes), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                foreach (var frame in decoder.Frames)   // TIFF scans can hold several pages
                {
                    frame.Freeze();
                    pages++;
                    onPage(new ScannedPage(frame, ImageDpi(frame, s.Dpi)));
                }
            }
            return pages;
        }
        finally
        {
            try { Directory.Delete(folder, recursive: true); } catch { }
        }
    }

    private static void ConfigureWindowsSource(WinScan.IImageScannerSourceConfiguration cfg, ScanSettings s)
    {
        // Each setting is best effort — scanners support different subsets.
        Try(() =>
        {
            var mode = s.Color switch
            {
                ScanColorMode.Gray => WinScan.ImageScannerColorMode.Grayscale,
                ScanColorMode.BlackWhite => WinScan.ImageScannerColorMode.Monochrome,
                _ => WinScan.ImageScannerColorMode.Color,
            };
            if (cfg.IsColorModeSupported(mode)) cfg.ColorMode = mode;
        });
        Try(() =>
        {
            float dpi = Math.Clamp(s.Dpi, cfg.MinResolution.DpiX, cfg.MaxResolution.DpiX);
            cfg.DesiredResolution = new WinScan.ImageScannerResolution { DpiX = dpi, DpiY = dpi };
        });
        Try(() =>
        {
            // Transfer losslessly where possible; the dialog saves in the file type you pick.
            var format = new[] { WinScan.ImageScannerFormat.Png, WinScan.ImageScannerFormat.Tiff,
                                 WinScan.ImageScannerFormat.DeviceIndependentBitmap, WinScan.ImageScannerFormat.Jpeg }
                .FirstOrDefault(cfg.IsFormatSupported, cfg.DefaultFormat);
            cfg.Format = format;
        });
        Try(() =>
        {
            cfg.AutoCroppingMode = s.AutoCrop && cfg.IsAutoCroppingModeSupported(WinScan.ImageScannerAutoCroppingMode.SingleRegion)
                ? WinScan.ImageScannerAutoCroppingMode.SingleRegion
                : WinScan.ImageScannerAutoCroppingMode.Disabled;
        });
        Try(() =>
        {
            // Scan area (inches): a region picked on the preview, else the page size, else the whole bed.
            var max = cfg.MaxScanArea;
            var region = s.Region ?? (PaperInches(s.PaperSize) is { } p ? new System.Windows.Rect(0, 0, p.Width, p.Height) : (System.Windows.Rect?)null);
            if (s.AutoCrop || region is not { } r || max.Width <= 0 || max.Height <= 0) return;
            double x = Math.Clamp(r.X, 0, max.Width), y = Math.Clamp(r.Y, 0, max.Height);
            double w = Math.Min(r.Width, max.Width - x), h = Math.Min(r.Height, max.Height - y);
            if (w > 0.1 && h > 0.1) cfg.SelectedScanRegion = new Windows.Foundation.Rect(x, y, w, h);
        });
        // Our sliders run -100…100 around the driver's default.
        Try(() => { if (s.Brightness != 0) cfg.Brightness = Scale(s.Brightness, cfg.MinBrightness, cfg.DefaultBrightness, cfg.MaxBrightness, cfg.BrightnessStep); });
        Try(() => { if (s.Contrast != 0) cfg.Contrast = Scale(s.Contrast, cfg.MinContrast, cfg.DefaultContrast, cfg.MaxContrast, cfg.ContrastStep); });
    }

    private static int Scale(int value, int min, int def, int max, uint step)
    {
        double v = value > 0 ? def + (max - def) * value / 100.0 : def + (def - min) * value / 100.0;
        int st = (int)Math.Max(1, step);
        return Math.Clamp((int)Math.Round((v - min) / st) * st + min, min, max);
    }

    private static WinPrint.PrintMediaSize? FeederPageSize(string paper) => paper switch
    {
        "A4" => WinPrint.PrintMediaSize.IsoA4,
        "A5" => WinPrint.PrintMediaSize.IsoA5,
        "Letter" => WinPrint.PrintMediaSize.NorthAmericaLetter,
        "Legal" => WinPrint.PrintMediaSize.NorthAmericaLegal,
        _ => null,
    };
}
