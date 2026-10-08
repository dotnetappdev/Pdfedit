namespace PdfEdit.Services;

/// <summary>
/// The PdfEdit website's help pages (built from staticsite/ and published on GitHub Pages), opened
/// from Help in the Windows app, the web version and the desktop app.
/// </summary>
public static class HelpLinks
{
    public const string Site = $"https://{UpdateService.Owner}.github.io/Pdfedit/";
    public const string UserGuide = Site + "docs/index.html";
    public const string Tutorials = Site + "tutorials/index.html";
    public const string GettingStarted = Site + "docs/getting-started.html";
    public const string Troubleshooting = Site + "docs/troubleshooting.html";
    public const string KeyboardShortcuts = Site + "docs/keyboard-shortcuts.html";

    /// <summary>A page of the user guide, e.g. Topic("forms") → …/docs/forms.html.</summary>
    public static string Topic(string slug) => $"{Site}docs/{slug}.html";
}
