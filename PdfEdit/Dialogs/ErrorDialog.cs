using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PdfEdit.Dialogs;

/// <summary>
/// A self-contained, code-only error dialog. It has no XAML so it can be shown
/// even when the application's resource dictionaries or themes fail to load.
/// Presents a friendly summary plus the full, selectable exception details with
/// a one-click "Copy" button so the user can easily paste the error to us.
/// </summary>
public sealed class ErrorDialog : Window
{
    private ErrorDialog(string summary, string details)
    {
        Title = "PdfEdit — Something went wrong";
        Width = 720;
        Height = 480;
        MinWidth = 480;
        MinHeight = 320;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E));
        ShowInTaskbar = true;

        var root = new Grid { Margin = new Thickness(16) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // summary
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // details
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // buttons

        var summaryText = new TextBlock
        {
            Text = summary,
            Foreground = Brushes.White,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        };
        Grid.SetRow(summaryText, 0);
        root.Children.Add(summaryText);

        var detailsBox = new TextBox
        {
            Text = details,
            IsReadOnly = true,
            IsReadOnlyCaretVisible = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12,
            Background = new SolidColorBrush(Color.FromRgb(0x25, 0x25, 0x26)),
            Foreground = new SolidColorBrush(Color.FromRgb(0xD4, 0xD4, 0xD4)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x3E, 0x3E, 0x3E)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8)
        };
        Grid.SetRow(detailsBox, 1);
        root.Children.Add(detailsBox);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };

        var copyButton = new Button
        {
            Content = "Copy to clipboard",
            Padding = new Thickness(14, 6, 14, 6),
            Margin = new Thickness(0, 0, 8, 0),
            MinWidth = 140
        };
        copyButton.Click += (_, _) =>
        {
            try
            {
                Clipboard.SetText(details);
                copyButton.Content = "Copied ✓";
            }
            catch
            {
                copyButton.Content = "Copy failed";
            }
        };
        buttons.Children.Add(copyButton);

        var closeButton = new Button
        {
            Content = "Close",
            Padding = new Thickness(14, 6, 14, 6),
            MinWidth = 90,
            IsDefault = true,
            IsCancel = true
        };
        closeButton.Click += (_, _) => Close();
        buttons.Children.Add(closeButton);

        Grid.SetRow(buttons, 2);
        root.Children.Add(buttons);

        Content = root;
    }

    /// <summary>Shows the dialog for an exception, marshalling to the UI thread if needed.</summary>
    public static void Show(string summary, Exception exception)
        => Show(summary, BuildDetails(exception));

    /// <summary>Shows the dialog with pre-formatted detail text.</summary>
    public static void Show(string summary, string details)
    {
        void ShowCore()
        {
            var owner = Application.Current?.Windows
                .OfType<Window>()
                .FirstOrDefault(w => w.IsActive && w is not ErrorDialog);

            var dlg = new ErrorDialog(summary, details);
            if (owner != null && owner.IsLoaded)
                dlg.Owner = owner;
            dlg.ShowDialog();
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.CheckAccess())
            ShowCore();
        else
            dispatcher.Invoke(ShowCore);
    }

    private static string BuildDetails(Exception exception)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"PdfEdit {GetVersion()}");
        sb.AppendLine($"Time:    {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"OS:      {Environment.OSVersion}");
        sb.AppendLine($".NET:    {Environment.Version}");
        sb.AppendLine();

        var current = exception;
        int depth = 0;
        while (current != null)
        {
            sb.AppendLine(depth == 0 ? "Exception:" : $"Inner exception (level {depth}):");
            sb.AppendLine($"  Type:    {current.GetType().FullName}");
            sb.AppendLine($"  Message: {current.Message}");
            if (!string.IsNullOrWhiteSpace(current.StackTrace))
            {
                sb.AppendLine("  Stack trace:");
                sb.AppendLine(current.StackTrace);
            }
            sb.AppendLine();
            current = current.InnerException;
            depth++;
        }

        return sb.ToString().TrimEnd();
    }

    private static string GetVersion()
    {
        try
        {
            return System.Reflection.Assembly.GetExecutingAssembly()
                .GetName().Version?.ToString() ?? "unknown";
        }
        catch
        {
            return "unknown";
        }
    }
}
