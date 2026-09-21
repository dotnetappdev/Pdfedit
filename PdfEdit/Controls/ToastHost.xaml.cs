using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using PdfEdit.Services;

namespace PdfEdit.Controls;

public partial class ToastHost : UserControl
{
    public ToastHost()
    {
        InitializeComponent();
        ToastService.Instance.ToastRequested += OnToastRequested;
    }

    private void OnToastRequested(string message, ToastType type)
    {
        Dispatcher.BeginInvoke(() => ShowToast(message, type));
    }

    private void ShowToast(string message, ToastType type)
    {
        var (icon, bgColor) = type switch
        {
            ToastType.Success => ("✓", Color.FromRgb(15, 125, 67)),
            ToastType.Warning => ("⚠", Color.FromRgb(157, 93, 0)),
            ToastType.Error   => ("✕", Color.FromRgb(196, 43, 28)),
            _                 => ("ℹ", Color.FromRgb(14, 99, 156))
        };

        var toast = new Border
        {
            Background = new SolidColorBrush(bgColor),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(0, 6, 0, 0),
            MinWidth = 240,
            MaxWidth = 380,
            IsHitTestVisible = true,
            Opacity = 0,
        };

        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(new TextBlock
        {
            Text = icon,
            Foreground = Brushes.White,
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        });
        panel.Children.Add(new TextBlock
        {
            Text = message,
            Foreground = Brushes.White,
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 300,
            VerticalAlignment = VerticalAlignment.Center,
        });

        toast.Child = panel;

        // Click to dismiss early
        toast.MouseLeftButtonDown += (_, _) => DismissToast(toast);

        ToastList.Items.Add(toast);

        // Slide in
        var slideIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        toast.BeginAnimation(OpacityProperty, slideIn);

        // Auto-dismiss after 4 seconds
        var timer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(4)
        };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            DismissToast(toast);
        };
        timer.Start();
    }

    private void DismissToast(Border toast)
    {
        var slideOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        slideOut.Completed += (_, _) => ToastList.Items.Remove(toast);
        toast.BeginAnimation(OpacityProperty, slideOut);
    }
}
