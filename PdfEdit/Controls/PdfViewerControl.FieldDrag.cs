using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using PdfEdit.Models;

namespace PdfEdit.Controls;

/// <summary>
/// Move and resize form fields while filling them in (no need to switch to Edit Fields): hovering
/// a field shows a ✥ grip beside it to drag the field and a corner handle to resize it — the same
/// way placed text is moved with its drag grip. Each move / resize is one undo step.
/// </summary>
public partial class PdfViewerControl
{
    private Thumb? _fieldMoveGrip, _fieldSizeGrip;
    private FormFieldInfo? _gripField;
    private FrameworkElement? _gripCtrl;
    private Rect _gripRect;          // the field on screen (canvas DIPs), updated while dragging
    private Rect _gripStartRect;     // where the drag started
    private bool _gripDragging;
    private DispatcherTimer? _gripHideTimer;

    private const double GripSize = 16, SizeGripSize = 10;

    /// <summary>Called on every overlay rebuild: the canvas was cleared, so the grips are gone.</summary>
    private void ResetFieldGrips()
    {
        _fieldMoveGrip = _fieldSizeGrip = null;
        _gripField = null;
        _gripCtrl = null;
        _gripDragging = false;
        _gripHideTimer?.Stop();
    }

    /// <summary>Shows the move / resize grips when the pointer is over <paramref name="ctrl"/>.</summary>
    private void AttachFieldGrips(FormFieldInfo field, FrameworkElement ctrl, Rect rect)
    {
        ctrl.MouseEnter += (_, _) =>
        {
            if (_gripDragging) return;
            ShowFieldGrips(field, ctrl, rect);
        };
        ctrl.MouseLeave += (_, _) => ScheduleHideFieldGrips();
    }

    private void ShowFieldGrips(FormFieldInfo field, FrameworkElement ctrl, Rect rect)
    {
        EnsureFieldGrips();
        _gripHideTimer?.Stop();
        _gripField = field;
        _gripCtrl = ctrl;
        _gripRect = rect;
        PositionFieldGrips();
        _fieldMoveGrip!.Visibility = Visibility.Visible;
        _fieldSizeGrip!.Visibility = Visibility.Visible;
        _fieldMoveGrip.ToolTip = $"Drag to move \"{field.DisplayName}\"";
    }

    private void ScheduleHideFieldGrips()
    {
        if (_gripHideTimer == null)
        {
            _gripHideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
            _gripHideTimer.Tick += (_, _) =>
            {
                // Keep them while the pointer is on the field or a grip (moving between them).
                if (_gripDragging || _gripCtrl?.IsMouseOver == true
                    || _fieldMoveGrip?.IsMouseOver == true || _fieldSizeGrip?.IsMouseOver == true)
                    return;
                _gripHideTimer!.Stop();
                if (_fieldMoveGrip != null) _fieldMoveGrip.Visibility = Visibility.Collapsed;
                if (_fieldSizeGrip != null) _fieldSizeGrip.Visibility = Visibility.Collapsed;
            };
        }
        _gripHideTimer.Start();
    }

    private void PositionFieldGrips()
    {
        if (_fieldMoveGrip == null || _fieldSizeGrip == null) return;
        // Grip to the left of the field (above it when the field touches the page's left edge).
        bool roomLeft = _gripRect.X >= GripSize + 2;
        Canvas.SetLeft(_fieldMoveGrip, roomLeft ? _gripRect.X - GripSize - 2 : _gripRect.X);
        Canvas.SetTop(_fieldMoveGrip, roomLeft ? _gripRect.Y : Math.Max(0, _gripRect.Y - GripSize - 2));
        Canvas.SetLeft(_fieldSizeGrip, _gripRect.Right - SizeGripSize / 2);
        Canvas.SetTop(_fieldSizeGrip, _gripRect.Bottom - SizeGripSize / 2);
    }

