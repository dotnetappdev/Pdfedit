using System.Windows;

namespace PdfEdit.Dialogs;

public partial class PageRangeDialog : Window
{
    private readonly int _totalPages;

    public int FirstPage { get; private set; }
    public int LastPage  { get; private set; }

    public PageRangeDialog(int currentPage, int totalPages, string actionLabel = "OK", string description = "")
    {
        _totalPages = totalPages;
        InitializeComponent();
        FirstPageBox.Text = currentPage.ToString();
        LastPageBox.Text  = currentPage.ToString();
        ActionButton.Content = actionLabel;
        if (!string.IsNullOrEmpty(description))
            DescriptionLabel.Text = description;
        Title = actionLabel + " Pages";
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(FirstPageBox.Text, out int first) || first < 1 || first > _totalPages)
        {
            ShowError($"First page must be between 1 and {_totalPages}.");
            return;
        }
        if (!int.TryParse(LastPageBox.Text, out int last) || last < first || last > _totalPages)
        {
            ShowError($"Last page must be between {first} and {_totalPages}.");
            return;
        }
        FirstPage    = first;
        LastPage     = last;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void ShowError(string msg)
    {
        ErrorText.Text = msg;
        ErrorText.Visibility = Visibility.Visible;
    }
}
