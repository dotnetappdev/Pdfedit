using System.Windows;

namespace PdfEdit.Dialogs;

public partial class CropPageDialog : Window
{
    public float LeftMargin   { get; private set; }
    public float RightMargin  { get; private set; }
    public float TopMargin    { get; private set; }
    public float BottomMargin { get; private set; }

    public CropPageDialog() => InitializeComponent();

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        LeftMargin   = Parse(LeftBox.Text);
        RightMargin  = Parse(RightBox.Text);
        TopMargin    = Parse(TopBox.Text);
        BottomMargin = Parse(BottomBox.Text);
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private static float Parse(string text) =>
        float.TryParse(text, out float v) && v >= 0 ? v : 0f;
}
