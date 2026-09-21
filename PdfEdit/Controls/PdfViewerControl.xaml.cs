using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using PdfEdit.Models;
using PdfEdit.Services;
using PdfEdit.ViewModels;

namespace PdfEdit.Controls;

public partial class PdfViewerControl : UserControl
{
    private MainViewModel? _vm;

    // Points-to-pixels scale at current zoom:  pts * Scale = pixels
    private double Scale => PdfRenderService.PointsToDips * (_vm?.Zoom ?? 1.0);

    // Pan / drag state for Hand tool
    private bool _isPanning;
    private Point _panStart;
    private double _scrollHStart, _scrollVStart;

    public PdfViewerControl()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        MouseWheel += OnMouseWheel;
        MouseLeftButtonDown += OnMouseLeftButtonDown;
        MouseLeftButtonUp += OnMouseLeftButtonUp;
        MouseMove += OnMouseMove;
        AllowDrop = true;
    }

    // ── Setup ────────────────────────────────────────────────────────────────

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_vm != null)
        {
            _vm.PageChanged -= RefreshPage;
            _vm.DocumentLoaded -= OnDocumentLoaded;
        }
        _vm = DataContext as MainViewModel;
        if (_vm != null)
        {
            _vm.PageChanged += RefreshPage;
            _vm.DocumentLoaded += OnDocumentLoaded;
        }
    }

    private void OnDocumentLoaded()
    {
        PlaceholderPanel.Visibility = Visibility.Collapsed;
        PdfScrollViewer.Visibility = Visibility.Visible;
        RefreshPage();
    }

    // ── Page rendering ───────────────────────────────────────────────────────

    public async void RefreshPage()
    {
        if (_vm?.Document == null) return;

        ShowLoading(true);
        try
        {
            var bmp = await _vm.RenderService.RenderPageAsync(_vm.CurrentPageIndex, _vm.Zoom);
            PageImage.Source = bmp;

            // Set canvas sizes to match rendered image
            double w = bmp.PixelWidth;
            double h = bmp.PixelHeight;
            FieldOverlayCanvas.Width = w;
            FieldOverlayCanvas.Height = h;
            HighlightCanvas.Width = w;
            HighlightCanvas.Height = h;

            _vm.RefreshCurrentPageFields();
            BuildFieldOverlay(_vm.CurrentPageFields, _vm.HighlightFields);
        }
        catch (Exception ex)
        {
            _vm.StatusText = $"Render error: {ex.Message}";
        }
        finally
        {
            ShowLoading(false);
        }
    }

    // ── Form field overlay ───────────────────────────────────────────────────

    private void BuildFieldOverlay(IEnumerable<FormFieldInfo> fields, bool highlight)
    {
        FieldOverlayCanvas.Children.Clear();
        HighlightCanvas.Children.Clear();

        if (_vm?.Document == null) return;

        int pageNum = _vm.CurrentPageIndex + 1;
        if (pageNum < 1 || pageNum > _vm.Document.PageSizes.Count) return;

        double pageHeightPts = _vm.Document.PageSizes[pageNum - 1].Height;

        foreach (var field in fields)
        {
            double x = field.Left * Scale;
            double y = (pageHeightPts - field.Bottom - field.Height) * Scale;
            double w = field.Width * Scale;
            double h = field.Height * Scale;

            if (highlight)
                AddHighlight(x, y, w, h, field.IsRequired);

            if (!field.IsReadOnly)
                AddFieldControl(field, x, y, w, h);
        }
    }

    private void AddHighlight(double x, double y, double w, double h, bool isRequired)
    {
        var rect = new Rectangle
        {
            Width = w,
            Height = h,
            Fill = isRequired
                ? new SolidColorBrush(Color.FromArgb(40, 255, 80, 80))
                : new SolidColorBrush(Color.FromArgb(40, 30, 130, 255)),
            Stroke = isRequired
                ? new SolidColorBrush(Color.FromArgb(120, 255, 80, 80))
                : new SolidColorBrush(Color.FromArgb(120, 30, 130, 255)),
            StrokeThickness = 1,
            IsHitTestVisible = false
        };
        Canvas.SetLeft(rect, x);
        Canvas.SetTop(rect, y);
        HighlightCanvas.Children.Add(rect);
    }

    private void AddFieldControl(FormFieldInfo field, double x, double y, double w, double h)
    {
        UIElement? ctrl = field.FieldType switch
        {
            FieldType.Text => BuildTextBox(field, w, h),
            FieldType.Checkbox => BuildCheckBox(field, w, h),
            FieldType.RadioButton => BuildRadioButton(field, w, h),
            FieldType.ComboBox => BuildComboBox(field, w, h),
            FieldType.ListBox => BuildListBox(field, w, h),
            FieldType.Signature => BuildSignatureBox(field, w, h),
            _ => null
        };

        if (ctrl == null) return;

        Canvas.SetLeft(ctrl, x);
        Canvas.SetTop(ctrl, y);
        FieldOverlayCanvas.Children.Add(ctrl);
    }

    private TextBox BuildTextBox(FormFieldInfo field, double w, double h)
    {
        var tb = new TextBox
        {
            Width = w,
            Height = h,
            Text = _vm!.FieldValues.TryGetValue(field.Name, out var v) ? v : field.Value,
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            FontSize = Math.Max(8, h * 0.6),
            VerticalContentAlignment = VerticalAlignment.Center,
            AcceptsReturn = field.IsMultiline,
            TextWrapping = field.IsMultiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
            Padding = new Thickness(2, 0, 2, 0),
            ToolTip = string.IsNullOrEmpty(field.Tooltip) ? field.Name : field.Tooltip,
        };
        if (field.IsPassword) tb.Tag = "password";

        tb.TextChanged += (_, _) => _vm!.UpdateFieldValue(field.Name, tb.Text);
        tb.GotFocus += (_, _) => { _vm!.SelectedField = field; HighlightActiveField(tb); };
        return tb;
    }

    private CheckBox BuildCheckBox(FormFieldInfo field, double w, double h)
    {
        var cb = new CheckBox
        {
            Width = w,
            Height = h,
            IsChecked = field.Value is "Yes" or "true" or "On" or "1",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = string.IsNullOrEmpty(field.Tooltip) ? field.Name : field.Tooltip,
        };
        cb.Checked += (_, _) => _vm!.UpdateFieldValue(field.Name, "Yes");
        cb.Unchecked += (_, _) => _vm!.UpdateFieldValue(field.Name, "Off");
        cb.GotFocus += (_, _) => _vm!.SelectedField = field;
        return cb;
    }

    private RadioButton BuildRadioButton(FormFieldInfo field, double w, double h)
    {
        var rb = new RadioButton
        {
            Width = w,
            Height = h,
            GroupName = field.RadioGroup ?? field.Name,
            IsChecked = field.Value == "Yes",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = field.Name,
        };
        rb.Checked += (_, _) => _vm!.UpdateFieldValue(field.Name, "Yes");
        rb.Unchecked += (_, _) => _vm!.UpdateFieldValue(field.Name, "Off");
        rb.GotFocus += (_, _) => _vm!.SelectedField = field;
        return rb;
    }

    private ComboBox BuildComboBox(FormFieldInfo field, double w, double h)
    {
        var cb = new ComboBox
        {
            Width = w,
            Height = h,
            FontSize = Math.Max(8, h * 0.55),
            ToolTip = string.IsNullOrEmpty(field.Tooltip) ? field.Name : field.Tooltip,
        };
        foreach (var opt in field.Options) cb.Items.Add(opt);
        var current = _vm!.FieldValues.TryGetValue(field.Name, out var cv) ? cv : field.Value;
        cb.SelectedItem = current;
        if (cb.SelectedItem == null && field.Options.Count > 0) cb.SelectedIndex = 0;

        cb.SelectionChanged += (_, _) =>
        {
            if (cb.SelectedItem is string val) _vm.UpdateFieldValue(field.Name, val);
        };
        cb.GotFocus += (_, _) => _vm.SelectedField = field;
        return cb;
    }

    private ListBox BuildListBox(FormFieldInfo field, double w, double h)
    {
        var lb = new ListBox
        {
            Width = w,
            Height = h,
            FontSize = Math.Max(8, h * 0.4),
            ToolTip = field.Name,
        };
        foreach (var opt in field.Options) lb.Items.Add(opt);
        var current = _vm!.FieldValues.TryGetValue(field.Name, out var cv) ? cv : field.Value;
        lb.SelectedItem = current;

        lb.SelectionChanged += (_, _) =>
        {
            if (lb.SelectedItem is string val) _vm.UpdateFieldValue(field.Name, val);
        };
        lb.GotFocus += (_, _) => _vm.SelectedField = field;
        return lb;
    }

    private Border BuildSignatureBox(FormFieldInfo field, double w, double h)
    {
        var border = new Border
        {
            Width = w,
            Height = h,
            BorderBrush = new SolidColorBrush(Color.FromRgb(100, 100, 200)),
            BorderThickness = new Thickness(1),
            Background = new SolidColorBrush(Color.FromArgb(20, 100, 100, 200)),
            ToolTip = $"Signature field: {field.Name}",
            Cursor = Cursors.Pen,
        };
        var tb = new TextBlock
        {
            Text = "Click to Sign",
            Foreground = new SolidColorBrush(Color.FromRgb(100, 100, 200)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = Math.Max(8, h * 0.35),
            FontStyle = FontStyles.Italic,
        };
        border.Child = tb;
        border.MouseLeftButtonDown += (_, _) =>
        {
            _vm!.SelectedField = field;
            MessageBox.Show("Digital signature support coming soon.\nFor now, type your name to indicate signing.", "Signature", MessageBoxButton.OK, MessageBoxImage.Information);
        };
        return border;
    }

    private void HighlightActiveField(UIElement ctrl)
    {
        // Give focused TextBox a visible cue
        if (ctrl is TextBox tb)
        {
            tb.Background = new SolidColorBrush(Color.FromArgb(30, 255, 255, 100));
            tb.BorderBrush = new SolidColorBrush(Color.FromRgb(0, 120, 215));
            tb.BorderThickness = new Thickness(1);
            tb.LostFocus += (_, _) =>
            {
                tb.Background = Brushes.Transparent;
                tb.BorderBrush = Brushes.Transparent;
                tb.BorderThickness = new Thickness(0);
            };
        }
    }

    // ── Mouse interaction (Hand / Zoom tools) ─────────────────────────────────

    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            _vm!.Zoom += e.Delta > 0 ? 0.1 : -0.1;
            e.Handled = true;
        }
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_vm?.ActiveTool == ActiveTool.Hand)
        {
            _isPanning = true;
            _panStart = e.GetPosition(this);
            _scrollHStart = PdfScrollViewer.HorizontalOffset;
            _scrollVStart = PdfScrollViewer.VerticalOffset;
            CaptureMouse();
            Cursor = Cursors.SizeAll;
        }
    }

    private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isPanning)
        {
            _isPanning = false;
            ReleaseMouseCapture();
            Cursor = Cursors.Arrow;
        }
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_isPanning && e.LeftButton == MouseButtonState.Pressed)
        {
            var pos = e.GetPosition(this);
            double dx = _panStart.X - pos.X;
            double dy = _panStart.Y - pos.Y;
            PdfScrollViewer.ScrollToHorizontalOffset(_scrollHStart + dx);
            PdfScrollViewer.ScrollToVerticalOffset(_scrollVStart + dy);
        }
    }

    // ── Drag & Drop ───────────────────────────────────────────────────────────

    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            var pdf = files.FirstOrDefault(f => f.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase));
            if (pdf != null && _vm != null)
                await _vm.OpenFileAsync(pdf);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void ShowLoading(bool show)
    {
        LoadingOverlay.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    }
}
