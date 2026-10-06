using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using PdfEdit.Services;
using Shapes = System.Windows.Shapes;

namespace PdfEdit.Controls;

/// <summary>One stop on the tour: what to say, what to point at, and anything to do first (open a tab …).</summary>
public sealed record TourStep(string Title, string Text, Func<FrameworkElement?>? Target = null, Action? Prepare = null);

/// <summary>
/// A guided tour drawn over the main window: everything is dimmed except the part being
/// explained, which gets an accent outline and a card beside it with Back / Next / Skip / Close
/// and a Read aloud toggle. Keys: → or Enter for next, ← for back, R read aloud, Esc to close.
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
    private readonly Border _card = new() { Width = 480, CornerRadius = new CornerRadius(10), Padding = new Thickness(22, 18, 22, 18), BorderThickness = new Thickness(1) };
    private readonly TextBlock _title = new() { FontSize = 18, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _text = new() { FontSize = 14, TextWrapping = TextWrapping.Wrap, LineHeight = 22, Margin = new Thickness(0, 10, 0, 0) };
    private readonly TextBlock _count = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
    private readonly WrapPanel _dots = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
    private readonly Button _back, _next, _skip, _close, _speak;
    private int _index;
    private bool _closed;

    /// <summary>Whether each step is read out. Remembered for the rest of the session.</summary>
    private static bool? _readAloud;
    private static bool ReadAloud
    {
        get => _readAloud ??= AppSettings.Current.NarrateAnnouncements || AppSettings.Current.NarrateFocus;
        set => _readAloud = value;
    }

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

        _back = MakeButton("Back", () => Go(_index - 1), "Previous step");
        _next = MakeButton("Next", () => Go(_index + 1), "Next step");
        _next.SetResourceReference(StyleProperty, "ModernButton");
        _next.Padding = new Thickness(16, 0, 16, 0);
        _skip = MakeButton("Skip tour", Finish, "Skip the tour");
        _skip.Margin = new Thickness(0);
        _skip.Background = Brushes.Transparent;
        _skip.BorderThickness = new Thickness(0);
        _skip.SetResourceReference(Control.ForegroundProperty, "DimForegroundBrush");

        _close = MakeIconButton("\uE711", "Close the tour", Finish);
        _speak = MakeIconButton("\uE767", "Read aloud", ToggleReadAloud);

        // Header: title, then read-aloud and close at the top right.
        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(_speak, 1);
        Grid.SetColumn(_close, 2);
        header.Children.Add(_title);
        header.Children.Add(_speak);
        header.Children.Add(_close);

        // Progress on its own row so it never squeezes the buttons.
        var progress = new DockPanel { Margin = new Thickness(0, 16, 0, 0), LastChildFill = true };
        DockPanel.SetDock(_count, Dock.Left);
        progress.Children.Add(_count);
        progress.Children.Add(_dots);

        var buttons = new DockPanel { Margin = new Thickness(0, 14, 0, 0), LastChildFill = false };
        DockPanel.SetDock(_skip, Dock.Left);
        DockPanel.SetDock(_next, Dock.Right);
        DockPanel.SetDock(_back, Dock.Right);
        buttons.Children.Add(_skip);
        buttons.Children.Add(_next);
        buttons.Children.Add(_back);

        var body = new StackPanel();
        body.Children.Add(header);
        body.Children.Add(_text);
        body.Children.Add(progress);
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

    private static Button MakeButton(string text, Action click, string name)
    {
        var b = new Button { Content = text, MinWidth = 88, Height = 34, FontSize = 13, Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(14, 0, 14, 0), Cursor = Cursors.Hand };
        System.Windows.Automation.AutomationProperties.SetName(b, name);
        b.Click += (_, _) => click();
        return b;
    }

    private static Button MakeIconButton(string glyph, string name, Action click)
    {
        var b = new Button
        {
            Content = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 14,
            Width = 34, Height = 34, Padding = new Thickness(0), Margin = new Thickness(6, 0, 0, 0),
            Background = Brushes.Transparent, BorderThickness = new Thickness(0), Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Top, ToolTip = name,
        };
        System.Windows.Automation.AutomationProperties.SetName(b, name);
        b.Click += (_, _) => click();
        return b;
    }

    private void ToggleReadAloud()
    {
        ReadAloud = !ReadAloud;
        UpdateSpeakButton();
        if (ReadAloud) SpeakStep(); else NarrationService.Stop();
    }

    private void UpdateSpeakButton()
    {
        _speak.Content = ReadAloud ? "\uE767" : "\uE74F";   // Volume / Mute
        string tip = ReadAloud ? "Read aloud is on: click to stop (R)" : "Read each step aloud (R)";
        _speak.ToolTip = tip;
        System.Windows.Automation.AutomationProperties.SetName(_speak, tip);
        if (ReadAloud) _speak.SetResourceReference(Control.ForegroundProperty, "AccentBrush");
        else _speak.SetResourceReference(Control.ForegroundProperty, "DimForegroundBrush");
    }

    private void SpeakStep()
    {
        if (!ReadAloud || _closed) return;
        var step = _steps[_index];
        NarrationService.Speak($"{step.Title}. {step.Text} Step {_index + 1} of {_steps.Count}.", evenIfQuiet: true);
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape: Finish(); break;
            case Key.Enter or Key.Space when Keyboard.FocusedElement is Button b && b != _next && b.IsDescendantOf(_card):
                return;   // let the focused Back / Skip / Close / Read aloud button handle it
            case Key.Right or Key.Enter or Key.Space: Go(_index + 1); break;
            case Key.Left: if (_index > 0) Go(_index - 1); break;
            case Key.R: ToggleReadAloud(); break;
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
        _count.Text = $"Step {_index + 1} of {_steps.Count}";
        _back.IsEnabled = _index > 0;   // always shown so Back / Next stay in the same place
        _next.Content = _index == _steps.Count - 1 ? "Finish" : "Next";
        _skip.Visibility = _index == _steps.Count - 1 ? Visibility.Collapsed : Visibility.Visible;
        _close.SetResourceReference(Control.ForegroundProperty, "DimForegroundBrush");
        UpdateSpeakButton();

        _dots.Children.Clear();
        for (int i = 0; i < _steps.Count; i++)
        {
            var dot = new Shapes.Ellipse { Width = 7, Height = 7, Margin = new Thickness(0, 2, 5, 2), Opacity = i == _index ? 1 : 0.35 };
            dot.SetResourceReference(Shapes.Shape.FillProperty, i == _index ? "AccentBrush" : "DimForegroundBrush");
            _dots.Children.Add(dot);
        }
        if (_steps.Count > 16) _dots.Visibility = Visibility.Collapsed;

        // Let a tab switch or panel change lay out before measuring the target.
        Dispatcher.BeginInvoke(new Action(PlaceAll), System.Windows.Threading.DispatcherPriority.Loaded);
        _next.Focus();
        SpeakStep();   // after Focus, so it isn't cut off by the focus narration
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
        if (ReadAloud) NarrationService.Stop();
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
