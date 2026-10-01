using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using PdfEdit.Models;
using PdfEdit.ViewModels;

namespace PdfEdit.Controls;

/// <summary>
/// Acrobat's Comments list: every comment in the document (notes, text, highlights, drawings,
/// text edits, measurements) grouped by page, with search, type / status / author filters and
/// sorting. Each comment shows its author, date, note and reply thread, and can be replied to,
/// given a review status, checked off, edited or deleted. Clicking one goes to its page.
/// </summary>
public partial class CommentsPanel : UserControl
{
    private MainViewModel? _vm;
    private bool _rebuildQueued;
    private bool _editing;                 // a note / reply box is open: don't rebuild under it

    private sealed record Entry(
        string Category, string TypeLabel, string Glyph, int Page, CommentInfo Comment,
        Func<string> GetText, Action<string> SetText, Action Delete, string? Extra, string? AuthorOverride);

    public CommentsPanel()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Attach(DataContext as MainViewModel);
    }

    private void Attach(MainViewModel? vm)
    {
        if (_vm != null)
        {
            foreach (var c in Collections(_vm)) c.CollectionChanged -= OnCollectionChanged;
            _vm.CommentsChanged -= QueueRebuild;
            _vm.PageChanged -= QueueRebuild;
            _vm.DocumentLoaded -= QueueRebuild;
        }
        _vm = vm;
        if (_vm != null)
        {
            foreach (var c in Collections(_vm)) c.CollectionChanged += OnCollectionChanged;
            _vm.CommentsChanged += QueueRebuild;
            _vm.PageChanged += QueueRebuild;
            _vm.DocumentLoaded += QueueRebuild;
        }
        QueueRebuild();
    }

    private static IEnumerable<INotifyCollectionChanged> Collections(MainViewModel vm) => new INotifyCollectionChanged[]
    {
        vm.StickyNotes, vm.FreeTextAnnotations, vm.HighlightAnnotations, vm.ShapeAnnotations, vm.TextEditMarks,
    };

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => QueueRebuild();

    private void QueueRebuild()
    {
        if (_rebuildQueued) return;
        _rebuildQueued = true;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, () =>
        {
            _rebuildQueued = false;
            if (_editing) return;          // rebuilt when the edit ends
            Rebuild();
        });
    }

    private void Filter_Changed(object sender, RoutedEventArgs e)
    {
        if (IsLoaded) Rebuild();
    }

    private void AddNote_Click(object sender, RoutedEventArgs e)
    {
        if (_vm == null) return;
        _vm.IsDesignMode = false;
        _vm.ActiveTool = ActiveTool.StickyNote;
        _vm.StatusText = "Click the page to place your comment.";
    }

    // ── Collecting the comments ──────────────────────────────────────────────

    private List<Entry> CollectEntries()
    {
        var list = new List<Entry>();
        if (_vm == null) return list;
        var vm = _vm;
        void Refresh() => vm.RefreshAfterCommentEdit();

        foreach (var n in vm.StickyNotes.ToList())
            list.Add(new Entry("Notes", "Sticky note", "", n.PageNumber, n.Comment,
                () => n.Text, t => { n.Text = t; Refresh(); },
                () => { vm.RemoveStickyNote(n); Refresh(); }, null,
                string.IsNullOrEmpty(n.Author) ? null : n.Author));

        foreach (var a in vm.FreeTextAnnotations.ToList())
        {
            if (a.Text.StartsWith("__INK__:", StringComparison.Ordinal))
                list.Add(new Entry("Drawings", "Drawing", "", a.PageNumber, a.Comment,
                    () => a.Comment.Note, t => a.Comment.Note = t,
                    () => { vm.FreeTextAnnotations.Remove(a); Refresh(); }, null, null));
            else if (a.IsHighlight)
                list.Add(new Entry("Highlights", "Highlight", "", a.PageNumber, a.Comment,
                    () => a.Comment.Note, t => a.Comment.Note = t,
                    () => { vm.RemoveFreeTextAnnotation(a); Refresh(); }, null, null));
            else if (MarkShapes.FromGlyph(a.Text) is { } mark)
                list.Add(new Entry("Text", $"Mark {a.Text}", "", a.PageNumber, a.Comment,
                    () => a.Comment.Note, t => a.Comment.Note = t,
                    () => { vm.RemoveFreeTextAnnotation(a); Refresh(); }, null, null));
            else
                list.Add(new Entry("Text", "Text", "", a.PageNumber, a.Comment,
                    () => a.Text, t => { a.Text = t; Refresh(); },
                    () => { vm.RemoveFreeTextAnnotation(a); Refresh(); }, null, null));
        }

        foreach (var h in vm.HighlightAnnotations.ToList())
            list.Add(new Entry("Highlights", h.Kind switch
                {
                    HighlightKind.Underline => "Underline",
                    HighlightKind.Strikethrough => "Strikethrough",
                    _ => "Highlight",
                }, h.Kind == HighlightKind.Highlight ? "" : h.Kind == HighlightKind.Underline ? "" : "",
                h.PageNumber, h.Comment, () => h.Comment.Note, t => h.Comment.Note = t,
                () => { vm.RemoveHighlightAnnotation(h); Refresh(); }, null, null));

        foreach (var s in vm.ShapeAnnotations.ToList())
        {
            bool measure = s.Kind is ShapeKind.Distance or ShapeKind.Perimeter or ShapeKind.Area;
            string glyph = s.Kind switch
            {
                ShapeKind.Ellipse => "", ShapeKind.Arrow => "", ShapeKind.Callout => "",
                ShapeKind.Line => "", ShapeKind.Cloud => "", ShapeKind.Polyline or ShapeKind.Perimeter => "",
                ShapeKind.Distance => "", _ => "",
            };
            list.Add(new Entry(measure ? "Measurements" : "Drawings", s.Kind.ToString(), glyph, s.PageNumber, s.Comment,
                () => s.Kind == ShapeKind.Callout ? s.CalloutText : s.Comment.Note,
                t => { if (s.Kind == ShapeKind.Callout) { s.CalloutText = t; Refresh(); } else s.Comment.Note = t; },
                () => { vm.RemoveShapeAnnotation(s); Refresh(); },
                measure ? PdfViewerControl.MeasureLabel(s) : null, null));
        }

        foreach (var m in vm.TextEditMarks.ToList())
            list.Add(new Entry("Text edits", m.Kind == TextEditKind.Insert ? "Insert text" : "Replace text",
                m.Kind == TextEditKind.Insert ? "" : "", m.PageNumber, m.Comment,
                () => m.Comment.Note, t => { m.Comment.Note = t; Refresh(); },
                () => { vm.RemoveTextEditMark(m); Refresh(); }, null, null));

        return list;
    }

    private static string AuthorOf(Entry e) => e.AuthorOverride ?? e.Comment.Author;

    // ── Building the list ────────────────────────────────────────────────────

    private void Rebuild()
    {
        var all = CollectEntries();
        RefreshAuthorFilter(all);

        string search = SearchBox.Text.Trim();
        string type = (TypeFilter.SelectedItem as ComboBoxItem)?.Content as string ?? "All types";
        string status = (StatusFilter.SelectedItem as ComboBoxItem)?.Content as string ?? "Any status";
        string author = AuthorFilter.SelectedItem as string ?? "All authors";
        string sort = (SortBy.SelectedItem as ComboBoxItem)?.Content as string ?? "Page";

        IEnumerable<Entry> shown = all;
        if (type != "All types") shown = shown.Where(e => e.Category == type);
        shown = status switch
        {
            "Any status" => shown,
            "Checked" => shown.Where(e => e.Comment.Checked),
            "Unchecked" => shown.Where(e => !e.Comment.Checked),
            _ => shown.Where(e => e.Comment.Status.ToString() == status),
        };
        if (author != "All authors") shown = shown.Where(e => AuthorOf(e) == author);
        if (search.Length > 0)
            shown = shown.Where(e =>
                e.GetText().Contains(search, StringComparison.OrdinalIgnoreCase)
                || AuthorOf(e).Contains(search, StringComparison.OrdinalIgnoreCase)
                || e.TypeLabel.Contains(search, StringComparison.OrdinalIgnoreCase)
                || e.Comment.Replies.Any(r => r.Text.Contains(search, StringComparison.OrdinalIgnoreCase)
                                           || r.Author.Contains(search, StringComparison.OrdinalIgnoreCase)));

        var ordered = sort switch
        {
            "Date" => shown.OrderByDescending(e => e.Comment.Created).ToList(),
            "Author" => shown.OrderBy(e => AuthorOf(e)).ThenBy(e => e.Page).ToList(),
            "Type" => shown.OrderBy(e => e.TypeLabel).ThenBy(e => e.Page).ToList(),
            _ => shown.OrderBy(e => e.Page).ThenBy(e => e.Comment.Created).ToList(),
        };

        CountText.Text = ordered.Count == all.Count ? $"{all.Count}" : $"{ordered.Count} of {all.Count}";
        ListHost.Children.Clear();

        if (all.Count == 0)
        {
            ListHost.Children.Add(Hint("No comments yet. Use the comment and drawing tools, or + Comment, to add one."));
            return;
        }
        if (ordered.Count == 0)
        {
            ListHost.Children.Add(Hint("No comments match the search or filters."));
            return;
        }

        int? lastPage = null;
        foreach (var e in ordered)
        {
            if (sort == "Page" && e.Page != lastPage)
            {
                ListHost.Children.Add(new TextBlock
                {
                    Text = $"Page {e.Page}", FontWeight = FontWeights.SemiBold, FontSize = 11,
                    Margin = new Thickness(4, 10, 0, 4), Foreground = (Brush)FindResource("DimForegroundBrush"),
                });
                lastPage = e.Page;
            }
            ListHost.Children.Add(BuildCard(e, sort != "Page"));
        }
    }

    private void RefreshAuthorFilter(List<Entry> all)
    {
        string current = AuthorFilter.SelectedItem as string ?? "All authors";
        var authors = new[] { "All authors" }.Concat(all.Select(AuthorOf).Where(a => a.Length > 0).Distinct().OrderBy(a => a)).ToList();
        if (AuthorFilter.Items.Cast<object>().Select(o => o as string).SequenceEqual(authors)) return;
        AuthorFilter.SelectionChanged -= Filter_Changed;
        AuthorFilter.ItemsSource = authors;
        AuthorFilter.SelectedItem = authors.Contains(current) ? current : "All authors";
        AuthorFilter.SelectionChanged += Filter_Changed;
    }

    private TextBlock Hint(string text) => new()
    {
        Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(6, 12, 6, 0), FontSize = 12,
        Foreground = (Brush)FindResource("DimForegroundBrush"),
    };

    // ── One comment card ─────────────────────────────────────────────────────

    private Border BuildCard(Entry e, bool showPage)
    {
        var fg = (Brush)FindResource("ForegroundBrush");
        var dim = (Brush)FindResource("DimForegroundBrush");
        var c = e.Comment;
        var body = new StackPanel();

        // Header: icon · type · status · page          [✓]
        var header = new DockPanel();
        var check = new CheckBox
        {
            IsChecked = c.Checked, VerticalAlignment = VerticalAlignment.Center, Focusable = false,
            ToolTip = "Check off (Acrobat \"Add checkmark\")",
        };
        System.Windows.Automation.AutomationProperties.SetName(check, "Checkmark");
        check.Click += (_, _) => { c.Checked = check.IsChecked == true; Touch(c); };
        DockPanel.SetDock(check, Dock.Right);
        header.Children.Add(check);

        var title = new StackPanel { Orientation = Orientation.Horizontal };
        title.Children.Add(new TextBlock { Text = e.Glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 13,
                                           Foreground = fg, Margin = new Thickness(0, 1, 6, 0) });
        title.Children.Add(new TextBlock { Text = e.TypeLabel, FontWeight = FontWeights.SemiBold, Foreground = fg });
        if (showPage)
            title.Children.Add(new TextBlock { Text = $"  · p. {e.Page}", Foreground = dim, FontSize = 11, VerticalAlignment = VerticalAlignment.Center });
        if (c.Status != CommentStatus.None)
            title.Children.Add(StatusPill(c.Status));
        header.Children.Add(title);
        body.Children.Add(header);

        body.Children.Add(new TextBlock
        {
            Text = $"{AuthorOf(e)}  ·  {c.Created:g}", FontSize = 11, Foreground = dim, Margin = new Thickness(19, 1, 0, 3),
        });

        // Note (double-click to edit)
        string text = e.GetText();
        var note = new TextBlock
        {
            Text = text.Length > 0 ? text : "Add a note…",
            FontStyle = text.Length > 0 ? FontStyles.Normal : FontStyles.Italic,
            Foreground = text.Length > 0 ? fg : dim, TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(19, 0, 0, 2), Cursor = Cursors.IBeam,
            ToolTip = "Double-click to edit",
        };
        var noteHost = new ContentControl { Content = note, Focusable = false };
        note.MouseLeftButtonDown += (_, me) =>
        {
            if (me.ClickCount < 2) return;
            me.Handled = true;
            BeginInlineEdit(noteHost, e.GetText(), saved => { e.SetText(saved); Touch(c); });
        };
        body.Children.Add(noteHost);

        if (e.Extra != null)
            body.Children.Add(new TextBlock { Text = e.Extra, Foreground = fg, FontWeight = FontWeights.SemiBold, Margin = new Thickness(19, 0, 0, 2) });

        // Replies
        foreach (var r in c.Replies)
        {
            var rep = new StackPanel { Margin = new Thickness(19, 4, 0, 0) };
            rep.Children.Add(new TextBlock { Text = $"↳ {r.Author}  ·  {r.Created:g}", FontSize = 11, Foreground = dim });
            rep.Children.Add(new TextBlock { Text = r.Text, TextWrapping = TextWrapping.Wrap, Foreground = fg, Margin = new Thickness(12, 0, 0, 0) });
            var rm = new ContextMenu();
            var del = new MenuItem { Header = "Delete reply" };
            del.Click += (_, _) => { c.Replies.Remove(r); Touch(c); };
            rm.Items.Add(del);
            rep.ContextMenu = rm;
            body.Children.Add(rep);
        }

        // Reply box (shown by "Reply")
        var replyHost = new ContentControl { Focusable = false, Margin = new Thickness(19, 4, 0, 0) };
        body.Children.Add(replyHost);

        // Actions
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(13, 4, 0, 0) };
        actions.Children.Add(LinkButton("Reply", "Reply to this comment", () =>
            BeginInlineEdit(replyHost, string.Empty, saved =>
            {
                if (saved.Trim().Length == 0) return;
                c.Replies.Add(new CommentReply { Text = saved.Trim() });
                Touch(c);
            }, placeholder: "Write a reply…")));

        var statusBtn = LinkButton("Status ▾", "Set review status (Acrobat \"Set Status\")", () => { });
        statusBtn.Click += (_, _) =>
        {
            var menu = new ContextMenu { PlacementTarget = statusBtn, Placement = PlacementMode.Bottom };
            foreach (var st in Enum.GetValues<CommentStatus>())
            {
                var mi = new MenuItem { Header = st.ToString(), IsChecked = c.Status == st };
                mi.Click += (_, _) => { c.Status = st; Touch(c); };
                menu.Items.Add(mi);
            }
            menu.IsOpen = true;
        };
        actions.Children.Add(statusBtn);
        actions.Children.Add(LinkButton("Delete", "Delete this comment", () =>
        {
            if (MessageBox.Show(Window.GetWindow(this), $"Delete this {e.TypeLabel.ToLowerInvariant()}?", "Delete comment",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                e.Delete();
        }));
        body.Children.Add(actions);

        var card = new Border
        {
            Child = body, Padding = new Thickness(8, 6, 8, 6), Margin = new Thickness(0, 0, 0, 6),
            CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1),
            BorderBrush = (Brush)FindResource("AppBorderBrush"),
            Background = c.Checked ? (Brush)FindResource("HoverBgBrush") : Brushes.Transparent,
            Cursor = Cursors.Hand, ToolTip = "Click to go to this comment",
        };
        System.Windows.Automation.AutomationProperties.SetName(card, $"{e.TypeLabel} on page {e.Page} by {AuthorOf(e)}");
        card.MouseEnter += (_, _) => card.BorderBrush = (Brush)FindResource("AccentBrush");
        card.MouseLeave += (_, _) => card.BorderBrush = (Brush)FindResource("AppBorderBrush");
        card.MouseLeftButtonUp += (_, me) =>
        {
            if (me.OriginalSource is DependencyObject d && IsInside<ButtonBase>(d)) return;
            if (me.OriginalSource is DependencyObject d2 && IsInside<TextBox>(d2)) return;
            _vm?.GoToComment(e.Page);
        };
        return card;
    }

    private static bool IsInside<T>(DependencyObject d) where T : DependencyObject
    {
        for (var cur = d; cur != null; cur = cur is Visual ? VisualTreeHelper.GetParent(cur) : LogicalTreeHelper.GetParent(cur))
            if (cur is T) return true;
        return false;
    }

    private Border StatusPill(CommentStatus status)
    {
        var color = status switch
        {
            CommentStatus.Accepted => Color.FromRgb(0x2E, 0x7D, 0x32),
            CommentStatus.Rejected => Color.FromRgb(0xC6, 0x28, 0x28),
            CommentStatus.Completed => Color.FromRgb(0x15, 0x65, 0xC0),
            _ => Color.FromRgb(0x75, 0x75, 0x75),
        };
        return new Border
        {
            Background = new SolidColorBrush(color), CornerRadius = new CornerRadius(8),
            Padding = new Thickness(6, 0, 6, 1), Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = status.ToString(), Foreground = Brushes.White, FontSize = 10 },
        };
    }

    private Button LinkButton(string text, string tip, Action onClick)
    {
        var b = new Button
        {
            Content = text, ToolTip = tip, Padding = new Thickness(6, 1, 6, 1), Margin = new Thickness(0, 0, 4, 0),
            Background = Brushes.Transparent, BorderThickness = new Thickness(0), Cursor = Cursors.Hand,
            Foreground = (Brush)FindResource("AccentBrush"), FontSize = 11,
        };
        System.Windows.Automation.AutomationProperties.SetName(b, text);
        b.Click += (_, _) => onClick();
        return b;
    }

    /// <summary>Inline editor: Ctrl+Enter or the Post button saves, Esc cancels.</summary>
    private void BeginInlineEdit(ContentControl host, string initial, Action<string> save, string? placeholder = null)
    {
        _editing = true;
        var previous = host.Content;
        var box = new TextBox
        {
            Text = initial, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 44,
            Padding = new Thickness(4, 2, 4, 2), ToolTip = "Ctrl+Enter to post, Esc to cancel",
        };
        System.Windows.Automation.AutomationProperties.SetName(box, placeholder ?? "Edit comment");
        var post = new Button { Content = "Post", Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(0, 4, 6, 0), IsDefault = false };
        var cancel = new Button { Content = "Cancel", Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(0, 4, 0, 0) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(post);
        buttons.Children.Add(cancel);
        var panel = new StackPanel();
        panel.Children.Add(box);
        panel.Children.Add(buttons);
        host.Content = panel;

        bool done = false;
        void Finish(bool commit)
        {
            if (done) return;
            done = true;
            _editing = false;
            host.Content = previous;
            if (commit) save(box.Text);
            QueueRebuild();
        }
        post.Click += (_, _) => Finish(true);
        cancel.Click += (_, _) => Finish(false);
        box.PreviewKeyDown += (_, ke) =>
        {
            if (ke.Key == Key.Escape) { Finish(false); ke.Handled = true; }
            else if (ke.Key == Key.Enter && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) { Finish(true); ke.Handled = true; }
        };
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, () => { box.Focus(); box.SelectAll(); });
    }

    /// <summary>A comment changed: stamp it, re-draw the list and the page, keep it with the document.</summary>
    private void Touch(CommentInfo c)
    {
        c.Modified = DateTime.Now;
        _vm?.NotifyCommentsChanged();
        _vm?.SaveDocumentState();
        QueueRebuild();
    }
}
