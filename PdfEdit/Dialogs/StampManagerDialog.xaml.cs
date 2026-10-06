using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using PdfEdit.Controls;
using PdfEdit.Models;
using PdfEdit.ViewModels;

namespace PdfEdit.Dialogs;

/// <summary>
/// Stamps…: every stamp with a preview, grouped like Acrobat. Pick one to use, set the default the
/// Stamp tool starts with, create / edit / duplicate / delete your own (text, colour, name + time),
/// or put one behind the page as a background (watermark).
/// </summary>
public partial class StampManagerDialog : Window
{
    /// <summary>One row in the list: the stamp and its drawing.</summary>
    public sealed class StampRow
    {
        public required StampDefinition Def { get; init; }
        public required Brush Preview { get; init; }
        public double Width { get; init; }
        public double Height { get; init; }
        public bool IsDefault { get; init; }
        public string Category => Def.Category;
        public Visibility DefaultVisibility => IsDefault ? Visibility.Visible : Visibility.Collapsed;
        public string Tooltip => $"{Def.Title}, {Def.Category}{(Def.Dynamic ? ", adds your name and the time" : "")}{(IsDefault ? ", default" : "")}";
    }

    private readonly MainViewModel _vm;
    private readonly List<StampRow> _rows = new();
    private ICollectionView? _view;
    private bool _editingNew;     // the editor holds a stamp that isn't saved yet
    private bool _loading = true;   // true until the first stamp is shown: XAML sets ColorBox.Text during InitializeComponent

    /// <summary>The stamp chosen (Use this stamp / Use as page background).</summary>
    public StampDefinition? Chosen { get; private set; }
    /// <summary>True when the chosen stamp should go behind the pages as a watermark.</summary>
    public bool UseAsBackground { get; private set; }

    private StampDefinition? Selected => (StampList.SelectedItem as StampRow)?.Def;
    private bool SelectedIsCustom => Selected?.Category == StampCatalog.CustomCategory;

