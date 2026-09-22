using System.Windows;

namespace PdfEdit.Dialogs;

public partial class LinkUriDialog : Window
{
    public string Uri { get; private set; } = string.Empty;

    public LinkUriDialog() => InitializeComponent();

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        string text = UriBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(text) ||
            (!text.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
             !text.StartsWith("https://", StringComparison.OrdinalIgnoreCase) &&
             !text.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)))
        {
            ErrorText.Text = "Please enter a valid URL (http://, https://, or mailto:).";
            ErrorText.Visibility = Visibility.Visible;
            return;
        }
        Uri = text;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
