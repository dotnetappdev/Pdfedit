using System.IO;
using System.Text.Json;
using iText.Forms;
using iText.Kernel.Pdf;
using PdfEdit.Models;

namespace PdfEdit.Services;

/// <summary>Progress of a batch run (shown in the batch window).</summary>
public sealed record BatchProgress(int FileIndex, int FileCount, string File, string Step, string? Message = null, bool IsError = false);

/// <summary>Where batch results go.</summary>
public sealed record BatchOutput(string? Folder, string Suffix, bool Overwrite);

/// <summary>
/// Acrobat's Action Wizard: runs a list of steps (OCR, compress, watermark, flatten, numbering,
/// password …) over many PDFs. Each file goes through the steps in order via temp files, so a
/// failure leaves the original untouched; the result is written next to it or to an output folder.
/// </summary>
public static class BatchService
{
    public static string ActionsFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PdfEdit", "Actions");

    // ── Saved actions ─────────────────────────────────────────────────────────
    public static List<BatchAction> LoadActions()
    {
        var list = new List<BatchAction>();
        if (!Directory.Exists(ActionsFolder)) return list;
        foreach (var f in Directory.GetFiles(ActionsFolder, "*.json").OrderBy(f => f))
        {
            try { if (JsonSerializer.Deserialize<BatchAction>(File.ReadAllText(f)) is { } a) list.Add(a); } catch { }
        }
        return list;
    }

    public static void SaveAction(BatchAction action)
    {
        Directory.CreateDirectory(ActionsFolder);
        // Passwords and other secrets are never written to disk.
        var copy = new BatchAction
        {
            Name = action.Name,
            Steps = action.Steps.Select(s => new BatchStep
            {
                Kind = s.Kind,
                Settings = s.Settings.Where(kv => BatchStep.Schema(s.Kind).All(p => p.Key != kv.Key || !p.Secret))
                                     .ToDictionary(kv => kv.Key, kv => kv.Value),
            }).ToList(),
        };
        File.WriteAllText(Path.Combine(ActionsFolder, SafeName(action.Name) + ".json"),
            JsonSerializer.Serialize(copy, new JsonSerializerOptions { WriteIndented = true }));
    }

    public static void DeleteAction(string name)
    {
        var f = Path.Combine(ActionsFolder, SafeName(name) + ".json");
        if (File.Exists(f)) File.Delete(f);
    }

