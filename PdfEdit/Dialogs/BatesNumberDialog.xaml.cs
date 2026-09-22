using System.Windows;
using System.Windows.Controls;

namespace PdfEdit.Dialogs;

public partial class BatesNumberDialog : Window
{
    public string Prefix      { get; private set; } = string.Empty;
    public string Suffix      { get; private set; } = string.Empty;
    public int    StartNumber { get; private set; } = 1;
    public int    Padding     { get; private set; } = 6;
    public float  FontSize    { get; private set; } = 8f;
    public string Position    { get; private set; } = "BottomRight";

    public BatesNumberDialog() => InitializeComponent();

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        Prefix = PrefixBox.Text;
        Suffix = SuffixBox.Text;
        Position = (PositionBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "BottomRight";

        if (!int.TryParse(StartNumberBox.Text, out int start) || start < 1) start = 1;
        StartNumber = start;

        if (!int.TryParse(PaddingBox.Text, out int pad) || pad < 1 || pad > 20) pad = 6;
        Padding = pad;

        if (!float.TryParse(FontSizeBox.Text, out float fs) || fs < 4 || fs > 36) fs = 8f;
        FontSize = fs;

        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
