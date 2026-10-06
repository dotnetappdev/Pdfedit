using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using PdfEdit.Services;

namespace PdfEdit.Dialogs;

/// <summary>Resize Pages: new paper size (or custom), fit or centre, margin, which pages.</summary>
public partial class ResizePagesDialog : Window
{
    private const float PtPerMm = 72f / 25.4f;

    public float PageWidth { get; private set; }
    public float PageHeight { get; private set; }
    public ResizeFit Fit { get; private set; }
    public float MarginPt { get; private set; }
    public bool KeepOrientation { get; private set; }
    /// <summary>"all", "current" or a range like "1-3, 5".</summary>
    public string Pages { get; private set; } = "all";

    public ResizePagesDialog()
    {
        InitializeComponent();
        foreach (var (name, w, h) in PageLayoutService.PaperSizes)
            SizeBox.Items.Add(new ComboBoxItem { Content = $"{name}  ({w / PtPerMm:0} × {h / PtPerMm:0} mm)", Tag = (w, h) });
        SizeBox.Items.Add(new ComboBoxItem { Content = "Custom size…", Tag = null });
        SizeBox.SelectedIndex = 0;
    }

    private void SizeBox_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        CustomPanel.Visibility = (SizeBox.SelectedItem as ComboBoxItem)?.Tag == null ? Visibility.Visible : Visibility.Collapsed;

    private void RangeBox_GotFocus(object sender, RoutedEventArgs e) => PagesRange.IsChecked = true;

    private static bool Num(string text, out float v) =>
        float.TryParse(text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out v) && v >= 0;

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if ((SizeBox.SelectedItem as ComboBoxItem)?.Tag is ValueTuple<float, float> size)
            (PageWidth, PageHeight) = size;
        else
        {
            float unit = float.Parse((string)((ComboBoxItem)UnitBox.SelectedItem).Tag, CultureInfo.InvariantCulture);
            if (!Num(WidthBox.Text, out var w) || !Num(HeightBox.Text, out var h) || w * unit < 72 || h * unit < 72 || w * unit > 14400 || h * unit > 14400)
            {
                AppDialog.ShowInfo("Enter a width and height between 1 inch (25 mm) and 200 inches.", "Resize Pages");
                return;
            }
            (PageWidth, PageHeight) = (w * unit, h * unit);
        }
        if (!Num(MarginBox.Text, out var marginMm) || marginMm * PtPerMm * 2 >= Math.Min(PageWidth, PageHeight))
        {
            AppDialog.ShowInfo("Enter a margin in millimetres that leaves room for the page.", "Resize Pages");
            return;
        }
        MarginPt = marginMm * PtPerMm;
        Fit = FitBox.IsChecked == true ? ResizeFit.Fit : ResizeFit.Centre;
        KeepOrientation = KeepOrientationBox.IsChecked == true;
        Pages = PagesAll.IsChecked == true ? "all" : PagesCurrent.IsChecked == true ? "current" : RangeBox.Text;
        DialogResult = true;
    }
}
