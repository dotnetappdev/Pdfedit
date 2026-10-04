using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;

namespace PdfEdit.Controls;

/// <summary>
/// Shows an AI reply's Markdown: headings, paragraphs, bulleted and numbered lists, quotes,
/// code blocks, tables, rules, **bold**, *italic*, `code` and links. Page references such as
/// "(p. 4)" or "page 12" become links that jump to that page (<see cref="PageRequested"/>).
/// While a reply streams in, it redraws at most a few times a second.
/// </summary>
public class MarkdownView : StackPanel
{
    public static readonly DependencyProperty MarkdownProperty = DependencyProperty.Register(
        nameof(Markdown), typeof(string), typeof(MarkdownView), new PropertyMetadata("", (d, _) => ((MarkdownView)d).Schedule()));

    public string Markdown
    {
        get => (string)GetValue(MarkdownProperty);
        set => SetValue(MarkdownProperty, value);
    }

    /// <summary>Turn "p. 4" into links to the open document's pages (off where answers cite other files).</summary>
    public bool PageLinks { get; set; } = true;

    /// <summary>Raised when a page reference is clicked (1-based page).</summary>
    public static event Action<int>? PageRequested;

    private readonly DispatcherTimer _timer;
    private DateTime _lastBuild = DateTime.MinValue;

