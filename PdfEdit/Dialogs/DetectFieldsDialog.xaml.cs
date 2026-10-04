using System.Windows;
using PdfEdit.Services;

namespace PdfEdit.Dialogs;

/// <summary>Review fields found on a flat form: choose which to add and rename them first.</summary>
public partial class DetectFieldsDialog : Window
{
    private readonly List<NewField> _fields;
    public IReadOnlyList<NewField> Selected => _fields.Where(f => f.Include).ToList();

    public DetectFieldsDialog(List<NewField> fields)
    {
        InitializeComponent();
        _fields = fields;
        List.ItemsSource = _fields;
        int boxes = fields.Count(f => f.IsCheckBox);
        Intro.Text = $"Found {fields.Count - boxes} text field(s) and {boxes} check box(es). Untick any that aren't fields and fix the names, " +
                     "then Add fields. You can move and resize them afterwards with Edit Fields.";
    }

    private void Refresh() { List.ItemsSource = null; List.ItemsSource = _fields; }
    private void All_Click(object sender, RoutedEventArgs e) { _fields.ForEach(f => f.Include = true); Refresh(); }
    private void None_Click(object sender, RoutedEventArgs e) { _fields.ForEach(f => f.Include = false); Refresh(); }
    private void Add_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
