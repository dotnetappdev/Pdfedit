using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using iText.Kernel.Pdf;

namespace PdfEdit.Services;

/// <summary>
/// Acrobat's "Export to Excel": rebuilds the rows and columns of each page from where the text
/// sits (lines become rows; text that lines up vertically becomes a column) and writes an .xlsx
/// workbook, one sheet per page. Numbers, currency and percentages are stored as numbers so they
/// can be added up straight away.
/// </summary>
public static class ExcelExportService
{
    /// <returns>The number of rows written.</returns>
    public static int Export(string pdfPath, string xlsxPath, bool oneSheet = false)
    {
        int pageCount;
        using (var pdf = new PdfDocument(new PdfReader(pdfPath))) pageCount = pdf.GetNumberOfPages();

        var sheets = new List<(string Name, List<List<string>> Rows)>();
        var all = new List<List<string>>();
        for (int p = 1; p <= pageCount; p++)
        {
            var rows = Table(PageTextLocator.GetChunks(pdfPath, p));
            if (oneSheet || pageCount > 250)
            {
                if (all.Count > 0 && rows.Count > 0) all.Add(new());   // blank row between pages
                all.AddRange(rows);
            }
            else sheets.Add(($"Page {p}", rows));
        }
        if (oneSheet || pageCount > 250) sheets.Add(("Document", all));

        WriteXlsx(xlsxPath, sheets);
        return sheets.Sum(s => s.Rows.Count);
    }

    /// <summary>Rows of cells from positioned text.</summary>
    public static List<List<string>> Table(List<TextChunk> chunks)
    {
        if (chunks.Count == 0) return new();

        // 1. Rows: chunks whose middles are within half a line of each other, top to bottom.
        var rows = new List<List<TextChunk>>();
        foreach (var c in chunks.OrderByDescending(c => c.MidY))
        {
            var row = rows.LastOrDefault();
            double tol = Math.Max(2.5, (c.Top - c.Bottom) * 0.5);
            if (row != null && Math.Abs(row.Average(r => r.MidY) - c.MidY) <= tol) row.Add(c);
            else rows.Add(new() { c });
        }
        foreach (var r in rows) r.Sort((a, b) => a.X0.CompareTo(b.X0));

        // 2. Columns: left edges that line up on several rows.
        var edges = rows.SelectMany(r => r.Select(c => c.X0)).OrderBy(x => x).ToList();
        var clusters = new List<(double X, int Count)>();
        foreach (var x in edges)
        {
            if (clusters.Count > 0 && x - clusters[^1].X <= 6)
                clusters[^1] = ((clusters[^1].X * clusters[^1].Count + x) / (clusters[^1].Count + 1), clusters[^1].Count + 1);
            else clusters.Add((x, 1));
        }
        int need = Math.Max(2, (int)Math.Ceiling(rows.Count * 0.15));
        var anchors = clusters.Where(c => c.Count >= need).Select(c => c.X).OrderBy(x => x).ToList();
        if (anchors.Count == 0 || anchors[0] > edges[0] + 6) anchors.Insert(0, edges[0]);

        // 3. Each chunk goes in the last column that starts at or before it.
        var table = new List<List<string>>();
        foreach (var r in rows)
        {
            var cells = new string[anchors.Count];
            foreach (var c in r)
            {
                int col = 0;
                for (int i = 0; i < anchors.Count; i++) if (anchors[i] <= c.X0 + 4) col = i;
                cells[col] = cells[col] == null ? c.Text : cells[col] + " " + c.Text;
            }
            table.Add(cells.Select(s => s ?? "").ToList());
        }

        // Drop columns that are empty on every row.
        var keep = Enumerable.Range(0, anchors.Count).Where(i => table.Any(r => r[i].Length > 0)).ToList();
        return table.Select(r => keep.Select(i => r[i]).ToList()).ToList();
    }

    private static readonly Regex NumberRx = new(@"^\(?[-−]?[£$€¥]?\s?[-−]?\d{1,3}([,\s]?\d{3})*(\.\d+)?\s?%?\)?$|^\(?[-−]?[£$€¥]?\d+(\.\d+)?%?\)?$");

    /// <summary>"£1,234.50" → 1234.5, "(12)" → -12, "15%" → 0.15. Null if it isn't a number.</summary>
    public static double? AsNumber(string text)
    {
        string t = text.Trim();
        if (t.Length == 0 || t.Length > 24 || !NumberRx.IsMatch(t)) return null;
        if (t.Length > 1 && t[0] == '0' && char.IsDigit(t[1])) return null;   // keep leading zeros (IDs, codes)
        bool negative = (t.StartsWith('(') && t.EndsWith(')')) || t.Contains('-') || t.Contains('−');
        bool percent = t.EndsWith('%') || t.EndsWith("%)");
        string digits = Regex.Replace(t, @"[^\d.]", "");
        if (!double.TryParse(digits, NumberStyles.Float, CultureInfo.InvariantCulture, out double v)) return null;
        if (negative) v = -v;
        if (percent) v /= 100;
        return v;
    }

