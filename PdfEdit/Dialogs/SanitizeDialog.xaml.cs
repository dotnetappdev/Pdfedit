using System.Windows;
using PdfEdit.Services;

namespace PdfEdit.Dialogs;

public partial class SanitizeDialog : Window
{
    public SanitizeOptions Options { get; private set; } = new();

    public SanitizeDialog() => InitializeComponent();

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        Options = new SanitizeOptions(MetaBox.IsChecked == true, ScriptBox.IsChecked == true, FilesBox.IsChecked == true,
                                      CommentsBox.IsChecked == true, BookmarksBox.IsChecked == true);
        DialogResult = true;
    }
}
