using System.IO.Compression;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;
using PdfEdit.Blazor.Components.Editor;
using PdfEdit.Blazor.Services;
using PdfEdit.Models;
using PdfEdit.Services;

namespace PdfEdit.Blazor.Components.Pages;

// Batch processing (Acrobat's Action Wizard) over uploaded PDFs, and bulk fill: one filled copy of
// the open form per spreadsheet row. Both run on the server and download as a ZIP (or one PDF).
public partial class Editor
{
    private string? _batchFolder;
    private CancellationTokenSource? _batchCts;

    // ── Batch ────────────────────────────────────────────────────────────────

    /// <summary>Uploaded PDFs to process (shown name, path on the server).</summary>
    public List<(string Name, string Path)> BatchFiles { get; } = new();
    public bool BatchIncludeOpen { get; set; } = true;
    public List<BatchStep> BatchSteps { get; } = new();
    public string BatchSuffix { get; set; } = "_processed";
    public string BatchOcrLanguages { get; set; } = "eng";
    public List<(bool Error, string Text)> BatchLog { get; } = new();
    public string? BatchStatus { get; private set; }
    public bool BatchRunning { get; private set; }
    public BatchStep? SelectedBatchStep { get; set; }

    private string BatchFolder => _batchFolder ??= Directory.CreateTempSubdirectory("pdfedit-batch-").FullName;

    public void ShowBatch()
    {
        _backstage = null;
        _dialog = DialogKind.Batch;
        BatchIncludeOpen = Doc != null;
    }

    public async Task AddBatchFilesAsync(IReadOnlyList<IBrowserFile> files)
    {
        var folder = Path.Combine(BatchFolder, "in");
        Directory.CreateDirectory(folder);
        foreach (var f in files)
        {
            if (!f.Name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) { Toast($"{f.Name} isn't a PDF.", "error"); continue; }
            if (f.Size > PdfDocumentStore.MaxUploadBytes) { Toast($"{f.Name} is too big.", "error"); continue; }
            var path = Path.Combine(folder, $"{Guid.NewGuid():N}.pdf");
            await using (var s = f.OpenReadStream(PdfDocumentStore.MaxUploadBytes))
            await using (var o = File.Create(path))
                await s.CopyToAsync(o);
            BatchFiles.Add((f.Name, path));
        }
    }

    public void RemoveBatchFile(int index)
    {
        if (index < 0 || index >= BatchFiles.Count) return;
        TryDelete(BatchFiles[index].Path);
        BatchFiles.RemoveAt(index);
    }

    public void AddBatchStep(BatchStepKind kind)
    {
        var step = BatchStep.Create(kind);
        BatchSteps.Add(step);
        SelectedBatchStep = step;
    }

    public void MoveBatchStep(BatchStep step, int by)
    {
        int i = BatchSteps.IndexOf(step), j = i + by;
        if (i < 0 || j < 0 || j >= BatchSteps.Count) return;
        (BatchSteps[i], BatchSteps[j]) = (BatchSteps[j], BatchSteps[i]);
    }

    public void RemoveBatchStep(BatchStep step)
    {
        BatchSteps.Remove(step);
        if (SelectedBatchStep == step) SelectedBatchStep = BatchSteps.FirstOrDefault();
    }

