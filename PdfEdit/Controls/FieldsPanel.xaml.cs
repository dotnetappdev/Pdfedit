using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using PdfEdit.Models;
using PdfEdit.ViewModels;

namespace PdfEdit.Controls;

/// <summary>Acrobat's "Fields" panel: every form field, grouped by page, in reading order.</summary>
public partial class FieldsPanel : UserControl
{
    private MainViewModel? _vm;
    private ListCollectionView? _view;
    private bool _syncing;

    public FieldsPanel()
    {
        InitializeComponent();
        FieldList.ItemTemplate = BuildItemTemplate();
        DataContextChanged += (_, _) => Attach(DataContext as MainViewModel);
    }

    private void Attach(MainViewModel? vm)
    {
        if (_vm != null)
        {
            _vm.PageChanged -= Refresh;
            _vm.PropertyChanged -= OnVmPropertyChanged;
            _vm.AllFields.CollectionChanged -= OnFieldsChanged;
        }
        _vm = vm;
        if (_vm == null) return;

        _view = new ListCollectionView(_vm.AllFields);
        _view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(FormFieldInfo.PageNumber)));
        _view.SortDescriptions.Add(new SortDescription(nameof(FormFieldInfo.PageNumber), ListSortDirection.Ascending));
        // Top of the page first (PDF Y grows upward), then left to right.
        _view.SortDescriptions.Add(new SortDescription(nameof(FormFieldInfo.Bottom), ListSortDirection.Descending));
        _view.SortDescriptions.Add(new SortDescription(nameof(FormFieldInfo.Left), ListSortDirection.Ascending));
        FieldList.ItemsSource = _view;

        _vm.PageChanged += Refresh;
        _vm.PropertyChanged += OnVmPropertyChanged;
        _vm.AllFields.CollectionChanged += OnFieldsChanged;
        UpdateCount();
    }

    private void OnFieldsChanged(object? s, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) => UpdateCount();

    private void UpdateCount() => CountText.Text = _vm == null ? string.Empty
        : $"{_vm.AllFields.Select(f => f.Name).Distinct().Count()} fields";

    /// <summary>Names, required flags and positions are plain properties — re-read them after edits.</summary>
    private void Refresh()
    {
        Dispatcher.BeginInvoke(() =>
        {
            _view?.Refresh();
            UpdateCount();
            SyncSelection();
        });
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.SelectedField)) SyncSelection();
    }

    private void SyncSelection()
    {
        if (_vm == null) return;
        _syncing = true;
        try
        {
            FieldList.SelectedItem = _vm.SelectedField;
            if (_vm.SelectedField != null) FieldList.ScrollIntoView(_vm.SelectedField);
        }
        finally { _syncing = false; }
    }

    private void FieldList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || _vm == null || FieldList.SelectedItem is not FormFieldInfo f) return;
        _vm.SelectFieldForEditing(f);
    }

    private void FieldList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (FieldList.SelectedItem is FormFieldInfo f) _vm?.OpenFieldProperties(f);
    }

    private void Properties_Click(object sender, RoutedEventArgs e)
    {
        if (FieldList.SelectedItem is FormFieldInfo f) _vm?.OpenFieldProperties(f);
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (FieldList.SelectedItem is FormFieldInfo f) _vm?.DeleteField(f);
    }

    private static string Glyph(FormFieldInfo f) => f.FieldType switch
    {
        FieldType.Checkbox => "☑",
        FieldType.RadioButton => "◉",
        FieldType.ComboBox => "▾",
        FieldType.ListBox => "☰",
        FieldType.Signature => "✍",
        _ => f.IsDateField ? "📅" : "T",
    };

    /// <summary>Row: type glyph · name (· choice for radio buttons) · red * when required.</summary>
    private static DataTemplate BuildItemTemplate()
    {
        var sp = new FrameworkElementFactory(typeof(StackPanel));
        sp.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        sp.SetValue(MarginProperty, new Thickness(8, 1, 0, 1));

        var glyph = new FrameworkElementFactory(typeof(TextBlock));
        glyph.SetValue(WidthProperty, 20.0);
        glyph.SetValue(TextBlock.ForegroundProperty, new SolidColorBrush(Color.FromRgb(0x5B, 0x8D, 0xEF)));
        glyph.SetBinding(TextBlock.TextProperty, new Binding { Converter = new FuncConverter(o => o is FormFieldInfo f ? Glyph(f) : "") });

        var name = new FrameworkElementFactory(typeof(TextBlock));
        name.SetBinding(TextBlock.TextProperty, new Binding
        {
            Converter = new FuncConverter(o => o is FormFieldInfo f
                ? f.FieldType == FieldType.RadioButton ? $"{f.DisplayName} ({f.ExportValue})" : f.DisplayName
                : ""),
        });

        var req = new FrameworkElementFactory(typeof(TextBlock));
        req.SetValue(TextBlock.ForegroundProperty, Brushes.IndianRed);
        req.SetValue(MarginProperty, new Thickness(3, 0, 0, 0));
        req.SetBinding(TextBlock.TextProperty, new Binding { Converter = new FuncConverter(o => o is FormFieldInfo { IsRequired: true } ? "*" : "") });

        sp.AppendChild(glyph);
        sp.AppendChild(name);
        sp.AppendChild(req);
        return new DataTemplate { VisualTree = sp };
    }

    private sealed class FuncConverter(Func<object?, object> f) : IValueConverter
    {
        public object Convert(object? value, Type t, object? p, System.Globalization.CultureInfo c) => f(value);
        public object ConvertBack(object? value, Type t, object? p, System.Globalization.CultureInfo c) => throw new NotSupportedException();
    }
}
