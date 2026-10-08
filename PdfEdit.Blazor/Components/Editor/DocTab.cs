using PdfEdit.Blazor.Services;
using PdfEdit.Models;
using PdfEdit.Services;

namespace PdfEdit.Blazor.Components.Editor;

/// <summary>
/// One open document (a tab above the pages): its PDF and everything about it that isn't written
/// into the PDF yet, so switching tabs keeps each document's work as it was.
/// </summary>
public sealed class DocTab
{
    public required PdfSession Session { get; init; }

    // Shared with the editor while this tab is shown (the editor works on these very objects).
    public Dictionary<string, string> Values { get; } = new();
    public Dictionary<string, string> Original { get; } = new();
    public List<PageItem> Items { get; } = new();
    public Dictionary<(string Name, int WidgetIndex), FieldBounds> Bounds { get; } = new();
    public Dictionary<string, FormFieldInfo> Edits { get; } = new();
    public HashSet<string> Deleted { get; } = new();

    // Copied in and out when the tab is left and shown again.
    public List<TextMatch> SearchHits { get; set; } = new();
    public int HitIndex { get; set; } = -1;
    public int Page { get; set; }
    public double Zoom { get; set; } = 1.0;
    /// <summary>Where the pages were scrolled to, or -1 (then the tab comes back at <see cref="Page"/>).</summary>
    public double ScrollTop { get; set; } = -1;
    public string? SelectedField { get; set; }
    public bool PrepareMode { get; set; }
    public (string Name, int Widget)? SelectedWidget { get; set; }

    /// <summary>Has changes that aren't downloaded or saved yet.</summary>
    public bool HasChanges =>
        Session.IsModified || Items.Count > 0 || Bounds.Count + Edits.Count + Deleted.Count > 0
        || Values.Any(kv => Original.GetValueOrDefault(kv.Key) != kv.Value);
}
