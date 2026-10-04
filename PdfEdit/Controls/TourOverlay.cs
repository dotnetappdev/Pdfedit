using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Shapes = System.Windows.Shapes;

namespace PdfEdit.Controls;

/// <summary>One stop on the tour: what to say, what to point at, and anything to do first (open a tab …).</summary>
public sealed record TourStep(string Title, string Text, Func<FrameworkElement?>? Target = null, Action? Prepare = null);

/// <summary>
/// A guided tour drawn over the main window: everything is dimmed except the part being
/// explained, which gets an accent outline and a card beside it with Back / Next / Skip.
/// Keys: → or Enter for next, ← for back, Esc to skip.
/// </summary>
public sealed class TourOverlay : Adorner
{
    private readonly IReadOnlyList<TourStep> _steps;
    private readonly Action _finished;
    private readonly AdornerLayer _layer;
    private readonly Window _window;
    private readonly Canvas _root = new();
    private readonly Shapes.Path _dim = new() { Fill = new SolidColorBrush(Color.FromArgb(0xA8, 0, 0, 0)) };
    private readonly Border _ring = new() { BorderThickness = new Thickness(2.5), CornerRadius = new CornerRadius(8), IsHitTestVisible = false };
    private readonly Border _card = new() { Width = 360, CornerRadius = new CornerRadius(10), Padding = new Thickness(18, 16, 18, 14), BorderThickness = new Thickness(1) };
    private readonly TextBlock _title = new() { FontSize = 16, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
    private readonly TextBlock _text = new() { FontSize = 13, TextWrapping = TextWrapping.Wrap, LineHeight = 20 };
    private readonly TextBlock _count = new() { FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
    private readonly StackPanel _dots = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
    private readonly Button _back, _next, _skip;
    private int _index;
    private bool _closed;

    private TourOverlay(UIElement adorned, AdornerLayer layer, Window window, IReadOnlyList<TourStep> steps, Action finished) : base(adorned)
    {
        _layer = layer;
        _window = window;
        _steps = steps;
        _finished = finished;

        _ring.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
        _card.SetResourceReference(Border.BackgroundProperty, "ContentBgBrush");
        _card.SetResourceReference(Border.BorderBrushProperty, "AppBorderBrush");
        _title.SetResourceReference(TextBlock.ForegroundProperty, "ForegroundBrush");
        _text.SetResourceReference(TextBlock.ForegroundProperty, "ForegroundBrush");
        _count.SetResourceReference(TextBlock.ForegroundProperty, "DimForegroundBrush");
        _card.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 24, ShadowDepth = 4, Opacity = 0.45 };

        _back = MakeButton("Back", () => Go(_index - 1));
        _next = MakeButton("Next", () => Go(_index + 1));
        _skip = MakeButton("Skip tour", Finish);
        _skip.Background = Brushes.Transparent;
        _skip.BorderThickness = new Thickness(0);
        _next.SetResourceReference(Control.BackgroundProperty, "AccentBrush");
        _next.Foreground = Brushes.White;

        var buttons = new Grid { Margin = new Thickness(0, 16, 0, 0) };
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        buttons.ColumnDefinitions.Add(new ColumnDefinition());
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var left = new StackPanel { Orientation = Orientation.Horizontal };
        left.Children.Add(_count);
        left.Children.Add(_dots);
        var right = new StackPanel { Orientation = Orientation.Horizontal };
        right.Children.Add(_skip);
        right.Children.Add(_back);
        right.Children.Add(_next);
        Grid.SetColumn(right, 2);
        buttons.Children.Add(left);
        buttons.Children.Add(right);

        var body = new StackPanel();
        body.Children.Add(_title);
        body.Children.Add(_text);
        body.Children.Add(buttons);
        _card.Child = body;
        System.Windows.Automation.AutomationProperties.SetName(_card, "Tour");

        _root.Children.Add(_dim);
        _root.Children.Add(_ring);
        _root.Children.Add(_card);
        AddVisualChild(_root);

        if (adorned is FrameworkElement fe) fe.SizeChanged += OnSizeChanged;
        _window.PreviewKeyDown += OnKey;
    }

    /// <summary>Starts a tour over <paramref name="window"/>. <paramref name="finished"/> runs once when it ends or is skipped.</summary>
    public static bool Start(Window window, IReadOnlyList<TourStep> steps, Action finished)
    {
        var content = window.Content is AdornerDecorator d ? d.Child : window.Content as UIElement;
        if (content is not UIElement root || steps.Count == 0) return false;
        var layer = AdornerLayer.GetAdornerLayer(root);
        if (layer == null) return false;
        var tour = new TourOverlay(root, layer, window, steps, finished);
        layer.Add(tour);
        tour.Go(0);
        return true;
    }

    private static Button MakeButton(string text, Action click)
    {
        var b = new Button { Content = text, MinWidth = 64, Height = 28, Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(12, 0, 12, 0), Cursor = Cursors.Hand };
        b.Click += (_, _) => click();
        return b;
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape: Finish(); break;
            case Key.Right or Key.Enter or Key.Space: Go(_index + 1); break;
            case Key.Left: if (_index > 0) Go(_index - 1); break;
            default: return;
        }
        e.Handled = true;
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e) => Dispatcher.BeginInvoke(new Action(PlaceAll), System.Windows.Threading.DispatcherPriority.Loaded);

