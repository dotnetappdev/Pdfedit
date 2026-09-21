using System.Windows;
using System.Windows.Controls;
using PdfEdit.Models;
using PdfEdit.ViewModels;

namespace PdfEdit.Controls;

public partial class ToolboxPanel : UserControl
{
    public ToolboxPanel()
    {
        InitializeComponent();
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
}
