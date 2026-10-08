using System.Diagnostics;
using System.Security.Cryptography;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using PdfEdit.Blazor;
using PdfEdit.Blazor.Services;
using PdfEdit.Services;

namespace PdfEdit.Avalonia;

/// <summary>
/// The PdfEdit window: starts the PdfEdit web app inside this process (on 127.0.0.1, a free port,
/// answering only this window) and shows it in the platform's web view — the same pages, ribbon and
/// tools as the web version and the same PdfEdit.Core underneath as the WPF app.
/// </summary>
public partial class MainWindow : Window, IDesktopShell
{
    private WebApplication? _server;
    private Uri? _home;

    public MainWindow()
    {
        InitializeComponent();
        Web.NavigationCompleted += (_, _) => ShowApp();
        // Links to other sites open in the browser, not inside PdfEdit.
        Web.NewWindowRequested += (_, e) =>
        {
            e.Handled = true;
            if (e.Request is { } uri) OpenInBrowser(uri);
        };
    }

    protected override async void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        try
        {
            // A new secret each time PdfEdit starts: other programs on this computer can't use its server.
            string token = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
            PdfEditWebHost.Desktop = this;
            _server = await Task.Run(() => PdfEditWebHost.Build([], token));
            await _server.StartAsync();
            var address = _server.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
            _home = new Uri(address.TrimEnd('/') + "/");
            string open = Program.FilesToOpen.Concat(Pending).FirstOrDefault() is { } file ? "&desktop-open=" + Uri.EscapeDataString(file) : "";
            Web.Source = new Uri(_home, $"?desktop-token={token}{open}");
            Pending.Clear();
            _ready = this;
        }
        catch (Exception ex)
        {
            SplashText.Text = "PdfEdit couldn't start: " + ex.Message;
        }
    }

    private static readonly List<string> Pending = new();
    private static MainWindow? _ready;

    /// <summary>Opens a PDF in PdfEdit: now if the window is ready, otherwise as soon as it is.</summary>
    public static void OpenFile(string path)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_ready is { _home: { } home } window) window.Web.Source = new Uri(home, "?desktop-open=" + Uri.EscapeDataString(path));
            else if (!Pending.Contains(path)) Pending.Add(path);
        });
    }

    private void ShowApp()
    {
        Dispatcher.UIThread.Post(() =>
        {
            Splash.IsVisible = false;
            Web.IsVisible = true;
        });
    }

    // ── Help menu (Mac menu bar): the PdfEdit website's guide and tutorials ───

    private void UserGuide_Click(object? sender, EventArgs e) => OpenInBrowser(new Uri(HelpLinks.UserGuide));
    private void Tutorials_Click(object? sender, EventArgs e) => OpenInBrowser(new Uri(HelpLinks.Tutorials));
    private void GettingStarted_Click(object? sender, EventArgs e) => OpenInBrowser(new Uri(HelpLinks.GettingStarted));
    private void Shortcuts_Click(object? sender, EventArgs e) => OpenInBrowser(new Uri(HelpLinks.KeyboardShortcuts));
    private void Troubleshooting_Click(object? sender, EventArgs e) => OpenInBrowser(new Uri(HelpLinks.Troubleshooting));
    private void ReportProblem_Click(object? sender, EventArgs e) => OpenInBrowser(new Uri($"https://github.com/{UpdateService.Owner}/{UpdateService.Repo}/issues/new"));
    private void WhatsNew_Click(object? sender, EventArgs e) => OpenInBrowser(new Uri(UpdateService.ReleasesPage));

    private static void OpenInBrowser(Uri uri)
    {
        if (uri.Scheme is not ("http" or "https" or "mailto")) return;
        try { Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); } catch { }
    }

    // ── What the web app asks of the desktop (IDesktopShell) ─────────────────

    /// <summary>Save, Save As, Export…: the system's Save dialog instead of a browser download.</summary>
    public Task<string?> SaveFileAsync(string suggestedName, string sourcePath) =>
        Dispatcher.UIThread.InvokeAsync(async () =>
        {
            string ext = Path.GetExtension(suggestedName).TrimStart('.').ToLowerInvariant();
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save",
                SuggestedFileName = suggestedName,
                DefaultExtension = ext,
                ShowOverwritePrompt = true,
                FileTypeChoices = ext.Length == 0 ? null :
                [
                    new FilePickerFileType(ext.ToUpperInvariant() + " file") { Patterns = [$"*.{ext}"] },
                    FilePickerFileTypes.All,
                ],
            });
            if (file == null) return null;
            await using (var dest = await file.OpenWriteAsync())
            await using (var src = File.OpenRead(sourcePath))
                await src.CopyToAsync(dest);
            return file.TryGetLocalPath() ?? file.Name;
        });

    /// <summary>Print: the PDF opens in the computer's PDF viewer.</summary>
    public void OpenWithSystem(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch
        {
            // Linux without a file association handler set up for Process.Start.
            try { Process.Start("xdg-open", path); } catch { }
        }
    }

    public void OpenExternal(Uri uri) => OpenInBrowser(uri);

    protected override async void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        if (PdfEditWebHost.Desktop == this) PdfEditWebHost.Desktop = null;
        if (_server != null)
        {
            try { using var wait = new CancellationTokenSource(TimeSpan.FromSeconds(3)); await _server.StopAsync(wait.Token); } catch { }
            await _server.DisposeAsync();
        }
    }
}
