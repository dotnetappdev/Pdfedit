using System.IO;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using PdfEdit.Models;
using PdfEdit.Services;

namespace PdfEdit.ViewModels;

/// <summary>Certificate (digital) signatures: sign with a Digital ID and check existing signatures.</summary>
public partial class MainViewModel
{
    private ICommand? _certSignCommand, _verifySignaturesCommand;
    private Dialogs.CertSignDialog? _pendingCertSign;

    /// <summary>Sign with a certificate (Digital ID), like Acrobat's "Use a certificate".</summary>
    public ICommand CertSignCommand => _certSignCommand ??= new AsyncRelayCommand(async () =>
    {
        if (_currentFilePath == null) return;
        List<(string, int)> empty;
        try { empty = DigitalSignatureService.EmptySignatureFields(_currentFilePath); } catch { empty = new(); }
        var dlg = new Dialogs.CertSignDialog(empty) { Owner = Application.Current.MainWindow };
        if (dlg.ShowDialog() != true) return;
        if (dlg.DrawBox)
        {
            _pendingCertSign = dlg;
            ActiveTool = ActiveTool.DigitalSignature;
            StatusText = "Drag a box on the page where the signature should go (Esc to cancel).";
            ToastService.Instance.Info("Drag a box where the signature should go.");
            return;
        }
        _pendingCertSign = dlg;
        await CompleteCertificateSignAsync(1, 0, 0, 0, 0);
    }, () => HasDocument);

    /// <summary>Called when the signature box has been drawn (or straight away for a field / invisible signature).</summary>
    public async Task CompleteCertificateSignAsync(int page, double left, double bottom, double width, double height)
    {
        var dlg = _pendingCertSign;
        _pendingCertSign = null;
        if (ActiveTool == ActiveTool.DigitalSignature) ActiveTool = ActiveTool.Select;
        if (dlg?.Certificate == null || _currentFilePath == null) return;

        var save = new SaveFileDialog
        {
            Title = "Save Signed PDF",
            Filter = "PDF Files (*.pdf)|*.pdf",
            FileName = Path.GetFileNameWithoutExtension(_currentFilePath) + "_signed.pdf",
            InitialDirectory = Path.GetDirectoryName(_currentFilePath),
        };
        if (save.ShowDialog() != true) return;
        string dest = save.FileName;
        if (string.Equals(dest, _currentFilePath, StringComparison.OrdinalIgnoreCase))
        {
            Dialogs.AppDialog.ShowInfo("Save the signed copy under a different name — the open file is being read while signing.", "Sign");
            return;
        }

        string tmp = Path.Combine(Path.GetTempPath(), $"pdfedit-sign-{Guid.NewGuid():N}.pdf");
        try
        {
            IsLoading = true;
            StatusText = "Signing…";
            if (IsDesignMode) SyncDesignFieldsToLive();
            // Everything added so far goes into the PDF first, then the signature seals it.
            _formService.SaveFull(_currentFilePath, tmp, FieldValues,
                _pageRotations, FreeTextAnnotations, PlacedSignatures, flatten: false,
                deletedFieldNames: DeletedFieldNames, fieldExportValues: BuildExportValuesForSave(),
                highlightAnnotations: HighlightAnnotations, stickyNotes: StickyNotes,
                shapeAnnotations: ShapeAnnotations, fieldBounds: ModifiedFieldBounds,
                fieldEdits: GetFieldEditsForSave(), textEdits: TextEditMarks);

            var req = new SignRequest
            {
                Certificate = dlg.Certificate, Page = page, FieldName = dlg.FieldName,
                Rect = dlg.DrawBox && width > 4 && height > 4 ? new iText.Kernel.Geom.Rectangle((float)left, (float)bottom, (float)width, (float)height) : null,
                Reason = dlg.Reason, Location = dlg.Location, Contact = dlg.Contact,
                SignatureImage = dlg.SignatureImage, Certify = dlg.Certify, TimestampUrl = dlg.TimestampUrl,
            };
            try
            {
                await Task.Run(() => DigitalSignatureService.Sign(tmp, dest, req));
            }
            catch (Exception ex) when (req.TimestampUrl != null && ex.ToString().Contains("TSA", StringComparison.OrdinalIgnoreCase)
                                       || ex is System.Net.Http.HttpRequestException || ex.InnerException is System.Net.Http.HttpRequestException)
            {
                if (!Dialogs.AppDialog.ShowConfirm($"The timestamp server couldn't be reached ({ex.Message}).\n\nSign without a timestamp?", "Timestamp", "Sign without", "Cancel"))
                    return;
                var noTs = new SignRequest
                {
                    Certificate = req.Certificate, Page = req.Page, FieldName = req.FieldName, Rect = req.Rect, Reason = req.Reason,
                    Location = req.Location, Contact = req.Contact, SignatureImage = req.SignatureImage, Certify = req.Certify,
                };
                await Task.Run(() => DigitalSignatureService.Sign(tmp, dest, noTs));
            }
            await LoadDocumentAsync(dest);
            StatusText = $"Signed by {dlg.Certificate.GetNameInfo(System.Security.Cryptography.X509Certificates.X509NameType.SimpleName, false)}.";
            ToastService.Instance.Success("Document signed.");
        }
        catch (Exception ex) { Dialogs.AppDialog.ShowError("Signing failed.", ex); }
        finally
        {
            IsLoading = false;
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
        }
    }

    /// <summary>Signature panel: lists every signature and whether it is intact and trusted.</summary>
    public ICommand VerifySignaturesCommand => _verifySignaturesCommand ??= new RelayCommand(() =>
    {
        if (_currentFilePath == null) return;
        try
        {
            var checks = DigitalSignatureService.Verify(_currentFilePath);
            if (checks.Count == 0) { Dialogs.AppDialog.ShowInfo("This PDF has no digital signatures.", "Signatures"); return; }
            new Dialogs.SignaturesDialog(checks) { Owner = Application.Current.MainWindow }.ShowDialog();
        }
        catch (Exception ex) { Dialogs.AppDialog.ShowError("Couldn't check the signatures.", ex); }
    }, () => HasDocument);
}
