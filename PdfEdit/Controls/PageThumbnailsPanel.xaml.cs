using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using PdfEdit.Models;
using PdfEdit.ViewModels;

namespace PdfEdit.Controls;

public partial class PageThumbnailsPanel : UserControl
{
    private MainViewModel? _vm;
    private readonly ObservableCollection<ThumbnailItem> _thumbs = new();
    private bool _suppressSelectionChanged;

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
}
