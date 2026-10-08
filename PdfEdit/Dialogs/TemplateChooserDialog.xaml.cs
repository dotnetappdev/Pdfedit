using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using PdfEdit.Services;
using PdfEdit.Templates;

namespace PdfEdit.Dialogs;

/// <summary>
/// File → New from Template: every template in the shared catalog as a thumbnail, by category or search,
/// with a large preview — like PowerPoint's and Google Docs' template galleries. The web version shows
/// the same templates in File → New.
/// </summary>
public partial class TemplateChooserDialog : Window
{
    public enum Choice { None, Customise, OpenAsPdf, BlankPdf, BlankDesign }

    /// <summary>What was chosen, and the template's id for Customise / Open as PDF.</summary>
    public Choice Result { get; private set; }
    public string? TemplateId { get; private set; }

    private const string All = "All templates";
    private readonly ObservableCollection<Card> _cards = new();
    private CancellationTokenSource? _thumbs;

    // Thumbnails stay drawn for the rest of the session.
    private static readonly Dictionary<string, BitmapSource> ThumbCache = new();
    private static readonly Dictionary<string, BitmapSource> PreviewCache = new();

    public TemplateChooserDialog()
    {
        InitializeComponent();
        CategoryList.ItemsSource = new[] { All }.Concat(TemplateCatalog.Categories).ToList();
        Gallery.ItemsSource = _cards;
        CategoryList.SelectedIndex = 0;
        Loaded += (_, _) => SearchBox.Focus();
        Closed += (_, _) => _thumbs?.Cancel();
    }

    /// <summary>One template in the gallery; its thumbnail is drawn in the background.</summary>
    public sealed class Card(PdfTemplate template) : INotifyPropertyChanged
    {
        public PdfTemplate Template { get; } = template;
        public string Meta => Template.Category + (Template.Fillable ? " · Fillable" : "");
        private BitmapSource? _thumb;
        public BitmapSource? Thumb { get => _thumb; set { _thumb = value; Changed(); } }
        public event PropertyChangedEventHandler? PropertyChanged;
        private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    private void Filter_Changed(object sender, EventArgs e)
    {
        if (!IsInitialized) return;
        SearchHint.Visibility = string.IsNullOrEmpty(SearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        string? category = CategoryList.SelectedItem as string is { } c && c != All ? c : null;
        _cards.Clear();
        foreach (var t in TemplateCatalog.Search(category, SearchBox.Text))
            _cards.Add(new Card(t) { Thumb = ThumbCache.GetValueOrDefault(t.Id) });
        EmptyText.Visibility = _cards.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        _ = DrawThumbnailsAsync();
    }

    /// <summary>Draws the missing thumbnails one after another (each template made into its PDF and rendered).</summary>
    private async Task DrawThumbnailsAsync()
    {
        _thumbs?.Cancel();
        var cts = _thumbs = new CancellationTokenSource();
        foreach (var card in _cards.ToList())
        {
            if (cts.IsCancellationRequested) return;
            if (card.Thumb != null) continue;
            try
            {
                var bmp = await RenderAsync(card.Template, 0.32);
                if (bmp == null) continue;
                ThumbCache[card.Template.Id] = bmp;
                card.Thumb = bmp;
            }
            catch { /* leave the placeholder */ }
        }
    }

    private static async Task<BitmapSource?> RenderAsync(PdfTemplate template, double zoom)
    {
        var pdf = Path.Combine(Path.GetTempPath(), $"pdfedit-template-{Guid.NewGuid():N}.pdf");
        try
        {
            await Task.Run(() => DesignPdfExporter.Export(template.Create(), pdf));
            using var renderer = RendererFactory.Create();
            await renderer.LoadAsync(pdf);
            return await renderer.RenderPageAsync(0, zoom);
        }
        finally { try { File.Delete(pdf); } catch { } }
    }

    private async void Gallery_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Gallery.SelectedItem is not Card card)
        {
            CustomiseButton.IsEnabled = PdfButton.IsEnabled = false;
            return;
        }
        var t = card.Template;
        PreviewCategory.Text = t.Category.ToUpperInvariant();
        PreviewTitle.Text = t.Title;
        PreviewDescription.Text = t.Description;
        PreviewFillable.Visibility = t.Fillable ? Visibility.Visible : Visibility.Collapsed;
        CustomiseButton.IsEnabled = PdfButton.IsEnabled = true;
        PreviewImage.Source = PreviewCache.GetValueOrDefault(t.Id) ?? card.Thumb;
        if (PreviewCache.ContainsKey(t.Id)) return;
        try
        {
            var big = await RenderAsync(t, 0.9);
            if (big == null) return;
            PreviewCache[t.Id] = big;
            if ((Gallery.SelectedItem as Card)?.Template.Id == t.Id) PreviewImage.Source = big;
        }
        catch { /* keep the thumbnail */ }
    }

    private void Gallery_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Gallery.SelectedItem is Card) Finish(Choice.Customise);
    }

    private void Gallery_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Gallery.SelectedItem is Card) { Finish(Choice.Customise); e.Handled = true; }
    }

    private void Customise_Click(object sender, RoutedEventArgs e) => Finish(Choice.Customise);
    private void OpenPdf_Click(object sender, RoutedEventArgs e) => Finish(Choice.OpenAsPdf);
    private void Blank_Click(object sender, RoutedEventArgs e) => Finish(Choice.BlankPdf);
    private void BlankDesign_Click(object sender, RoutedEventArgs e) => Finish(Choice.BlankDesign);

    private void Finish(Choice choice)
    {
        if (choice is Choice.Customise or Choice.OpenAsPdf && Gallery.SelectedItem is not Card) return;
        Result = choice;
        TemplateId = (Gallery.SelectedItem as Card)?.Template.Id;
        DialogResult = true;
    }
}
