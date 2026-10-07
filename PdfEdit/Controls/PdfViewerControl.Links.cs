using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using PdfEdit.Models;
using PdfEdit.Services;

namespace PdfEdit.Controls;

/// <summary>
/// Hyperlinks stored in the PDF like other PDF editors: with the Hand / Select tool a link shows a hand
/// cursor and its target, and a click follows it (web page, email, phone, or a page here). With
/// the Link tool every link is outlined and a click edits it. Right-click: open, edit, copy, remove.
/// </summary>
public partial class PdfViewerControl
{
    private bool LinksClickable => _vm?.ActiveTool is ActiveTool.Hand or ActiveTool.Select or ActiveTool.Link;

    private void UpdateLinkHitTesting()
    {
        LinkCanvas.IsHitTestVisible = LinksClickable;
        bool editing = _vm?.ActiveTool == ActiveTool.Link;
        foreach (var r in LinkCanvas.Children.OfType<Rectangle>())
            r.Stroke = editing ? LinkEditStroke : Brushes.Transparent;
    }

    private static readonly Brush LinkEditStroke = Freeze(Color.FromRgb(0x0A, 0x84, 0xFF));
    private static readonly Brush LinkHoverFill = Freeze(Color.FromArgb(0x33, 0x0A, 0x84, 0xFF));

    private void BuildLinkOverlay(double width, double height)
    {
        LinkCanvas.Children.Clear();
        LinkCanvas.Width = width;
        LinkCanvas.Height = height;
        if (_vm?.Document == null) return;
        int pageNum = _vm.CurrentPageIndex + 1;
        if (pageNum < 1 || pageNum > _vm.Document.PageSizes.Count) return;
        double pageH = _vm.Document.PageSizes[pageNum - 1].Height;

        foreach (var link in _vm.GetLinksForCurrentPage())
        {
            var rect = new Rectangle
            {
                Width = Math.Max(4, link.Width * Scale),
                Height = Math.Max(4, link.Height * Scale),
                Fill = Brushes.Transparent,
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection { 3, 2 },
                Cursor = Cursors.Hand,
                ToolTip = link.Target.Describe() + (_vm.ActiveTool == ActiveTool.Link ? "  —  click to edit" : "  —  click to open"),
            };
            AutomationProperties_SetName(rect, "Link: " + link.Target.Describe());
            Canvas.SetLeft(rect, link.Left * Scale);
            Canvas.SetTop(rect, (pageH - link.Bottom - link.Height) * Scale);

            var captured = link;
            rect.MouseEnter += (_, _) => rect.Fill = LinkHoverFill;
            rect.MouseLeave += (_, _) => rect.Fill = Brushes.Transparent;
            rect.MouseLeftButtonDown += (_, e) => e.Handled = true; // don't start panning / selection
            rect.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                if (_vm == null) return;
                if (_vm.ActiveTool == ActiveTool.Link) _ = _vm.EditLinkAsync(captured);
                else _vm.FollowLink(captured);
            };

            var cm = new ContextMenu();
            var open = new MenuItem { Header = captured.Target.IsPage ? $"Go to page {captured.Target.PageNumber}" : "Open link" };
            open.Click += (_, _) => _vm?.FollowLink(captured);
            var edit = new MenuItem { Header = "Edit link…" };
            edit.Click += (_, _) => { if (_vm != null) _ = _vm.EditLinkAsync(captured); };
            var copy = new MenuItem { Header = "Copy link address", IsEnabled = !captured.Target.IsPage };
            copy.Click += (_, _) => { try { Clipboard.SetText(captured.Target.Uri ?? ""); } catch { } };
            var remove = new MenuItem { Header = "Remove link" };
            remove.Click += (_, _) => { if (_vm != null) _ = _vm.RemoveLinkAsync(captured); };
            cm.Items.Add(open);
            cm.Items.Add(edit);
            cm.Items.Add(copy);
            cm.Items.Add(new Separator());
            cm.Items.Add(remove);
            rect.ContextMenu = cm;

            LinkCanvas.Children.Add(rect);
        }
        UpdateLinkHitTesting();
    }

    private static void AutomationProperties_SetName(DependencyObject d, string name) =>
        System.Windows.Automation.AutomationProperties.SetName(d, name);
}
