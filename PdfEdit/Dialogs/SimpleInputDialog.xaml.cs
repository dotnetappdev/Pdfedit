using System.Windows;
using System.Windows.Input;

namespace PdfEdit.Dialogs;

public partial class SimpleInputDialog : Window
{
    public string InputValue => InputBox.Text;

    public SimpleInputDialog(string title, string prompt, string defaultValue = "")
    {
        InitializeComponent();
        Title = title;
        PromptLabel.Text = prompt;
        InputBox.Text = defaultValue;
        Loaded += (_, _) => { InputBox.Focus(); InputBox.SelectAll(); };
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;
    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void InputBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { DialogResult = true; e.Handled = true; }
    }
}
