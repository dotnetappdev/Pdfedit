using System.Windows;
using System.Windows.Media;

namespace PdfEdit.Dialogs;

public partial class ThemedConfirmDialog : Window
{
    public ThemedConfirmDialog(string title, string message,
        string confirmText, string cancelText, bool isDanger)
    {
        InitializeComponent();
        TitleText.Text = title;
        MessageText.Text = message;
        ConfirmBtn.Content = confirmText;
        CancelBtn.Content = cancelText;

        if (isDanger)
        {
            IconBorder.Background = new SolidColorBrush(Color.FromRgb(204, 68, 68));
            IconText.Text = "!";
            ConfirmBtn.Background = new SolidColorBrush(Color.FromRgb(204, 68, 68));
            ConfirmBtn.Foreground = Brushes.White;
        }
    }

    private void ConfirmBtn_Click(object sender, RoutedEventArgs e) => DialogResult = true;
    private void CancelBtn_Click(object sender, RoutedEventArgs e)  => DialogResult = false;
}
