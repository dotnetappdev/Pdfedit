using System.Windows;

namespace PdfEdit.Dialogs;

/// <summary>Centralized themed dialog service — replaces all raw MessageBox.Show calls.</summary>
public static class AppDialog
{
    /// <summary>Shows a themed error dialog with optional exception details.</summary>
    public static void ShowError(string message, Exception? ex = null, string title = "Error")
    {
        void ShowCore()
        {
            var dlg = new ThemedErrorDialog(title, message, ex)
            {
                Owner = GetActiveWindow()
            };
            dlg.ShowDialog();
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.CheckAccess()) ShowCore();
        else dispatcher.Invoke(ShowCore);
    }

    /// <summary>Shows a themed confirmation dialog. Returns true if user confirmed.</summary>
    public static bool ShowConfirm(string message, string title = "Confirm",
        string confirmText = "Confirm", string cancelText = "Cancel", bool isDanger = false)
    {
        bool result = false;
        void ShowCore()
        {
            var dlg = new ThemedConfirmDialog(title, message, confirmText, cancelText, isDanger)
            {
                Owner = GetActiveWindow()
            };
            result = dlg.ShowDialog() == true;
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.CheckAccess()) ShowCore();
        else dispatcher.Invoke(ShowCore);

        return result;
    }

    /// <summary>Shows a themed informational dialog.</summary>
    public static void ShowInfo(string message, string title = "Information")
    {
        void ShowCore()
        {
            var dlg = new ThemedInfoDialog(title, message)
            {
                Owner = GetActiveWindow()
            };
            dlg.ShowDialog();
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.CheckAccess()) ShowCore();
        else dispatcher.Invoke(ShowCore);
    }

    private static Window? GetActiveWindow() =>
        Application.Current?.Windows.OfType<Window>()
            .FirstOrDefault(w => w.IsActive && w is not ThemedErrorDialog
                                           and not ThemedConfirmDialog
                                           and not ThemedInfoDialog);
}
