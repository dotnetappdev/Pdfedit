using System.Windows;
using PdfEdit.Models;

namespace PdfEdit.Dialogs;

public partial class FindReplaceFieldsDialog : Window
{
    public string FindText    { get; private set; } = string.Empty;
    public string ReplaceText { get; private set; } = string.Empty;
    public bool   CaseSensitive { get; private set; }

    public FindReplaceFieldsDialog(IReadOnlyList<FormFieldInfo> fields)
    {
        InitializeComponent();

        int textCount = fields.Count(f => f.FieldType == FieldType.Text);
        InfoLabel.Text = $"{textCount} text field(s) in this document will be searched.";
    }

    private void ReplaceButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(FindBox.Text))
        {
            MessageBox.Show("Enter text to search for.", "Find & Replace",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        FindText      = FindBox.Text;
        ReplaceText   = ReplaceBox.Text;
        CaseSensitive = CaseCheck.IsChecked == true;
        DialogResult  = true;
    }
}
