using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using PdfEdit.ViewModels;

namespace PdfEdit.Controls;

/// <summary>
/// Acrobat's "All tools" list: each tool expands to show its actions, which run the app's
/// commands (export, edit, create, combine, organise, AI, summary, e-signatures, OCR, protect,
/// redact, compress, prepare form). Docked to the left of the page thumbnails.
/// </summary>
public partial class AllToolsPanel : UserControl
{
    private sealed record ToolAction(string Label, ICommand Command, string? Tip = null);

    private sealed class ToolCategory
    {
        public required string Title { get; init; }
        public required string Glyph { get; init; }
        public required Color Accent { get; init; }
        public required string Description { get; init; }
        public required List<ToolAction> Actions { get; init; }
        public bool IsExpanded { get; set; }
        public FrameworkElement? View { get; set; }
        public StackPanel? ActionsPanel { get; set; }
    }

    private List<ToolCategory> _categories = new();

    public AllToolsPanel()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Build();
    }

    private MainViewModel? VM => DataContext as MainViewModel;

    /// <summary>
    /// Wraps a command so tools that act on the page also bring the Live View to the front
    /// (the panel is visible in both views).
    /// </summary>
    private sealed class ToolCommand(MainViewModel vm, ICommand inner, object? parameter, bool switchToLive) : ICommand
    {
        public event EventHandler? CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; inner.CanExecuteChanged += value; }
            remove { CommandManager.RequerySuggested -= value; inner.CanExecuteChanged -= value; }
        }
        public bool CanExecute(object? p) => inner.CanExecute(parameter);
        public void Execute(object? p)
        {
            if (switchToLive && vm.IsDesignMode) vm.IsDesignMode = false;
            inner.Execute(parameter);
        }
    }

    private void Build()
    {
        if (VM is not MainViewModel vm) return;

        ToolAction Run(string label, ICommand cmd, string? tip = null, object? param = null, bool live = false)
            => new(label, new ToolCommand(vm, cmd, param, live), tip);
        ToolAction Tool(string label, string tool, string? tip = null)
            => Run(label, vm.SetToolCommand, tip, tool, live: true);
        ToolAction Page(string label, ICommand cmd, string? tip = null) => Run(label, cmd, tip, live: true);

        _categories = new()
        {
            new() { Title = "Export a PDF", Glyph = "", Accent = Color.FromRgb(0x3D, 0xC9, 0xA8),
                Description = "Convert to Word, text, images or PDF/A",
                Actions = new()
                {
                    Run("Microsoft Word (.docx)", vm.ExportWordCommand, "Text of every page as a Word document"),
                    Run("Microsoft Excel (.xlsx)", vm.ExportExcelCommand, "Tables and columns rebuilt as rows and cells, numbers as numbers"),
                    Run("Microsoft PowerPoint (.pptx)", vm.ExportPowerPointCommand, "One slide per page"),
                    Run("All images", vm.ExtractImagesCommand, "Save every picture in the PDF to a folder"),
                    Run("Plain text (.txt)", vm.ExportTextCommand),
                    Run("Images — all pages (PNG)", vm.ExportPagesAsImagesCommand),
                    Run("Image — current page", vm.ExportPageAsImageCommand),
                    Run("PDF/A (archival)", vm.ExportPdfACommand),
                    Run("Form data", vm.ExportDataCommand),
                    Run("Comments (XFDF)", vm.ExportXfdfCommand),
                    Run("Comment summary (CSV)", vm.ExportAnnotationSummaryCommand),
                } },
            new() { Title = "Edit a PDF", Glyph = "", Accent = Color.FromRgb(0xFF, 0x4F, 0x9A),
                Description = "Add text, watermarks, headers, page numbers",
                Actions = new()
                {
                    Tool("Add text", "AddText", "Click on the page to type"),
                    Run("Edit page on the Design canvas", vm.NewDesignCommand, "Move and restyle the page's text, images and fields"),
                    Page("Watermark", vm.WatermarkCommand),
                    Page("Header & footer", vm.AddHeaderFooterCommand),
                    Page("Page numbers", vm.AddPageNumbersCommand),
                    Page("Bates numbering", vm.BatesNumberCommand),
                    Page("Add bookmark", vm.AddBookmarkCommand),
                    Page("Crop pages", vm.CropPagesCommand),
                    Page("Resize pages (A4, Letter, margins)…", vm.ResizePagesCommand, "Change the paper size; content is scaled to fit"),
                    Page("Convert to greyscale", vm.GreyscaleCommand, "Text, drawings and pictures in shades of grey"),
                    Page("Find & replace in fields", vm.FindReplaceFieldsCommand),
                    Run("Document properties", vm.DocumentPropertiesCommand),
                } },
            new() { Title = "Create a PDF", Glyph = "", Accent = Color.FromRgb(0xFF, 0x5A, 0x4F),
                Description = "Blank, from Office files, images or a design",
                Actions = new()
                {
                    Run("Blank PDF", vm.CreateBlankPdfCommand),
                    Run("From images", vm.CreatePdfFromImagesCommand, "One page per image"),
                    Run("From Word, Excel or PowerPoint", vm.CreatePdfFromOfficeCommand, "Word works on its own; Excel and PowerPoint use Office or LibreOffice"),
                    Run("From a Google Docs link", vm.ImportGoogleLinkCommand, "Google Docs, Sheets, Slides, or a Word file on Drive"),
                    Run("Design a PDF (Design canvas)", vm.NewDesignCommand),
                    Run("Export design as PDF", vm.ExportDesignCommand),
                } },
            new() { Title = "Cloud storage", Glyph = "\uE753", Accent = Color.FromRgb(0x4D, 0xA3, 0xFF),
                Description = "Google Drive and OneDrive",
                Actions = new()
                {
                    Run("Import from Google Drive or OneDrive", vm.OpenFromCloudCommand, "PDFs, Word files and Google Docs, brought in as PDFs"),
                    Page("Save to Google Drive or OneDrive", vm.SaveToCloudCommand),
                } },
            new() { Title = "Combine files", Glyph = "", Accent = Color.FromRgb(0x8C, 0x7C, 0xFF),
                Description = "Merge PDFs and images into one PDF",
                Actions = new()
                {
                    Run("Combine files (PDFs & images)", vm.CombineFilesCommand),
                    Page("Merge PDFs into this one", vm.MergePdfCommand),
                    Page("Insert pages from a PDF", vm.InsertPdfCommand),
                    Run("Compare two PDFs (text)", vm.ComparePdfsCommand),
                    Run("Compare two PDFs (visual)", vm.VisualCompareCommand, "Colours what was added, removed and changed on each page"),
                } },
            new() { Title = "Organize pages", Glyph = "", Accent = Color.FromRgb(0xB6, 0xE0, 0x4D),
                Description = "Rotate, insert, move, delete, extract, split",
                Actions = new()
                {
                    Run("Show / hide page thumbnails", vm.ToggleThumbnailsCommand),
                    Run("Slide show (full screen)", vm.SlideShowCommand),
                    Run("Two-page view on / off", vm.ToggleTwoPageViewCommand),
                    Run("Night mode on / off", vm.ToggleNightModeCommand),
                    Page("Rotate page right", vm.RotatePageCWCommand),
                    Page("Rotate page left", vm.RotatePageCCWCommand),
                    Page("Rotate all pages right", vm.RotateAllPagesCWCommand),
                    Page("Insert blank page after", vm.InsertBlankPageCommand),
                    Page("Insert blank page before", vm.InsertPageBeforeCommand),
                    Page("Duplicate page", vm.DuplicateCurrentPageCommand),
                    Page("Move page up", vm.MovePageUpCommand),
                    Page("Move page down", vm.MovePageDownCommand),
                    Page("Delete page", vm.DeleteCurrentPageCommand),
                    Page("Delete page range…", vm.DeletePageRangeCommand),
                    Page("Extract page", vm.ExtractCurrentPageCommand),
                    Page("Extract page range…", vm.ExtractPageRangeCommand),
                    Page("Split PDF", vm.SplitPdfCommand),
                    Page("Pages per sheet / booklet…", vm.PrintLayoutCommand, "A new PDF with several pages on each sheet, or a booklet to fold"),
                } },
            new() { Title = "AI Assistant", Glyph = "", Accent = Color.FromRgb(0xE0, 0xE0, 0xE0),
                Description = "Chat about the document, fill forms with AI",
                Actions = new()
                {
                    Run("Open AI Assistant", vm.ToggleAiPanelCommand),
                    Page("Translate the PDF (keep layout)", vm.TranslatePdfCommand, "A translated copy, paragraph by paragraph in place"),
                    Run("Ask across several PDFs", vm.AskAcrossPdfsCommand, "Answers cite the file and page"),
                    Page("Mind map of this document", vm.MindMapCommand),
                    Run("Design a form with AI", vm.DesignFormWithAiCommand, "Describe the form; the AI lays out fillable fields on the Design canvas"),
                    Page("Summary outline", vm.GenerateSummaryCommand),
                    Page("Fill the form with AI", vm.RunAiFillCommand),
                    Page("Smart fill from another document", vm.SmartFillFromDocCommand),
                    Run("Analyse contract", vm.AnalyzeContractCommand),
                    Run("Extract key data", vm.ExtractKeyDataCommand),
                } },
            new() { Title = "Generative summary", Glyph = "", Accent = Color.FromRgb(0xE0, 0xE0, 0xE0),
                Description = "Summarise the document with AI",
                Actions = new()
                {
                    Run("Summarise this document", vm.SummarizeDocumentCommand),
                    Page("Read this page aloud", vm.ReadPageAloudCommand),
                    Page("Read to the end aloud", vm.ReadToEndAloudCommand),
                    Run("Stop reading", vm.StopReadingCommand),
                    Run("Document statistics", vm.DocumentStatisticsCommand),
                } },
            new() { Title = "Request e-signatures", Glyph = "", Accent = Color.FromRgb(0xE0, 0x61, 0xF5),
                Description = "Send for signature or sign yourself",
                Actions = new()
                {
                    Run("Send for signature (email)", vm.RequestSignaturesCommand, "Opens an email to the signer and shows the file to attach"),
                    Tool("Sign yourself", "Signature"),
                    Page("Sign with a certificate (Digital ID)…", vm.CertSignCommand, "A digital signature that proves who signed and that nothing changed"),
                    Run("Check signatures", vm.VerifySignaturesCommand),
                    Run("Check required fields", vm.ValidateRequiredFieldsCommand),
                } },
            new() { Title = "Automate", Glyph = "\uE9F5", Accent = Color.FromRgb(0xFF, 0xB0, 0x3B),
                Description = "Run steps on many PDFs at once",
                Actions = new()
                {
                    Run("Batch process files…", vm.BatchCommand, "OCR, compress, watermark, flatten, number, protect… many files at once"),
                    Page("Bulk fill from spreadsheet…", vm.BulkFillCommand, "One filled copy of this form per CSV / Excel row"),
                    Run("Search PDFs in a folder…", vm.SearchFolderCommand, "Find text in every PDF in a folder and its subfolders"),
                } },
            new() { Title = "Scan & OCR", Glyph = "", Accent = Color.FromRgb(0x6F, 0xDC, 0x6F),
                Description = "Make scanned pages searchable",
                Actions = new()
                {
                    Run("Scan from scanner (TWAIN / WIA)…", vm.ScanCommand, "Preview, scan from flatbed or feeder, make a searchable PDF"),
                    Run("Recognise text (make searchable)", vm.OcrMakeSearchableCommand, "OCR every scanned page and save a searchable copy"),
                    Page("Clean up scans", vm.ScanCleanupCommand, "Remove blank pages, straighten crooked pages, split two-page spreads"),
                    Run("Recognise text on this page (copy)", vm.OcrCurrentPageCommand),
                    Run("Create PDF from scans / images", vm.CreatePdfFromImagesCommand),
                } },
            new() { Title = "Protect a PDF", Glyph = "", Accent = Color.FromRgb(0x7A, 0xA7, 0xFF),
                Description = "Passwords and permissions",
                Actions = new()
                {
                    Run("Protect with password", vm.PasswordProtectCommand),
                    Run("Remove password", vm.RemovePasswordCommand),
                    Page("Remove hidden information", vm.SanitizeCommand, "Metadata, scripts, attachments, comments"),
                    Page("Check accessibility", vm.AccessibilityCheckCommand, "Screen reader and keyboard checks, with fixes"),
                    Run("Flatten form & save", vm.FlattenAndSaveCommand, "Make field values part of the page so they can't be edited"),
                } },
            new() { Title = "Redact a PDF", Glyph = "", Accent = Color.FromRgb(0xFF, 0x7B, 0xAC),
                Description = "Permanently remove sensitive content",
                Actions = new()
                {
                    Tool("Mark areas to redact", "Redact"),
                    Page("Apply redactions", vm.ApplyRedactionsCommand),
                    Run("Find personal information (AI)", vm.FindPiiCommand),
                } },
            new() { Title = "Compress a PDF", Glyph = "", Accent = Color.FromRgb(0xFF, 0x8A, 0x6A),
                Description = "Reduce file size",
                Actions = new() { Run("Compress PDF", vm.CompressPdfCommand) } },
            new() { Title = "Prepare a form", Glyph = "", Accent = Color.FromRgb(0xB9, 0x8C, 0xFF),
                Description = "Add, move and edit form fields",
                Actions = new()
                {
                    Page("Detect fields automatically", vm.DetectFieldsCommand, "Turn a flat form's boxes, squares and blank lines into fillable fields"),
                    Tool("Edit fields (move / resize / align)", "EditFields"),
                    Tool("Add text field", "AddTextField"),
                    Tool("Add checkbox", "AddCheckbox"),
                    Tool("Add radio button", "AddRadioButton"),
                    Tool("Add dropdown", "AddComboBox"),
                    Tool("Add list box", "AddListBox"),
                    Tool("Add signature field", "AddSignatureField"),
                    Tool("Add date field", "AddDateField"),
                    Run("Check required fields", vm.ValidateRequiredFieldsCommand),
                    Page("Clear all fields", vm.ClearAllFieldsCommand),
                    Run("Import form data", vm.ImportDataCommand),
                } },
        };

        CategoryList.Items.Clear();
        foreach (var c in _categories)
            CategoryList.Items.Add(BuildCategoryView(c));
    }

    private FrameworkElement BuildCategoryView(ToolCategory c)
    {
        var accent = new SolidColorBrush(c.Accent);
        accent.Freeze();

        var chevron = new TextBlock
        {
            Text = "", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 9,
            Foreground = (Brush)FindResource("DimForegroundBrush"), VerticalAlignment = VerticalAlignment.Center,
        };
        var headerGrid = new Grid();
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var icon = new TextBlock
        {
            Text = c.Glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 18,
            Foreground = accent, VerticalAlignment = VerticalAlignment.Center,
        };
        var title = new TextBlock { Text = c.Title, FontSize = 14, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        Grid.SetColumn(title, 1);
        Grid.SetColumn(chevron, 2);
        headerGrid.Children.Add(icon);
        headerGrid.Children.Add(title);
        headerGrid.Children.Add(chevron);

        var header = new Button
        {
            Style = (Style)FindResource("ToolRow"),
            Padding = new Thickness(10, 9, 10, 9),
            Content = headerGrid,
            ToolTip = c.Description,
        };
        System.Windows.Automation.AutomationProperties.SetName(header, c.Title);

        var actions = new StackPanel { Margin = new Thickness(34, 0, 0, 6), Visibility = Visibility.Collapsed };
        foreach (var a in c.Actions)
        {
            var btn = new Button
            {
                Style = (Style)FindResource("ToolRow"),
                Padding = new Thickness(10, 5, 8, 5),
                Command = a.Command,
                ToolTip = a.Tip,
                Content = new TextBlock { Text = a.Label, FontSize = 12.5, TextWrapping = TextWrapping.Wrap },
            };
            System.Windows.Automation.AutomationProperties.SetName(btn, $"{c.Title}: {a.Label}");
            actions.Children.Add(btn);
        }

        header.Click += (_, _) =>
        {
            c.IsExpanded = !c.IsExpanded;
            actions.Visibility = c.IsExpanded ? Visibility.Visible : Visibility.Collapsed;
            chevron.Text = c.IsExpanded ? "" : "";
        };

        var panel = new StackPanel();
        panel.Children.Add(header);
        panel.Children.Add(actions);
        c.View = panel;
        c.ActionsPanel = actions;
        return panel;
    }

    /// <summary>Filters by tool or action name; matching tools open to show the matching actions.</summary>
    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        string q = SearchBox.Text.Trim();
        foreach (var c in _categories)
        {
            if (c.View == null || c.ActionsPanel == null) continue;
            if (q.Length == 0)
            {
                c.View.Visibility = Visibility.Visible;
                c.ActionsPanel.Visibility = c.IsExpanded ? Visibility.Visible : Visibility.Collapsed;
                foreach (UIElement b in c.ActionsPanel.Children) b.Visibility = Visibility.Visible;
                continue;
            }

            bool titleMatch = c.Title.Contains(q, StringComparison.OrdinalIgnoreCase)
                           || c.Description.Contains(q, StringComparison.OrdinalIgnoreCase);
            int shown = 0;
            for (int i = 0; i < c.Actions.Count; i++)
            {
                bool match = titleMatch || c.Actions[i].Label.Contains(q, StringComparison.OrdinalIgnoreCase);
                c.ActionsPanel.Children[i].Visibility = match ? Visibility.Visible : Visibility.Collapsed;
                if (match) shown++;
            }
            c.View.Visibility = shown > 0 ? Visibility.Visible : Visibility.Collapsed;
            c.ActionsPanel.Visibility = shown > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}