    private static string SafeName(string s) => string.Concat(s.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim();

    /// <summary>All PDFs in a folder (optionally its subfolders).</summary>
    public static IEnumerable<string> PdfsIn(string folder, bool recursive) =>
        Directory.EnumerateFiles(folder, "*.pdf", recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);

    public static string OutputPathFor(string file, BatchOutput output)
    {
        string dir = string.IsNullOrWhiteSpace(output.Folder) ? Path.GetDirectoryName(file)! : output.Folder!;
        string name = Path.GetFileNameWithoutExtension(file) + (output.Overwrite && string.IsNullOrWhiteSpace(output.Folder) ? "" : output.Suffix) + ".pdf";
        return Path.Combine(dir, name);
    }

    // ── Run ───────────────────────────────────────────────────────────────────
    /// <summary>
    /// Runs the steps over the files. <paramref name="ocr"/>(source, destination, ct) makes a
    /// searchable copy for the OCR step — the Windows app and the web version each bring their own
    /// OCR engine; without one the OCR step fails for each file. (The Windows app starts this on
    /// the UI thread because its OCR renders pages.)
    /// </summary>
    public static async Task<(int Ok, int Failed)> RunAsync(IReadOnlyList<string> files, IReadOnlyList<BatchStep> steps,
        BatchOutput output, IProgress<BatchProgress> progress, CancellationToken ct,
        Func<string, string, CancellationToken, Task>? ocr = null)
    {
        int ok = 0, failed = 0;
        int batesNext = steps.FirstOrDefault(s => s.Kind == BatchStepKind.Bates)?.GetInt("Start", 1) ?? 1;
        var form = new PdfFormService();
        if (output.Folder is { Length: > 0 } of) Directory.CreateDirectory(of);

        for (int fi = 0; fi < files.Count; fi++)
        {
            ct.ThrowIfCancellationRequested();
            string file = files[fi], shortName = Path.GetFileName(file);
            var temps = new List<string>();
            string Temp() { var t = Path.Combine(Path.GetTempPath(), $"pdfedit-batch-{Guid.NewGuid():N}.pdf"); temps.Add(t); return t; }
            try
            {
                string cur = file;
                foreach (var step in steps)
                {
                    ct.ThrowIfCancellationRequested();
                    progress.Report(new BatchProgress(fi, files.Count, shortName, BatchStep.Title(step.Kind)));
                    string next = Temp();
                    string src = cur;
                    switch (step.Kind)
                    {
                        case BatchStepKind.Ocr:
                            if (ocr == null) throw new InvalidOperationException("OCR isn't available here.");
                            await ocr(src, next, ct);
                            break;
                        case BatchStepKind.Compress:
                            await Task.Run(() => form.CompressPdf(src, next), ct);
                            break;
                        case BatchStepKind.Watermark:
                        {
                            var opt = new WatermarkOptions
                            {
                                Text = step.Get("Text"), FontSize = step.GetInt("FontSize", 72),
                                Opacity = Math.Clamp(step.GetInt("Opacity", 25), 5, 100) / 100f, Color = step.Get("Color"),
                                Diagonal = step.GetBool("Diagonal"), AngleDeg = 45, Behind = step.GetBool("Behind"),
                            };
                            await Task.Run(() => WatermarkService.Apply(src, next, opt, 1), ct);
                            break;
                        }
                        case BatchStepKind.Flatten:
                            await Task.Run(() => Flatten(src, next), ct);
                            break;
                        case BatchStepKind.Rotate:
                            await Task.Run(() => Rotate(src, next, step.GetInt("Degrees", 90)), ct);
                            break;
                        case BatchStepKind.PageNumbers:
                            await Task.Run(() => form.AddPageNumbers(src, next, step.Get("Format"), position: step.Get("Position")), ct);
                            break;
                        case BatchStepKind.HeaderFooter:
                        {
                            string Fill(string t) => t.Replace("{file}", Path.GetFileNameWithoutExtension(file)).Replace("{date}", DateTime.Now.ToString("d"));
                            string h = Fill(step.Get("Header")), f = Fill(step.Get("Footer"));
                            await Task.Run(() => form.AddHeaderFooter(src, next, h.Length > 0 ? h : null, f.Length > 0 ? f : null, alignment: step.Get("Alignment")), ct);
                            break;
                        }
                        case BatchStepKind.Bates:
                        {
                            int start = step.GetBool("Continue") ? batesNext : step.GetInt("Start", 1);
                            await Task.Run(() => form.AddBatesNumbers(src, next, start, step.GetInt("Digits", 6), step.Get("Prefix"), position: step.Get("Position")), ct);
                            batesNext = start + PageCount(next);
                            break;
                        }
                        case BatchStepKind.Sanitize:
                        {
                            var o = new SanitizeOptions(step.GetBool("Metadata"), step.GetBool("Scripts"), step.GetBool("Attachments"),
                                                        step.GetBool("Comments"), step.GetBool("Bookmarks"));
                            string what = await Task.Run(() => SanitizeService.Sanitize(src, next, o), ct);
                            progress.Report(new BatchProgress(fi, files.Count, shortName, BatchStep.Title(step.Kind), $"removed {what}"));
                            break;
                        }
                        case BatchStepKind.Password:
                        {
                            string open = step.Get("Open"), owner = step.Get("Owner");
                            if (open.Length == 0 && owner.Length == 0) throw new InvalidOperationException("Password protect: enter a password.");
                            await Task.Run(() => form.EncryptPdf(src, next, open, owner.Length > 0 ? owner : open, step.GetBool("Print"), step.GetBool("Copy")), ct);
                            break;
                        }
                        case BatchStepKind.PdfA:
                            await Task.Run(() => form.ConvertToPdfA(src, next), ct);
                            break;
                        case BatchStepKind.ExportText:
                        {
                            string txt = Path.ChangeExtension(OutputPathFor(file, output), ".txt");
                            await Task.Run(() => form.ExportTextToFile(src, txt), ct);
                            next = src; // the PDF itself is unchanged
                            break;
                        }
                    }
                    cur = next;
                }

                string dest = OutputPathFor(file, output);
                if (!string.Equals(cur, file, StringComparison.OrdinalIgnoreCase))
                    File.Copy(cur, dest, overwrite: true);
                ok++;
                progress.Report(new BatchProgress(fi, files.Count, shortName, "Done", $"→ {dest}"));
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                failed++;
                progress.Report(new BatchProgress(fi, files.Count, shortName, "Failed", ex.Message, IsError: true));
            }
            finally
            {
                foreach (var t in temps) { try { if (File.Exists(t)) File.Delete(t); } catch { } }
            }
        }
        return (ok, failed);
    }

    private static int PageCount(string path)
    {
        using var pdf = new PdfDocument(new PdfReader(path));
        return pdf.GetNumberOfPages();
    }

    /// <summary>Makes the form fields part of the page (like Flatten &amp; Save).</summary>
    public static void Flatten(string src, string dest)
    {
        using var pdf = new PdfDocument(new PdfReader(src), new PdfWriter(dest));
        PdfAcroForm.GetAcroForm(pdf, false)?.FlattenFields();
    }

    public static void Rotate(string src, string dest, int degrees)
    {
        using var pdf = new PdfDocument(new PdfReader(src), new PdfWriter(dest));
        for (int p = 1; p <= pdf.GetNumberOfPages(); p++)
        {
            var page = pdf.GetPage(p);
            page.SetRotation(((page.GetRotation() + degrees) % 360 + 360) % 360);
        }
    }
}
