using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace PdfEdit.Services;

/// <summary>
/// Applies Settings → Accessibility across the app: the high-contrast focus ring, reduced motion
/// (menus and notifications appear without fading) and tooltip timing.
/// </summary>
public static class UiAccessibilityService
{
    private static bool _registered;
    private static FocusRingAdorner? _ring;

    /// <summary>True when fades and slides should be skipped: the setting, or Windows' animation effects turned off.</summary>
    public static bool ReduceMotion => AppSettings.Current.ReduceMotion || !SystemParameters.ClientAreaAnimation;

    /// <summary>Call once at startup, after settings are loaded.</summary>
    public static void Initialize()
    {
        if (_registered) return;
        _registered = true;

        // Tooltip timing is read when tooltips are set up, so it applies from the next start.
        var s = AppSettings.Current;
        try
        {
            ToolTipService.InitialShowDelayProperty.OverrideMetadata(typeof(FrameworkElement),
                new FrameworkPropertyMetadata(Math.Clamp(s.TooltipDelayMs, 0, 5000)));
            if (s.TooltipsStayOpen)
                ToolTipService.ShowDurationProperty.OverrideMetadata(typeof(FrameworkElement),
                    new FrameworkPropertyMetadata(int.MaxValue));
        }
        catch { /* already set for this type */ }

        EventManager.RegisterClassHandler(typeof(UIElement), Keyboard.GotKeyboardFocusEvent,
            new KeyboardFocusChangedEventHandler(OnGotKeyboardFocus), true);
        Apply();
    }

    /// <summary>Re-applies the settings that can change while PdfEdit is running.</summary>
    public static void Apply()
    {
        if (Application.Current is not { } app) return;
        app.Resources["PopupAnim"] = ReduceMotion ? PopupAnimation.None : PopupAnimation.Fade;
        if (!AppSettings.Current.HighContrastFocusIndicators) RemoveRing();
        else if (Keyboard.FocusedElement is UIElement el) ShowRing(el);
    }

    private static void OnGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (!ReferenceEquals(sender, e.NewFocus)) return;
        if (!AppSettings.Current.HighContrastFocusIndicators) { RemoveRing(); return; }
        if (sender is UIElement el) ShowRing(el);
    }

    private static void ShowRing(UIElement el)
    {
        RemoveRing();
        if (el is Window || !el.IsVisible) return;
        var layer = AdornerLayer.GetAdornerLayer(el);
        if (layer == null) return;
        _ring = new FocusRingAdorner(el);
        layer.Add(_ring);
        el.LostKeyboardFocus += OnLost;
    }

    private static void OnLost(object sender, KeyboardFocusChangedEventArgs e)
    {
        ((UIElement)sender).LostKeyboardFocus -= OnLost;
        RemoveRing();
    }

    private static void RemoveRing()
    {
        if (_ring == null) return;
        _ring.AdornedElement.LostKeyboardFocus -= OnLost;
        AdornerLayer.GetAdornerLayer(_ring.AdornedElement)?.Remove(_ring);
        _ring = null;
    }

    /// <summary>A thick yellow ring with a black edge, visible on any background.</summary>
    private sealed class FocusRingAdorner : Adorner
    {
        private static readonly Pen Outer = MakePen(Colors.Black, 5);
        private static readonly Pen Inner = MakePen(Color.FromRgb(0xFF, 0xD7, 0x00), 3);

        public FocusRingAdorner(UIElement el) : base(el) => IsHitTestVisible = false;

        private static Pen MakePen(Color c, double w)
        {
            var p = new Pen(new SolidColorBrush(c), w);
            p.Freeze();
            return p;
        }

        protected override void OnRender(DrawingContext dc)
        {
            var r = new Rect(AdornedElement.RenderSize);
            r.Inflate(3, 3);
            dc.DrawRoundedRectangle(null, Outer, r, 4, 4);
            dc.DrawRoundedRectangle(null, Inner, r, 4, 4);
        }
    }
}
