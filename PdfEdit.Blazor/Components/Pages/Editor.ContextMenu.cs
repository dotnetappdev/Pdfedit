using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using PdfEdit.Blazor.Components.Editor;
using PdfEdit.Models;

namespace PdfEdit.Blazor.Components.Pages;

// Right-click menus on the page, as in the Windows app: one for the thing under the pointer (cut, copy,
// delete, rotate, fit, colour, line width, opacity, comments, properties) and one for the page itself
// (paste here, add text or a note here). Text being edited and form fields keep the browser's menu.
public partial class Editor
{
    private PageItem? _menuItem;
    private int _menuPage = -1;
    private double _menuX, _menuY, _menuPageX, _menuPageY;

    public bool PageMenuOpen => _menuPage >= 0;
    public PageItem? MenuItem => _menuItem;

    [JSInvokable]
    public Task OnContextMenu(string itemId, int page, double xPct, double yPct, double clientX, double clientY, double viewW, double viewH)
    {
        if (Doc == null || DesignMode) return Task.CompletedTask;
        _menuItem = _items.FirstOrDefault(i => i.Id == itemId);
        // Kept on screen: the item menu is about 270 × 470 pixels, the page menu 250 × 260.
        double scale = UiScale > 0 ? UiScale : 1;
        double mw = 270 * scale, mh = (_menuItem != null ? 470 : 260) * scale;
        _menuX = Math.Max(4, Math.Min(clientX, viewW - mw - 4)) / scale;
        _menuY = Math.Max(4, Math.Min(clientY, viewH - mh - 4)) / scale;
        _menuPage = _menuItem?.Page ?? page;
        if (_menuPage < 0 || _menuPage >= PageCount) { CloseMenu(); return Task.CompletedTask; }
        var (pw, ph) = ViewSize(_menuPage);
        _menuPageX = xPct / 100 * pw;
        _menuPageY = yPct / 100 * ph;
        if (_menuItem != null) SelectItem(_menuItem);
        return InvokeAsync(StateHasChanged);
    }

    private void CloseMenu()
    {
        _menuItem = null;
        _menuPage = -1;
    }

    /// <summary>Runs a menu command, then closes the menu.</summary>
    private async Task MenuAsync(Func<Task> action)
    {
        CloseMenu();
        await action();
    }

    private Task MenuAct(Action action) => MenuAsync(() => { action(); return Task.CompletedTask; });

    private async Task PasteHereAsync()
    {
        int page = _menuPage;
        double x = _menuPageX, y = _menuPageY;
        CloseMenu();
        _page = page;
        int before = _items.Count;
        await PasteAsync();
        // Put what was pasted where the menu was opened.
        if (_items.Count > before && _items[^1] is { } pasted && pasted.Page == page)
        {
            var (pw, ph) = ViewSize(page);
            double dx = Math.Clamp(x, 0, Math.Max(0, pw - pasted.Width)) - pasted.Left;
            double dy = Math.Clamp(y, 0, Math.Max(0, ph - pasted.Height)) - pasted.Top;
            pasted.Left += dx;
            pasted.Top += dy;
            if (pasted.Points != null) pasted.Points = pasted.Points.Select(p => new PointD(p.X + dx, p.Y + dy)).ToList();
        }
    }

    /// <summary>Places a tool's item where the menu was opened (Add text here, Add a note here…).</summary>
    private async Task AddHereAsync(Tool tool)
    {
        int page = _menuPage;
        double scale = DisplayWidth(page) / ViewSize(page).W;
        var at = new MouseEventArgs { OffsetX = _menuPageX * scale, OffsetY = _menuPageY * scale };
        CloseMenu();
        SetTool(tool);
        ToolDown(page, at);
        await ToolUpAsync(page, at);
    }

    private void SetLineWidth(PageItem item, double width)
    {
        item.LineWidth = width;
        if (item.IsSketch) FitSketchBox(item);
    }

    /// <summary>Shows the item in the Comments panel.</summary>
    private void ShowInComments(PageItem item)
    {
        SelectItem(item);
        ShowRight(RightTab.Comments);
    }

    private void ShowProperties(PageItem item)
    {
        SelectItem(item);
        ShowRight(RightTab.Properties);
    }

    // Arrow keys nudge the selected item a point (Shift: ten), like the Windows app's design canvas.
    [JSInvokable]
    public Task OnNudge(int dx, int dy)
    {
        if (DesignMode || PrepareMode || SelectedItem is not { } item) return Task.CompletedTask;
        var (pw, ph) = ViewSize(item.Page);
        var (oldLeft, oldTop) = (item.Left, item.Top);
        item.Left = Math.Clamp(item.Left + dx, 0, Math.Max(0, pw - item.Width));
        item.Top = Math.Clamp(item.Top + dy, 0, Math.Max(0, ph - item.Height));
        MoveSketchPoints(item, oldLeft, oldTop, item.Width, item.Height);
        return InvokeAsync(StateHasChanged);
    }
}
