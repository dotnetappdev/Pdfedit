using System.Reflection;
using System.Windows;

namespace PdfEdit.Dialogs;

public partial class AboutDialog : Window
{
    public AboutDialog()
    {
        InitializeComponent();

        var asm = Assembly.GetExecutingAssembly();
        var ver = asm.GetName().Version ?? new Version(1, 0, 0);
        VersionText.Text = $"Version {ver.Major}.{ver.Minor}.{ver.Build}";

        var copyright = asm.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright
                        ?? "Copyright © 2026";
        CopyrightText.Text = copyright;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
