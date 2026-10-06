namespace PdfEdit.Models;

public class PdfPageDiff
{
    public int PageIndex { get; set; }
    public bool ExistsInA { get; set; }
    public bool ExistsInB { get; set; }
    public List<string> OnlyInA { get; set; } = new();
    public List<string> OnlyInB { get; set; } = new();
    public int CommonLineCount { get; set; }

    public bool HasDifferences => OnlyInA.Count > 0 || OnlyInB.Count > 0 || ExistsInA != ExistsInB;
}
