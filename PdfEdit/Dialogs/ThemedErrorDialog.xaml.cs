using System.Text;
using System.Windows;

namespace PdfEdit.Dialogs;

public partial class ThemedErrorDialog : Window
{
    private string _details = string.Empty;

    public ThemedErrorDialog(string title, string message, Exception? exception = null)
    {
        InitializeComponent();
        TitleText.Text = title;
        MessageText.Text = message;

        if (exception != null)
        {
            _details = BuildDetails(exception);
            DetailsBox.Text = _details;
            DetailsExpander.Visibility = Visibility.Visible;
            CopyBtn.Visibility = Visibility.Visible;
        }
    }

    private void OkBtn_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private void CopyBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(_details);
            CopyBtn.Content = "Copied ✓";
        }
        catch
        {
            CopyBtn.Content = "Copy failed";
        }
    }

    private static string BuildDetails(Exception ex)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($".NET: {Environment.Version}  OS: {Environment.OSVersion}");
        sb.AppendLine();

        var current = ex;
        int depth = 0;
        while (current != null)
        {
            sb.AppendLine(depth == 0 ? "Exception:" : $"Caused by:");
            sb.AppendLine($"  {current.GetType().FullName}: {current.Message}");
            if (!string.IsNullOrWhiteSpace(current.StackTrace))
            {
                foreach (var line in current.StackTrace.Split('\n').Take(12))
                    sb.AppendLine("  " + line.TrimEnd());
                if (current.StackTrace.Split('\n').Length > 12)
                    sb.AppendLine("  …");
            }
            sb.AppendLine();
            current = current.InnerException;
            depth++;
        }

        return sb.ToString().TrimEnd();
    }
}
