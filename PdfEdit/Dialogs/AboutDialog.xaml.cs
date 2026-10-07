using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using PdfEdit.Services;

namespace PdfEdit.Dialogs;

/// <summary>Help → About PdfEdit: version, how it's installed, links, and Check for Updates.</summary>
public partial class AboutDialog : Window
{
    public AboutDialog()
    {
        InitializeComponent();

        var asm = Assembly.GetExecutingAssembly();
        var version = UpdateInstaller.CurrentVersion;
        VersionText.Text = $"Version {version}";

        DetailVersion.Text = version.ToString();
        DetailInstall.Text = Capitalise(UpdateInstaller.Describe(UpdateInstaller.InstallKind));
        DetailFolder.Text = UpdateInstaller.AppDir;
        DetailRuntime.Text = $"{RuntimeInformation.FrameworkDescription} ({RuntimeInformation.ProcessArchitecture})";
        DetailOs.Text = $"{RuntimeInformation.OSDescription}";

        CopyrightText.Text = asm.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright
                             ?? "Copyright © 2026";
    }

    private static string Capitalise(string s) => s.Length == 0 ? s : char.ToUpper(s[0]) + s[1..];

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void CheckForUpdates_Click(object sender, RoutedEventArgs e)
    {
        Close();
        if (Owner is MainWindow main) main.ShowUpdateDialog();
        else new UpdateDialog { Owner = Owner }.ShowDialog();
    }

    /// <summary>Copies the details as text, ready to paste into a bug report.</summary>
    private void CopyDetails_Click(object sender, RoutedEventArgs e)
    {
        var text = $"""
            PdfEdit {DetailVersion.Text}
            Installed as: {DetailInstall.Text}
            Folder: {DetailFolder.Text}
            .NET: {DetailRuntime.Text}
            Windows: {DetailOs.Text}
            """;
        try
        {
            Clipboard.SetText(text);
            CopyBtn.Content = "Copied";
        }
        catch { /* clipboard busy: nothing to do */ }
    }

    private void GitHub_Click(object sender, RoutedEventArgs e) => MainWindow.OpenWebPage(MainWindow.GitHubUrl);

    private void WhatsNew_Click(object sender, RoutedEventArgs e) => MainWindow.OpenWebPage(UpdateService.ReleasesPage);

    private void ReportProblem_Click(object sender, RoutedEventArgs e) => MainWindow.OpenWebPage(MainWindow.GitHubUrl + "/issues/new");

    private void License_Click(object sender, RoutedEventArgs e) => MainWindow.OpenWebPage(MainWindow.GitHubUrl + "/blob/main/LICENSE");
}
