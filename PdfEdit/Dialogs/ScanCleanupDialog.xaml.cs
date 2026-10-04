using System.Windows;
using System.Windows.Media.Imaging;
using PdfEdit.Services;

namespace PdfEdit.Dialogs;

/// <summary>Finds blank, crooked and two-page-spread pages, and lets you choose what to fix.</summary>
public partial class ScanCleanupDialog : Window
{
    private readonly int _pageCount;
    private readonly Func<int, Task<BitmapSource>> _render;
    private readonly CancellationTokenSource _cts = new();
    private readonly List<PageScan> _scans = new();

    public List<PagePlan> Plans { get; } = new();

    public ScanCleanupDialog(int pageCount, Func<int, Task<BitmapSource>> render)
    {
        InitializeComponent();
        _pageCount = pageCount;
        _render = render;
        Progress.Maximum = pageCount;
        Loaded += async (_, _) => await AnalyseAsync();
        Closed += (_, _) => _cts.Cancel();
    }

    private async Task AnalyseAsync()
    {
        try
        {
            for (int i = 0; i < _pageCount; i++)
            {
                if (_cts.IsCancellationRequested) return;
                StatusLine.Text = $"Looking at page {i + 1} of {_pageCount}…";
                var bmp = await _render(i);
                int index = i;
                _scans.Add(await Task.Run(() => ScanCleanupService.Analyse(index, bmp)));
                Progress.Value = i + 1;
            }
        }
        catch (Exception ex) { StatusLine.Text = "Couldn't finish looking: " + ex.Message; return; }

        var blank = _scans.Where(s => s.Blank).ToList();
        var crooked = _scans.Where(s => !s.Blank && Math.Abs(s.SkewDegrees) >= 0.3).ToList();
        var spreads = _scans.Where(s => s.Spread).ToList();
        if (blank.Count == _pageCount) blank.Clear();   // never remove every page

        BlankText.Text = blank.Count == 0 ? "No blank pages found" : $"Remove {blank.Count} blank page(s): {Pages(blank)}";
        SkewText.Text = crooked.Count == 0 ? "No crooked pages found"
            : $"Straighten {crooked.Count} crooked page(s), up to {crooked.Max(s => Math.Abs(s.SkewDegrees)):0.#}°: {Pages(crooked)}";
        SplitText.Text = spreads.Count == 0 ? "No two-page spreads found" : $"Split {spreads.Count} two-page spread(s): {Pages(spreads)}";
        BlankBox.IsEnabled = blank.Count > 0;
        BlankBox.IsChecked = blank.Count > 0;
        SkewBox.IsEnabled = crooked.Count > 0;
        SkewBox.IsChecked = crooked.Count > 0;
        SplitBox.IsEnabled = spreads.Count > 0;
        SplitBox.IsChecked = false;   // a wide page may be meant to be wide: opt in
        ApplyBtn.IsEnabled = blank.Count + crooked.Count + spreads.Count > 0;
        StatusLine.Text = ApplyBtn.IsEnabled ? "Choose what to fix." : "Nothing to clean up — the pages look fine.";
    }

    private static string Pages(List<PageScan> list) =>
        string.Join(", ", list.Take(12).Select(s => (s.Index + 1).ToString())) + (list.Count > 12 ? "…" : "");

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        foreach (var s in _scans)
        {
            bool remove = BlankBox.IsChecked == true && s.Blank && _scans.Any(x => !x.Blank);
            double rotate = SkewBox.IsChecked == true && !s.Blank && Math.Abs(s.SkewDegrees) >= 0.3 ? s.SkewDegrees : 0;
            bool split = SplitBox.IsChecked == true && s.Spread;
            if (remove || rotate != 0 || split) Plans.Add(new PagePlan(s.Index, remove, rotate, split));
        }
        DialogResult = Plans.Count > 0;
    }
}
