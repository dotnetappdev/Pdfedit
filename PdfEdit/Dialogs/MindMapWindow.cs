using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;


namespace PdfEdit.Dialogs;

/// <summary>A node of the mind map: a topic, the page it's on, and its subtopics.</summary>
public sealed class MindNode
{
    public string Title { get; set; } = "";
    public int Page { get; set; }
    public List<MindNode> Children { get; set; } = new();

    /// <summary>Reads the AI's JSON ({"title","page","children"}), tolerating text around it.</summary>
    public static MindNode? Parse(string reply)
    {
        int a = reply.IndexOf('{'), z = reply.LastIndexOf('}');
        if (a < 0 || z <= a) return null;
        try
        {
            using var doc = JsonDocument.Parse(reply[a..(z + 1)]);
            return From(doc.RootElement, 0);
        }
        catch { return null; }

        static MindNode From(JsonElement e, int depth)
        {
            var n = new MindNode
            {
                Title = e.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "",
                Page = e.TryGetProperty("page", out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out int pg) ? pg : 0,
            };
            if (depth < 4 && e.TryGetProperty("children", out var c) && c.ValueKind == JsonValueKind.Array)
                foreach (var child in c.EnumerateArray().Take(10))
                    if (child.ValueKind == JsonValueKind.Object) n.Children.Add(From(child, depth + 1));
            return n;
        }
    }
}

/// <summary>
/// Mind map of the document (UPDF style): the main topic on the left, branches to the right, one
/// colour per branch. Click a topic with a page to go there; Ctrl+wheel zooms; save as PNG.
/// </summary>
public sealed class MindMapWindow : Window
{
    private const double ColW = 250, NodeW = 210, RowH = 46;
    private static readonly Color[] Palette =
    {
        Color.FromRgb(0x3B, 0x82, 0xF6), Color.FromRgb(0x10, 0xB9, 0x81), Color.FromRgb(0xF5, 0x9E, 0x0B),
        Color.FromRgb(0xEF, 0x44, 0x44), Color.FromRgb(0x8B, 0x5C, 0xF6), Color.FromRgb(0x06, 0xB6, 0xD4),
        Color.FromRgb(0xEC, 0x48, 0x99), Color.FromRgb(0x84, 0xCC, 0x16),
    };

    private readonly Canvas _canvas = new() { Background = Brushes.Transparent };
    private readonly ScaleTransform _zoom = new(1, 1);
    private readonly Action<int> _goToPage;
    private readonly string _baseName;

    public MindMapWindow(MindNode root, string baseName, Action<int> goToPage)
    {
        _goToPage = goToPage;
        _baseName = baseName;
        Title = $"Mind map — {baseName}";
        Width = 1100; Height = 720;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, "PanelBgBrush");

