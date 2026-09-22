using System.Windows;
using PdfEdit.Models;

namespace PdfEdit.Dialogs;

public partial class ComparePdfsDialog : Window
{
    public ComparePdfsDialog(string fileA, string fileB, List<PdfPageDiff> diffs)
    {
        InitializeComponent();

        SummaryLabel.Text = $"Comparing: {System.IO.Path.GetFileName(fileA)} vs {System.IO.Path.GetFileName(fileB)}";

        int changed = diffs.Count(d => d.HasDifferences);
        StatLabel.Text = $"{diffs.Count} pages compared · {changed} page(s) with differences";

        var items = diffs
            .OrderByDescending(d => d.HasDifferences)
            .Select(d => new DiffViewModel(d))
            .ToList();

        ResultsList.ItemsSource = items;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private class DiffViewModel
    {
        public int PageIndex { get; }
        public List<string> OnlyInA { get; }
        public List<string> OnlyInB { get; }
        public string StatusText { get; }

        public DiffViewModel(PdfPageDiff d)
        {
            PageIndex = d.PageIndex;
            OnlyInA = d.OnlyInA.Take(20).ToList();
            OnlyInB = d.OnlyInB.Take(20).ToList();

            if (!d.ExistsInA)
                StatusText = "Only in second document";
            else if (!d.ExistsInB)
                StatusText = "Only in first document";
            else if (!d.HasDifferences)
                StatusText = $"Identical ({d.CommonLineCount} lines)";
            else
                StatusText = $"{d.OnlyInA.Count} lines removed · {d.OnlyInB.Count} lines added · {d.CommonLineCount} lines common";
        }
    }
}
