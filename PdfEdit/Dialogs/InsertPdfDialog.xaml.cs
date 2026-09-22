using System.Windows;

namespace PdfEdit.Dialogs;

public enum InsertPdfPosition { Beginning, BeforeCurrent, AfterCurrent, End }

public partial class InsertPdfDialog : Window
{
    public string? SelectedFilePath { get; private set; }
    public InsertPdfPosition InsertPosition { get; private set; }

    private readonly int _currentPage;
    private readonly int _totalPages;

    public InsertPdfDialog(int currentPageOneBased, int totalPages)
    {
        InitializeComponent();
        _currentPage = currentPageOneBased;
        _totalPages  = totalPages;

        RadioBeforeCurrent.Content = $"Before current page (page {_currentPage})";
        RadioAfterCurrent.Content  = $"After current page (page {_currentPage})";
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title  = "Select PDF to Insert",
            Filter = "PDF Files (*.pdf)|*.pdf"
        };
        if (dlg.ShowDialog() != true) return;

        SelectedFilePath   = dlg.FileName;
        FilePathBox.Text   = System.IO.Path.GetFileName(dlg.FileName);
        InsertBtn.IsEnabled = true;
    }

    private void Insert_Click(object sender, RoutedEventArgs e)
    {
        InsertPosition = RadioBeginning.IsChecked == true    ? InsertPdfPosition.Beginning
                       : RadioBeforeCurrent.IsChecked == true ? InsertPdfPosition.BeforeCurrent
                       : RadioAfterCurrent.IsChecked == true  ? InsertPdfPosition.AfterCurrent
                       : InsertPdfPosition.End;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
