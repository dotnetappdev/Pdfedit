using System.Windows;

namespace PdfEdit.Dialogs;

public partial class HeaderFooterDialog : Window
{
    public string HeaderText  { get; private set; } = string.Empty;
    public string FooterText  { get; private set; } = string.Empty;
    public float  FontSize    { get; private set; } = 10f;
    public string Alignment   { get; private set; } = "Center";

    public HeaderFooterDialog() => InitializeComponent();

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        HeaderText = HeaderTextBox.Text.Trim();
        FooterText = FooterTextBox.Text.Trim();
        Alignment  = (AlignmentBox.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content?.ToString() ?? "Center";

        if (!float.TryParse(FontSizeBox.Text, out float fs) || fs < 4 || fs > 72)
            fs = 10f;
        FontSize = fs;

        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
