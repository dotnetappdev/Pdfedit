using System.Windows;
using PdfEdit.Models;

namespace PdfEdit.Dialogs;

public partial class WatermarkDialog : Window
{
    public WatermarkOptions Options { get; private set; } = new();

    public WatermarkDialog() => InitializeComponent();

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        Options = new WatermarkOptions
        {
            Text      = TxtText.Text,
            FontSize  = (float)SliderSize.Value,
            Opacity   = (float)SliderOpacity.Value,
            AngleDeg  = (float)SliderAngle.Value,
            Color     = TxtColor.Text,
        };
        DialogResult = true;
    }
}
