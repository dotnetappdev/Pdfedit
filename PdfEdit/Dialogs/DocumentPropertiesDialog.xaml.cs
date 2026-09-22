using System.Windows;
using PdfEdit.Models;

namespace PdfEdit.Dialogs;

public partial class DocumentPropertiesDialog : Window
{
    public PdfMetadataInfo? Result { get; private set; }

    public DocumentPropertiesDialog(PdfMetadataInfo info)
    {
        InitializeComponent();

        TitleBox.Text    = info.Title;
        AuthorBox.Text   = info.Author;
        SubjectBox.Text  = info.Subject;
        KeywordsBox.Text = info.Keywords;

        CreatorLabel.Text  = string.IsNullOrEmpty(info.Creator)  ? "—" : info.Creator;
        ProducerLabel.Text = string.IsNullOrEmpty(info.Producer) ? "—" : info.Producer;

        PagesLabel.Text = info.PageCount.ToString();
        SizeLabel.Text  = FormatBytes(info.FileSizeBytes);
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        Result = new PdfMetadataInfo
        {
            Title    = TitleBox.Text.Trim(),
            Author   = AuthorBox.Text.Trim(),
            Subject  = SubjectBox.Text.Trim(),
            Keywords = KeywordsBox.Text.Trim(),
        };
        DialogResult = true;
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes <= 0) return "—";
        if (bytes < 1024)         return $"{bytes} B";
        if (bytes < 1024 * 1024)  return $"{bytes / 1024.0:F1} KB";
        return $"{bytes / (1024.0 * 1024):F2} MB";
    }
}
