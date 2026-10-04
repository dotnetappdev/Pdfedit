using System.IO;
using System.Windows;
using System.Windows.Input;
using PdfEdit.Services;

namespace PdfEdit.ViewModels;

/// <summary>Hyperlinks in the PDF: shown on the page, followed, added, edited and removed.</summary>
public partial class MainViewModel
{
    private (string Path, DateTime Stamp, List<PdfLinkInfo> Links)? _linkCache;

    /// <summary>Links on the current page (read from the PDF, cached until the file changes).</summary>
    public IReadOnlyList<PdfLinkInfo> GetLinksForCurrentPage()
    {
        if (_currentFilePath == null || _document == null) return Array.Empty<PdfLinkInfo>();
        try
        {
            var stamp = File.GetLastWriteTimeUtc(_currentFilePath);
            if (_linkCache is not { } c || c.Path != _currentFilePath || c.Stamp != stamp)
                _linkCache = (_currentFilePath, stamp, LinkService.GetLinks(_currentFilePath));
            int page = _currentPageIndex + 1;
            return _linkCache.Value.Links.Where(l => l.PageNumber == page).ToList();
        }
        catch
        {
            return Array.Empty<PdfLinkInfo>();
        }
    }

    /// <summary>Opens the link's web address / email / phone, or jumps to its page.</summary>
    public void FollowLink(PdfLinkInfo link)
    {
        if (link.Target.PageNumber is { } p)
        {
            CurrentPageIndex = p - 1;
            StatusText = $"Page {p}.";
            return;
        }
        string? uri = link.Target.Uri;
        if (string.IsNullOrWhiteSpace(uri)) return;
        bool web = uri.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || uri.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                   || uri.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) || uri.StartsWith("tel:", StringComparison.OrdinalIgnoreCase);
        // Like Acrobat's security warning: a link can point anywhere.
        if (!Dialogs.AppDialog.ShowConfirm($"This document is trying to open:\n\n{uri}\n\nOnly continue if you trust the document.",
                "Open link", web ? "Open" : "Open anyway", "Cancel", isDanger: !web))
            return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Dialogs.AppDialog.ShowError($"Could not open {uri}.", ex);
        }
    }

    /// <summary>Asks for the target and adds a link over the rectangle (PDF points) on the current page.</summary>
    public async Task AddLinkInteractiveAsync(double left, double bottom, double width, double height)
    {
        if (_document == null) return;
        int page = _currentPageIndex + 1;
        var dlg = new Dialogs.LinkUriDialog(_document.PageCount) { Owner = Application.Current.MainWindow };
        if (dlg.ShowDialog() != true || dlg.Target == null) return;
        var target = dlg.Target;
        bool border = dlg.ShowBorder;
        if (await ModifyCurrentFileAsync((i, o) => LinkService.AddLink(i, o, page, left, bottom, width, height, target, border),
                $"Link added ({target.Describe()})"))
            ToastService.Instance.Success("Link added.");
    }

    public async Task EditLinkAsync(PdfLinkInfo link)
    {
        if (_document == null) return;
        var dlg = new Dialogs.LinkUriDialog(_document.PageCount, link) { Owner = Application.Current.MainWindow };
        if (dlg.ShowDialog() != true) return;
        if (dlg.RemoveRequested) { await RemoveLinkAsync(link); return; }
        if (dlg.Target == null) return;
        var target = dlg.Target;
        bool border = dlg.ShowBorder;
        await ModifyCurrentFileAsync((i, o) => LinkService.UpdateLink(i, o, link.PageNumber, link.AnnotIndex, target, border),
            $"Link changed ({target.Describe()})");
    }

    public async Task RemoveLinkAsync(PdfLinkInfo link)
    {
        if (await ModifyCurrentFileAsync((i, o) => LinkService.RemoveLinks(i, o, link.PageNumber, link.AnnotIndex), "Link removed"))
            ToastService.Instance.Success("Link removed.");
    }

    private ICommand? _removeAllLinksCommand;
    /// <summary>Removes every link in the document (after confirming).</summary>
    public ICommand RemoveAllLinksCommand => _removeAllLinksCommand ??= new AsyncRelayCommand(async () =>
    {
        if (_currentFilePath == null) return;
        int count;
        try { count = LinkService.GetLinks(_currentFilePath).Count; }
        catch { count = 0; }
        if (count == 0) { ToastService.Instance.Info("This PDF has no links."); return; }
        if (!Dialogs.AppDialog.ShowConfirm($"Remove all {count} link(s) from this document?", "Remove links", "Remove all", "Cancel", isDanger: true))
            return;
        await ModifyCurrentFileAsync((i, o) => LinkService.RemoveLinks(i, o, 0), $"{count} link(s) removed");
    }, () => HasDocument);
}
