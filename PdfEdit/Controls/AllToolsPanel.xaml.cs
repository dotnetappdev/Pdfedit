using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using PdfEdit.Services;
using PdfEdit.ViewModels;

namespace PdfEdit.Controls;

/// <summary>
/// The Toolkit: every feature grouped into Create / Shape / Sign &amp; secure / Smart. Each tool
/// opens to show its actions, which run the app's commands. The user can pin actions to the top,
/// hide or reorder tools, and switch between List, Compact and Tiles; all of it is remembered
/// in settings.json. Docked to the left of the page thumbnails.
/// </summary>
public partial class AllToolsPanel : UserControl
{
    private sealed record ToolAction(string Label, ICommand Command, string? Tip = null);

    private sealed class ToolCategory
    {
        public required string Id { get; init; }
        public required string Section { get; init; }
        public required string Title { get; init; }
        public required string Glyph { get; init; }
        public required string Description { get; init; }
        public required List<ToolAction> Actions { get; init; }
    }

    private static readonly string[] Sections = { "Create", "Shape", "Sign & secure", "Smart" };

    private List<ToolCategory> _categories = new();

    public AllToolsPanel()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Build();
    }

    private MainViewModel? VM => DataContext as MainViewModel;
    private static AppSettings Settings => AppSettings.Current;

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
            new() { Id = "convert", Section = "Create", Title = "Convert & export", Glyph = "\uE8AB",
                Description = "Turn the PDF into Word, Excel, web pages, e-books, images or PDF/A",
                Actions = new()
                {
                    Run("Microsoft Word (.docx)", vm.ExportWordCommand, "Text of every page as a Word document"),
                    Run("Microsoft Excel (.xlsx)", vm.ExportExcelCommand, "Tables and columns rebuilt as rows and cells, numbers as numbers"),
                    Run("Microsoft PowerPoint (.pptx)", vm.ExportPowerPointCommand, "One slide per page"),
                    Run("All images", vm.ExtractImagesCommand, "Save every picture in the PDF to a folder"),
                    Run("Plain text (.txt)", vm.ExportTextCommand),
                    Run("Web page (.html)", vm.ExportHtmlCommand, "Text with its headings, lists and paragraphs"),
                    Run("Markdown (.md)", vm.ExportMarkdownCommand),
                    Run("E-book (.epub)", vm.ExportEpubCommand, "One chapter per page, for e-readers"),
                    Run("Images — all pages (PNG)", vm.ExportPagesAsImagesCommand),
                    Run("Image — current page", vm.ExportPageAsImageCommand),
                    Page("Snapshot of an area (copy / save picture)", vm.SnapshotCommand, "Drag a box round any area, then copy it or save it as PNG"),
                    Run("PDF/A (archival)", vm.ExportPdfACommand),
                    Run("Form data", vm.ExportDataCommand),
                    Run("Comments (XFDF)", vm.ExportXfdfCommand),
                    Run("Comment summary (CSV)", vm.ExportAnnotationSummaryCommand),
                } },
            new() { Id = "edit", Section = "Shape", Title = "Edit page content", Glyph = "\uE70F",
                Description = "Text, pictures, watermarks, headers, page numbers",
                Actions = new()
                {
                    Tool("Add text", "AddText", "Click on the page to type"),
                    Tool("Edit images (move, resize, replace, delete)", "EditImages", "Select a picture already in the PDF"),
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
            new() { Id = "create", Section = "Create", Title = "Start a new PDF", Glyph = "\uE8A5",
                Description = "Blank, from Office files, images, web text or a design",
                Actions = new()
                {
                    Run("Blank PDF", vm.CreateBlankPdfCommand),
                    Run("From images", vm.CreatePdfFromImagesCommand, "One page per image"),
                    Run("From Word, Excel or PowerPoint", vm.CreatePdfFromOfficeCommand, "Word works on its own; Excel and PowerPoint use Office or LibreOffice"),
                    Run("From text, Markdown or HTML", vm.CreatePdfFromOfficeCommand, "Headings, lists, tables and pictures are kept"),
                    Run("From a Google Docs link", vm.ImportGoogleLinkCommand, "Google Docs, Sheets, Slides, or a Word file on Drive"),
                    Run("Design a PDF (Design canvas)", vm.NewDesignCommand),
                    Run("Export design as PDF", vm.ExportDesignCommand),
                } },
            new() { Id = "cloud", Section = "Create", Title = "Cloud drives", Glyph = "\uE753",
                Description = "Open from and save to Google Drive and OneDrive",
                Actions = new()
                {
                    Run("Import from Google Drive or OneDrive", vm.OpenFromCloudCommand, "PDFs, Word files and Google Docs, brought in as PDFs"),
                    Page("Save to Google Drive or OneDrive", vm.SaveToCloudCommand),
                } },
            new() { Id = "merge", Section = "Create", Title = "Merge & compare", Glyph = "\uE8C8",
                Description = "Join PDFs and pictures, or see what changed between two",
                Actions = new()
                {
                    Run("Merge files (PDFs & images)", vm.CombineFilesCommand),
                    Page("Merge PDFs into this one", vm.MergePdfCommand),
                    Page("Insert pages from a PDF", vm.InsertPdfCommand),
                    Run("Compare two PDFs (text)", vm.ComparePdfsCommand),
                    Run("Compare two PDFs (visual)", vm.VisualCompareCommand, "Colours what was added, removed and changed on each page"),
                } },
            new() { Id = "pages", Section = "Shape", Title = "Arrange pages", Glyph = "\uE8A9",
                Description = "Rotate, insert, move, delete, extract, split",
                Actions = new()
                {
                    Run("Show / hide page thumbnails", vm.ToggleThumbnailsCommand),
                    Run("Slide show (full screen)", vm.SlideShowCommand),
                    Run("Two-page view on / off", vm.ToggleTwoPageViewCommand),
                    Run("Night mode on / off", vm.ToggleNightModeCommand),
                    Run("Auto-scroll on / off", vm.ToggleAutoScrollCommand, "Hands-free reading: Up / Down change the speed, Esc stops"),
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
            new() { Id = "ai", Section = "Smart", Title = "Ask AI", Glyph = "\uE945",
                Description = "Chat about the document, translate, fill forms with AI",
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
            new() { Id = "summary", Section = "Smart", Title = "Summaries & read-aloud", Glyph = "\uE736",
                Description = "Summarise the document or have it read to you",
                Actions = new()
                {
                    Run("Summarise this document", vm.SummarizeDocumentCommand),
                    Page("Read this page aloud", vm.ReadPageAloudCommand),
                    Page("Read to the end aloud", vm.ReadToEndAloudCommand),
                    Run("Stop reading", vm.StopReadingCommand),
                    Run("Document statistics", vm.DocumentStatisticsCommand),
                } },
            new() { Id = "sign", Section = "Sign & secure", Title = "Signing", Glyph = "\uE8FB",
                Description = "Sign yourself, with a certificate, or send for signature",
                Actions = new()
                {
                    Run("Send for signature (email)", vm.RequestSignaturesCommand, "Opens an email to the signer and shows the file to attach"),
                    Tool("Sign yourself", "Signature"),
                    Page("Sign with a certificate (Digital ID)…", vm.CertSignCommand, "A digital signature that proves who signed and that nothing changed"),
                    Run("Check signatures", vm.VerifySignaturesCommand),
                    Run("Check required fields", vm.ValidateRequiredFieldsCommand),
                } },
            new() { Id = "batch", Section = "Smart", Title = "Batch jobs", Glyph = "\uE895",
                Description = "Run the same steps on many PDFs at once",
                Actions = new()
                {
                    Run("Batch process files…", vm.BatchCommand, "OCR, compress, watermark, flatten, number, protect… many files at once"),
                    Page("Bulk fill from spreadsheet…", vm.BulkFillCommand, "One filled copy of this form per CSV / Excel row"),
                    Run("Search PDFs in a folder…", vm.SearchFolderCommand, "Find text in every PDF in a folder and its subfolders"),
                } },
            new() { Id = "scan", Section = "Smart", Title = "Scan & text recognition", Glyph = "\uE8FE",
                Description = "Scan paper and make scanned pages searchable",
                Actions = new()
                {
                    Run("Scan from scanner (TWAIN / WIA)…", vm.ScanCommand, "Preview, scan from flatbed or feeder, make a searchable PDF"),
                    Run("Recognise text (make searchable)", vm.OcrMakeSearchableCommand, "OCR every scanned page and save a searchable copy"),
                    Page("Clean up scans", vm.ScanCleanupCommand, "Remove blank pages, straighten crooked pages, split two-page spreads"),
                    Run("Recognise text on this page (copy)", vm.OcrCurrentPageCommand),
                    Run("Create PDF from scans / images", vm.CreatePdfFromImagesCommand),
                } },
            new() { Id = "security", Section = "Sign & secure", Title = "Lock & clean up", Glyph = "\uE72E",
                Description = "Passwords, hidden information, accessibility",
                Actions = new()
                {
                    Run("Protect with password", vm.PasswordProtectCommand),
                    Run("Remove password", vm.RemovePasswordCommand),
                    Page("Remove hidden information", vm.SanitizeCommand, "Metadata, scripts, attachments, comments"),
                    Page("Check accessibility", vm.AccessibilityCheckCommand, "Screen reader and keyboard checks, with fixes"),
                    Run("Flatten form & save", vm.FlattenAndSaveCommand, "Make field values part of the page so they can't be edited"),
                } },
            new() { Id = "redact", Section = "Sign & secure", Title = "Black out content", Glyph = "\uE8C6",
                Description = "Permanently remove sensitive text and areas",
                Actions = new()
                {
                    Tool("Mark areas to redact", "Redact"),
                    Page("Apply redactions", vm.ApplyRedactionsCommand),
                    Run("Find personal information (AI)", vm.FindPiiCommand),
                } },
            new() { Id = "shrink", Section = "Shape", Title = "Shrink file size", Glyph = "\uE73F",
                Description = "Make the PDF smaller to send or store",
                Actions = new() { Run("Compress PDF", vm.CompressPdfCommand) } },
            new() { Id = "forms", Section = "Shape", Title = "Form builder", Glyph = "\uE9D5",
                Description = "Add, move and edit fillable fields",
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

        Render();
    }

    // ── State ────────────────────────────────────────────────────────────────

    private static string PinKey(ToolCategory c, ToolAction a) => $"{c.Id}|{a.Label}";
    private static bool IsPinned(ToolCategory c, ToolAction a) => Settings.ToolkitPinned.Contains(PinKey(c, a));
    private static bool IsExpanded(ToolCategory c) => Settings.ToolkitExpanded.Contains(c.Id);
    private static bool IsHidden(ToolCategory c) => Settings.ToolkitHidden.Contains(c.Id);

    /// <summary>Tools in the user's order: saved ids first, then any new tools in their default place.</summary>
    private List<ToolCategory> Ordered()
    {
        var order = Settings.ToolkitOrder;
        return _categories
            .Select((c, i) => (c, rank: order.Contains(c.Id) ? order.IndexOf(c.Id) : order.Count + i))
            .OrderBy(t => t.rank).Select(t => t.c).ToList();
    }

    private void SaveAndRender()
    {
        Settings.Save();
        Render();
    }

    private static void Toggle(List<string> list, string key)
    {
        if (!list.Remove(key)) list.Add(key);
    }

    private void TogglePin(ToolCategory c, ToolAction a)
    {
        Toggle(Settings.ToolkitPinned, PinKey(c, a));
        SaveAndRender();
    }

    /// <summary>Swaps a tool with its visible neighbour in the same section.</summary>
    private void Move(ToolCategory c, int delta)
    {
        var all = Ordered();
        var peers = all.Where(x => x.Section == c.Section && !IsHidden(x)).ToList();
        int i = peers.IndexOf(c), j = i + delta;
        if (i < 0 || j < 0 || j >= peers.Count) return;
        int a = all.IndexOf(c), b = all.IndexOf(peers[j]);
        (all[a], all[b]) = (all[b], all[a]);
        Settings.ToolkitOrder = all.Select(x => x.Id).ToList();
        SaveAndRender();
    }

    // ── Rendering ────────────────────────────────────────────────────────────

    private void Render()
    {
        CategoryList.Items.Clear();
        string q = SearchBox.Text.Trim();
        if (q.Length > 0) { RenderSearch(q); return; }

        var pins = Ordered()
            .SelectMany(c => c.Actions.Where(a => IsPinned(c, a)).Select(a => (c, a)))
            .ToList();
        if (pins.Count > 0)
        {
            CategoryList.Items.Add(SectionHeading("Pinned"));
            foreach (var (c, a) in pins)
                CategoryList.Items.Add(PinnedRow(c, a));
        }

        bool tiles = Settings.ToolkitView == "Tiles";
        foreach (var section in Sections)
        {
            var tools = Ordered().Where(c => c.Section == section && !IsHidden(c)).ToList();
            if (tools.Count == 0) continue;
            if (Settings.ToolkitShowSections) CategoryList.Items.Add(SectionHeading(section));
            if (tiles)
            {
                var wrap = new WrapPanel { Margin = new Thickness(4, 0, 0, 4) };
                foreach (var c in tools) wrap.Children.Add(BuildTile(c));
                CategoryList.Items.Add(wrap);
            }
            else
            {
                foreach (var c in tools) CategoryList.Items.Add(BuildCategoryView(c, IsExpanded(c), null));
            }
        }

        int hidden = _categories.Count(IsHidden);
        if (hidden > 0)
        {
            var more = new Button
            {
                Style = (Style)FindResource("ToolRow"),
                Padding = new Thickness(10, 6, 10, 6),
                Margin = new Thickness(0, 6, 0, 0),
                Content = new TextBlock { Text = hidden == 1 ? "1 hidden tool — show…" : $"{hidden} hidden tools — show…", FontSize = 12 },
            };
            more.SetResourceReference(ForegroundProperty, "AccentBrush");
            more.Click += (_, _) => OpenCustomiseMenu(more);
            CategoryList.Items.Add(more);
        }
    }

    /// <summary>Search looks through every tool, hidden ones too, and opens the matches.</summary>
    private void RenderSearch(string q)
    {
        foreach (var c in Ordered())
        {
            bool titleMatch = c.Title.Contains(q, StringComparison.OrdinalIgnoreCase)
                           || c.Description.Contains(q, StringComparison.OrdinalIgnoreCase);
            var matches = c.Actions.Where(a => titleMatch || a.Label.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
            if (matches.Count > 0) CategoryList.Items.Add(BuildCategoryView(c, true, matches));
        }
        if (CategoryList.Items.Count == 0)
            CategoryList.Items.Add(new TextBlock
            {
                Text = "No tool matches that.", Margin = new Thickness(10, 8, 10, 8),
                Foreground = (Brush)FindResource("DimForegroundBrush"),
            });
    }

    private FrameworkElement SectionHeading(string text)
    {
        var tb = new TextBlock
        {
            Text = text.ToUpperInvariant(), FontSize = 10.5, FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(10, 12, 10, 4),
            Foreground = (Brush)FindResource("DimForegroundBrush"),
        };
        System.Windows.Automation.AutomationProperties.SetHeadingLevel(tb, System.Windows.Automation.AutomationHeadingLevel.Level2);
        return tb;
    }

    private static TextBlock Symbol(string glyph, double size) => new()
    {
        Text = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = size,
        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>The tool's symbol on a softly tinted rounded square in the theme's accent colour.</summary>
    private static Grid IconTile(string glyph, double box, double size)
    {
        var tint = new Border { CornerRadius = new CornerRadius(box / 4), Opacity = 0.16 };
        tint.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
        var symbol = Symbol(glyph, size);
        symbol.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
        var icon = new Grid { Width = box, Height = box, VerticalAlignment = VerticalAlignment.Center };
        icon.Children.Add(tint);
        icon.Children.Add(symbol);
        return icon;
    }

    private FrameworkElement BuildCategoryView(ToolCategory c, bool expanded, List<ToolAction>? only)
    {
        bool compact = Settings.ToolkitView == "Compact";
        var chevron = new TextBlock
        {
            Text = expanded ? "" : "", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 9,
            Foreground = (Brush)FindResource("DimForegroundBrush"), VerticalAlignment = VerticalAlignment.Center,
        };
        var headerGrid = new Grid();
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(compact ? 28 : 40) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        FrameworkElement icon = compact ? Symbol(c.Glyph, 13) : IconTile(c.Glyph, 30, 15);
        icon.HorizontalAlignment = HorizontalAlignment.Left;
        if (compact) icon.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
        var title = new TextBlock { Text = c.Title, FontSize = compact ? 12.5 : 14, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        Grid.SetColumn(title, 1);
        Grid.SetColumn(chevron, 2);
        headerGrid.Children.Add(icon);
        headerGrid.Children.Add(title);
        headerGrid.Children.Add(chevron);

        var header = new Button
        {
            Style = (Style)FindResource("ToolRow"),
            Padding = compact ? new Thickness(10, 4, 10, 4) : new Thickness(10, 9, 10, 9),
            Content = headerGrid,
            ToolTip = c.Description,
            ContextMenu = ToolMenu(c),
        };
        System.Windows.Automation.AutomationProperties.SetName(header, c.Title);
        System.Windows.Automation.AutomationProperties.SetHelpText(header, c.Description);

        var actions = new StackPanel
        {
            Margin = new Thickness(compact ? 22 : 34, 0, 0, 6),
            Visibility = expanded ? Visibility.Visible : Visibility.Collapsed,
        };
        foreach (var a in only ?? c.Actions)
            actions.Children.Add(ActionRow(c, a, compact));

        header.Click += (_, _) =>
        {
            bool open = actions.Visibility != Visibility.Visible;
            actions.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            chevron.Text = open ? "" : "";
            if (only != null) return;   // search results: don't remember
            Toggle(Settings.ToolkitExpanded, c.Id);
            Settings.Save();
        };

        var panel = new StackPanel();
        panel.Children.Add(header);
        panel.Children.Add(actions);
        return panel;
    }

    /// <summary>An action, with a pin that shows on hover (always, once pinned).</summary>
    private FrameworkElement ActionRow(ToolCategory c, ToolAction a, bool compact)
    {
        bool pinned = IsPinned(c, a);
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var btn = new Button
        {
            Style = (Style)FindResource("ToolRow"),
            Padding = compact ? new Thickness(10, 3, 8, 3) : new Thickness(10, 5, 8, 5),
            Command = a.Command,
            ToolTip = a.Tip,
            Content = new TextBlock { Text = a.Label, FontSize = compact ? 12 : 12.5, TextWrapping = TextWrapping.Wrap },
        };
        System.Windows.Automation.AutomationProperties.SetName(btn, $"{c.Title}: {a.Label}");

        var pin = PinButton(pinned, () => TogglePin(c, a), a.Label);
        pin.Opacity = pinned ? 1 : 0;
        grid.MouseEnter += (_, _) => pin.Opacity = 1;
        grid.MouseLeave += (_, _) => pin.Opacity = pinned || pin.IsKeyboardFocused ? 1 : 0;
        pin.GotKeyboardFocus += (_, _) => pin.Opacity = 1;
        Grid.SetColumn(pin, 1);
        grid.Children.Add(btn);
        grid.Children.Add(pin);

        var cm = new ContextMenu();
        var pinItem = new MenuItem { Header = pinned ? "Unpin from top" : "Pin to top" };
        pinItem.Click += (_, _) => TogglePin(c, a);
        cm.Items.Add(pinItem);
        btn.ContextMenu = cm;
        return grid;
    }

    private Button PinButton(bool pinned, Action toggle, string label)
    {
        var symbol = Symbol(pinned ? "" : "", 11);   // PinFill / Pin
        if (pinned) symbol.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
        else symbol.Foreground = (Brush)FindResource("DimForegroundBrush");
        var pin = new Button
        {
            Style = (Style)FindResource("ToolRow"),
            Padding = new Thickness(6, 4, 6, 4),
            VerticalAlignment = VerticalAlignment.Center,
            Content = symbol,
            ToolTip = pinned ? "Unpin" : "Pin to the top of the Toolkit",
        };
        System.Windows.Automation.AutomationProperties.SetName(pin, (pinned ? "Unpin " : "Pin ") + label);
        pin.Click += (_, _) => toggle();
        return pin;
    }

    private FrameworkElement PinnedRow(ToolCategory c, ToolAction a)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var content = new StackPanel { Orientation = Orientation.Horizontal };
        var icon = IconTile(c.Glyph, 22, 11);
        icon.Margin = new Thickness(0, 0, 10, 0);
        content.Children.Add(icon);
        content.Children.Add(new TextBlock { Text = a.Label, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
        var btn = new Button
        {
            Style = (Style)FindResource("ToolRow"),
            Padding = new Thickness(10, 4, 8, 4),
            Command = a.Command,
            ToolTip = a.Tip ?? c.Title,
            Content = content,
        };
        System.Windows.Automation.AutomationProperties.SetName(btn, $"Pinned: {a.Label}");
        var pin = PinButton(true, () => TogglePin(c, a), a.Label);
        Grid.SetColumn(pin, 1);
        grid.Children.Add(btn);
        grid.Children.Add(pin);
        return grid;
    }

    /// <summary>Tiles view: a square per tool; clicking it lists its actions in a menu.</summary>
    private FrameworkElement BuildTile(ToolCategory c)
    {
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        var icon = IconTile(c.Glyph, 34, 17);
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        stack.Children.Add(icon);
        stack.Children.Add(new TextBlock
        {
            Text = c.Title, FontSize = 11, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 0), MaxHeight = 30, TextTrimming = TextTrimming.CharacterEllipsis,
        });
        var tile = new Button
        {
            Style = (Style)FindResource("ToolRow"),
            Width = 74, Height = 80, Margin = new Thickness(2),
            Padding = new Thickness(4, 8, 4, 4),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Content = stack,
            ToolTip = c.Description,
            ContextMenu = ToolMenu(c),
        };
        System.Windows.Automation.AutomationProperties.SetName(tile, c.Title);
        tile.Click += (_, _) =>
        {
            var menu = new ContextMenu { PlacementTarget = tile, Placement = PlacementMode.Bottom };
            foreach (var a in c.Actions)
            {
                var item = new MenuItem { Header = a.Label, Command = a.Command, ToolTip = a.Tip };
                if (IsPinned(c, a)) item.Icon = Symbol("", 11);
                menu.Items.Add(item);
            }
            menu.IsOpen = true;
        };
        return tile;
    }

    /// <summary>Right-click on a tool: move it within its section, or hide it.</summary>
    private ContextMenu ToolMenu(ToolCategory c)
    {
        var cm = new ContextMenu();
        var up = new MenuItem { Header = "Move up" };
        up.Click += (_, _) => Move(c, -1);
        var down = new MenuItem { Header = "Move down" };
        down.Click += (_, _) => Move(c, +1);
        var hide = new MenuItem { Header = "Hide from Toolkit" };
        hide.Click += (_, _) => { Toggle(Settings.ToolkitHidden, c.Id); SaveAndRender(); };
        cm.Items.Add(up);
        cm.Items.Add(down);
        cm.Items.Add(new Separator());
        cm.Items.Add(hide);
        return cm;
    }

    // ── Customise menu ───────────────────────────────────────────────────────

    private void CustomiseButton_Click(object sender, RoutedEventArgs e) => OpenCustomiseMenu((FrameworkElement)sender);

    private void OpenCustomiseMenu(FrameworkElement anchor)
    {
        var menu = new ContextMenu { PlacementTarget = anchor, Placement = PlacementMode.Bottom };

        foreach (var (view, label) in new[] { ("List", "List"), ("Compact", "Compact list"), ("Tiles", "Tiles") })
        {
            var item = new MenuItem { Header = label, IsCheckable = true, IsChecked = Settings.ToolkitView == view };
            item.Click += (_, _) => { Settings.ToolkitView = view; SaveAndRender(); };
            menu.Items.Add(item);
        }
        menu.Items.Add(new Separator());

        var sections = new MenuItem { Header = "Show section headings", IsCheckable = true, IsChecked = Settings.ToolkitShowSections };
        sections.Click += (_, _) => { Settings.ToolkitShowSections = !Settings.ToolkitShowSections; SaveAndRender(); };
        menu.Items.Add(sections);

        var collapse = new MenuItem { Header = "Close all tools", IsEnabled = Settings.ToolkitExpanded.Count > 0 };
        collapse.Click += (_, _) => { Settings.ToolkitExpanded.Clear(); SaveAndRender(); };
        menu.Items.Add(collapse);

        var unpin = new MenuItem { Header = "Unpin everything", IsEnabled = Settings.ToolkitPinned.Count > 0 };
        unpin.Click += (_, _) => { Settings.ToolkitPinned.Clear(); SaveAndRender(); };
        menu.Items.Add(unpin);

        var shown = new MenuItem { Header = "Show tools" };
        foreach (var c in Ordered())
        {
            var item = new MenuItem { Header = c.Title, IsCheckable = true, IsChecked = !IsHidden(c), StaysOpenOnClick = true };
            item.Click += (_, _) => { Toggle(Settings.ToolkitHidden, c.Id); SaveAndRender(); };
            shown.Items.Add(item);
        }
        menu.Items.Add(shown);
        menu.Items.Add(new Separator());

        var reset = new MenuItem { Header = "Reset Toolkit" };
        reset.Click += (_, _) =>
        {
            Settings.ToolkitView = "List";
            Settings.ToolkitShowSections = true;
            Settings.ToolkitHidden.Clear();
            Settings.ToolkitOrder.Clear();
            Settings.ToolkitExpanded.Clear();
            SaveAndRender();
        };
        menu.Items.Add(reset);
        menu.IsOpen = true;
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => Render();
}
