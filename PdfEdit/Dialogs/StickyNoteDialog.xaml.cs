using System.Windows;

namespace PdfEdit.Dialogs;

public partial class StickyNoteDialog : Window
{
    public string NoteText   { get; private set; } = string.Empty;
    public string Author     { get; private set; } = string.Empty;
    public string NoteColor  { get; private set; } = "#FFFF88";

    public StickyNoteDialog()
    {
        InitializeComponent();
        AuthorBox.Text = Environment.UserName;
        NoteTextBox.Focus();
    }

    private void OkClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NoteTextBox.Text))
        {
            MessageBox.Show("Please enter some text for the sticky note.", "Sticky Note",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        NoteText  = NoteTextBox.Text.Trim();
        Author    = AuthorBox.Text.Trim();
        NoteColor = ColorGreen.IsChecked == true ? "#88FF88"
                  : ColorBlue.IsChecked  == true ? "#88AAFF"
                  : ColorPink.IsChecked  == true ? "#FFB0C8"
                  : "#FFFF88";
        DialogResult = true;
    }
}