    private void EnsureFieldGrips()
    {
        if (_fieldMoveGrip != null) return;

        _fieldMoveGrip = new Thumb
        {
            Width = GripSize, Height = GripSize,
            Cursor = Cursors.SizeAll,
            Template = GripTemplate(isMove: true),
            Visibility = Visibility.Collapsed,
        };
        System.Windows.Automation.AutomationProperties.SetName(_fieldMoveGrip, "Move field");
        _fieldMoveGrip.DragStarted += (_, _) => BeginGripDrag();
        _fieldMoveGrip.DragDelta += (_, e) =>
        {
            _gripRect.Offset(e.HorizontalChange, e.VerticalChange);
            ClampGripRectToPage();
            ApplyGripRectToControl();
        };
        _fieldMoveGrip.DragCompleted += (_, _) => CommitGripDrag("Moved field");

        _fieldSizeGrip = new Thumb
        {
            Width = SizeGripSize, Height = SizeGripSize,
            Cursor = Cursors.SizeNWSE,
            Template = GripTemplate(isMove: false),
            Visibility = Visibility.Collapsed,
            ToolTip = "Drag to resize the field",
        };
        System.Windows.Automation.AutomationProperties.SetName(_fieldSizeGrip, "Resize field");
        _fieldSizeGrip.DragStarted += (_, _) => BeginGripDrag();
        _fieldSizeGrip.DragDelta += (_, e) =>
        {
            double min = 6 * Scale;
            _gripRect.Width = Math.Max(min, _gripRect.Width + e.HorizontalChange);
            _gripRect.Height = Math.Max(min, _gripRect.Height + e.VerticalChange);
            ClampGripRectToPage();
            ApplyGripRectToControl();
        };
        _fieldSizeGrip.DragCompleted += (_, _) => CommitGripDrag("Resized field");

        Panel.SetZIndex(_fieldMoveGrip, 9998);
        Panel.SetZIndex(_fieldSizeGrip, 9998);
        FieldOverlayCanvas.Children.Add(_fieldMoveGrip);
        FieldOverlayCanvas.Children.Add(_fieldSizeGrip);
        _fieldMoveGrip.MouseLeave += (_, _) => ScheduleHideFieldGrips();
        _fieldSizeGrip.MouseLeave += (_, _) => ScheduleHideFieldGrips();
    }

    private static ControlTemplate GripTemplate(bool isMove)
    {
        var tpl = new ControlTemplate(typeof(Thumb));
        var bd = new FrameworkElementFactory(typeof(Border));
        bd.SetValue(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(0, 120, 215)));
        bd.SetValue(Border.BorderBrushProperty, Brushes.White);
        bd.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        bd.SetValue(Border.CornerRadiusProperty, new CornerRadius(isMove ? 3 : 1));
        if (isMove)
        {
            var glyph = new FrameworkElementFactory(typeof(TextBlock));
            glyph.SetValue(TextBlock.TextProperty, "✥");
            glyph.SetValue(TextBlock.FontSizeProperty, 11.0);
            glyph.SetValue(TextBlock.ForegroundProperty, Brushes.White);
            glyph.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            glyph.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            bd.AppendChild(glyph);
        }
        tpl.VisualTree = bd;
        return tpl;
    }

    private void BeginGripDrag()
    {
        _gripDragging = true;
        _gripStartRect = _gripRect;
        _gripHideTimer?.Stop();
    }

    private void ClampGripRectToPage()
    {
        double pw = FieldOverlayCanvas.Width > 0 ? FieldOverlayCanvas.Width : FieldOverlayCanvas.ActualWidth;
        double ph = FieldOverlayCanvas.Height > 0 ? FieldOverlayCanvas.Height : FieldOverlayCanvas.ActualHeight;
        if (pw <= 0 || ph <= 0) return;
        _gripRect.Width = Math.Min(_gripRect.Width, pw);
        _gripRect.Height = Math.Min(_gripRect.Height, ph);
        _gripRect.X = Math.Clamp(_gripRect.X, 0, pw - _gripRect.Width);
        _gripRect.Y = Math.Clamp(_gripRect.Y, 0, ph - _gripRect.Height);
    }

    /// <summary>Live preview: the field control follows the grip while dragging.</summary>
    private void ApplyGripRectToControl()
    {
        if (_gripCtrl != null)
        {
            Canvas.SetLeft(_gripCtrl, _gripRect.X);
            Canvas.SetTop(_gripCtrl, _gripRect.Y);
            // Vertical-text fields are laid out rotated; their size is applied on commit.
            if (_gripCtrl.LayoutTransform is not RotateTransform { Angle: not 0 })
            {
                _gripCtrl.Width = _gripRect.Width;
                _gripCtrl.Height = _gripRect.Height;
            }
        }
        PositionFieldGrips();
    }

    private void CommitGripDrag(string description)
    {
        _gripDragging = false;
        if (_vm?.Document == null || _gripField is not { } field) return;
        var r = _gripRect;
        if (Math.Abs(r.X - _gripStartRect.X) < 0.5 && Math.Abs(r.Y - _gripStartRect.Y) < 0.5
            && Math.Abs(r.Width - _gripStartRect.Width) < 0.5 && Math.Abs(r.Height - _gripStartRect.Height) < 0.5)
            return;

        double pageH = _vm.Document.PageSizes[_vm.CurrentPageIndex].Height;
        double wPt = r.Width / Scale, hPt = r.Height / Scale;
        var bounds = new FieldBounds(
            Math.Round(r.X / Scale, 2),
            Math.Round(pageH - r.Y / Scale - hPt, 2),
            Math.Round(wPt, 2),
            Math.Round(hPt, 2));
        // Rebuilds the page so the value, highlight and date picker all follow; Ctrl+Z undoes it.
        _vm.SetFieldBoundsBatch(new[] { (field, bounds) }, description);
    }
}