    public StampManagerDialog(MainViewModel vm, bool createNew = false)
    {
        InitializeComponent();
        _vm = vm;

        foreach (var (hex, name) in new[] { ("#1B7A2E", "Green"), ("#C62828", "Red"), ("#1F4FB5", "Blue"), ("#6A1B9A", "Purple"),
                                            ("#D35400", "Orange"), ("#555555", "Grey"), ("#000000", "Black") })
        {
            var b = new Button
            {
                Width = 22, Height = 22, Margin = new Thickness(0, 0, 5, 0), Padding = new Thickness(0),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)), ToolTip = name,
            };
            System.Windows.Automation.AutomationProperties.SetName(b, name);
            b.Click += (_, _) => { if (ColorBox.IsEnabled) ColorBox.Text = hex; };
            Swatches.Children.Add(b);
        }

        Reload(vm.SelectedStampDefinition);
        Loaded += (_, _) =>
        {
            if (createNew) StartNew(null);
            else { StampList.ScrollIntoView(StampList.SelectedItem); (StampList.ItemContainerGenerator.ContainerFromItem(StampList.SelectedItem) as ListBoxItem)?.Focus(); }
        };
    }

    // ── List ─────────────────────────────────────────────────────────────────

    private void Reload(StampDefinition? select)
    {
        _rows.Clear();
        foreach (var d in _vm.Stamps)
        {
            var (brush, w, h) = Draw(d.Title, d.Color, d.Dynamic);
            _rows.Add(new StampRow { Def = d, Preview = brush, Width = w, Height = h, IsDefault = _vm.IsDefaultStamp(d) });
        }
        var cvs = new CollectionViewSource { Source = _rows };
        cvs.GroupDescriptions.Add(new PropertyGroupDescription(nameof(StampRow.Category)));
        _view = cvs.View;
        _view.Filter = Matches;
        StampList.ItemsSource = _view;
        StampList.SelectedItem = _rows.FirstOrDefault(r => select != null && r.Def.Category == select.Category && r.Def.Title == select.Title)
                                 ?? _rows.FirstOrDefault();
        if (StampList.SelectedItem != null) StampList.ScrollIntoView(StampList.SelectedItem);
        ShowSelected();
    }

    private bool Matches(object o)
    {
        string q = SearchBox.Text.Trim();
        return q.Length == 0 || o is StampRow r && (r.Def.Title.Contains(q, StringComparison.OrdinalIgnoreCase)
                                                    || r.Def.Category.Contains(q, StringComparison.OrdinalIgnoreCase));
    }

    private void Search_Changed(object sender, TextChangedEventArgs e) => _view?.Refresh();

    private void StampList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (StampList.SelectedItem == null) return;
        _editingNew = false;
        ShowSelected();
    }

    private void StampList_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Selected != null && !_editingNew) Use_Click(sender, e);
    }

    /// <summary>Fills the editor from the selected stamp (editable only for custom stamps).</summary>
    private void ShowSelected()
    {
        var d = Selected;
        _loading = true;
        TitleBox.Text = d?.Title ?? "";
        ColorBox.Text = d?.Color ?? "#6A1B9A";
        DynamicBox.IsChecked = d?.Dynamic == true;
        _loading = false;

        bool editable = SelectedIsCustom;
        TitleBox.IsEnabled = ColorBox.IsEnabled = DynamicBox.IsEnabled = editable;
        Swatches.IsEnabled = editable;
        BuiltInNote.Visibility = d != null && !editable ? Visibility.Visible : Visibility.Collapsed;
        SaveBtn.Content = "Save changes";
        SaveBtn.Visibility = editable ? Visibility.Visible : Visibility.Collapsed;
        SaveBtn.IsEnabled = false;
        CancelEditBtn.Visibility = Visibility.Collapsed;
        DeleteBtn.IsEnabled = editable;
        DuplicateBtn.IsEnabled = d != null;
        DefaultBtn.IsEnabled = d != null && !_vm.IsDefaultStamp(d);
        DefaultBtn.Content = d != null && _vm.IsDefaultStamp(d) ? "Default stamp ✓" : "Set as default";
        UseBtn.IsEnabled = d != null;
        BackgroundBtn.IsEnabled = d != null && _vm.HasDocument;
        StampList.IsEnabled = true;
        ModeText.Text = d == null ? "PREVIEW" : $"PREVIEW: {d.Category.ToUpperInvariant()}";
        UpdatePreview();
    }

    // ── Editor ───────────────────────────────────────────────────────────────

    private void StartNew(StampDefinition? from)
    {
        _editingNew = true;
        _loading = true;
        TitleBox.Text = from?.Title ?? "MY STAMP";
        ColorBox.Text = from?.Color ?? "#6A1B9A";
        DynamicBox.IsChecked = from?.Dynamic == true;
        _loading = false;

        TitleBox.IsEnabled = ColorBox.IsEnabled = DynamicBox.IsEnabled = Swatches.IsEnabled = true;
        BuiltInNote.Visibility = Visibility.Collapsed;
        SaveBtn.Content = "Create stamp";
        SaveBtn.Visibility = CancelEditBtn.Visibility = Visibility.Visible;
        SaveBtn.IsEnabled = true;
        DeleteBtn.IsEnabled = DuplicateBtn.IsEnabled = DefaultBtn.IsEnabled = UseBtn.IsEnabled = BackgroundBtn.IsEnabled = false;
        StampList.IsEnabled = false;
        ModeText.Text = "NEW STAMP";
        UpdatePreview();
        TitleBox.Focus();
        TitleBox.SelectAll();
    }

    private void New_Click(object sender, RoutedEventArgs e) => StartNew(null);

    private void Duplicate_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is { } d) StartNew(d);
    }

    private void CancelEdit_Click(object sender, RoutedEventArgs e)
    {
        _editingNew = false;
        ShowSelected();
        StampList.Focus();
    }

    private void Editor_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        if (!_editingNew && SelectedIsCustom) SaveBtn.IsEnabled = true;
        UpdatePreview();
    }

    private string EditorColor()
    {
        string c = ColorBox.Text.Trim();
        if (c.Length > 0 && c[0] != '#') c = "#" + c;
        try { ColorConverter.ConvertFromString(c); return c.ToUpperInvariant(); }
        catch { return ""; }
    }

    private void UpdatePreview()
    {
        string title = string.IsNullOrWhiteSpace(TitleBox.Text) ? " " : TitleBox.Text.Trim();
        string color = EditorColor();
        ColorBox.BorderBrush = color.Length == 0 ? Brushes.IndianRed : (Brush)FindResource("InputBorderBrush");
        var (brush, w, h) = Draw(title, color.Length == 0 ? "#6A1B9A" : color, DynamicBox.IsChecked == true);
        BigPreview.Fill = brush;
        BigPreview.Width = w;
        BigPreview.Height = h;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        string color = EditorColor();
        if (color.Length == 0)
        {
            AppDialog.ShowInfo("Type a colour like #C62828, or click one of the swatches.", "Stamp colour");
            ColorBox.Focus();
            return;
        }
        string? oldTitle = _editingNew ? null : Selected?.Title;
        var saved = _vm.SaveCustomStamp(oldTitle, TitleBox.Text, color, DynamicBox.IsChecked == true, out var error);
        if (saved == null)
        {
            AppDialog.ShowInfo(error ?? "The stamp couldn't be saved.", "Stamp");
            TitleBox.Focus();
            return;
        }
        _editingNew = false;
        SearchBox.Text = "";
        Reload(saved);
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (!SelectedIsCustom || Selected is not { } d) return;
        if (!AppDialog.ShowConfirm($"Delete the custom stamp \"{d.Title}\"?\nStamps already placed on pages are kept.",
                "Delete stamp", "Delete", "Cancel", isDanger: true))
            return;
        _vm.DeleteCustomStamp(d.Title);
        Reload(_vm.SelectedStampDefinition);
    }

    // ── Use ──────────────────────────────────────────────────────────────────

    private void Default_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } d) return;
        _vm.SetDefaultStamp(d);
        Reload(d);
    }

    private void Use_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } d || _editingNew) return;
        Chosen = d;
        DialogResult = true;
    }

    private void Background_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } d || _editingNew) return;
        Chosen = d;
        UseAsBackground = true;
        DialogResult = true;
    }

    // ── Drawing ──────────────────────────────────────────────────────────────

    /// <summary>The stamp as it will look on the page (same drawing the viewer uses), and its size in points.</summary>
    private static (Brush Brush, double Width, double Height) Draw(string title, string color, bool dynamic)
    {
        string? subtitle = dynamic ? new StampDefinition("", title, color, true).MakeSubtitle(DateTime.Now) : null;
        var (w, h) = StampCatalog.SizeFor(title, subtitle);
        var ann = new FreeTextAnnotation
        {
            Text = title, StampSubtitle = subtitle, IsStamp = true, FontColor = color, Width = w, Height = h,
        };
        return (PdfViewerControl.StampBrush(ann), w, h);
    }
}
