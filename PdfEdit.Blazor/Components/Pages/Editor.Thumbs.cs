using Microsoft.AspNetCore.Components.Web;

namespace PdfEdit.Blazor.Components.Pages;

// The thumbnails panel's page tools, as in the Windows app's PageThumbnailsPanel: rotate and delete on
// hover, the right-click menu (rotate, insert, move, delete, extract) and drag a thumbnail to reorder.
public partial class Editor
{
    private int? _thumbMenu;
    private double _thumbMenuX, _thumbMenuY;
    private int? _thumbDrag, _thumbDrop;

    private void OpenThumbMenu(int page, MouseEventArgs e)
    {
        // The menu is placed in the app's (zoomed) coordinates.
        double scale = UiScale > 0 ? UiScale : 1;
        _thumbMenu = page;
        _thumbMenuX = e.ClientX / scale;
        _thumbMenuY = e.ClientY / scale;
    }

    /// <summary>Runs a page command on <paramref name="page"/> (the commands work on the current page).</summary>
    private async Task ThumbActionAsync(int page, Func<Task> action)
    {
        _thumbMenu = null;
        if (Doc == null || page < 0 || page >= PageCount) return;
        if (page != _page) await GoToPageAsync(page, smooth: false);
        _page = page;
        await action();
    }

    private void ThumbDragStart(int page)
    {
        _thumbDrag = page;
        _thumbDrop = null;
    }

    private void ThumbDragEnd()
    {
        _thumbDrag = _thumbDrop = null;
    }

    private async Task ThumbDropAsync(int target)
    {
        var from = _thumbDrag;
        _thumbDrag = _thumbDrop = null;
        if (Doc == null || from is not int source || source == target) return;
        var order = Enumerable.Range(0, PageCount).ToList();
        order.RemoveAt(source);
        order.Insert(target, source);
        await ChangeAsync("Moving the page…", $"Moved page {source + 1} to position {target + 1}",
            (src, dest) => Store.Forms.ReorderPages(src, dest, order));
        await GoToPageAsync(target);
    }
}
