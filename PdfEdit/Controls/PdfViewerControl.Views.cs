using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using PdfEdit.Services;

namespace PdfEdit.Controls;

/// <summary>Reading views: two pages side by side, and night mode (dark pages).</summary>
public partial class PdfViewerControl
{
    private async Task ShowFacingPageAsync(double dpiScale)
    {
        if (_vm?.Document == null || !_vm.TwoPageView || _vm.CurrentPageIndex + 1 >= _vm.PageCount)
        {
            FacingPageBorder.Visibility = Visibility.Collapsed;
            FacingImage.Source = null;
            return;
        }
        int index = _vm.CurrentPageIndex + 1;
        try
        {
            var bmp = await _vm.RenderService.RenderPageAsync(index, _vm.Zoom, dpiScale);
            if (_vm.CurrentPageIndex + 1 != index) return;   // moved on while rendering
            FacingImage.Source = _vm.NightMode ? NightFilter.Apply(bmp) : bmp;
            FacingPageBorder.Background = _vm.NightMode ? new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E)) : Brushes.White;
            int rotation = _vm.GetPageRotation(index);
            FacingPageBorder.LayoutTransform = rotation == 0 ? Transform.Identity : new RotateTransform(rotation);
            FacingPageBorder.Visibility = Visibility.Visible;
        }
        catch { FacingPageBorder.Visibility = Visibility.Collapsed; }
    }

    private void FacingPage_Click(object sender, MouseButtonEventArgs e)
    {
        if (_vm == null) return;
        e.Handled = true;
        _vm.CurrentPageIndex = Math.Min(_vm.CurrentPageIndex + 1, _vm.PageCount - 1);
    }
}
