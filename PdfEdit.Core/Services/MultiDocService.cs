using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;

namespace PdfEdit.Services;

/// <summary>A page of text from one of several PDFs.</summary>
public sealed record DocPage(string Path, int Page, string Text)
{
    public string FileName => System.IO.Path.GetFileName(Path);
}

/// <summary>
/// Questions across several PDFs (PDF Spaces): reads every page, and when everything
/// won't fit in one request, picks the passages that best match the question. Answers cite
/// [file.pdf p.N], which the window turns into links.
/// </summary>
public static class MultiDocService
{
    public const int Budget = 110_000;   // characters of document text per question

    public static List<DocPage> Read(IEnumerable<string> files, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var pages = new List<DocPage>();
        foreach (var f in files)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report(System.IO.Path.GetFileName(f));
            try
            {
                using var reader = new PdfReader(f);
                reader.SetUnethicalReading(true);
                using var pdf = new PdfDocument(reader);
                for (int p = 1; p <= pdf.GetNumberOfPages(); p++)
                {
                    string t = "";
                    try { t = PdfTextExtractor.GetTextFromPage(pdf.GetPage(p)); } catch { }
                    t = Regex.Replace(t, @"[ \t]+", " ").Trim();
                    if (t.Length > 0) pages.Add(new DocPage(f, p, t));
                }
            }
            catch { /* unreadable or password-protected: skip */ }
        }
        return pages;
    }

    private static readonly Regex WordRx = new(@"\p{L}[\p{L}\p{N}'-]{2,}", RegexOptions.Compiled);

    /// <summary>The document text to send with a question: everything if it fits, else the best-matching passages.</summary>
    public static string Context(string question, List<DocPage> pages)
    {
        int total = pages.Sum(p => p.Text.Length + 30);
        IEnumerable<DocPage> chosen = pages;
        if (total > Budget)
        {
            // Split into passages and rank them by how many (rarer) question words they contain.
            var passages = new List<DocPage>();
            foreach (var p in pages)
                for (int i = 0; i < p.Text.Length; i += 1200)
                    passages.Add(p with { Text = p.Text.Substring(i, Math.Min(1500, p.Text.Length - i)) });
            var terms = WordRx.Matches(question.ToLowerInvariant()).Select(m => m.Value).Distinct().ToList();
            var df = terms.ToDictionary(t => t, t => passages.Count(p => p.Text.Contains(t, StringComparison.OrdinalIgnoreCase)));
            double Score(DocPage p)
            {
                string low = p.Text.ToLowerInvariant();
                double s = 0;
                foreach (var t in terms)
                {
                    int n = Regex.Matches(low, Regex.Escape(t)).Count;
                    if (n > 0) s += (1 + Math.Log(n)) * Math.Log(1 + passages.Count / (1.0 + df[t]));
                }
                return s;
            }
            var ranked = passages.Select(p => (p, s: Score(p))).OrderByDescending(x => x.s).ToList();
            var keep = new List<DocPage>();
            int used = 0;
            foreach (var (p, _) in ranked)
            {
                if (used + p.Text.Length > Budget) break;
                keep.Add(p);
                used += p.Text.Length + 30;
            }
            chosen = keep.OrderBy(p => p.Path).ThenBy(p => p.Page);
        }
        var sb = new StringBuilder();
        foreach (var p in chosen) sb.Append($"\n[{p.FileName} p.{p.Page}]\n{p.Text}\n");
        return sb.ToString();
    }

    public const string Instructions =
        "You answer questions using only the documents provided. Cite the source of every fact in square brackets " +
        "exactly as [file name.pdf p.N], using the markers in the text. If the documents don't say, say so. " +
        "Compare the documents when the question asks about more than one. Answer in Markdown.";

    private static readonly Regex CiteRx = new(@"\[([^\[\]]+?\.pdf)\s*,?\s*p(?:age|p)?\.?\s*(\d+)\]", RegexOptions.IgnoreCase);

    /// <summary>The [file p.N] citations in an answer, in order of first use.</summary>
    public static List<(string FileName, int Page)> Citations(string answer) =>
        CiteRx.Matches(answer).Select(m => (m.Groups[1].Value.Trim(), int.Parse(m.Groups[2].Value))).Distinct().ToList();
}
