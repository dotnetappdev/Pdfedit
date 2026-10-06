using System.Windows.Input;
using System.Windows.Media;

namespace PdfEdit.Controls;

/// <summary>
/// Auto-scroll (View → Auto Scroll, like PDFgear's): the page moves up at a steady speed, turns to
/// the next page at the bottom and stops at the end of the document. Esc, a click or the wheel stops it.
/// </summary>
public partial class PdfViewerControl
{
    private TimeSpan _lastAutoScrollTick;
    private double _autoScrollCarry;   // fractional pixels, and "reading time" on pages that fit the window
    private DateTime _autoScrollPauseUntil;
    private System.Windows.Window? _autoScrollWindow;

    private void SyncAutoScroll()
    {
        CompositionTarget.Rendering -= OnAutoScrollFrame;
        PreviewMouseDown -= StopAutoScrollOnInput;
        if (_autoScrollWindow != null) { _autoScrollWindow.PreviewKeyDown -= StopAutoScrollOnKey; _autoScrollWindow = null; }
        if (_vm?.IsAutoScrolling != true) return;
        _lastAutoScrollTick = TimeSpan.Zero;
        _autoScrollCarry = 0;
        _autoScrollPauseUntil = DateTime.MinValue;
        CompositionTarget.Rendering += OnAutoScrollFrame;
        PreviewMouseDown += StopAutoScrollOnInput;
        // Keys work wherever focus is (e.g. still on the ribbon button), except while typing.
        _autoScrollWindow = System.Windows.Window.GetWindow(this);
        if (_autoScrollWindow != null) _autoScrollWindow.PreviewKeyDown += StopAutoScrollOnKey;
    }

    private void StopAutoScrollOnInput(object sender, MouseButtonEventArgs e)
    {
        if (_vm != null) _vm.IsAutoScrolling = false;
    }

    private void StopAutoScrollOnKey(object sender, KeyEventArgs e)
    {
        if (_vm == null || IsTextInputFocused()) return;
        switch (e.Key)
        {
            case Key.Escape or Key.Space: _vm.IsAutoScrolling = false; e.Handled = true; break;
            case Key.Up or Key.Add or Key.OemPlus: _vm.AutoScrollSpeed *= 1.25; e.Handled = true; break;
            case Key.Down or Key.Subtract or Key.OemMinus: _vm.AutoScrollSpeed /= 1.25; e.Handled = true; break;
        }
    }

    private void OnAutoScrollFrame(object? sender, EventArgs e)
    {
        if (_vm is not { IsAutoScrolling: true } vm || vm.Document == null) { SyncAutoScroll(); return; }
        var now = ((RenderingEventArgs)e).RenderingTime;
        double dt = _lastAutoScrollTick == TimeSpan.Zero ? 0 : Math.Min(0.1, (now - _lastAutoScrollTick).TotalSeconds);
        _lastAutoScrollTick = now;
        if (dt <= 0 || DateTime.UtcNow < _autoScrollPauseUntil) return;

        var sv = PdfScrollViewer;
        double step = vm.AutoScrollSpeed * dt;
        bool atBottom;
        if (sv.ScrollableHeight > 1)
        {
            _autoScrollCarry += step;
            if (_autoScrollCarry >= 1)
            {
                double whole = Math.Floor(_autoScrollCarry);
                _autoScrollCarry -= whole;
                sv.ScrollToVerticalOffset(Math.Min(sv.ScrollableHeight, sv.VerticalOffset + whole));
            }
            atBottom = sv.VerticalOffset >= sv.ScrollableHeight - 0.5;
        }
        else
        {
            // The whole page fits: give it as long as scrolling its height would take.
            _autoScrollCarry += step;
            atBottom = _autoScrollCarry >= Math.Max(200, sv.ViewportHeight);
        }
        if (!atBottom) return;

        if (vm.CurrentPageIndex >= vm.PageCount - 1)
        {
            vm.IsAutoScrolling = false;
            vm.StatusText = "Auto-scroll reached the end of the document.";
            return;
        }
        vm.CurrentPageIndex++;
        _autoScrollCarry = 0;
        _autoScrollPauseUntil = DateTime.UtcNow.AddMilliseconds(700);   // a moment to start the new page
        Dispatcher.BeginInvoke(() => sv.ScrollToTop(), System.Windows.Threading.DispatcherPriority.Loaded);
    }
}
