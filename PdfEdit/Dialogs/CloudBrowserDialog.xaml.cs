using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using PdfEdit.Services;
using PdfEdit.Services.Cloud;

namespace PdfEdit.Dialogs;

/// <summary>
/// Browse Google Drive or OneDrive: open a PDF, or pick a folder and name to save into.
/// Connecting (signing in) can be done from here once the app details are in Settings → Cloud.
/// </summary>
public partial class CloudBrowserDialog : Window
{
    private readonly bool _save;
    private CloudProvider? _provider;
    private readonly List<(string Id, string Name)> _path = new();
    private CancellationTokenSource? _cts;
    private bool _searching;

    public CloudProvider? Provider => _provider;
    public CloudItem? SelectedFile { get; private set; }
    public string FolderId => _path.Count > 0 ? _path[^1].Id : _provider?.RootId ?? "root";
    public string FileName => NameBox.Text.Trim();
    /// <summary>The user asked to set up credentials (open Settings → Cloud).</summary>
    public bool OpenSettingsRequested { get; private set; }

    public CloudBrowserDialog(bool save, string? suggestedName = null)
    {
        InitializeComponent();
        _save = save;
        Title = save ? "Save to cloud storage" : "Import from cloud storage";
        OkBtn.Content = save ? "Save here" : "Import";
        SavePanel.Visibility = save ? Visibility.Visible : Visibility.Collapsed;
        NewFolderBtn.Visibility = save ? Visibility.Visible : Visibility.Collapsed;
        NameBox.Text = suggestedName ?? "document.pdf";

        foreach (var p in CloudStorage.Providers)
        {
            var b = new ToggleButton { Content = p.DisplayName, Padding = new Thickness(14, 4, 14, 4), Margin = new Thickness(0, 0, 6, 0), Tag = p };
            b.Click += (_, _) => _ = SelectProviderAsync(p);
            ProviderButtons.Children.Add(b);
        }
        var start = CloudStorage.Providers.FirstOrDefault(p => p.IsConnected)
                    ?? CloudStorage.Providers.FirstOrDefault(p => p.IsConfigured)
                    ?? CloudStorage.Providers[0];
        Loaded += (_, _) => _ = SelectProviderAsync(start);
        Closed += (_, _) => _cts?.Cancel();
    }

    private async Task SelectProviderAsync(CloudProvider p)
    {
        _provider = p;
        foreach (ToggleButton b in ProviderButtons.Children) b.IsChecked = b.Tag == p;
        _path.Clear();
        UpdateAccount();
        await LoadAsync();
    }

