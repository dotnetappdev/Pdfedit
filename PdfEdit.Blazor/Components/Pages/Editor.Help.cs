using PdfEdit.Blazor.Components.Editor;
using PdfEdit.Services;

namespace PdfEdit.Blazor.Components.Pages;

// Help: Tip of the Day and Take the Tour (the Windows app's tips and tour steps).
public partial class Editor
{
    public int TipIndex { get; private set; }
    public Tip CurrentTip => TipsCatalog.All[TipIndex % TipsCatalog.All.Count];

    private void ShowTip()
    {
        TipIndex = Random.Shared.Next(TipsCatalog.All.Count);
        _dialog = DialogKind.Tip;
    }

    public void NextTip(int by) => TipIndex = (TipIndex + by + TipsCatalog.All.Count) % TipsCatalog.All.Count;

    /// <summary>A step of the tour: what to say, and the part of the window to point at (a CSS selector).</summary>
    public sealed record TourStep(string Title, string Text, string? Target = null, RibbonTab? Tab = null);

    public static readonly TourStep[] TourSteps =
    [
        new("Welcome to PdfEdit",
            "A quick look round: about a minute. Use Next and Back (or the arrow keys) to move, and × or Esc to close. You can take the tour again from Help → Take the Tour."),
        new("The ribbon: Home", "Everything is grouped into tabs, like Office. Home is for files and pages: open, save, merge, split, rotate, number, watermark and protect.", ".pe-ribbon", RibbonTab.Home),
        new("Fill & Sign", "Type on any form, even one without fields. Add ticks, dates, stamps and your signature, or sign with a certificate.", ".pe-ribbon", RibbonTab.FillSign),
        new("Edit", "Create and arrange form fields, or let Detect Fields find them for you. Import and export the data, and check required fields.", ".pe-ribbon", RibbonTab.Edit),
        new("Tools", "Highlight, underline, draw, add sticky notes, measure, and redact text for good.", ".pe-ribbon", RibbonTab.Tools),
        new("View", "Zoom and rotate, read the document aloud, search a folder of PDFs or compare two versions side by side.", ".pe-ribbon", RibbonTab.View),
        new("The toolbox", "The quick tools beside the pages: select, comment, highlight, draw, add text and sign. The small arrow on a button shows its other tools and colours.", ".pe-rail"),
        new("Pages", "Thumbnails of every page. Click one to jump to it.", ".pe-panel.left"),
        new("Your document", "Open a PDF with Ctrl+O or drag one in. Click a field to fill it in; single keys switch tools: H hand, V select, T text, D date, M stamp.", ".pe-viewer"),
        new("Properties and more", "Properties shows the details of what you've selected; the other tabs list the fields, comments and bookmarks, search results and the AI chat.", ".pe-panel.right"),
        new("AI Assistant", "Ask questions about the open PDF and get answers with page links. It can fill fields, highlight text and add notes; you apply each change.", ".pe-ribbon", RibbonTab.AI),
        new("Page number", "Shows where you are. Type a page number to jump to it.", ".pe-status"),
        new("Help", "The User Guide and Tutorials, What's New, this tour, Tip of the Day, keyboard shortcuts, Report a Problem and About PdfEdit are all here.", ".pe-ribbon", RibbonTab.Help),
        new("You're all set", "Press F1 any time for keyboard shortcuts. Enjoy PdfEdit!"),
    ];

    public int? TourIndex { get; private set; }
    private RibbonTab _tourStartTab;

    private void StartTour()
    {
        _tourStartTab = _tab;
        _dialog = DialogKind.None;
        TourGo(0);
    }

    public void TourGo(int index)
    {
        if (index < 0) return;
        if (index >= TourSteps.Length) { EndTour(); return; }
        TourIndex = index;
        if (TourSteps[index].Tab is { } tab) _tab = tab;
        if (TourSteps[index].Target == ".pe-panel.left") _showLeft = true;
        if (TourSteps[index].Target == ".pe-panel.right") _showRight = true;
    }

    public void EndTour()
    {
        TourIndex = null;
        _tab = _tourStartTab;
    }
}
