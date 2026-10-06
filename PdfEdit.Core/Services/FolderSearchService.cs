using System.IO;
using System.Text.RegularExpressions;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;

namespace PdfEdit.Services;

/// <summary>A match found by <see cref="FolderSearchService"/>.</summary>
public sealed record SearchHit(string File, int Page, string Snippet)
{
    public string FileName => Path.GetFileName(File);
    public string Folder => Path.GetDirectoryName(File) ?? "";
}

/// <summary>Acrobat's Advanced Search: finds text in every PDF in a folder (and its subfolders).</summary>
public static class FolderSearchService
{
    public static void Search(string folder, bool recursive, string query, bool matchCase, bool wholeWord,
        Action<SearchHit> onHit, IProgress<(int Done, int Total, string File)>? progress, CancellationToken ct)
    {
        var files = Directory.EnumerateFiles(folder, "*.pdf", recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly).ToList();
        string pattern = Regex.Escape(query.Trim());
        if (wholeWord) pattern = $@"\b{pattern}\b";
        var rx = new Regex(pattern, matchCase ? RegexOptions.None : RegexOptions.IgnoreCase);

        for (int i = 0; i < files.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report((i, files.Count, files[i]));
            try
            {
                using var pdf = new PdfDocument(new PdfReader(files[i]));
                for (int p = 1; p <= pdf.GetNumberOfPages(); p++)
                {
                    ct.ThrowIfCancellationRequested();
                    string text = PdfTextExtractor.GetTextFromPage(pdf.GetPage(p));
                    foreach (Match m in rx.Matches(text))
                    {
                        int start = Math.Max(0, m.Index - 50), end = Math.Min(text.Length, m.Index + m.Length + 50);
                        string snippet = (start > 0 ? "…" : "") + Regex.Replace(text[start..end], @"\s+", " ").Trim() + (end < text.Length ? "…" : "");
                        onHit(new SearchHit(files[i], p, snippet));
                    }
                }
            }
            catch (OperationCanceledException) { throw; }
            catch { /* password-protected or damaged PDF: skip */ }
        }
        progress?.Report((files.Count, files.Count, ""));
    }
}
