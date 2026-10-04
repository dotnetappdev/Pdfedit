using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace PdfEdit.Services;

/// <summary>A sheet of data: column headers and rows of cell text.</summary>
public sealed record DataTableData(List<string> Headers, List<List<string>> Rows)
{
    public string Cell(int row, int col) => col >= 0 && col < Rows[row].Count ? Rows[row][col] : "";
}

/// <summary>
/// Reads CSV / TSV / TXT (delimiter detected, quoted values) and Excel .xlsx files (first
/// worksheet) into headers + rows, for bulk form filling. No Excel install needed.
/// </summary>
public static class TableReader
{
    public static DataTableData Read(string path) =>
        Path.GetExtension(path).Equals(".xlsx", StringComparison.OrdinalIgnoreCase) ? ReadXlsx(path) : ReadDelimited(path);

    // ── CSV ──────────────────────────────────────────────────────────────────
    public static DataTableData ReadDelimited(string path)
    {
        string text = File.ReadAllText(path, Encoding.UTF8);
        string firstLine = text.Split('\n')[0];
        char sep = new[] { ',', ';', '\t', '|' }.OrderByDescending(c => firstLine.Count(ch => ch == c)).First();

        var rows = new List<List<string>>();
        var row = new List<string>();
        var cell = new StringBuilder();
        bool quoted = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; }
                else if (c == '"') quoted = false;
                else cell.Append(c);
            }
            else if (c == '"' && cell.Length == 0) quoted = true;
            else if (c == sep) { row.Add(cell.ToString()); cell.Clear(); }
            else if (c == '\n' || c == '\r')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                row.Add(cell.ToString()); cell.Clear();
                if (row.Any(v => v.Length > 0)) rows.Add(row);
                row = new List<string>();
            }
            else cell.Append(c);
        }
        row.Add(cell.ToString());
        if (row.Any(v => v.Length > 0)) rows.Add(row);
        return Split(rows);
    }

    // ── XLSX (first sheet) ───────────────────────────────────────────────────
    public static DataTableData ReadXlsx(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XDocument Load(string entry) { using var s = zip.GetEntry(entry)!.Open(); return XDocument.Load(s); }

        // Shared strings
        var shared = new List<string>();
        if (zip.GetEntry("xl/sharedStrings.xml") != null)
            foreach (var si in Load("xl/sharedStrings.xml").Root!.Elements(ns + "si"))
                shared.Add(string.Concat(si.Descendants(ns + "t").Select(t => t.Value)));

        // First worksheet, via the workbook and its relationships
        string sheetPath = "xl/worksheets/sheet1.xml";
        try
        {
            var firstSheet = Load("xl/workbook.xml").Root!.Element(ns + "sheets")!.Elements(ns + "sheet").First();
            string rid = firstSheet.Attribute(rel + "id")!.Value;
            XNamespace pr = "http://schemas.openxmlformats.org/package/2006/relationships";
            var target = Load("xl/_rels/workbook.xml.rels").Root!.Elements(pr + "Relationship")
                .First(r => r.Attribute("Id")!.Value == rid).Attribute("Target")!.Value;
            sheetPath = target.StartsWith("/") ? target.TrimStart('/') : "xl/" + target;
        }
        catch { /* fall back to sheet1.xml */ }

        var rows = new List<List<string>>();
        foreach (var r in Load(sheetPath).Root!.Element(ns + "sheetData")!.Elements(ns + "row"))
        {
            var row = new List<string>();
            foreach (var c in r.Elements(ns + "c"))
            {
                int col = ColumnIndex(c.Attribute("r")?.Value ?? "");
                while (row.Count < col) row.Add("");
                string type = c.Attribute("t")?.Value ?? "";
                string v = c.Element(ns + "v")?.Value ?? "";
                string value = type switch
                {
                    "s" => int.TryParse(v, out int si) && si < shared.Count ? shared[si] : "",
                    "inlineStr" => string.Concat(c.Descendants(ns + "t").Select(t => t.Value)),
                    "b" => v == "1" ? "TRUE" : "FALSE",
                    _ => v,
                };
                if (col >= 0 && col < row.Count) row[col] = value; else row.Add(value);
            }
            if (row.Any(v => v.Length > 0)) rows.Add(row);
        }
        return Split(rows);
    }

    // "C7" → 2
    private static int ColumnIndex(string cellRef)
    {
        int n = 0;
        foreach (char ch in cellRef)
        {
            if (!char.IsLetter(ch)) break;
            n = n * 26 + (char.ToUpperInvariant(ch) - 'A' + 1);
        }
        return Math.Max(0, n - 1);
    }

    private static DataTableData Split(List<List<string>> rows)
    {
        if (rows.Count == 0) return new DataTableData(new(), new());
        var headers = rows[0].Select((h, i) => string.IsNullOrWhiteSpace(h) ? $"Column {i + 1}" : h.Trim()).ToList();
        var data = rows.Skip(1).ToList();
        int width = Math.Max(headers.Count, data.Count == 0 ? 0 : data.Max(r => r.Count));
        while (headers.Count < width) headers.Add($"Column {headers.Count + 1}");
        return new DataTableData(headers, data);
    }
}