    public MarkdownView()
    {
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(120) };
        _timer.Tick += (_, _) => { _timer.Stop(); Build(); };
    }

    private void Schedule()
    {
        if ((DateTime.Now - _lastBuild).TotalMilliseconds > 150) { _timer.Stop(); Build(); }
        else if (!_timer.IsEnabled) _timer.Start();
    }

    // ── Blocks ──────────────────────────────────────────────────────────────

    private static readonly Regex HeadingRx = new(@"^(#{1,6})\s+(.*)$");
    private static readonly Regex BulletRx = new(@"^(\s*)[-*+•]\s+(.*)$");
    private static readonly Regex NumberRx = new(@"^(\s*)(\d+)[.)]\s+(.*)$");
    private static readonly Regex RuleRx = new(@"^\s*([-*_])(\s*\1){2,}\s*$");
    private static readonly Regex TableSepRx = new(@"^\s*\|?\s*:?-{2,}:?\s*(\|\s*:?-{2,}:?\s*)*\|?\s*$");

    private void Build()
    {
        _lastBuild = DateTime.Now;
        Children.Clear();
        var lines = (Markdown ?? "").Replace("\r\n", "\n").Split('\n');
        var para = new List<string>();

        void FlushPara()
        {
            if (para.Count == 0) return;
            Children.Add(Paragraph(string.Join(" ", para.Select(p => p.Trim()))));
            para.Clear();
        }

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            string t = line.Trim();

            if (t.Length == 0) { FlushPara(); continue; }

            if (t.StartsWith("```"))
            {
                FlushPara();
                var code = new List<string>();
                for (i++; i < lines.Length && !lines[i].TrimStart().StartsWith("```"); i++) code.Add(lines[i]);
                Children.Add(CodeBlock(string.Join("\n", code)));
                continue;
            }

            var h = HeadingRx.Match(t);
            if (h.Success)
            {
                FlushPara();
                int level = h.Groups[1].Value.Length;
                var tb = Text(h.Groups[2].Value.TrimEnd('#', ' '));
                tb.FontWeight = FontWeights.SemiBold;
                tb.FontSize = level switch { 1 => 17, 2 => 15.5, 3 => 14.5, _ => 13.5 };
                tb.Margin = new Thickness(0, Children.Count == 0 ? 0 : 8, 0, 4);
                Children.Add(tb);
                continue;
            }

            if (RuleRx.IsMatch(t))
            {
                FlushPara();
                var rule = new Border { Height = 1, Margin = new Thickness(0, 6, 0, 6) };
                rule.SetResourceReference(Border.BackgroundProperty, "AppBorderBrush");
                Children.Add(rule);
                continue;
            }

            if (t.StartsWith('|') && i + 1 < lines.Length && TableSepRx.IsMatch(lines[i + 1]))
            {
                FlushPara();
                var rows = new List<string> { t };
                for (i += 2; i < lines.Length && lines[i].Trim().StartsWith('|'); i++) rows.Add(lines[i].Trim());
                i--;
                Children.Add(Table(rows));
                continue;
            }

            if (t.StartsWith('>'))
            {
                FlushPara();
                var quote = new List<string>();
                for (; i < lines.Length && lines[i].Trim().StartsWith('>'); i++) quote.Add(lines[i].Trim().TrimStart('>').Trim());
                i--;
                var border = new Border { BorderThickness = new Thickness(3, 0, 0, 0), Padding = new Thickness(8, 2, 0, 2), Margin = new Thickness(0, 2, 0, 6) };
                border.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
                var tb = Text(string.Join(" ", quote));
                tb.FontStyle = FontStyles.Italic;
                border.Child = tb;
                Children.Add(border);
                continue;
            }

            var b = BulletRx.Match(line);
            var n = NumberRx.Match(line);
            if (b.Success || n.Success)
            {
                FlushPara();
                int indent = (b.Success ? b.Groups[1].Value : n.Groups[1].Value).Replace("\t", "    ").Length / 2;
                string marker = b.Success ? (indent == 0 ? "•" : "◦") : n.Groups[2].Value + ".";
                string body = b.Success ? b.Groups[2].Value : n.Groups[3].Value;
                // Lines that continue the item (indented, not a new item).
                while (i + 1 < lines.Length && lines[i + 1].StartsWith("  ") && lines[i + 1].Trim().Length > 0
                       && !BulletRx.IsMatch(lines[i + 1]) && !NumberRx.IsMatch(lines[i + 1]))
                    body += " " + lines[++i].Trim();
                Children.Add(ListItem(marker, body, indent));
                continue;
            }

            para.Add(line);
        }
        FlushPara();
        if (Children.Count > 0 && Children[^1] is FrameworkElement last)
            last.Margin = new Thickness(last.Margin.Left, last.Margin.Top, last.Margin.Right, 0);
    }

    private TextBlock Paragraph(string text)
    {
        var tb = Text(text);
        tb.Margin = new Thickness(0, 0, 0, 7);
        return tb;
    }

    private TextBlock Text(string text)
    {
        var tb = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 13, LineHeight = 20 };
        tb.SetResourceReference(TextBlock.ForegroundProperty, "ForegroundBrush");
        foreach (var inline in Inlines(text)) tb.Inlines.Add(inline);
        return tb;
    }

    private UIElement ListItem(string marker, string body, int indent)
    {
        var g = new Grid { Margin = new Thickness(4 + indent * 16, 0, 0, 4) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(marker.Length > 2 ? 24 : 16) });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        var m = new TextBlock { Text = marker, FontSize = 13, LineHeight = 20 };
        m.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
        var tb = Text(body);
        Grid.SetColumn(tb, 1);
        g.Children.Add(m);
        g.Children.Add(tb);
        return g;
    }

    private static UIElement CodeBlock(string code)
    {
        var box = new TextBox
        {
            Text = code, IsReadOnly = true, BorderThickness = new Thickness(0), Background = Brushes.Transparent,
            FontFamily = new FontFamily("Cascadia Mono, Consolas, Courier New"), FontSize = 12,
            TextWrapping = TextWrapping.Wrap, Padding = new Thickness(0),
        };
        box.SetResourceReference(Control.ForegroundProperty, "ForegroundBrush");
        var border = new Border { CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 8, 10, 8), Margin = new Thickness(0, 2, 0, 8), BorderThickness = new Thickness(1), Child = box };
        border.SetResourceReference(Border.BackgroundProperty, "PanelBgBrush");
        border.SetResourceReference(Border.BorderBrushProperty, "AppBorderBrush");
        return border;
    }

    private UIElement Table(List<string> rows)
    {
        static List<string> Cells(string row)
        {
            row = row.Trim();
            if (row.StartsWith('|')) row = row[1..];
            if (row.EndsWith('|')) row = row[..^1];
            return row.Split('|').Select(c => c.Trim()).ToList();
        }
        var all = rows.Select(Cells).ToList();
        int cols = all.Max(r => r.Count);
        var g = new Grid();
        for (int c = 0; c < cols; c++) g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        for (int r = 0; r < all.Count; r++)
        {
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            for (int c = 0; c < cols; c++)
            {
                var tb = Text(c < all[r].Count ? all[r][c] : "");
                tb.FontSize = 12;
                tb.LineHeight = 17;
                tb.MaxWidth = 260;
                if (r == 0) tb.FontWeight = FontWeights.SemiBold;
                var cell = new Border { Padding = new Thickness(7, 4, 7, 4), BorderThickness = new Thickness(0, 0, c < cols - 1 ? 1 : 0, r < all.Count - 1 ? 1 : 0), Child = tb };
                cell.SetResourceReference(Border.BorderBrushProperty, "AppBorderBrush");
                if (r == 0) cell.SetResourceReference(Border.BackgroundProperty, "PanelBgBrush");
                Grid.SetRow(cell, r);
                Grid.SetColumn(cell, c);
                g.Children.Add(cell);
            }
        }
        var outer = new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Child = g };
        outer.SetResourceReference(Border.BorderBrushProperty, "AppBorderBrush");
        return new ScrollViewer
        {
            Content = outer, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Margin = new Thickness(0, 2, 0, 8),
        };
    }

    // ── Inline formatting ───────────────────────────────────────────────────

    private static readonly Regex InlineRx = new(
        @"(?<code>`[^`]+`)" +
        @"|(?<bold>\*\*[^*]+\*\*|__[^_]+__)" +
        @"|(?<italic>(?<![\w*])\*[^*\s][^*]*\*(?!\w)|(?<![\w_])_[^_\s][^_]*_(?![\w_]))" +
        @"|(?<link>\[[^\]]+\]\([^)\s]+\))" +
        @"|(?<page>\b(?:pp?\.|[Pp]ages?)\s?(?<n>\d{1,5})(?:\s?[-–]\s?(?<m>\d{1,5}))?\b)");

    private IEnumerable<Inline> Inlines(string text)
    {
        int pos = 0;
        foreach (Match m in InlineRx.Matches(text))
        {
            if (m.Index > pos) yield return new Run(text[pos..m.Index]);
            pos = m.Index + m.Length;
            string v = m.Value;
            if (m.Groups["code"].Success)
            {
                var run = new Run(v[1..^1]) { FontFamily = new FontFamily("Cascadia Mono, Consolas, Courier New"), FontSize = 12 };
                run.SetResourceReference(TextElement.BackgroundProperty, "PanelBgBrush");
                yield return run;
            }
            else if (m.Groups["bold"].Success)
            {
                var bold = new Bold();
                foreach (var i in Inlines(v[2..^2])) bold.Inlines.Add(i);
                yield return bold;
            }
            else if (m.Groups["italic"].Success)
            {
                var it = new Italic();
                foreach (var i in Inlines(v[1..^1])) it.Inlines.Add(i);
                yield return it;
            }
            else if (m.Groups["link"].Success)
            {
                int close = v.IndexOf("](", StringComparison.Ordinal);
                string label = v[1..close], url = v[(close + 2)..^1];
                if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeMailto))
                {
                    var link = new Hyperlink(new Run(label)) { NavigateUri = uri, ToolTip = url };
                    link.RequestNavigate += (_, e) =>
                    {
                        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true }); } catch { }
                        e.Handled = true;
                    };
                    link.SetResourceReference(TextElement.ForegroundProperty, "AccentBrush");
                    yield return link;
                }
                else yield return new Run(label);
            }
            else if (PageLinks && m.Groups["page"].Success && int.TryParse(m.Groups["n"].Value, out int page) && page > 0)
            {
                var link = new Hyperlink(new Run(v)) { ToolTip = $"Go to page {page}", TextDecorations = null, FontWeight = FontWeights.SemiBold };
                link.SetResourceReference(TextElement.ForegroundProperty, "AccentBrush");
                link.Click += (_, _) => PageRequested?.Invoke(page);
                yield return link;
            }
            else yield return new Run(v);
        }
        if (pos < text.Length) yield return new Run(text[pos..]);
    }
}
