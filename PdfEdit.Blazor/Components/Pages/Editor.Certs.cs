using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;
using PdfEdit.Blazor.Components.Editor;
using PdfEdit.Services;

namespace PdfEdit.Blazor.Components.Pages;

/// <summary>
/// Certificate signatures (Acrobat's "Use a certificate"), like the Windows app: sign with a .pfx /
/// .p12 Digital ID or a self-signed one made here, optionally timestamped, and check the
/// signatures in a PDF. The certificate stays in this session's memory only.
/// </summary>
public partial class Editor
{
    public X509Certificate2? SigningCert { get; private set; }
    public List<SignatureCheck>? SignatureChecks { get; private set; }
    public List<(string Name, int Page)> EmptySignatureFields { get; private set; } = new();

    public string SigningCertDescription => SigningCert == null ? "" : DigitalSignatureService.Describe(SigningCert);

    /// <summary>Loads a Digital ID (.pfx / .p12). Returns an error message, or null when it worked.</summary>
    public async Task<string?> LoadCertificateAsync(IBrowserFile file, string password)
    {
        try
        {
            using var ms = new MemoryStream();
            await using (var s = file.OpenReadStream(5 * 1024 * 1024)) await s.CopyToAsync(ms);
            var cert = DigitalSignatureService.LoadPfx(ms.ToArray(), password);
            if (!cert.HasPrivateKey) { cert.Dispose(); return "That file has no private key, so it can't sign."; }
            SigningCert?.Dispose();
            SigningCert = cert;
            return null;
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return "The password is wrong, or that isn't a Digital ID file (.pfx or .p12).";
        }
    }

    /// <summary>Makes a self-signed Digital ID, uses it, and downloads it so it can be used again.</summary>
    public async Task CreateDigitalIdAsync(string name, string email, string organisation, string password)
    {
        var pfx = DigitalSignatureService.CreateDigitalIdPfx(name, email, organisation, password);
        SigningCert?.Dispose();
        SigningCert = DigitalSignatureService.LoadPfx(pfx, password);
        var url = await Store.StageDownloadAsync($"{name} (Digital ID).pfx", path => File.WriteAllBytes(path, pfx));
        await JS.InvokeVoidAsync("pdfedit.download", url);
        Toast("Digital ID created. Keep the downloaded .pfx file and its password to sign with it again.", "success");
    }

    public void PrepareCertSign()
    {
        EmptySignatureFields = Doc == null ? new() : DigitalSignatureService.EmptySignatureFields(Doc.CurrentPath);
    }

    /// <summary>
    /// Signs the PDF: in <paramref name="fieldName"/>, in a box at the bottom right of the current
    /// page (<paramref name="visible"/>), or invisibly. The signed PDF downloads straight away.
    /// </summary>
    public async Task SignWithCertificateAsync(string? fieldName, bool visible, string reason, string location, string contact,
        bool certify, string? timestampUrl, bool withImage)
    {
        if (Doc == null || SigningCert == null) return;
        var cert = SigningCert;
        int page = _page;
        var (w, _) = PageSize(page);
        var image = withImage ? SignaturePng : null;
        await RunAsync("Signing…", async () =>
        {
            await CommitPendingAsync();
            var req = new SignRequest
            {
                Certificate = cert,
                FieldName = fieldName,
                Page = page + 1,
                Rect = fieldName == null && visible ? new iText.Kernel.Geom.Rectangle((float)(w - 36 - 220), 36, 220, 64) : null,
                Reason = reason, Location = location, Contact = contact, Certify = certify,
                TimestampUrl = string.IsNullOrWhiteSpace(timestampUrl) ? null : timestampUrl.Trim(),
                SignatureImage = image,
            };
            await Store.ApplyAsync(Doc, (src, dest) => DigitalSignatureService.Sign(src, dest, req));
            LoadValues();
            _observePages = true;
            await JS.InvokeVoidAsync("pdfedit.download", $"/documents/{Doc.Id}/file?v={Doc.Version}");
            Doc.IsModified = false;
            SignatureChecks = DigitalSignatureService.Verify(Doc.CurrentPath);
            Status("Signed and downloaded. Changing the PDF here afterwards makes the signature invalid.");
            Toast("Signed with " + cert.GetNameInfo(X509NameType.SimpleName, false) + ". The signed PDF has been downloaded.", "success");
        });
    }

    public async Task CheckSignaturesAsync()
    {
        if (Doc == null) return;
        var path = Doc.CurrentPath;
        SignatureChecks = await Task.Run(() => DigitalSignatureService.Verify(path));
        _dialog = DialogKind.Signatures;
    }
}
