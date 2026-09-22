using System.Windows;

namespace PdfEdit.Dialogs;

public partial class ThemedInfoDialog : Window
{
    public ThemedInfoDialog(string title, string message)
    {
        InitializeComponent();
        TitleText.Text = title;
        MessageText.Text = message;
    }

    private void OkBtn_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