    /// <summary>Runs the steps over every file and downloads the results as a ZIP.</summary>
    public async Task RunBatchAsync()
    {
        if (BatchRunning) return;
        if (BatchSteps.Count == 0) { Toast("Add at least one step.", "error"); return; }
        var inputs = new List<(string Name, string Path)>();
        string run = Path.Combine(BatchFolder, "run-" + Guid.NewGuid().ToString("N")[..8]);
        string named = Path.Combine(run, "files"), output = Path.Combine(run, "out");
        Directory.CreateDirectory(named);
        Directory.CreateDirectory(output);
        if (BatchIncludeOpen && Doc != null)
        {
            await CommitPendingAsync();
            inputs.Add((Doc.FileName, Doc.CurrentPath));
        }
        inputs.AddRange(BatchFiles);
        if (inputs.Count == 0) { Toast("Add the PDFs to process.", "error"); return; }

        // Copies under their own names, so the results are named after them.
        var files = new List<string>();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, path) in inputs)
        {
            string baseName = Path.GetFileNameWithoutExtension(PdfDocumentStore.SafeName(name)), n = baseName;
            for (int k = 2; !used.Add(n); k++) n = $"{baseName} ({k})";
            var copy = Path.Combine(named, n + ".pdf");
            File.Copy(path, copy, overwrite: true);
            files.Add(copy);
        }

        BatchRunning = true;
        BatchLog.Clear();
        BatchStatus = "Starting…";
        _batchCts = new CancellationTokenSource();
        var progress = new UiProgress<BatchProgress>(p => InvokeAsync(() =>
        {
            if (p.Step is "Done" or "Failed" || p.Message != null)
            {
                string msg = p.Step == "Done" ? "done" : p.Step == "Failed" ? p.Message ?? "failed" : $"{p.Step} — {p.Message}";
                BatchLog.Add((p.IsError, $"{p.File}: {msg.Replace(output + Path.DirectorySeparatorChar, "")}"));
            }
            BatchStatus = $"File {p.FileIndex + 1} of {p.FileCount}: {p.File} — {p.Step}";
            StateHasChanged();
        }));
        string languages = BatchOcrLanguages;
        try
        {
            var steps = BatchSteps.Select(s => new BatchStep { Kind = s.Kind, Settings = new(s.Settings) }).ToList();
            var (ok, failed) = await Task.Run(() => BatchService.RunAsync(files, steps, new BatchOutput(output, BatchSuffix, false), progress,
                _batchCts.Token, (src, dest, ct) => Ocr.MakeSearchableAsync(src, dest, languages, ct)));
            await Task.Delay(50);   // let the last progress reports land
            BatchStatus = failed == 0 ? $"Finished: {ok} file{(ok == 1 ? "" : "s")} processed." : $"Finished: {ok} processed, {failed} failed.";
            if (ok > 0)
            {
                var url = await Store.StageDownloadAsync("Batch results.zip", zip => ZipFile.CreateFromDirectory(output, zip));
                await JS.InvokeVoidAsync("pdfedit.download", url);
                Toast($"Batch finished — {ok} file{(ok == 1 ? "" : "s")} downloaded as a ZIP.", failed == 0 ? "success" : "");
            }
        }
        catch (OperationCanceledException) { BatchStatus = "Stopped."; }
        catch (Exception ex) { BatchStatus = "Failed: " + ex.Message; }
        finally
        {
            BatchRunning = false;
            _batchCts.Dispose();
            _batchCts = null;
            try { Directory.Delete(run, true); } catch { }
        }
    }

    public void StopBatch() => _batchCts?.Cancel();

    /// <summary>Reports progress straight away (Progress&lt;T&gt; would post to a context Blazor doesn't have).</summary>
    private sealed class UiProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }

    // ── Bulk fill ────────────────────────────────────────────────────────────

    public List<BulkField> BulkFields { get; private set; } = new();
    public DataTableData? BulkData { get; private set; }
    public string? BulkDataName { get; private set; }
    public Dictionary<string, int> BulkMapping { get; } = new();
    public string BulkPattern { get; set; } = "{row}";
    public bool BulkFlatten { get; set; }
    /// <summary>"zip" (one PDF per row), "combined" (one PDF) or "both".</summary>
    public string BulkOutput { get; set; } = "zip";
    public string? BulkStatus { get; private set; }

    public async Task ShowBulkFillAsync()
    {
        if (Doc == null) return;
        await RunAsync("Reading the form…", async () =>
        {
            await CommitPendingAsync();
            var path = Doc.CurrentPath;
            BulkFields = await Task.Run(() => BulkFillService.GetFields(path));
        });
        if (BulkFields.Count == 0)
        {
            Toast("This PDF has no fields to fill. Add some with Edit → Prepare Form, or open a fillable form.", "error");
            return;
        }
        BulkStatus = null;
        if (BulkData != null) GuessBulkMapping();
        _dialog = DialogKind.BulkFill;
    }

    public async Task LoadBulkDataAsync(IBrowserFile file)
    {
        string ext = Path.GetExtension(file.Name).ToLowerInvariant();
        if (ext is not (".csv" or ".xlsx" or ".tsv" or ".txt")) { Toast("Choose a CSV or Excel (.xlsx) file.", "error"); return; }
        var temp = Path.Combine(Path.GetTempPath(), $"pdfedit-data-{Guid.NewGuid():N}{ext}");
        try
        {
            await using (var s = file.OpenReadStream(PdfDocumentStore.MaxUploadBytes))
            await using (var o = File.Create(temp))
                await s.CopyToAsync(o);
            var data = await Task.Run(() => TableReader.Read(temp));
            if (data.Headers.Count == 0) { Toast("That file has no columns.", "error"); return; }
            BulkData = data;
            BulkDataName = file.Name;
            int nameCol = data.Headers.FindIndex(h => h.Contains("name", StringComparison.OrdinalIgnoreCase));
            BulkPattern = nameCol >= 0 ? $"{{{data.Headers[nameCol]}}}" : "{row}";
            GuessBulkMapping();
        }
        catch (Exception ex) { Toast("Couldn't read that file (save Excel files as .xlsx or CSV): " + ex.Message, "error"); }
        finally { TryDelete(temp); }
    }

    private void GuessBulkMapping()
    {
        BulkMapping.Clear();
        foreach (var f in BulkFields) BulkMapping[f.Name] = BulkData == null ? -1 : BulkFillService.GuessColumn(f.Name, BulkData.Headers);
    }

    public string BulkExampleName => BulkData is { Rows.Count: > 0 } d ? BulkFillService.FileNameFor(BulkPattern, d, 0) + ".pdf" : "";

    public async Task RunBulkFillAsync()
    {
        if (Doc == null || BulkData is not { } data) return;
        if (data.Rows.Count == 0) { Toast("The spreadsheet has no rows.", "error"); return; }
        if (BulkMapping.Values.All(c => c < 0)) { Toast("Match at least one field to a column.", "error"); return; }
        var doc = Doc;
        var mapping = new Dictionary<string, int>(BulkMapping);
        string pattern = BulkPattern, mode = BulkOutput;
        bool flatten = BulkFlatten;
        string run = Path.Combine(BatchFolder, "bulk-" + Guid.NewGuid().ToString("N")[..8]);
        string output = Path.Combine(run, "out");
        string baseName = Path.GetFileNameWithoutExtension(doc.FileName);
        string? combined = mode is "combined" or "both" ? Path.Combine(run, baseName + " (all).pdf") : null;
        await RunAsync("Filling…", async () =>
        {
            try
            {
                await CommitPendingAsync();
                var template = doc.CurrentPath;
                var progress = new UiProgress<(int Row, int Total)>(p => InvokeAsync(() =>
                {
                    BulkStatus = $"Filling {Math.Min(p.Row + 1, p.Total)} of {p.Total}…";
                    Status(BulkStatus);
                    StateHasChanged();
                }));
                var written = await Task.Run(() => BulkFillService.Run(template, data, mapping, output, pattern, flatten, combined, progress, CancellationToken.None));
                string url;
                if (mode == "combined")
                    url = await Store.StageDownloadAsync(baseName + " (all).pdf", dest => File.Copy(combined!, dest));
                else
                {
                    if (combined != null) File.Copy(combined, Path.Combine(output, Path.GetFileName(combined)));
                    url = await Store.StageDownloadAsync(baseName + " (filled).zip", zip => ZipFile.CreateFromDirectory(output, zip));
                }
                await JS.InvokeVoidAsync("pdfedit.download", url);
                BulkStatus = $"Made {written.Count} filled PDF{(written.Count == 1 ? "" : "s")}{(combined != null ? (mode == "combined" ? ", downloaded as one PDF" : " and a combined copy") : "")}.";
                Status(BulkStatus);
                Toast(BulkStatus, "success");
            }
            finally { try { Directory.Delete(run, true); } catch { } }
        });
    }
}