        var save = new Button { Content = "Save as PNG…", Padding = new Thickness(12, 3, 12, 3), Margin = new Thickness(0, 0, 8, 0) };
        save.Click += (_, _) => SavePng();
        var fit = new Button { Content = "Fit", Padding = new Thickness(12, 3, 12, 3) };
        fit.Click += (_, _) => Fit();
        var hint = new TextBlock { Text = "Click a topic to go to its page · Ctrl+wheel to zoom", VerticalAlignment = VerticalAlignment.Center, FontSize = 11 };
        hint.SetResourceReference(TextBlock.ForegroundProperty, "DimForegroundBrush");
        var bar = new DockPanel { Margin = new Thickness(12, 8, 12, 8) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(save);
        buttons.Children.Add(fit);
        DockPanel.SetDock(buttons, Dock.Right);
        bar.Children.Add(buttons);
        bar.Children.Add(hint);

        var holder = new Grid { LayoutTransform = _zoom, Margin = new Thickness(30) };
        holder.Children.Add(_canvas);
        var scroll = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = holder };
        scroll.PreviewMouseWheel += (_, e) =>
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
            e.Handled = true;
            double z = Math.Clamp(_zoom.ScaleX * (e.Delta > 0 ? 1.1 : 1 / 1.1), 0.25, 3);
            _zoom.ScaleX = _zoom.ScaleY = z;
        };

        var root2 = new DockPanel();
        DockPanel.SetDock(bar, Dock.Top);
        root2.Children.Add(bar);
        root2.Children.Add(scroll);
        Content = root2;

        Draw(root);
        Loaded += (_, _) => Fit();
    }

    private static int Leaves(MindNode n) => n.Children.Count == 0 ? 1 : n.Children.Sum(Leaves);

    private void Draw(MindNode root)
    {
        double height = Leaves(root) * RowH;
        Place(root, 0, 0, height, null, null);
        _canvas.Width = (Depth(root) + 1) * ColW;
        _canvas.Height = height;

        static int Depth(MindNode n) => n.Children.Count == 0 ? 0 : 1 + n.Children.Max(Depth);
    }

    private void Place(MindNode n, int depth, double top, double bottom, Point? parentRight, Color? branch)
    {
        double cy = (top + bottom) / 2, x = depth * ColW;
        var colour = branch ?? Color.FromRgb(0x33, 0x41, 0x55);

        var text = new TextBlock
        {
            Text = n.Title, TextWrapping = TextWrapping.Wrap, MaxWidth = NodeW - 20,
            FontSize = depth == 0 ? 16 : depth == 1 ? 13.5 : 12.5, FontWeight = depth <= 1 ? FontWeights.SemiBold : FontWeights.Normal,
            Foreground = depth == 0 ? Brushes.White : new SolidColorBrush(Color.FromRgb(0x1F, 0x29, 0x37)),
        };
        if (n.Page > 0) text.Inlines.Add(new System.Windows.Documents.Run($"  p.{n.Page}") { FontSize = 10.5, Foreground = depth == 0 ? Brushes.White : new SolidColorBrush(colour) });
        var box = new Border
        {
            Child = text, Padding = new Thickness(10, 6, 10, 6), CornerRadius = new CornerRadius(depth == 0 ? 10 : 8),
            Background = depth == 0 ? new SolidColorBrush(colour) : new SolidColorBrush(Color.FromArgb(depth == 1 ? (byte)0x33 : (byte)0x18, colour.R, colour.G, colour.B)),
            BorderBrush = new SolidColorBrush(colour), BorderThickness = new Thickness(depth == 0 ? 0 : 1.5),
            MaxWidth = NodeW, Cursor = n.Page > 0 ? Cursors.Hand : Cursors.Arrow,
            ToolTip = n.Page > 0 ? $"Go to page {n.Page}" : null,
        };
        if (depth > 1) { box.Background = Brushes.White; }
        int page = n.Page;
        if (page > 0) box.MouseLeftButtonUp += (_, _) => _goToPage(page);
        box.Measure(new Size(NodeW, double.PositiveInfinity));
        var size = box.DesiredSize;
        Canvas.SetLeft(box, x);
        Canvas.SetTop(box, cy - size.Height / 2);
        Panel.SetZIndex(box, 2);
        _canvas.Children.Add(box);

        if (parentRight is { } pr)
        {
            var start = pr; var end = new Point(x, cy);
            double mid = (start.X + end.X) / 2;
            var fig = new PathFigure { StartPoint = start };
            fig.Segments.Add(new BezierSegment(new Point(mid, start.Y), new Point(mid, end.Y), end, true));
            _canvas.Children.Add(new System.Windows.Shapes.Path
            {
                Data = new PathGeometry(new[] { fig }), Stroke = new SolidColorBrush(colour),
                StrokeThickness = depth == 1 ? 2.5 : 1.6, Opacity = 0.8,
            });
        }

        var right = new Point(x + size.Width, cy);
        double y = top;
        int i = 0;
        foreach (var c in n.Children)
        {
            double h = Leaves(c) * RowH;
            Place(c, depth + 1, y, y + h, right, depth == 0 ? Palette[i++ % Palette.Length] : colour);
            y += h;
        }
    }

    private void Fit()
    {
        if (_canvas.Width <= 0 || ActualWidth <= 0) return;
        double z = Math.Min((ActualWidth - 100) / _canvas.Width, (ActualHeight - 140) / _canvas.Height);
        _zoom.ScaleX = _zoom.ScaleY = Math.Clamp(z, 0.25, 1.5);
    }

    private void SavePng()
    {
        var dlg = new Microsoft.Win32.SaveFileDialog { Filter = "PNG image|*.png", FileName = _baseName + " mind map.png", AddExtension = true };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            const double scale = 2;
            int w = (int)(_canvas.Width * scale) + 80, h = (int)(_canvas.Height * scale) + 80;
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, w, h));
                dc.PushTransform(new TranslateTransform(40, 40));
                dc.PushTransform(new ScaleTransform(scale, scale));
                dc.DrawRectangle(new VisualBrush(_canvas) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top },
                    null, new Rect(0, 0, _canvas.Width, _canvas.Height));
            }
            var bmp = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(visual);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(bmp));
            using var fs = File.Create(dlg.FileName);
            enc.Save(fs);
        }
        catch (Exception ex) { AppDialog.ShowError("Couldn't save the picture.", ex); }
    }
}
