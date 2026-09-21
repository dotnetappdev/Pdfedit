using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PdfEdit.Dialogs;
using PdfEdit.Models;
using PdfEdit.Services;
using PdfEdit.ViewModels;

namespace PdfEdit.Controls;

public partial class ToolboxPanel : UserControl
{
    public ToolboxPanel()
    {
        InitializeComponent();
        Loaded += (_, _) => RefreshSignatures();
    }

    private void Tool_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton rb && rb.Tag is string toolName
            && Enum.TryParse<ActiveTool>(toolName, out var tool))
        {
            if (DataContext is MainViewModel vm)
                vm.ActiveTool = tool;
        }
    }

    private void AddSig_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SignatureDialog { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() == true)
            RefreshSignatures();
    }

    public void RefreshSignatures()
    {
        SavedSigPanel.Children.Clear();
        var sigs = SignatureStore.Load();
        foreach (var sig in sigs)
            SavedSigPanel.Children.Add(BuildSignatureThumb(sig));
    }

    private UIElement BuildSignatureThumb(SavedSignature sig)
    {
        BitmapImage? bmp = null;
        try
        {
            using var ms = new MemoryStream(sig.ImageBytes);
            bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.StreamSource = ms;
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.DecodePixelWidth = 44;
            bmp.EndInit();
            bmp.Freeze();
        }
        catch { }

        var img = new Image
        {
            Source = bmp,
            Width = 40,
            Height = 20,
            Stretch = Stretch.Uniform,
        };

        var border = new Border
        {
            Width = 44,
            Height = 28,
            Margin = new Thickness(4, 2, 4, 2),
            BorderBrush = new SolidColorBrush(Color.FromRgb(70, 70, 70)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Background = Brushes.White,
            ToolTip = $"{sig.Name}\nAdded {sig.CreatedAt}\nLeft-click to place · Right-click to delete",
            Cursor = System.Windows.Input.Cursors.Hand,
            Child = img
        };
        System.Windows.Automation.AutomationProperties.SetName(border, $"Saved signature: {sig.Name}");

        border.MouseLeftButtonDown += (_, e) =>
        {
            if (DataContext is MainViewModel vm)
            {
                vm.PendingLibrarySignature = sig.ImageBytes;
                vm.ActiveTool = ActiveTool.Signature;
                ToastService.Instance.Info($"'{sig.Name}' selected — click on the page to place.");
                // Highlight selected state
                border.BorderBrush = new SolidColorBrush(Color.FromRgb(14, 99, 156));
                border.BorderThickness = new Thickness(2);
            }
            e.Handled = true;
        };

        border.MouseEnter += (_, _) =>
            border.BorderBrush = new SolidColorBrush(Color.FromRgb(100, 160, 220));

        border.MouseLeave += (_, _) =>
        {
            if (DataContext is MainViewModel vm && vm.PendingLibrarySignature == sig.ImageBytes)
                border.BorderBrush = new SolidColorBrush(Color.FromRgb(14, 99, 156));
            else
                border.BorderBrush = new SolidColorBrush(Color.FromRgb(70, 70, 70));
        };

        var cm = new ContextMenu();
        var deleteItem = new MenuItem { Header = $"Delete '{sig.Name}'" };
        deleteItem.Click += (_, _) =>
        {
            SignatureStore.Remove(sig.Id);
            RefreshSignatures();
            ToastService.Instance.Info($"Signature '{sig.Name}' removed from library.");
        };
        cm.Items.Add(deleteItem);
        border.ContextMenu = cm;

        return border;
    }
}
