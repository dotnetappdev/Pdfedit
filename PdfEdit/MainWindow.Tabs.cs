using System.Windows;
using System.Windows.Input;
using PdfEdit.Models;

namespace PdfEdit;

/// <summary>Document tab strip: click to switch, × or middle-click to close, right-click for more.</summary>
public partial class MainWindow
{
    private static OpenDocumentTab? TabOf(object sender) => (sender as FrameworkElement)?.DataContext as OpenDocumentTab;

    private async void DocTab_Click(object sender, MouseButtonEventArgs e)
    {
        if (TabOf(sender) is { } tab && VM != null) await VM.SwitchToTabAsync(tab);
    }

    private async void DocTab_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle || TabOf(sender) is not { } tab || VM == null) return;
        e.Handled = true;
        await VM.CloseTabAsync(tab);
    }

    private async void DocTabClose_Click(object sender, RoutedEventArgs e)
    {
        if (TabOf(sender) is { } tab && VM != null) await VM.CloseTabAsync(tab);
    }

    private async void DocTabCloseOthers_Click(object sender, RoutedEventArgs e)
    {
        if (TabOf(sender) is { } tab && VM != null) await VM.CloseOtherTabsAsync(tab);
    }

    private void DocTabCopyPath_Click(object sender, RoutedEventArgs e)
    {
        if (TabOf(sender) is { } tab) try { Clipboard.SetText(tab.Path); } catch { }
    }

    private void DocTabShowInFolder_Click(object sender, RoutedEventArgs e)
    {
        if (TabOf(sender) is { } tab && System.IO.File.Exists(tab.Path))
            System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{tab.Path}\"");
    }
}
