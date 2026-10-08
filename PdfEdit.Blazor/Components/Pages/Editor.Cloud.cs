using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using PdfEdit.Blazor.Components.Editor;
using PdfEdit.Blazor.Services;
using PdfEdit.Services.Cloud;

namespace PdfEdit.Blazor.Components.Pages;

// Google Drive and OneDrive: open from, save to and save back to cloud storage.
public partial class Editor
{
    [Inject] public CloudConnections Cloud { get; set; } = default!;
    [Inject] private CloudSignIns SignIns { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;

    public CloudProvider? CloudService { get; private set; }
    /// <summary>The dialog saves into the folder shown (else it opens a file).</summary>
    public bool CloudSaving { get; private set; }
    /// <summary>Folders from the top down to the one shown (Id, Name).</summary>
    public List<(string Id, string Name)> CloudPath { get; } = new();
    public List<CloudItem> CloudItems { get; private set; } = new();
    public bool CloudLoading { get; private set; }
    public string? CloudError { get; private set; }
    public string CloudSearchText { get; set; } = "";
    public bool CloudSearching { get; private set; }
    public string CloudSaveName { get; set; } = "";
    /// <summary>The provider's sign-in page, shown as a link in case the browser blocked the pop-up.</summary>
    public string? CloudSignInUrl { get; private set; }

    /// <summary>The address Google and Microsoft send the browser back to (registered with the OAuth apps).</summary>
    public string CloudCallbackUrl => Nav.BaseUri.TrimEnd('/') + CloudEndpoints.CallbackPath;

    public string? CloudFileName => Doc?.CloudFile is { } f ? $"{f.Name} on {Cloud.Get(f.Provider)?.DisplayName ?? f.Provider}" : null;

    public async Task ShowCloudAsync(bool save, string? providerKey = null)
    {
        CloudSaving = save;
        CloudSaveName = Doc?.FileName ?? "document.pdf";
        CloudError = null;
        _backstage = null;
        _dialog = DialogKind.Cloud;
        var provider = (providerKey != null ? Cloud.Get(providerKey) : null)
                       ?? Cloud.Providers.FirstOrDefault(p => p.IsConnected) ?? Cloud.Providers.FirstOrDefault();
        if (provider != null) await SelectCloudAsync(provider);
    }

    public async Task SelectCloudAsync(CloudProvider provider)
    {
        if (CloudService == provider && provider.IsConnected && CloudPath.Count > 0)
        {
            var here = CloudPath[^1];   // same folder as last time, listed afresh
            await CloudLoadAsync(here.Id, here.Name);
            return;
        }
        CloudService = provider;
        CloudItems = new();
        CloudPath.Clear();
        CloudSignInUrl = null;
        if (provider.IsConnected) await CloudLoadAsync(provider.RootId, "My files", reset: true);
    }

    public async Task ConnectCloudAsync()
    {
        if (CloudService is not { } provider) return;
        CloudError = null;
        CloudSignInUrl = SignIns.Begin(provider, CloudCallbackUrl, ex => InvokeAsync(async () =>
        {
            CloudSignInUrl = null;
            if (ex != null) CloudError = ex.Message;
            else
            {
                Status($"Connected to {provider.DisplayName} as {provider.AccountName}");
                if (CloudService == provider) await CloudLoadAsync(provider.RootId, "My files", reset: true);
            }
            StateHasChanged();
        }));
        bool opened = await JS.InvokeAsync<bool>("pdfedit.openPopup", CloudSignInUrl);
        Status(opened ? $"Sign in to {provider.DisplayName} in the window that opened"
                      : $"Your browser blocked the sign-in window — use the link in the dialog");
    }

    public void DisconnectCloud()
    {
        if (CloudService is not { } provider) return;
        provider.Disconnect();
        CloudItems = new();
        CloudPath.Clear();
        Status($"Disconnected from {provider.DisplayName}");
    }

    public async Task CloudLoadAsync(string folderId, string name, bool reset = false)
    {
        if (CloudService is not { } provider) return;
        if (reset) CloudPath.Clear();
        int at = CloudPath.FindIndex(p => p.Id == folderId);
        if (at >= 0) CloudPath.RemoveRange(at + 1, CloudPath.Count - at - 1);
        else CloudPath.Add((folderId, name));
        CloudSearching = false;
        await CloudRunAsync(async () =>
        {
            var items = await provider.ListAsync(folderId);
            // Google: files other people shared live outside "My files".
            if (provider is GoogleDriveProvider && folderId == provider.RootId)
                items.Insert(0, new CloudItem(GoogleDriveProvider.SharedWithMe, "Shared with me", true, 0, null));
            CloudItems = items;
        });
    }

    public async Task CloudSearchAsync()
    {
        if (CloudService is not { } provider || string.IsNullOrWhiteSpace(CloudSearchText)) return;
        CloudSearching = true;
        await CloudRunAsync(async () => CloudItems = await provider.SearchAsync(CloudSearchText.Trim()));
    }

    private async Task CloudRunAsync(Func<Task> work)
    {
        CloudLoading = true;
        CloudError = null;
        StateHasChanged();
        try { await work(); }
        catch (Exception ex) { CloudError = ex.Message; }
        finally { CloudLoading = false; }
    }

    /// <summary>Opens a cloud file (PDFs as they are; Google Docs and Office files as a PDF copy).</summary>
    public async Task CloudOpenAsync(CloudItem item)
    {
        if (CloudService is not { } provider) return;
        if (item.IsFolder) { await CloudLoadAsync(item.Id, item.Name); return; }
        _dialog = DialogKind.None;
        string name = item.IsPdf ? item.Name : Path.GetFileNameWithoutExtension(item.Name) + ".pdf";
        var temp = Path.Combine(Path.GetTempPath(), $"pdfedit-upload-{Guid.NewGuid():N}.pdf");
        await RunAsync($"Opening {item.Name} from {provider.DisplayName}…", async () =>
        {
            try
            {
                await provider.DownloadAsPdfAsync(item, temp);
            }
            catch
            {
                TryDelete(temp);
                throw;
            }
        });
        if (!File.Exists(temp)) return;
        var before = Doc;
        await OpenPathAsync(temp, name, null);
        // Only a PDF can be saved back over the same file.
        if (Doc != null && Doc != before && item.IsPdf)
            Doc.CloudFile = (provider.Key, item.Id, item.Name);
    }

    /// <summary>Saves the document (with everything added on the pages) as a new file in the folder shown.</summary>
    public async Task CloudSaveHereAsync()
    {
        if (Doc == null || CloudService is not { } provider || CloudPath.Count == 0) return;
        var folder = CloudPath[^1];
        string name = string.IsNullOrWhiteSpace(CloudSaveName) ? Doc.FileName : CloudSaveName.Trim();
        if (!name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) name += ".pdf";
        _dialog = DialogKind.None;
        var doc = Doc;
        await RunAsync($"Saving to {provider.DisplayName}…", async () =>
        {
            await CommitPendingAsync();
            var saved = await provider.UploadNewAsync(doc.CurrentPath, folder.Id, name);
            doc.CloudFile = (provider.Key, saved.Id, name);
            doc.IsModified = false;
            Status($"Saved {name} to {provider.DisplayName}");
            Toast($"Saved to {provider.DisplayName}: {folder.Name} / {name}");
        });
    }

    /// <summary>Replaces the cloud file this document came from with the current version.</summary>
    public async Task SaveBackToCloudAsync()
    {
        if (Doc?.CloudFile is not { } link || Cloud.Get(link.Provider) is not { } provider) return;
        if (!provider.IsConnected)
        {
            Toast($"Connect {provider.DisplayName} again to save back.", "error");
            await ShowCloudAsync(save: true, provider.Key);
            return;
        }
        var doc = Doc;
        await RunAsync($"Saving to {provider.DisplayName}…", async () =>
        {
            await CommitPendingAsync();
            await provider.UpdateAsync(link.FileId, doc.CurrentPath);
            doc.IsModified = false;
            Status($"Saved {link.Name} on {provider.DisplayName}");
        });
    }
}
