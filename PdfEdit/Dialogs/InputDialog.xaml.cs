using System.Windows;

namespace PdfEdit.Dialogs;

public partial class InputDialog : Window
{
    public string InputText { get; private set; } = string.Empty;

    public InputDialog(string title, string prompt, string defaultValue = "")
    {
        InitializeComponent();
        Title = title;
        PromptText.Text = prompt;
        InputBox.Text = defaultValue;
        InputBox.SelectAll();
        InputBox.Focus();
    }

    private void OK_Click(object sender, RoutedEventArgs e)
    {
        InputText = InputBox.Text;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
        => DialogResult = false;
}
