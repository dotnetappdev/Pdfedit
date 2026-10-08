using System.IO;
using System.Text.RegularExpressions;
using iText.Forms;
using iText.Forms.Fields;
using iText.Kernel.Pdf;

namespace PdfEdit.Services;

/// <summary>A form field as bulk fill sees it.</summary>
public sealed record BulkField(string Name, string Kind, string[] OnStates);

/// <summary>
/// Bulk fill / mail merge: one filled copy of a PDF form per row of a spreadsheet. Text fields get
/// the cell text, check boxes are ticked for yes/true/x/1/on, radio buttons and dropdowns take the
/// option named in the cell.
/// </summary>
public static class BulkFillService
{
    public static List<BulkField> GetFields(string pdfPath)
    {
        using var pdf = new PdfDocument(new PdfReader(pdfPath));
        var form = PdfAcroForm.GetAcroForm(pdf, false);
        if (form == null) return new();
        var all = form.GetAllFormFields();
        return all
            // iText also lists a radio group's or checkbox's unnamed widgets as "Name." — the same field again.
            .Where(kv => !(kv.Key.EndsWith('.') && all.ContainsKey(kv.Key.TrimEnd('.'))))
            .Where(kv => kv.Value.GetFormType() != null && !kv.Value.IsReadOnly() && kv.Value is not PdfSignatureFormField)
            .Where(kv => kv.Value is not PdfButtonFormField b || !b.IsPushButton())
            .Select(kv => new BulkField(kv.Key, KindOf(kv.Value),
                kv.Value.GetAppearanceStates().Where(s => s != "Off").Distinct().ToArray()))
            .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string KindOf(PdfFormField f) => f switch
    {
        PdfButtonFormField b when b.IsRadio() => "Radio",
        PdfButtonFormField => "Checkbox",
        PdfChoiceFormField => "Choice",
        _ => "Text",
    };

    private static string Norm(string s) => Regex.Replace(s.ToLowerInvariant(), "[^a-z0-9]", "");

    /// <summary>Best column for each field by name (exact, then contained), or -1.</summary>
    public static int GuessColumn(string fieldName, IReadOnlyList<string> headers)
    {
        string f = Norm(fieldName.Split('.').Last());
        for (int i = 0; i < headers.Count; i++) if (Norm(headers[i]) == f) return i;
        for (int i = 0; i < headers.Count; i++)
        {
            string h = Norm(headers[i]);
            if (h.Length >= 3 && f.Length >= 3 && (f.Contains(h) || h.Contains(f))) return i;
        }
        return -1;
    }

    private static readonly string[] Truthy = { "yes", "y", "true", "x", "1", "on", "checked", "✓" };

    /// <summary>File name from a pattern like "{Last name} - {First name}", using the row's cells.</summary>
    public static string FileNameFor(string pattern, DataTableData data, int row)
    {
        string name = Regex.Replace(pattern, @"\{([^}]+)\}", m =>
        {
            string key = m.Groups[1].Value;
            if (key.Equals("row", StringComparison.OrdinalIgnoreCase)) return (row + 1).ToString();
            int col = data.Headers.FindIndex(h => h.Equals(key, StringComparison.OrdinalIgnoreCase));
            return col >= 0 ? data.Cell(row, col) : "";
        });
        name = string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim();
        return string.IsNullOrWhiteSpace(name) ? $"row {row + 1}" : name;
    }

    /// <summary>Writes one filled copy per row. Returns the files written.</summary>
    public static List<string> Run(string templatePdf, DataTableData data, IReadOnlyDictionary<string, int> mapping,
        string outputFolder, string namePattern, bool flatten, string? combinedPath,
        IProgress<(int Row, int Total)>? progress, CancellationToken ct)
    {
        Directory.CreateDirectory(outputFolder);
        var written = new List<string>();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int r = 0; r < data.Rows.Count; r++)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report((r, data.Rows.Count));
            string baseName = FileNameFor(namePattern, data, r), name = baseName;
            for (int n = 2; !used.Add(name); n++) name = $"{baseName} ({n})";
            string dest = Path.Combine(outputFolder, name + ".pdf");

            using (var pdf = new PdfDocument(new PdfReader(templatePdf), new PdfWriter(dest)))
            {
                var form = PdfAcroForm.GetAcroForm(pdf, false);
                if (form != null)
                {
                    form.SetGenerateAppearance(true);
                    foreach (var (fieldName, col) in mapping)
                    {
                        if (col < 0) continue;
                        var field = form.GetField(fieldName);
                        if (field == null) continue;
                        string value = data.Cell(r, col).Trim();
                        try { SetField(field, value); } catch { /* one bad cell shouldn't stop the row */ }
                    }
                    if (flatten) form.FlattenFields();
                }
            }
            written.Add(dest);
        }
        progress?.Report((data.Rows.Count, data.Rows.Count));

        if (combinedPath != null && written.Count > 0)
        {
            using var outPdf = new PdfDocument(new PdfWriter(combinedPath));
            var merger = new iText.Kernel.Utils.PdfMerger(outPdf);
            foreach (var f in written)
            {
                using var src = new PdfDocument(new PdfReader(f));
                merger.Merge(src, 1, src.GetNumberOfPages());
            }
        }
        return written;
    }

    private static void SetField(PdfFormField field, string value)
    {
        if (field is PdfButtonFormField btn && !btn.IsPushButton())
        {
            var states = field.GetAppearanceStates().Where(s => s != "Off").Distinct().ToList();
            if (btn.IsRadio())
            {
                // "1/2" picks the option named 1_2, "full" picks Full …
                var match = states.FirstOrDefault(s => s.Equals(value, StringComparison.OrdinalIgnoreCase))
                            ?? states.FirstOrDefault(s => Norm(s).Length > 0 && Norm(s) == Norm(value));
                if (match != null) field.SetValue(match);
                return;
            }
            bool on = Truthy.Contains(value.ToLowerInvariant()) || states.Any(s => s.Equals(value, StringComparison.OrdinalIgnoreCase));
            field.SetValue(on ? (states.FirstOrDefault() ?? "Yes") : "Off");
            return;
        }
        if (value.Length > 0) field.SetValue(value);
    }
}