    // ── Minimal .xlsx writer ────────────────────────────────────────────────

    private static void WriteXlsx(string path, List<(string Name, List<List<string>> Rows)> sheets)
    {
        if (File.Exists(path)) File.Delete(path);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);

        void Add(string name, string content)
        {
            var e = zip.CreateEntry(name, CompressionLevel.Optimal);
            using var w = new StreamWriter(e.Open(), new UTF8Encoding(false));
            w.Write(content);
        }

        var ct = new StringBuilder("""<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/><Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>""");
        for (int i = 1; i <= sheets.Count; i++)
            ct.Append($"""<Override PartName="/xl/worksheets/sheet{i}.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>""");
        ct.Append("</Types>");
        Add("[Content_Types].xml", ct.ToString());

        Add("_rels/.rels", """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/></Relationships>""");

        var wb = new StringBuilder("""<?xml version="1.0" encoding="UTF-8" standalone="yes"?><workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets>""");
        var rels = new StringBuilder("""<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">""");
        for (int i = 1; i <= sheets.Count; i++)
        {
            wb.Append($"""<sheet name="{Esc(sheets[i - 1].Name)}" sheetId="{i}" r:id="rId{i}"/>""");
            rels.Append($"""<Relationship Id="rId{i}" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet{i}.xml"/>""");
        }
        wb.Append("</sheets></workbook>");
        rels.Append($"""<Relationship Id="rId{sheets.Count + 1}" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/></Relationships>""");
        Add("xl/workbook.xml", wb.ToString());
        Add("xl/_rels/workbook.xml.rels", rels.ToString());

        // Styles: 0 normal, 1 percentage.
        Add("xl/styles.xml", """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><fonts count="1"><font><sz val="11"/><name val="Calibri"/></font></fonts><fills count="2"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill></fills><borders count="1"><border/></borders><cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs><cellXfs count="2"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/><xf numFmtId="10" fontId="0" fillId="0" borderId="0" xfId="0" applyNumberFormat="1"/></cellXfs></styleSheet>""");

        for (int i = 1; i <= sheets.Count; i++)
        {
            var rows = sheets[i - 1].Rows;
            int cols = rows.Count == 0 ? 0 : rows.Max(r => r.Count);
            var sb = new StringBuilder("""<?xml version="1.0" encoding="UTF-8" standalone="yes"?><worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">""");
            if (cols > 0)
            {
                sb.Append("<cols>");
                for (int c = 0; c < cols; c++)
                {
                    int len = rows.Max(r => c < r.Count ? r[c].Length : 0);
                    sb.Append($"""<col min="{c + 1}" max="{c + 1}" width="{Math.Clamp(len + 2, 6, 60)}" customWidth="1"/>""");
                }
                sb.Append("</cols>");
            }
            sb.Append("<sheetData>");
            for (int r = 0; r < rows.Count; r++)
            {
                sb.Append($"""<row r="{r + 1}">""");
                for (int c = 0; c < rows[r].Count; c++)
                {
                    string text = rows[r][c];
                    if (text.Length == 0) continue;
                    string cellRef = ColumnName(c) + (r + 1);
                    if (AsNumber(text) is double v)
                    {
                        string style = text.TrimEnd(')').EndsWith('%') ? " s=\"1\"" : "";
                        sb.Append($"""<c r="{cellRef}"{style}><v>{v.ToString("R", CultureInfo.InvariantCulture)}</v></c>""");
                    }
                    else sb.Append($"""<c r="{cellRef}" t="inlineStr"><is><t xml:space="preserve">{Esc(text)}</t></is></c>""");
                }
                sb.Append("</row>");
            }
            sb.Append("</sheetData></worksheet>");
            Add($"xl/worksheets/sheet{i}.xml", sb.ToString());
        }
    }

    private static string ColumnName(int index)
    {
        string name = "";
        for (index++; index > 0; index = (index - 1) / 26) name = (char)('A' + (index - 1) % 26) + name;
        return name;
    }

    private static string Esc(string s)
    {
        // Drop characters XML 1.0 doesn't allow.
        var clean = new string(s.Where(ch => ch == '\t' || ch == '\n' || ch == '\r' || ch >= 0x20).ToArray());
        return SecurityElement.Escape(clean) ?? "";
    }
}