    private void UpdateAccount()
    {
        if (_provider == null) return;
        AccountText.Text = _provider.IsConnected ? _provider.AccountName ?? "" : "";
        ConnectBtn.Content = _provider.IsConnected ? "Sign out" : "Connect";
        ConnectBtn.Visibility = _provider.IsConfigured ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ShowMessage(string text, string? button = null)
    {
        FileList.ItemsSource = null;
        MessagePanel.Visibility = Visibility.Visible;
        MessageText.Text = text;
        MessageBtn.Content = button;
        MessageBtn.Visibility = button != null ? Visibility.Visible : Visibility.Collapsed;
    }

    private async Task LoadAsync(string? search = null)
    {
        if (_provider == null) return;
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        _searching = search != null;
        UpBtn.IsEnabled = _path.Count > 0 || _searching;
        PathText.Text = _searching ? $"Search results for “{search}”"
            : _provider.DisplayName + string.Concat(_path.Select(p => "  ›  " + p.Name));
        OkBtn.IsEnabled = _save && _provider.IsConnected && !_searching;
        NewFolderBtn.IsEnabled = _provider.IsConnected && !_searching;

        if (!_provider.IsConfigured)
        {
            ShowMessage($"To use {_provider.DisplayName}, add your own app details (client ID{(_provider is GoogleDriveProvider ? " and secret" : "")}) in Settings → Cloud. It only takes a few minutes and the steps are there.",
                "Open Settings");
            return;
        }
        if (!_provider.IsConnected)
        {
            ShowMessage($"Sign in to {_provider.DisplayName} to see your files. Your browser opens to sign in; PdfEdit keeps the sign-in encrypted on this PC.", "Connect");
            return;
        }

        ShowMessage("Loading…");
        StatusText.Text = "";
        try
        {
            string folder = FolderId;
            var items = search != null ? await _provider.SearchAsync(search, ct) : await _provider.ListAsync(folder, ct);
            if (ct.IsCancellationRequested) return;
            if (search == null && _path.Count == 0 && _provider is GoogleDriveProvider)
                items.Insert(0, new CloudItem(GoogleDriveProvider.SharedWithMe, "Shared with me", true, 0, null));
            MessagePanel.Visibility = Visibility.Collapsed;
            FileList.ItemsSource = items;
            int pdfs = items.Count(i => !i.IsFolder), folders = items.Count(i => i.IsFolder);
            StatusText.Text = $"{folders} folder(s), {pdfs} PDF(s)";
            if (items.Count == 0) ShowMessage(search != null ? "No PDFs match that name." : "This folder has no PDFs or folders.");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            ShowMessage(ex.Message, "Try again");
            UpdateAccount();
        }
    }

    private async void MessageBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_provider == null) return;
        if (!_provider.IsConfigured) { OpenSettingsRequested = true; DialogResult = false; return; }
        if (!_provider.IsConnected) { await ConnectAsync(); return; }
        await LoadAsync();
    }

    private async void Connect_Click(object sender, RoutedEventArgs e)
    {
        if (_provider == null) return;
        if (_provider.IsConnected)
        {
            _provider.Disconnect();
            UpdateAccount();
            await LoadAsync();
        }
        else await ConnectAsync();
    }

    private async Task ConnectAsync()
    {
        if (_provider == null) return;
        ShowMessage("Finish signing in in your browser, then come back here…");
        ConnectBtn.IsEnabled = false;
        try
        {
            await _provider.ConnectAsync();
            UpdateAccount();
            await LoadAsync();
        }
        catch (Exception ex)
        {
            ShowMessage("Couldn't sign in: " + ex.Message, "Connect");
        }
        finally { ConnectBtn.IsEnabled = true; Activate(); }
    }

    private async void FileList_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (FileList.SelectedItem is not CloudItem item) return;
        if (item.IsFolder) { await OpenFolderAsync(item); return; }
        if (!_save) Accept(item);
        else NameBox.Text = item.Name;
    }

    private async void FileList_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && FileList.SelectedItem is CloudItem { IsFolder: true } f) { e.Handled = true; await OpenFolderAsync(f); }
        else if (e.Key == Key.Back) { e.Handled = true; Up_Click(sender, e); }
    }

    private void FileList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_save) OkBtn.IsEnabled = FileList.SelectedItem is CloudItem { IsFolder: false };
        else if (FileList.SelectedItem is CloudItem { IsFolder: false } f) NameBox.Text = f.Name;
    }

    private async Task OpenFolderAsync(CloudItem folder)
    {
        if (_searching) _path.Clear();
        SearchBox.Text = "";
        _path.Add((folder.Id, folder.Name));
        await LoadAsync();
    }

    private async void Up_Click(object sender, RoutedEventArgs e)
    {
        if (_searching) { SearchBox.Text = ""; await LoadAsync(); return; }
        if (_path.Count == 0) return;
        _path.RemoveAt(_path.Count - 1);
        await LoadAsync();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) =>
        await LoadAsync(string.IsNullOrWhiteSpace(SearchBox.Text) ? null : SearchBox.Text.Trim());

    private async void Search_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        await LoadAsync(string.IsNullOrWhiteSpace(SearchBox.Text) ? null : SearchBox.Text.Trim());
    }

    private async void NewFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_provider == null || FolderId == GoogleDriveProvider.SharedWithMe) return;
        var dlg = new InputDialog("New folder", "Folder name:", "New folder") { Owner = this };
        if (dlg.ShowDialog() != true || string.IsNullOrWhiteSpace(dlg.InputText)) return;
        try
        {
            var folder = await _provider.CreateFolderAsync(FolderId, dlg.InputText.Trim());
            _path.Add((folder.Id, folder.Name));
            await LoadAsync();
        }
        catch (Exception ex) { AppDialog.ShowError("Couldn't create the folder.", ex); }
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (_save)
        {
            if (FileName.Length == 0) { NameBox.Focus(); return; }
            if (FolderId == GoogleDriveProvider.SharedWithMe) { AppDialog.ShowInfo("Choose one of your own folders to save into.", "Save to cloud"); return; }
            if (!FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) NameBox.Text = FileName + ".pdf";
            DialogResult = true;
            return;
        }
        if (FileList.SelectedItem is CloudItem { IsFolder: false } item) Accept(item);
    }

    private void Accept(CloudItem item)
    {
        SelectedFile = item;
        DialogResult = true;
    }
}
