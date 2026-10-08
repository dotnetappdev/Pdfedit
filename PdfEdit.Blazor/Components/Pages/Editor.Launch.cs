using Microsoft.AspNetCore.WebUtilities;
using Microsoft.JSInterop;
using PdfEdit.Blazor.Components.Editor;

namespace PdfEdit.Blazor.Components.Pages;

/// <summary>
/// Links into PdfEdit: <c>?url=https://…/form.pdf</c> opens a PDF from the web, <c>?page=4</c> (or
/// <c>#page=4</c>, as PDF viewers use) goes to a page, and <c>?new=blank</c> / <c>?new=design</c>
/// start something new (the installed app's jump list).
/// </summary>
public partial class Editor
{
    private async Task StartFromLinkAsync()
    {
        var uri = new Uri(Nav.Uri);
        var query = QueryHelpers.ParseQuery(uri.Query);
        bool ours = query.ContainsKey("new") || query.ContainsKey("url") || query.ContainsKey("handoff") || query.ContainsKey("page")
                    || uri.Fragment.StartsWith("#page=", StringComparison.OrdinalIgnoreCase);
        if (!ours) return;

        if (query.TryGetValue("new", out var what))
        {
            if (what == "blank") await NewBlankAsync();
            else if (what == "design") NewDesignPage();
        }
        else if (query.TryGetValue("handoff", out var code) && code.ToString() is { Length: 32 } c)
        {
            await RunAsync("Opening the document from your other device…", async () =>
            {
                if (await Store.OpenHandoffAsync(c) is { } session) { SetDocument(session); Status($"Opened {session.FileName} from your other device"); }
                else Toast("That link has expired (they work for 15 minutes). Make a new one with Continue on Phone.", "error");
            });
        }
        else if (query.TryGetValue("url", out var url) && !string.IsNullOrWhiteSpace(url))
        {
            await ImportGoogleLinkAsync(url.ToString());
        }

        if (PageFromLink(uri, query) is { } page && Doc != null)
        {
            _page = Math.Clamp(page - 1, 0, Math.Max(0, PageCount - 1));
            _rememberedViewFor = null;
            _fitOnOpen = false;
            _restoreScroll = -1;
        }
        // The address bar goes back to plain PdfEdit, so a reload doesn't open the link again.
        Nav.NavigateTo(uri.GetLeftPart(UriPartial.Path), replace: true);
        StateHasChanged();
    }

    private static int? PageFromLink(Uri uri, Dictionary<string, Microsoft.Extensions.Primitives.StringValues> query)
    {
        string? text = query.TryGetValue("page", out var p) ? p.ToString() : null;
        if (text == null && uri.Fragment.StartsWith("#page=", StringComparison.OrdinalIgnoreCase)) text = uri.Fragment[6..];
        return int.TryParse(text, out int n) && n > 0 ? n : null;
    }

    /// <summary>A link that opens this PDF at <paramref name="page"/> (0-based) — only for a PDF opened from the web.</summary>
    public string? LinkToPage(int page) =>
        Doc?.SourceUrl is { } src ? $"{Nav.BaseUri}?url={Uri.EscapeDataString(src)}&page={page + 1}" : null;

    /// <summary>Copies a link to a page: anyone with it opens the same PDF at that page.</summary>
    public async Task CopyLinkToPageAsync(int page)
    {
        if (LinkToPage(page) is not { } link)
        {
            Toast("Links to a page work for PDFs opened from the web (From Link). This one came from your computer, so others can't open it from a link.", "error");
            return;
        }
        try
        {
            await JS.InvokeVoidAsync("navigator.clipboard.writeText", link);
            Status($"Link to page {page + 1} copied");
        }
        catch { Toast("Couldn't copy to the clipboard: " + link); }
    }

    // ── Continue on Phone ────────────────────────────────────────────────────

    /// <summary>The link and QR code shown by Continue on Phone.</summary>
    public (string Link, string QrDataUrl, bool LocalOnly)? Handoff { get; private set; }

    /// <summary>
    /// Continue on Phone: applies what's pending and shows a QR code that opens a copy of this PDF on
    /// a phone or tablet (to sign with a finger, say), at the same page.
    /// </summary>
    public async Task ContinueOnPhoneAsync()
    {
        if (Doc == null) return;
        await RunAsync("Making a link for your phone…", async () =>
        {
            await CommitPendingAsync();
            string code = Store.CreateHandoff(Doc);
            string link = $"{Nav.BaseUri}?handoff={code}&page={_page + 1}";
            using var qr = new QRCoder.QRCodeGenerator();
            using var data = qr.CreateQrCode(link, QRCoder.QRCodeGenerator.ECCLevel.M);
            var png = new QRCoder.PngByteQRCode(data).GetGraphic(8);
            var host = new Uri(Nav.BaseUri).Host;
            bool localOnly = host is "localhost" or "127.0.0.1" or "[::1]" or "::1";
            Handoff = (link, "data:image/png;base64," + Convert.ToBase64String(png), localOnly);
            _dialog = DialogKind.Handoff;
        });
    }
}
