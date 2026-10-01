using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;
using NTwain;
using NTwain.Data;

namespace PdfEdit.Services;

public enum ScanDriver { Twain, Wia }
public enum ScanColorMode { Color, Gray, BlackWhite }
public enum ScanPaperSource { Flatbed, Feeder, Duplex }

/// <summary>A scanner, reached through TWAIN or Windows Image Acquisition (WIA).</summary>
public sealed record ScanDevice(string Name, ScanDriver Driver, string Id)
{
    public string Display => $"{Name}  ({(Driver == ScanDriver.Twain ? "TWAIN" : "WIA")})";
    public override string ToString() => Display;
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
}

/// <summary>One scanned page and the resolution it was scanned at.</summary>
public sealed record ScannedPage(BitmapSource Image, double Dpi);

/// <summary>
/// Scanning from TWAIN scanners (via NTwain and the TWAIN DSM that scanner drivers install) and
/// WIA scanners (built into Windows — many newer scanners only ship WIA drivers). Pages are
/// returned as they arrive, so the dialog can show each one while a feeder is still running.
/// </summary>
public static class ScannerService
{
    // ── Devices ──────────────────────────────────────────────────────────────

    /// <summary>All TWAIN and WIA scanners. TWAIN needs the UI thread's window handle.</summary>
    public static List<ScanDevice> ListDevices(IntPtr hwnd, out string? note)
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
                                      Action<ScannedPage> onPage, CancellationToken ct) =>
        device.Driver == ScanDriver.Twain
            ? ScanTwainAsync(device, settings, hwnd, onPage, ct)
            : ScanWiaAsync(device, settings, onPage, ct);

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
                onPage(new ScannedPage(frame, frame.DpiX > 1 && Math.Abs(frame.DpiX - 96) > 0.5 ? frame.DpiX : s.Dpi));
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
            if (!caps.ICapSupportedSizes.CanSet || s.PaperSize == "Auto") return;
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
                    // The scanner's own WIA dialog (Windows' scan dialog with preview).
                    dynamic dlg = NewWia("WIA.CommonDialog");
                    dynamic img = dlg.ShowAcquireImage(1, IntentFor(s.Color), 0, WiaFormatPng, false, true, false);
                    if (img != null) Deliver(img);
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
                double dpi = frame.DpiX > 1 && Math.Abs(frame.DpiX - 96) > 0.5 ? frame.DpiX : s.Dpi;
                var page = new ScannedPage(frame, dpi);
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
}
