using System.Globalization;
using Microsoft.JSInterop;

namespace PdfEdit.Blazor.Components.Pages;

/// <summary>
/// Things a browser can do that the Windows app doesn't: share the PDF through the device's share
/// sheet, dictate into fields and notes, and add where you are to dynamic stamps.
/// </summary>
public partial class Editor
{
    // ── Share ────────────────────────────────────────────────────────────────

    /// <summary>Share: applies what's pending and opens the share sheet with the PDF (or downloads it).</summary>
    public async Task ShareAsync()
    {
        if (Doc == null) return;
        await RunAsync("Getting the PDF ready to share…", async () =>
        {
            await CommitPendingAsync();
            var result = await JS.InvokeAsync<string>("pdfedit.share", $"documents/{Doc.Id}/file?v={Doc.Version}", Doc.FileName);
            switch (result)
            {
                case "shared":
                    Doc.IsModified = false;
                    Status($"Shared {Doc.FileName}");
                    break;
                case "cancelled":
                    Status("Sharing cancelled");
                    break;
                case "unsupported":
                    await DownloadUrlAsync($"/documents/{Doc.Id}/file?v={Doc.Version}");
                    Doc.IsModified = false;
                    Toast("This browser can't share files, so the PDF was downloaded instead.");
                    break;
                default:
                    Toast("Couldn't share the PDF. Try Save to download it.", "error");
                    break;
            }
        });
    }

    // ── Dictation ────────────────────────────────────────────────────────────

    public bool Dictating { get; private set; }

    /// <summary>Dictate: turns the microphone on or off. What's said is typed where you last clicked.</summary>
    public async Task ToggleDictationAsync()
    {
        if (Dictating)
        {
            Dictating = false;
            await JS.InvokeVoidAsync("pdfedit.dictation.stop");
            Status("Dictation off");
            return;
        }
        if (!await JS.InvokeAsync<bool>("pdfedit.dictation.start", _self))
        {
            Toast("Your browser can't listen for speech. Try Chrome, Edge or Safari.", "error");
            return;
        }
        Dictating = true;
        Status("Dictating — click a field, note or text box and speak. Say “comma”, “full stop” or “new line” for punctuation.");
    }

    [JSInvokable]
    public async Task OnDictation(string kind, string text)
    {
        await InvokeAsync(() =>
        {
            switch (kind)
            {
                case "typed":
                    Status($"Dictated: “{(text.Length > 60 ? text[..60] + "…" : text)}”");
                    break;
                case "nowhere":
                    Status($"Heard “{text}” — click a field, note or text box to type into first");
                    break;
                case "error":
                    Dictating = false;
                    Status(text == "not-allowed" ? "Dictation off: the browser wasn't allowed to use the microphone" : $"Dictation stopped ({text})");
                    break;
                default:
                    Dictating = false;
                    Status("Dictation off");
                    break;
            }
            StateHasChanged();
        });
    }

    // ── Location on dynamic stamps ───────────────────────────────────────────

    private sealed record BrowserPlace(double? Lat, double? Lon, double? Accuracy, string? Error);
    private bool _locationWarned;

    /// <summary>"51.5074° N, 0.1278° W" when Settings → Add my location is on and the browser allows it.</summary>
    private async Task<string?> StampLocationAsync()
    {
        if (!Settings.StampLocation) return null;
        try
        {
            Status("Finding where you are for the stamp…");
            var place = await JS.InvokeAsync<BrowserPlace>("pdfedit.location");
            if (place.Lat is { } lat && place.Lon is { } lon) return FormatPlace(lat, lon);
            if (!_locationWarned)
            {
                _locationWarned = true;
                Toast(place.Error == "denied"
                    ? "The browser isn't allowed to tell PdfEdit where you are, so the stamp has no location. Allow location for this site, or turn it off in Settings."
                    : "Couldn't find where you are, so the stamp has no location.", "error");
            }
        }
        catch { /* no location: the stamp goes on without it */ }
        return null;
    }

    public static string FormatPlace(double lat, double lon) => string.Create(CultureInfo.InvariantCulture,
        $"{Math.Abs(lat):0.0000}° {(lat >= 0 ? 'N' : 'S')}, {Math.Abs(lon):0.0000}° {(lon >= 0 ? 'E' : 'W')}");

    // ── Done while you were away ─────────────────────────────────────────────

    private static readonly TimeSpan LongJob = TimeSpan.FromSeconds(8);

    /// <summary>
    /// After a long job (OCR, batch, translating…): if the tab isn't being looked at, its title flashes
    /// "✓ Done" and, when the browser allows it, a notification says what finished.
    /// </summary>
    private async Task NotifyIfAwayAsync(string what, DateTime startedUtc, bool ok = true)
    {
        if (DateTime.UtcNow - startedUtc < LongJob) return;
        try { await JS.InvokeVoidAsync("pdfedit.notifyDone", what, ok, Settings.NotifyWhenDone); } catch { }
    }

    /// <summary>Settings → notifications: asks the browser's permission (from the click on the box).</summary>
    public async Task<string> AskNotificationPermissionAsync()
    {
        try { return await JS.InvokeAsync<string>("pdfedit.askNotify"); } catch { return "unsupported"; }
    }
}
