using System.Windows;
using System.Windows.Controls;
using PdfEdit.Services;

namespace PdfEdit.Dialogs;

/// <summary>Pages per sheet (2, 4, 6, 9, 16) or a booklet, on a chosen sheet size.</summary>
public partial class PrintLayoutDialog : Window
{
    public bool IsBooklet { get; private set; }
    public int Columns { get; private set; } = 2;
    public int Rows { get; private set; } = 2;
    public float SheetWidth { get; private set; }
    public float SheetHeight { get; private set; }
    public bool Borders { get; private set; }
    public bool ColumnsFirst { get; private set; }
    public bool OpenResult { get; private set; }

    public PrintLayoutDialog()
    {
        InitializeComponent();
        foreach (var (name, w, h) in PageLayoutService.PaperSizes)
            SheetBox.Items.Add(new ComboBoxItem { Content = name, Tag = (w, h) });
        SheetBox.SelectedIndex = System.Globalization.RegionInfo.CurrentRegion.IsMetric ? 0 : 1;
    }

    private void LayoutBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (BookletNote == null) return;   // still loading
        bool booklet = (LayoutBox.SelectedItem as ComboBoxItem)?.Tag as string == "booklet";
        BookletNote.Visibility = booklet ? Visibility.Visible : Visibility.Collapsed;
        BordersBox.IsEnabled = DownFirstBox.IsEnabled = !booklet;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        string tag = (LayoutBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "2x2";
        var (w, h) = ((float, float))((ComboBoxItem)SheetBox.SelectedItem).Tag;
        IsBooklet = tag == "booklet";
        if (!IsBooklet)
        {
            var parts = tag.Split('x');
            Columns = int.Parse(parts[0]);
            Rows = int.Parse(parts[1]);
            // Two pages side by side read best on a landscape sheet.
            if (Columns > Rows) (w, h) = (Math.Max(w, h), Math.Min(w, h));
        }
        (SheetWidth, SheetHeight) = (w, h);
        Borders = BordersBox.IsChecked == true;
        ColumnsFirst = DownFirstBox.IsChecked == true;
        OpenResult = OpenBox.IsChecked == true;
        DialogResult = true;
    }
}
