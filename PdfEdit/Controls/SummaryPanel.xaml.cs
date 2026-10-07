using System.Windows;
using System.Windows.Controls;
using PdfEdit.ViewModels;

namespace PdfEdit.Controls;

/// <summary>standard generative summary: an outline with key points and page links, beside the document.</summary>
public partial class SummaryPanel : UserControl
{
    public SummaryPanel() => InitializeComponent();

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm && vm.HasSummary)
            try { Clipboard.SetText(MainViewModel.PlainText(vm.SummaryText)); } catch { }
    }
}
