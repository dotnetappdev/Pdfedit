using System.Windows;
using Fluent;
using PdfEdit.Converters;
using PdfEdit.ViewModels;

namespace PdfEdit;

public partial class MainWindow : RibbonWindow
{
    public MainWindow()
    {
        // Register converters before InitializeComponent so XAML can resolve them
        Resources.Add("ZeroToOneConverter", new ZeroToOneConverter());
        Resources.Add("ZoomPercentConverter", new ZoomPercentConverter());
        Resources.Add("EnumToBoolConverter", new EnumToBoolConverter());
        Resources.Add("NullToBoolConverter", new NullToBoolConverter());

        InitializeComponent();
    }

    public async Task OpenFileAsync(string path)
    {
        if (DataContext is MainViewModel vm)
            await vm.OpenFileAsync(path);
    }

    protected override void OnDrop(System.Windows.DragEventArgs e)
    {
        base.OnDrop(e);
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            var pdf = System.Array.Find(files, f => f.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase));
            if (pdf != null) _ = OpenFileAsync(pdf);
        }
    }
}
