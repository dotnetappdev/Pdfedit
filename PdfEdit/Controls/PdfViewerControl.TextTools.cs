using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using PdfEdit.Models;
using PdfEdit.Services;

namespace PdfEdit.Controls;

/// <summary>
/// Acrobat Fill &amp; Sign behaviour for placed text and marks in the live view:
/// <list type="bullet">
/// <item>Add Text creates a real annotation immediately, so the mini toolbar (smaller, larger,
/// delete, rotate, character spacing, colours) is there while you type, like Acrobat.</item>
/// <item>The box auto-sizes to its text until it is resized by hand.</item>
/// <item>Character spacing ("VA") spreads the letters, e.g. to line up with comb boxes.</item>
/// <item>The Properties panel edits the selected text through <c>MainViewModel.AnnotationChanged</c>,
/// even after focus has moved to the panel.</item>
/// </list>
/// </summary>
public partial class PdfViewerControl
{
    // Every text box currently on the page, by annotation (rebuilt with the overlay).
    private readonly Dictionary<FreeTextAnnotation, TextBox> _annotationBoxes = new();
    // Letter-spaced rendering shown over a text box while it is not being edited.
    private readonly Dictionary<FreeTextAnnotation, TextBlock> _spacingOverlays = new();

    private Popup? _spacingPopup;
    private Slider? _spacingSlider;

    /// <summary>Display size factor for fonts and spacing (matches ApplyAnnotationFormatting).</summary>
    private double FontDisplayScale => Scale / RendererFactory.PointsToDips;

    private static bool IsQuarterTurn(double angle)
    {
        double a = ((angle % 360) + 360) % 360;
        return Math.Abs(a - 90) < 0.5 || Math.Abs(a - 270) < 0.5;
    }

    /// <summary>Hooks a freshly created annotation text box up to auto-size, spacing and the view model.</summary>
    private void RegisterAnnotationBox(FreeTextAnnotation ann, TextBox tb, bool isMark)
    {
        _annotationBoxes[ann] = tb;

        if (ann.AutoSize && !isMark) tb.TextWrapping = TextWrapping.NoWrap;

        tb.TextChanged += (_, _) =>
        {
            if (ann.AutoSize && !isMark) AutoFitAnnotation(ann, tb);
            UpdateSpacingOverlay(ann, tb);
            _vm?.NotifyAnnotationEdited(ann);
        };
        tb.GotKeyboardFocus += (_, _) => UpdateSpacingOverlay(ann, tb);
        tb.LostKeyboardFocus += (_, _) => UpdateSpacingOverlay(ann, tb);

        UpdateSpacingOverlay(ann, tb);
    }

    /// <summary>Clears the per-annotation lookups (the canvas is about to be rebuilt).</summary>
    private void ResetAnnotationBoxes()
    {
        _annotationBoxes.Clear();
        _spacingOverlays.Clear();
    }

    // ── Auto-size (Acrobat grows the box as you type) ─────────────────────────

    private void AutoFitAnnotation(FreeTextAnnotation ann, TextBox tb)
    {
        if (_vm?.Document == null) return;
        string text = string.IsNullOrEmpty(tb.Text) ? "M" : tb.Text;
        var lines = text.Replace("\r\n", "\n").Split('\n');

        var typeface = new Typeface(tb.FontFamily, tb.FontStyle, tb.FontWeight, tb.FontStretch);
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        double spacing = ann.CharacterSpacing * FontDisplayScale;
        double width = 0;
        foreach (var line in lines)
        {
            var ft = new FormattedText(line.Length == 0 ? " " : line, CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight, typeface, tb.FontSize, Brushes.Black, dpi);
            width = Math.Max(width, ft.WidthIncludingTrailingWhitespace + spacing * Math.Max(0, line.Length - 1));
        }
        double lineHeight = tb.FontSize * tb.FontFamily.LineSpacing;

        // A little slack so the caret never scrolls the text sideways.
        double w = Math.Max(width + tb.FontSize * 0.8 + 4, 16);
        double h = Math.Max(lineHeight * lines.Length + 4, lineHeight + 4);
        bool quarter = IsQuarterTurn(ann.RotationAngle);

        // Keep the top-left corner fixed on screen; in PDF space that means moving the bottom.
        double oldHeightPt = ann.Height;
        tb.Width = w;
        tb.Height = h;
        ann.Width = (quarter ? h : w) / Scale;
        ann.Height = (quarter ? w : h) / Scale;
        ann.Bottom -= ann.Height - oldHeightPt;

        if (_focusedAnnotationTb == tb) ShowAnnotationToolbar(tb);
    }

