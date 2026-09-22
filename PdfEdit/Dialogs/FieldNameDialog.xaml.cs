using System.Windows;

namespace PdfEdit.Dialogs;

public partial class FieldNameDialog : Window
{
    private string _fieldType = "Text Field";

    public string FieldType
    {
        get => _fieldType;
        set
        {
            _fieldType = value;
            if (IsInitialized)
            {
                FieldTypeLabel.Text = $"{value} name:";
                ComboOptionsPanel.Visibility = value == "Combo Box" ? Visibility.Visible : Visibility.Collapsed;
            }
        }
    }

    public string FieldName     { get; private set; } = string.Empty;
    public string[]? ComboChoices { get; private set; }

    public FieldNameDialog()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            FieldTypeLabel.Text = $"{_fieldType} name:";
            ComboOptionsPanel.Visibility = _fieldType == "Combo Box" ? Visibility.Visible : Visibility.Collapsed;
        };
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        string name = FieldNameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            ErrorText.Text = "Please enter a field name.";
            ErrorText.Visibility = Visibility.Visible;
            return;
        }
        FieldName = name;
        if (_fieldType == "Combo Box")
        {
            ComboChoices = ComboOptionsBox.Text
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim())
                .Where(l => l.Length > 0)
                .ToArray();
        }
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
