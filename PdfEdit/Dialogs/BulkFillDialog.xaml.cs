using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PdfEdit.Services;

namespace PdfEdit.Dialogs;

/// <summary>
/// Bulk fill (mail merge): match the open form's fields to spreadsheet columns and create one
/// filled PDF per row, optionally flattened and combined into one file.
/// </summary>
public partial class BulkFillDialog : Window
{
    private const string Empty = "(leave empty)";
    private readonly string _template;
    private readonly List<BulkField> _fields;
    private readonly Dictionary<string, ComboBox> _maps = new();
    private readonly Dictionary<string, TextBlock> _samples = new();
    private DataTableData? _data;

    public BulkFillDialog(string templatePdf)
    {
        InitializeComponent();
        _template = templatePdf;
        _fields = BulkFillService.GetFields(templatePdf);
        TemplateText.Text = $"Form: {Path.GetFileName(templatePdf)} — {_fields.Count} fillable field(s). Save the form first if you've just added fields.";
        FolderBox.Text = Path.Combine(Path.GetDirectoryName(templatePdf)!, Path.GetFileNameWithoutExtension(templatePdf) + " (filled)");
        BuildGrid();
    }

    private Brush Dim => (Brush)FindResource("DimForegroundBrush");

    private void BuildGrid()
    {
        MapGrid.Children.Clear();
        MapGrid.RowDefinitions.Clear();
        _maps.Clear();
        _samples.Clear();
        AddRow(0, new TextBlock { Text = "Field", FontWeight = FontWeights.SemiBold, Foreground = Dim }, new TextBlock { Text = "Column", FontWeight = FontWeights.SemiBold, Foreground = Dim },
               new TextBlock { Text = "First row", FontWeight = FontWeights.SemiBold, Foreground = Dim });
        int r = 1;
        var choices = new List<string> { Empty };
        if (_data != null) choices.AddRange(_data.Headers);
        foreach (var f in _fields)
        {
            var name = new TextBlock { Text = f.Name, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = $"{f.Name} ({f.Kind})" };
            var combo = new ComboBox { ItemsSource = choices, Height = 24, Margin = new Thickness(6, 2, 6, 2), IsEnabled = _data != null };
            int guess = _data == null ? -1 : BulkFillService.GuessColumn(f.Name, _data.Headers);
            combo.SelectedIndex = guess + 1;
            var sample = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Foreground = Dim, TextTrimming = TextTrimming.CharacterEllipsis };
            combo.SelectionChanged += (_, _) => { UpdateSample(f.Name); UpdateNotes(); };
            _maps[f.Name] = combo;
            _samples[f.Name] = sample;
            AddRow(r++, name, combo, sample);
            UpdateSample(f.Name);
        }
        UpdateNotes();
    }

    private void AddRow(int r, UIElement a, UIElement b, UIElement c)
    {
        MapGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(a, r); Grid.SetRow(b, r); Grid.SetRow(c, r);
        Grid.SetColumn(b, 1); Grid.SetColumn(c, 2);
        MapGrid.Children.Add(a); MapGrid.Children.Add(b); MapGrid.Children.Add(c);
    }

    private int ColumnOf(string field) => _maps.TryGetValue(field, out var c) ? c.SelectedIndex - 1 : -1;

    private void UpdateSample(string field)
    {
        if (_data == null || _data.Rows.Count == 0) { _samples[field].Text = ""; return; }
        int col = ColumnOf(field);
        _samples[field].Text = col < 0 ? "" : _data.Cell(0, col);
    }

    private void UpdateNotes()
    {
        int matched = _maps.Count(m => m.Value.SelectedIndex > 0);
        PreviewNote.Text = _data == null ? "" : $"{matched} of {_fields.Count} fields matched";
        PatternBox_TextChanged(this, null!);
    }

    private void BrowseData_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Spreadsheets|*.csv;*.xlsx;*.tsv;*.txt|All files|*.*", Title = "Choose the data" };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            _data = TableReader.Read(dlg.FileName);
            DataPathBox.Text = dlg.FileName;
            DataInfo.Text = $"{_data.Rows.Count} row(s), {_data.Headers.Count} column(s): {string.Join(", ", _data.Headers.Take(8))}{(_data.Headers.Count > 8 ? "…" : "")}";
            // Default file names from the first column that looks like a name.
            int nameCol = _data.Headers.FindIndex(h => h.Contains("name", StringComparison.OrdinalIgnoreCase));
            PatternBox.Text = nameCol >= 0 ? $"{{{_data.Headers[nameCol]}}}" : "{row}";
            BuildGrid();
        }
        catch (Exception ex) { AppDialog.ShowError("Could not read that file. Save Excel files as .xlsx or CSV.", ex); }
    }

    private void PatternBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (NameExample == null) return;
        NameExample.Text = _data == null || _data.Rows.Count == 0 ? "" : "e.g. " + BulkFillService.FileNameFor(PatternBox.Text, _data, 0) + ".pdf";
    }

    private void BrowseOut_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Save the filled PDFs in" };
        if (dlg.ShowDialog(this) == true) FolderBox.Text = dlg.FolderName;
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(FolderBox.Text) { UseShellExecute = true }); } catch { }
    }

    private async void Run_Click(object sender, RoutedEventArgs e)
    {
        if (_data == null || _data.Rows.Count == 0) { AppDialog.ShowInfo("Choose a spreadsheet with at least one row of data.", "Bulk fill"); return; }
        var mapping = _fields.ToDictionary(f => f.Name, f => ColumnOf(f.Name));
        if (mapping.Values.All(c => c < 0)) { AppDialog.ShowInfo("Match at least one field to a column.", "Bulk fill"); return; }
        string folder = FolderBox.Text.Trim(), pattern = PatternBox.Text;
        bool flatten = FlattenBox.IsChecked == true;
        string? combined = CombineBox.IsChecked == true ? Path.Combine(folder, Path.GetFileNameWithoutExtension(_template) + " (all).pdf") : null;
        var data = _data;

        RunBtn.IsEnabled = false;
        Progress.Visibility = Visibility.Visible;
        Progress.Maximum = data.Rows.Count;
        var progress = new Progress<(int Row, int Total)>(p => { Progress.Value = p.Row; StatusText.Text = $"Filling {Math.Min(p.Row + 1, p.Total)} of {p.Total}…"; });
        try
        {
            var files = await Task.Run(() => BulkFillService.Run(_template, data, mapping, folder, pattern, flatten, combined, progress, CancellationToken.None));
            StatusText.Text = $"Created {files.Count} PDF(s){(combined != null ? " and a combined copy" : "")}.";
            OpenFolderBtn.Visibility = Visibility.Visible;
            ToastService.Instance.Success($"Bulk fill: {files.Count} PDF(s) created.");
        }
        catch (Exception ex) { AppDialog.ShowError("Bulk fill failed.", ex); StatusText.Text = "Failed."; }
        finally { RunBtn.IsEnabled = true; }
    }
}
