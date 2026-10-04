using System.IO;
using System.Windows;
using System.Windows.Input;
using PdfEdit.Services;
using PdfEdit.Services.Cloud;

namespace PdfEdit.ViewModels;

/// <summary>
/// Google Drive / OneDrive: open a PDF from the cloud (downloaded to a local copy that Save
/// uploads back), save the open PDF to the cloud, and upload changes on demand.
/// </summary>
public partial class MainViewModel
{
    private ICommand? _openFromCloudCommand, _saveToCloudCommand;
    private bool _uploading;

    public ICommand OpenFromCloudCommand => _openFromCloudCommand ??= new AsyncRelayCommand(OpenFromCloudAsync);

    private ICommand? _importGoogleLinkCommand;

    /// <summary>Import a Google Docs / Sheets / Slides (or Drive) link as a PDF.</summary>
    public ICommand ImportGoogleLinkCommand => _importGoogleLinkCommand ??= new AsyncRelayCommand(async () =>
    {
        string start = "";
        try { if (Clipboard.ContainsText() && GoogleLinkImport.Parse(Clipboard.GetText()) != null) start = Clipboard.GetText().Trim(); } catch { }
        var dlg = new Dialogs.InputDialog("Import from Google Docs",
            "Paste the link to a Google Doc, Sheet or Slides file (or a Word file on Google Drive):", start)
        { Owner = Application.Current.MainWindow };
        if (dlg.ShowDialog() != true || string.IsNullOrWhiteSpace(dlg.InputText)) return;
        if (GoogleLinkImport.Parse(dlg.InputText) == null)
        {
            Dialogs.AppDialog.ShowInfo("That isn't a Google Docs, Sheets, Slides or Drive link. It should start with https://docs.google.com/ or https://drive.google.com/.", "Import from Google Docs");
            return;
        }
        string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "PdfEdit", "Imported");
        try
        {
            StatusText = "Importing from Google…";
            IsLoading = true;
            string pdf = await GoogleLinkImport.ImportAsync(dlg.InputText.Trim(), folder);
            if (_currentFilePath != null) SaveDocumentState();
            await LoadDocumentAsync(pdf);
            ToastService.Instance.Success($"Imported as {Path.GetFileName(pdf)} (in Documents\\PdfEdit\\Imported).");
        }
        catch (Exception ex)
        {
            IsLoading = false;
            StatusText = "Import failed.";
            Dialogs.AppDialog.ShowError("Couldn't import from Google.", ex);
        }
    });
    public ICommand SaveToCloudCommand => _saveToCloudCommand ??= new AsyncRelayCommand(SaveToCloudAsync, () => HasDocument && !_uploading);

    /// <summary>"Google Drive" if the open file came from there, else null.</summary>
    public string? CurrentCloudName => CloudStorage.LinkFor(_currentFilePath) is { } l ? CloudStorage.Get(l.Provider)?.DisplayName : null;

    private void OpenCloudSettings()
    {
        var dlg = new Dialogs.SettingsWindow { Owner = Application.Current.MainWindow };
        dlg.ShowTab("Cloud");
        dlg.ShowDialog();
    }

    private async Task OpenFromCloudAsync()
    {
        var dlg = new Dialogs.CloudBrowserDialog(save: false) { Owner = Application.Current.MainWindow };
        bool ok = dlg.ShowDialog() == true;
        if (dlg.OpenSettingsRequested) { OpenCloudSettings(); return; }
        if (!ok || dlg.Provider is not { } provider || dlg.SelectedFile is not { } file) return;

        string local = CloudStorage.CachePath(provider, file.Id, file.IsPdf ? file.Name : Path.GetFileNameWithoutExtension(file.Name));
        try
        {
            StatusText = file.IsPdf ? $"Downloading {file.Name} from {provider.DisplayName}…" : $"Getting {file.Name} from {provider.DisplayName} as a PDF…";
            IsLoading = true;
            await provider.DownloadAsPdfAsync(file, local);
        }
        catch (Exception ex)
        {
            IsLoading = false;
            StatusText = "Ready";
            Dialogs.AppDialog.ShowError($"Couldn't download {file.Name}.", ex);
            return;
        }
        // Only PDFs are saved back in place; a Google Doc or Word file becomes a new PDF.
        if (file.IsPdf) CloudStorage.Remember(local, provider, file.Id, file.Name);
        if (_currentFilePath != null) SaveDocumentState();
        await LoadDocumentAsync(local);
        OnPropertyChanged(nameof(CurrentCloudName));
        ToastService.Instance.Success(file.IsPdf
            ? $"Opened from {provider.DisplayName}. Save uploads your changes back."
            : $"Imported the {file.Kind} as a PDF. Use Save to Cloud to keep the PDF in {provider.DisplayName}.");
    }

    /// <summary>Writes the open document, with everything added so far, to <paramref name="dest"/>.</summary>
    private void WriteCurrentDocument(string dest)
    {
        _formService.SaveFull(_currentFilePath!, dest, FieldValues,
            _pageRotations, FreeTextAnnotations, PlacedSignatures, flatten: false,
            deletedFieldNames: DeletedFieldNames, fieldExportValues: BuildExportValuesForSave(),
            highlightAnnotations: HighlightAnnotations, stickyNotes: StickyNotes,
            shapeAnnotations: ShapeAnnotations, fieldBounds: ModifiedFieldBounds,
            fieldEdits: GetFieldEditsForSave(), textEdits: TextEditMarks);
    }

    private async Task SaveToCloudAsync()
    {
        if (_currentFilePath == null) return;

        // A file that came from the cloud: save, then upload it back.
        if (CloudStorage.LinkFor(_currentFilePath) is { } link && CloudStorage.Get(link.Provider) is { IsConnected: true })
        {
            await SaveAsync();
            if (!AppSettings.Current.CloudAutoUpload) await UploadCurrentCloudFileAsync();
            return;
        }

        var dlg = new Dialogs.CloudBrowserDialog(save: true, Path.GetFileName(_currentFilePath)) { Owner = Application.Current.MainWindow };
        bool ok = dlg.ShowDialog() == true;
        if (dlg.OpenSettingsRequested) { OpenCloudSettings(); return; }
        if (!ok || dlg.Provider is not { } provider) return;

        string name = dlg.FileName, folder = dlg.FolderId;
        string tmp = Path.Combine(Path.GetTempPath(), "PdfEdit-upload-" + Guid.NewGuid().ToString("N")[..8] + ".pdf");
        _uploading = true;
        try
        {
            StatusText = $"Uploading {name} to {provider.DisplayName}…";
            string src = _currentFilePath;
            await Task.Run(() => WriteCurrentDocument(tmp));
            var item = await provider.UploadNewAsync(tmp, folder, name);
            StatusText = $"Saved to {provider.DisplayName} as {name}.";
            ToastService.Instance.Success($"Saved to {provider.DisplayName}.");

            // Offer to carry on with the cloud copy, so later saves go there.
            if (Dialogs.AppDialog.ShowConfirm($"Saved “{name}” to {provider.DisplayName}.\n\nKeep working on the cloud copy, so Save uploads your changes there?",
                    "Saved to cloud", "Work on cloud copy", "Keep this file"))
            {
                string local = CloudStorage.CachePath(provider, item.Id, name);
                Directory.CreateDirectory(Path.GetDirectoryName(local)!);
                File.Copy(tmp, local, overwrite: true);
                CloudStorage.Remember(local, provider, item.Id, name);
                SaveDocumentState();
                await LoadDocumentAsync(local);
                OnPropertyChanged(nameof(CurrentCloudName));
            }
        }
        catch (Exception ex)
        {
            StatusText = "Upload failed.";
            Dialogs.AppDialog.ShowError($"Couldn't save to {provider.DisplayName}.", ex);
        }
        finally
        {
            _uploading = false;
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
        }
    }

    /// <summary>After Save: if the file came from the cloud, upload it back.</summary>
    private async Task UploadIfCloudAsync()
    {
        if (AppSettings.Current.CloudAutoUpload) await UploadCurrentCloudFileAsync();
    }

    private async Task UploadCurrentCloudFileAsync()
    {
        if (_uploading || CloudStorage.LinkFor(_currentFilePath) is not { } link || CloudStorage.Get(link.Provider) is not { } provider) return;
        if (!provider.IsConnected)
        {
            ToastService.Instance.Warning($"Saved on this PC only: connect {provider.DisplayName} in Settings → Cloud to upload it.");
            return;
        }
        string path = _currentFilePath!;
        _uploading = true;
        try
        {
            StatusText = $"Uploading to {provider.DisplayName}…";
            await provider.UpdateAsync(link.FileId, path);
            StatusText = $"Saved and uploaded to {provider.DisplayName}.";
            ToastService.Instance.Success($"Uploaded to {provider.DisplayName}.");
        }
        catch (Exception ex)
        {
            StatusText = "Saved on this PC; the upload failed.";
            Dialogs.AppDialog.ShowError($"Saved on this PC, but uploading to {provider.DisplayName} failed. Use Save to Cloud to try again.", ex);
        }
        finally { _uploading = false; }
    }
}
