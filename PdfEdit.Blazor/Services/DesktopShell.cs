namespace PdfEdit.Blazor.Services;

/// <summary>
/// What the desktop app (PdfEdit.Avalonia) does for the web app it hosts: save files with the
/// system's Save dialog instead of a browser download, and open files and links with the computer's
/// own apps. Null when PdfEdit runs as a website.
/// </summary>
public interface IDesktopShell
{
    /// <summary>Asks where to save <paramref name="sourcePath"/> (suggesting <paramref name="suggestedName"/>) and copies it there. Returns where, or null if cancelled.</summary>
    Task<string?> SaveFileAsync(string suggestedName, string sourcePath);

    /// <summary>Opens a file with the app the computer uses for it (a PDF viewer to print, say).</summary>
    void OpenWithSystem(string path);

    /// <summary>Opens a web page or email link in the computer's browser or mail app.</summary>
    void OpenExternal(Uri uri);
}