    private void Go(int index)
    {
        if (_closed) return;
        if (index >= _steps.Count) { Finish(); return; }
        _index = Math.Max(0, index);
        var step = _steps[_index];
        try { step.Prepare?.Invoke(); } catch { }

        _title.Text = step.Title;
        _text.Text = step.Text;
        _count.Text = $"{_index + 1} of {_steps.Count}";
        _back.Visibility = _index == 0 ? Visibility.Collapsed : Visibility.Visible;
        _next.Content = _index == _steps.Count - 1 ? "Finish" : _index == 0 ? "Show me" : "Next";
        _skip.Visibility = _index == _steps.Count - 1 ? Visibility.Collapsed : Visibility.Visible;
        _skip.SetResourceReference(Control.ForegroundProperty, "DimForegroundBrush");

        _dots.Children.Clear();
        for (int i = 0; i < _steps.Count; i++)
        {
            var dot = new Shapes.Ellipse { Width = 6, Height = 6, Margin = new Thickness(0, 0, 4, 0), Opacity = i == _index ? 1 : 0.35 };
            dot.SetResourceReference(Shapes.Shape.FillProperty, i == _index ? "AccentBrush" : "DimForegroundBrush");
            _dots.Children.Add(dot);
        }
        if (_steps.Count > 16) _dots.Visibility = Visibility.Collapsed;

        // Let a tab switch or panel change lay out before measuring the target.
        Dispatcher.BeginInvoke(new Action(PlaceAll), System.Windows.Threading.DispatcherPriority.Loaded);
        _next.Focus();
    }

    private void PlaceAll()
    {
        if (_closed) return;
        var size = AdornedElement.RenderSize;
        var full = new RectangleGeometry(new Rect(size));
        Rect? hole = TargetRect(_steps[_index].Target?.Invoke());

        if (hole is { } r)
        {
            r.Inflate(6, 6);
            r.Intersect(new Rect(size));
            _dim.Data = new CombinedGeometry(GeometryCombineMode.Exclude, full, new RectangleGeometry(r, 8, 8));
            _ring.Visibility = Visibility.Visible;
            _ring.Width = r.Width;
            _ring.Height = r.Height;
            Canvas.SetLeft(_ring, r.Left);
            Canvas.SetTop(_ring, r.Top);
        }
        else
        {
            _dim.Data = full;
            _ring.Visibility = Visibility.Collapsed;
        }

        _card.Measure(new Size(_card.Width, double.PositiveInfinity));
        double cw = _card.Width, ch = _card.DesiredSize.Height, gap = 14;
        Point p;
        if (hole is not { } t) p = new((size.Width - cw) / 2, (size.Height - ch) / 2);
        else if (t.Bottom + gap + ch <= size.Height) p = new(t.Left + 24, t.Bottom + gap);              // below
        else if (t.Top - gap - ch >= 0) p = new(t.Left + 24, t.Top - gap - ch);                          // above
        else if (t.Right + gap + cw <= size.Width) p = new(t.Right + gap, t.Top + 24);                  // right
        else if (t.Left - gap - cw >= 0) p = new(t.Left - gap - cw, t.Top + 24);                        // left
        else p = new(t.Left + (t.Width - cw) / 2, t.Top + (t.Height - ch) / 2);                         // inside
        Canvas.SetLeft(_card, Math.Clamp(p.X, 12, Math.Max(12, size.Width - cw - 12)));
        Canvas.SetTop(_card, Math.Clamp(p.Y, 12, Math.Max(12, size.Height - ch - 12)));
    }

    private Rect? TargetRect(FrameworkElement? target)
    {
        if (target == null || !target.IsVisible || target.ActualWidth < 4 || target.ActualHeight < 4) return null;
        try
        {
            if (!target.IsDescendantOf(AdornedElement)) return null;   // e.g. a panel floated in its own window
            return target.TransformToAncestor(AdornedElement).TransformBounds(new Rect(target.RenderSize));
        }
        catch { return null; }
    }

    private void Finish()
    {
        if (_closed) return;
        _closed = true;
        _window.PreviewKeyDown -= OnKey;
        if (AdornedElement is FrameworkElement fe) fe.SizeChanged -= OnSizeChanged;
        _layer.Remove(this);
        _finished();
    }

    protected override int VisualChildrenCount => 1;
    protected override Visual GetVisualChild(int index) => _root;

    protected override Size MeasureOverride(Size constraint)
    {
        _root.Measure(AdornedElement.RenderSize);
        return AdornedElement.RenderSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        _root.Arrange(new Rect(finalSize));
        return finalSize;
    }
}
