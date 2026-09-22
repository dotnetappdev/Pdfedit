using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PdfEdit.Models;
using PdfEdit.ViewModels;

namespace PdfEdit.Controls;

public partial class PageThumbnailsPanel : UserControl
{
    private MainViewModel? _vm;
    private readonly ObservableCollection<ThumbnailItem> _thumbs = new();
    private bool _suppressSelectionChanged;
    private ThumbnailItem? _dragSource;
    private Point _dragStartPoint;

    public PageThumbnailsPanel()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        ThumbList.ItemsSource = _thumbs;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        _vm = DataContext as MainViewModel;
        if (_vm == null) return;

        _vm.DocumentLoaded += OnDocumentLoaded;
        _vm.PageChanged += OnPageChanged;
    }

    private void OnDocumentLoaded()
    {
        _ = LoadThumbnailsAsync();
    }

    private void OnPageChanged()
    {
        if (_vm == null) return;
        UpdateSelection();
    }

    private async Task LoadThumbnailsAsync()
    {
        _thumbs.Clear();
        if (_vm?.Document == null) return;

        int count = _vm.Document.PageCount;
        var items = new ThumbnailItem[count];

        for (int i = 0; i < count; i++)
        {
            items[i] = new ThumbnailItem { PageIndex = i };
            _thumbs.Add(items[i]);
        }

        UpdateSelection();

        for (int i = 0; i < count; i++)
        {
            int idx = i;
            try
            {
                var bmp = await _vm.RenderService.RenderPageAsync(idx, 0.12);
                items[idx].Thumbnail = bmp;
            }
            catch { }
        }
    }

    private void UpdateSelection()
    {
        if (_vm == null) return;
        _suppressSelectionChanged = true;
        var current = _thumbs.FirstOrDefault(t => t.PageIndex == _vm.CurrentPageIndex);
        if (current != null)
        {
            ThumbList.SelectedItem = current;
            ThumbList.ScrollIntoView(current);
        }
        _suppressSelectionChanged = false;
    }

    private void ThumbList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelectionChanged || _vm == null) return;
        if (ThumbList.SelectedItem is ThumbnailItem item)
            _vm.CurrentPageIndex = item.PageIndex;
    }

    // ── Context-menu handlers ────────────────────────────────────────────────

    private int ContextPageIndex()
    {
        // Use the currently selected thumbnail as the target for context actions
        if (ThumbList.SelectedItem is ThumbnailItem item) return item.PageIndex;
        return _vm?.CurrentPageIndex ?? 0;
    }

    private void ThumbRotateCW_Click(object sender, RoutedEventArgs e)
    {
        if (_vm == null) return;
        _vm.CurrentPageIndex = ContextPageIndex();
        _vm.RotatePageCWCommand.Execute(null);
    }

    private void ThumbRotateCCW_Click(object sender, RoutedEventArgs e)
    {
        if (_vm == null) return;
        _vm.CurrentPageIndex = ContextPageIndex();
        _vm.RotatePageCCWCommand.Execute(null);
    }

    private void ThumbInsertBefore_Click(object sender, RoutedEventArgs e)
    {
        if (_vm == null) return;
        _vm.CurrentPageIndex = ContextPageIndex();
        _vm.InsertPageBeforeCommand.Execute(null);
    }

    private void ThumbInsertAfter_Click(object sender, RoutedEventArgs e)
    {
        if (_vm == null) return;
        _vm.CurrentPageIndex = ContextPageIndex();
        _vm.InsertBlankPageCommand.Execute(null);
    }

    private void ThumbMoveUp_Click(object sender, RoutedEventArgs e)
    {
        if (_vm == null) return;
        _vm.CurrentPageIndex = ContextPageIndex();
        _vm.MovePageUpCommand.Execute(null);
    }

    private void ThumbMoveDown_Click(object sender, RoutedEventArgs e)
    {
        if (_vm == null) return;
        _vm.CurrentPageIndex = ContextPageIndex();
        _vm.MovePageDownCommand.Execute(null);
    }

    private void ThumbDelete_Click(object sender, RoutedEventArgs e)
    {
        if (_vm == null) return;
        _vm.CurrentPageIndex = ContextPageIndex();
        _vm.DeleteCurrentPageCommand.Execute(null);
    }

    private void ThumbExtract_Click(object sender, RoutedEventArgs e)
    {
        if (_vm == null) return;
        _vm.CurrentPageIndex = ContextPageIndex();
        _vm.ExtractCurrentPageCommand.Execute(null);
    }

    // ── Drag-and-drop page reordering ────────────────────────────────────────

    private void Thumb_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _vm == null) return;
        if (sender is StackPanel sp && sp.DataContext is ThumbnailItem item)
        {
            _dragSource = item;
            DragDrop.DoDragDrop(sp, item, DragDropEffects.Move);
        }
    }

    private void Thumb_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(ThumbnailItem))
            ? DragDropEffects.Move
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void Thumb_Drop(object sender, DragEventArgs e)
    {
        if (_vm == null || !e.Data.GetDataPresent(typeof(ThumbnailItem))) return;
        if (sender is not StackPanel sp || sp.DataContext is not ThumbnailItem dropTarget) return;
        if (_dragSource == null || _dragSource == dropTarget) return;

        int fromIdx = _dragSource.PageIndex;
        int toIdx   = dropTarget.PageIndex;

        // Navigate to the destination and reorder
        _vm.CurrentPageIndex = fromIdx;
        if (fromIdx < toIdx)
            _ = ReorderToAsync(fromIdx, toIdx);
        else
            _ = ReorderToAsync(fromIdx, toIdx);

        _dragSource = null;
    }

    private async Task ReorderToAsync(int fromIdx, int toIdx)
    {
        if (_vm == null) return;
        var newOrder = Enumerable.Range(0, _vm.Document!.PageCount).ToList();
        newOrder.RemoveAt(fromIdx);
        newOrder.Insert(toIdx, fromIdx);
        await _vm.ReorderPagesAsync(newOrder, toIdx);
    }
}