    // ── Character spacing ─────────────────────────────────────────────────────

    /// <summary>
    /// WPF text boxes cannot letter-space, so while the text is not being edited a spaced copy is
    /// drawn over it (the box's own text is made transparent). While typing, the plain text shows.
    /// </summary>
    private void UpdateSpacingOverlay(FreeTextAnnotation ann, TextBox tb)
    {
        bool want = ann.CharacterSpacing > 0.01 && !tb.IsKeyboardFocused && AnnotationCanvas.Children.Contains(tb);
        _spacingOverlays.TryGetValue(ann, out var overlay);

        if (!want)
        {
            if (overlay != null)
            {
                AnnotationCanvas.Children.Remove(overlay);
                _spacingOverlays.Remove(ann);
            }
            tb.Foreground = ParseBrush(ann.FontColor);
            return;
        }

        if (overlay == null)
        {
            overlay = new TextBlock { IsHitTestVisible = false };
            // Follow the text box wherever it goes (drag, resize, rotation).
            overlay.SetBinding(Canvas.LeftProperty, new Binding { Source = tb, Path = new PropertyPath(Canvas.LeftProperty) });
            overlay.SetBinding(Canvas.TopProperty, new Binding { Source = tb, Path = new PropertyPath(Canvas.TopProperty) });
            overlay.SetBinding(WidthProperty, new Binding(nameof(Width)) { Source = tb });
            overlay.SetBinding(HeightProperty, new Binding(nameof(Height)) { Source = tb });
            overlay.SetBinding(LayoutTransformProperty, new Binding(nameof(LayoutTransform)) { Source = tb });
            Panel.SetZIndex(overlay, Panel.GetZIndex(tb) + 1);
            AnnotationCanvas.Children.Add(overlay);
            _spacingOverlays[ann] = overlay;
        }

        overlay.FontFamily = tb.FontFamily;
        overlay.FontSize = tb.FontSize;
        overlay.FontWeight = tb.FontWeight;
        overlay.FontStyle = tb.FontStyle;
        overlay.TextDecorations = tb.TextDecorations;
        overlay.TextAlignment = tb.TextAlignment;
        overlay.Foreground = ParseBrush(ann.FontColor);
        overlay.Padding = new Thickness(tb.BorderThickness.Left + 2, tb.BorderThickness.Top, 0, 0);
        overlay.TextWrapping = tb.TextWrapping;

        double gap = ann.CharacterSpacing * FontDisplayScale;
        overlay.Inlines.Clear();
        string text = ann.ForceUpperCase ? tb.Text.ToUpperInvariant() : tb.Text;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '\r') continue;
            if (c == '\n') { overlay.Inlines.Add(new LineBreak()); continue; }
            overlay.Inlines.Add(new Run(c.ToString()));
            bool lastInLine = i == text.Length - 1 || text[i + 1] is '\n' or '\r';
            if (!lastInLine)
                overlay.Inlines.Add(new InlineUIContainer(new Border { Width = gap, Height = 1 })
                    { BaselineAlignment = BaselineAlignment.Baseline });
        }

        tb.Foreground = Brushes.Transparent;
    }

    private void ToggleSpacingPopup()
    {
        var ann = _focusedAnnotation;
        if (ann == null || _annotToolbar == null) return;

        if (_spacingPopup == null)
        {
            _spacingSlider = new Slider
            {
                Minimum = 0, Maximum = 30, Width = 160, SmallChange = 0.5, LargeChange = 2,
                IsSnapToTickEnabled = false, Margin = new Thickness(0, 4, 0, 0),
                ToolTip = "Character spacing (points)",
            };
            var label = new TextBlock { Foreground = Brushes.White, FontSize = 11 };
            _spacingSlider.ValueChanged += (_, e) =>
            {
                label.Text = $"Character spacing: {e.NewValue:0.#} pt";
                if (_vm?.SelectedAnnotation != null) _vm.SelectedAnnotationCharSpacing = e.NewValue;
            };
            var reset = new Button { Content = "Reset", FontSize = 11, Margin = new Thickness(0, 6, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
            reset.Click += (_, _) => _spacingSlider!.Value = 0;

            _spacingPopup = new Popup
            {
                StaysOpen = false,
                AllowsTransparency = true,
                Placement = PlacementMode.Bottom,
                Child = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(35, 35, 35)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(70, 70, 70)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(10, 8, 10, 8),
                    Child = new StackPanel { Children = { label, _spacingSlider, reset } },
                },
            };
        }

        _spacingSlider!.Value = ann.CharacterSpacing;
        // Anchor to the text itself: the toolbar hides when the slider takes focus.
        _spacingPopup.PlacementTarget = _focusedAnnotationTb;
        _spacingPopup.IsOpen = !_spacingPopup.IsOpen;
    }

    // ── Rotate ────────────────────────────────────────────────────────────────

    private void RotateFocusedAnnotation()
    {
        if (_vm?.SelectedAnnotation == null) return;
        _vm.SelectedAnnotationRotation = (_vm.SelectedAnnotationRotation + 90) % 360;
    }

    // ── Properties panel → page ───────────────────────────────────────────────

    private void OnAnnotationChanged(FreeTextAnnotation ann) => RefreshAnnotationVisual(ann);

    /// <summary>Redraws one annotation's text box from its model (text, style, position, rotation, lock).</summary>
    private void RefreshAnnotationVisual(FreeTextAnnotation ann)
    {
        if (_vm?.Document == null || !_annotationBoxes.TryGetValue(ann, out var tb)) return;
        int pageNum = _vm.CurrentPageIndex + 1;
        if (pageNum < 1 || pageNum > _vm.Document.PageSizes.Count || ann.PageNumber != pageNum) return;
        double pageH = _vm.Document.PageSizes[pageNum - 1].Height;

        string shown = ann.ForceUpperCase ? ann.Text.ToUpperInvariant() : ann.Text;
        if (tb.Text != shown) tb.Text = shown;

        ApplyAnnotationFormatting(ann, tb);

        bool quarter = IsQuarterTurn(ann.RotationAngle);
        double w = ann.Width * Scale, h = ann.Height * Scale;
        tb.Width = quarter ? h : w;
        tb.Height = quarter ? w : h;
        Canvas.SetLeft(tb, ann.Left * Scale);
        Canvas.SetTop(tb, (pageH - ann.Bottom - ann.Height) * Scale);
        tb.LayoutTransform = ann.RotationAngle == 0 ? Transform.Identity : new RotateTransform(ann.RotationAngle);
        tb.TextWrapping = IsMarkGlyph(ann.Text) || ann.AutoSize ? TextWrapping.NoWrap : TextWrapping.Wrap;
        tb.IsReadOnly = ann.IsLocked || IsMarkGlyph(ann.Text);

        if (ann.AutoSize && !IsMarkGlyph(ann.Text)) AutoFitAnnotation(ann, tb);
        UpdateSpacingOverlay(ann, tb);
        if (_focusedAnnotationTb == tb) ShowAnnotationToolbar(tb);
    }
}
