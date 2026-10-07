using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;
using PdfEdit.Blazor.Components.Editor;
using PdfEdit.Blazor.Services;
using PdfEdit.Models;
using PdfEdit.Render;
using PdfEdit.Services;

namespace PdfEdit.Blazor.Components.Pages;

/// <summary>One page of a comparison: the two versions, the difference picture and the text changes.</summary>
public sealed record ComparePage(int Page, string? OldUrl, string? NewUrl, string DiffUrl, double ChangedPercent, PdfPageDiff? Text);

/// <summary>
/// Compare (the Windows app's Compare PDFs and Visual Compare) and Read Aloud (the browser's own
/// voices, like the Windows app's Windows voices).
/// </summary>
public partial class Editor
{
    public List<ComparePage>? Comparison { get; private set; }
    public string CompareName { get; private set; } = "";

    /// <summary>Compares the open PDF (old) with an uploaded newer version, page by page.</summary>
    public async Task CompareWithAsync(IBrowserFile file)
    {
        if (Doc == null) return;
        await RunAsync("Comparing…", async () =>
        {
            await CommitPendingAsync();
            var folder = Directory.CreateTempSubdirectory("pdfedit-compare-").FullName;
            try
            {
                var newer = (await SaveUploadsAsync([file], folder))[0];
                var older = Doc.CurrentPath;
                var textDiffs = await Task.Run(() => Store.Forms.ComparePdfs(older, newer));
                using var a = new PdfiumRenderEngine();
                using var b = new PdfiumRenderEngine();
                await a.LoadAsync(older);
                await b.LoadAsync(newer);
                int pages = Math.Max(a.PageCount, b.PageCount);
                var result = new List<ComparePage>();
                for (int i = 0; i < pages; i++)
                {
                    Status($"Comparing page {i + 1} of {pages}…");
                    StateHasChanged();
                    RenderedPage? ra = i < a.PageCount ? await a.RenderPageAsync(i, 1.0, 1.0) : null;
                    RenderedPage? rb = i < b.PageCount ? await b.RenderPageAsync(i, 1.0, 1.0) : null;
                    int w = Math.Max(ra?.PixelWidth ?? 0, rb?.PixelWidth ?? 0), h = Math.Max(ra?.PixelHeight ?? 0, rb?.PixelHeight ?? 0);
                    if (w == 0 || h == 0) { w = h = 1; }
                    var (diff, pct) = await Task.Run(() => VisualDiff.Diff(
                        VisualDiff.GrayFromBgra(ra?.Pixels, ra?.PixelWidth ?? 0, ra?.PixelHeight ?? 0, w, h),
                        VisualDiff.GrayFromBgra(rb?.Pixels, rb?.PixelWidth ?? 0, rb?.PixelHeight ?? 0, w, h), w, h));
                    var diffPng = PngEncoder.FromBgra(diff, w, h);
                    var oldUrl = ra == null ? null : await Store.StageDownloadAsync($"old-{i + 1}.png", path => File.WriteAllBytes(path, PngEncoder.FromBgra(ra.Pixels ?? [], ra.PixelWidth, ra.PixelHeight)));
                    var newUrl = rb == null ? null : await Store.StageDownloadAsync($"new-{i + 1}.png", path => File.WriteAllBytes(path, PngEncoder.FromBgra(rb.Pixels ?? [], rb.PixelWidth, rb.PixelHeight)));
                    var diffUrl = await Store.StageDownloadAsync($"diff-{i + 1}.png", path => File.WriteAllBytes(path, diffPng));
                    result.Add(new ComparePage(i + 1, oldUrl, newUrl, diffUrl, pct, textDiffs.FirstOrDefault(t => t.PageIndex == i + 1)));   // its PageIndex is 1-based
                }
                Comparison = result;
                CompareName = file.Name;
                _dialog = DialogKind.Compare;
                int changed = result.Count(p => p.ChangedPercent > 0 || p.Text?.HasDifferences == true);
                Status(changed == 0 ? "No differences found" : $"{changed} page{(changed == 1 ? "" : "s")} changed");
            }
            finally { try { Directory.Delete(folder, true); } catch { } }
        });
    }

    // ── Read Aloud ───────────────────────────────────────────────────────────

    public bool Reading { get; private set; }
    public bool ReadingPaused { get; private set; }
    public string ReadVoice { get; set; } = "";
    public double ReadRate { get; set; } = 1.0;
    public List<string> Voices { get; private set; } = new();

    public async Task LoadVoicesAsync()
    {
        try { Voices = (await JS.InvokeAsync<string[]>("pdfedit.speech.voices")).ToList(); }
        catch { Voices = new(); }
    }

    /// <summary>Reads the current page (or every page from here to the end) with the browser's voice.</summary>
    public async Task ReadAloudAsync(bool toEnd)
    {
        if (Doc == null) return;
        var path = Doc.CurrentPath;
        int from = _page, to = toEnd ? PageCount - 1 : _page;
        var chunks = await Task.Run(() =>
        {
            var list = new List<object>();
            for (int p = from; p <= to; p++)
                foreach (var part in SpeechChunks(PdfTextExtractorService.GetPageText(path, p + 1)))
                    list.Add(new { page = p, text = part });
            return list;
        });
        if (chunks.Count == 0) { Toast("There's no text to read on " + (toEnd ? "these pages." : "this page.") + " Scanned pages need Recognise Text first."); return; }
        Reading = true;
        ReadingPaused = false;
        await JS.InvokeVoidAsync("pdfedit.speech.speak", _self, chunks, ReadVoice, ReadRate);
        Status(toEnd ? $"Reading from page {from + 1}…" : $"Reading page {from + 1}…");
    }

    public async Task PauseReadingAsync()
    {
        if (!Reading) return;
        ReadingPaused = !ReadingPaused;
        await JS.InvokeVoidAsync(ReadingPaused ? "pdfedit.speech.pause" : "pdfedit.speech.resume");
    }

    public async Task StopReadingAsync()
    {
        Reading = false;
        ReadingPaused = false;
        await JS.InvokeVoidAsync("pdfedit.speech.stop");
        Status("Stopped reading");
    }

    [JSInvokable]
    public async Task OnReadingPage(int page)
    {
        if (page != _page) await GoToPageAsync(page);
        await InvokeAsync(StateHasChanged);
    }

    [JSInvokable]
    public Task OnReadingDone()
    {
        Reading = false;
        ReadingPaused = false;
        Status("Finished reading");
        return InvokeAsync(StateHasChanged);
    }

    /// <summary>Sentences grouped into pieces of about 200 characters (long single utterances get cut off in some browsers).</summary>
    private static IEnumerable<string> SpeechChunks(string text)
    {
        text = Regex.Replace(text, @"\s+", " ").Trim();
        if (text.Length == 0) yield break;
        var sentences = Regex.Split(text, @"(?<=[.!?;:])\s+");
        var sb = new System.Text.StringBuilder();
        foreach (var s in sentences)
        {
            if (sb.Length > 0 && sb.Length + s.Length > 200) { yield return sb.ToString(); sb.Clear(); }
            if (sb.Length > 0) sb.Append(' ');
            sb.Append(s);
        }
        if (sb.Length > 0) yield return sb.ToString();
    }
}
