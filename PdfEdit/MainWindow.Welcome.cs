using System.Windows;
using System.Windows.Threading;
using PdfEdit.Controls;
using PdfEdit.Dialogs;
using PdfEdit.Services;

namespace PdfEdit;

/// <summary>
/// First launch: the guided tour. Later launches: Tip of the Day (unless turned off). Both are
/// remembered in settings.json (TourCompleted, ShowTipsAtStartup, NextTipIndex) and can be
/// opened again from File or View → Help.
/// </summary>
public partial class MainWindow
{
    private bool _tourRunning;

    private void OnFirstRendered(object? sender, EventArgs e)
    {
        ContentRendered -= OnFirstRendered;
        // Wait until the window has finished laying out (ribbon, docked panels) before measuring.
        Dispatcher.InvokeAsync(() =>
        {
            var s = AppSettings.Current;
            if (!s.TourCompleted) StartTour();
            else if (s.ShowTipsAtStartup) ShowTipOfTheDay();
        }, DispatcherPriority.ApplicationIdle);
    }

    private void TipOfTheDay_Click(object sender, RoutedEventArgs e) => ShowTipOfTheDay();

    private void TakeTour_Click(object sender, RoutedEventArgs e) => StartTour();

    private void ShowTipOfTheDay()
    {
        if (!IsVisible) return;
        new TipOfTheDayDialog { Owner = this }.ShowDialog();
    }

    private void StartTour()
    {
        if (_tourRunning) return;
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        var startTab = MainRibbon.SelectedTabItem;

        var steps = new List<TourStep>
        {
            new("Welcome to PdfEdit",
                "A quick look round: about a minute. You can skip it now and take it later from File → Take the Tour."),
            new("The ribbon: Home",
                "Everything is grouped into tabs, like Office. Home is for files and pages: open, save, merge, split, rotate, number, watermark and protect.",
                () => MainRibbon, () => MainRibbon.SelectedTabItem = HomeTab),
            new("Fill & Sign",
                "Type on any form, even one without fields. Add ticks, dates, stamps and your signature, or sign with a certificate.",
                () => MainRibbon, () => MainRibbon.SelectedTabItem = FillSignTab),
            new("Edit",
                "Create and arrange form fields, or let Detect Fields find them for you. Import and export the data, and check required fields.",
                () => MainRibbon, () => MainRibbon.SelectedTabItem = EditTab),
            new("Tools",
                "Highlight, underline, draw, add sticky notes, measure, and redact text for good.",
                () => MainRibbon, () => MainRibbon.SelectedTabItem = ToolsTab),
            new("View",
                "Zoom and rotate, read the document aloud, search a folder of PDFs or compare two versions side by side.",
                () => MainRibbon, () => MainRibbon.SelectedTabItem = ViewTab),
            new("All tools",
                "Every feature in one list, grouped like Acrobat's. Type in its search box to find a tool by name.",
                () => AllToolsPane, () => ShowDockPane("alltools")),
            new("Pages",
                "Thumbnails of every page. Click one to jump to it, or right-click to rotate, move, insert, extract or delete it.",
                () => ThumbnailsPane, () => ShowDockPane("thumbnails")),
            new("Your document",
                "Open a PDF with Ctrl+O or drag one onto the window. Click a field to fill it in; single keys switch tools: H hand, V select, T text, D date, K tick, M stamp.",
                () => PdfViewer),
            new("Properties",
                "Shows the details of whatever you've selected (a field, a text box, a stamp) so you can change its font, colour, format or date style.",
                () => PropertiesPane, () => ShowDockPane("properties")),
            new("AI Assistant",
                "Ask questions about the open PDF and get answers with page links. It can also fill fields, highlight text and add notes; you apply each change. Use your own Claude or OpenAI key, or a free local model.",
                () => AiChatPane, () => ShowDockPane("aichat")),
            new("Page number",
                "Shows where you are. Click it (or press Ctrl+G) to jump to a page.",
                () => PageNavStatusPanel),
            new("You're all set",
                "Press F1 any time for keyboard shortcuts. A Tip of the Day appears when PdfEdit starts; turn it off in the tip window, or find it under View → Help."),
        };

        _tourRunning = TourOverlay.Start(this, steps, () =>
        {
            _tourRunning = false;
            if (startTab != null) MainRibbon.SelectedTabItem = startTab;
            AppSettings.Current.TourCompleted = true;
            AppSettings.Current.Save();
        });
        if (!_tourRunning)
        {
            // No overlay layer (shouldn't happen): don't keep trying on every launch.
            AppSettings.Current.TourCompleted = true;
            AppSettings.Current.Save();
        }
    }
}
