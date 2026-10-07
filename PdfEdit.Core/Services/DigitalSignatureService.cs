using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using iText.Bouncycastle.X509;
using iText.Commons.Bouncycastle.Cert;
using iText.IO.Image;
using iText.Kernel.Geom;
using iText.Kernel.Pdf;
using iText.Signatures;

namespace PdfEdit.Services;

/// <summary>How to sign: certificate, where the signature goes and what it says.</summary>
public sealed class SignRequest
{
    public required X509Certificate2 Certificate { get; init; }
    public int Page { get; init; } = 1;
    /// <summary>Visible signature box in PDF points (null = invisible signature).</summary>
    public Rectangle? Rect { get; init; }
    /// <summary>Existing empty signature field to sign, instead of a new box.</summary>
    public string? FieldName { get; init; }
    public string Reason { get; init; } = "";
    public string Location { get; init; } = "";
    public string Contact { get; init; } = "";
    public byte[]? SignatureImage { get; init; }
    /// <summary>Certify: no further changes allowed after this signature.</summary>
    public bool Certify { get; init; }
    public string? TimestampUrl { get; init; }
}

/// <summary>A signature found in a PDF and whether it checks out.</summary>
public sealed record SignatureCheck(string FieldName, string SignedBy, DateTime SignedAt, string Reason, string Location,
                                    bool IntegrityOk, bool CoversWholeDocument, bool Trusted, string TrustNote, bool Timestamped, int Revision, int TotalRevisions);

/// <summary>
/// Certificate-based digital signatures (PAdES / CMS) like other PDF editors' "Use a certificate":
/// sign with a certificate from the Windows store, a .pfx / .p12 file or a self-signed signing ID
/// made here; optional RFC 3161 timestamp; and check the signatures already in a PDF.
/// </summary>
public static class DigitalSignatureService
{
    public static string DigitalIdFolder => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PdfEdit", "DigitalIDs");

    /// <summary>Certificates in the user's personal store that can sign (have a private key).</summary>
    public static List<X509Certificate2> StoreCertificates()
    {
        using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadOnly);
        return store.Certificates.Cast<X509Certificate2>()
            .Where(c => c.HasPrivateKey && DateTime.Now <= c.NotAfter && CanSign(c))
            .OrderBy(c => c.GetNameInfo(X509NameType.SimpleName, false))
            .ToList();
    }

    private static bool CanSign(X509Certificate2 c)
    {
        var ku = c.Extensions.OfType<X509KeyUsageExtension>().FirstOrDefault();
        return ku == null || ku.KeyUsages.HasFlag(X509KeyUsageFlags.DigitalSignature) || ku.KeyUsages.HasFlag(X509KeyUsageFlags.NonRepudiation);
    }

    public static X509Certificate2 LoadPfx(string path, string password) =>
        X509CertificateLoader.LoadPkcs12FromFile(path, password, X509KeyStorageFlags.Exportable | X509KeyStorageFlags.EphemeralKeySet);

    public static string Describe(X509Certificate2 c) =>
        $"{c.GetNameInfo(X509NameType.SimpleName, false)} — issued by {c.GetNameInfo(X509NameType.SimpleName, true)}, valid until {c.NotAfter:d}";

    /// <summary>Creates a self-signed signing ID (RSA 2048, 5 years) saved as a password-protected .pfx.</summary>
    public static string CreateDigitalId(string name, string email, string organisation, string password)
    {
        using var rsa = RSA.Create(2048);
        string dn = $"CN={Escape(name)}" + (organisation.Length > 0 ? $", O={Escape(organisation)}" : "") + (email.Length > 0 ? $", E={Escape(email)}" : "");
        var req = new CertificateRequest(dn, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        req.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.NonRepudiation, true));
        req.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        req.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(req.PublicKey, false));
        using var cert = req.CreateSelfSigned(DateTimeOffset.Now.AddMinutes(-5), DateTimeOffset.Now.AddYears(5));
        Directory.CreateDirectory(DigitalIdFolder);
        string file = System.IO.Path.Combine(DigitalIdFolder, string.Concat(name.Select(ch => System.IO.Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch)) + ".pfx");
        File.WriteAllBytes(file, cert.Export(X509ContentType.Pfx, password));
        return file;
    }

    private static string Escape(string s) => s.Replace(",", "\\,").Replace("=", "\\=");

    public static List<string> SavedDigitalIds() =>
        Directory.Exists(DigitalIdFolder) ? Directory.GetFiles(DigitalIdFolder, "*.pfx").OrderBy(f => f).ToList() : new();

    /// <summary>Empty signature fields in the PDF (name, page).</summary>
    public static List<(string Name, int Page)> EmptySignatureFields(string path)
    {
        using var pdf = new PdfDocument(new PdfReader(path));
        var util = new SignatureUtil(pdf);
        return util.GetBlankSignatureNames().Select(n =>
        {
            var form = iText.Forms.PdfAcroForm.GetAcroForm(pdf, false);
            var w = form?.GetField(n)?.GetWidgets().FirstOrDefault();
            return (n, w?.GetPage() is { } pg ? pdf.GetPageNumber(pg) : 1);
        }).ToList();
    }

    // ── Signing ──────────────────────────────────────────────────────────────
    private sealed class X509Signature : IExternalSignature
    {
        private readonly X509Certificate2 _cert;
        public X509Signature(X509Certificate2 cert) => _cert = cert;
        public string GetDigestAlgorithmName() => "SHA-256";
        public string GetSignatureAlgorithmName() => _cert.GetRSAPublicKey() != null ? "RSA" : "ECDSA";
        public ISignatureMechanismParams? GetSignatureMechanismParameters() => null;
        public byte[] Sign(byte[] message)
        {
            using var rsa = _cert.GetRSAPrivateKey();
            if (rsa != null) return rsa.SignData(message, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using var ec = _cert.GetECDsaPrivateKey() ?? throw new InvalidOperationException("This certificate has no usable private key.");
            return ec.SignData(message, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
        }
    }

    private static IX509Certificate[] Chain(X509Certificate2 cert)
    {
        var parser = new Org.BouncyCastle.X509.X509CertificateParser();
        var list = new List<IX509Certificate> { new X509CertificateBC(parser.ReadCertificate(cert.RawData)) };
        using var chain = new X509Chain();
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.ChainPolicy.VerificationFlags = X509VerificationFlags.AllowUnknownCertificateAuthority;
        if (chain.Build(cert))
            foreach (var e in chain.ChainElements.Cast<X509ChainElement>().Skip(1))
                list.Add(new X509CertificateBC(parser.ReadCertificate(e.Certificate.RawData)));
        return list.ToArray();
    }

    public static void Sign(string inputPath, string outputPath, SignRequest req)
    {
        using var reader = new PdfReader(inputPath);
        using var output = new FileStream(outputPath, FileMode.Create, FileAccess.Write);
        // Append mode keeps earlier signatures valid.
        var signer = new PdfSigner(reader, output, new StampingProperties().UseAppendMode());

        var appearance = signer.GetSignatureAppearance()
            .SetReason(req.Reason)
            .SetLocation(req.Location)
            .SetContact(req.Contact);
        if (req.FieldName != null)
            signer.SetFieldName(req.FieldName);
        else
        {
            signer.SetFieldName($"Signature{DateTime.Now:yyyyMMddHHmmss}");
            if (req.Rect != null) appearance.SetPageRect(req.Rect).SetPageNumber(req.Page);
        }
        string who = req.Certificate.GetNameInfo(X509NameType.SimpleName, false);
        appearance.SetLayer2Text($"Digitally signed by {who}\nDate: {DateTime.Now:yyyy.MM.dd HH:mm:ss zzz}"
                                 + (req.Reason.Length > 0 ? $"\nReason: {req.Reason}" : "")
                                 + (req.Location.Length > 0 ? $"\nLocation: {req.Location}" : ""));
        if (req.SignatureImage != null)
        {
            appearance.SetSignatureGraphic(ImageDataFactory.Create(req.SignatureImage));
            appearance.SetRenderingMode(PdfSignatureAppearance.RenderingMode.GRAPHIC_AND_DESCRIPTION);
        }
        if (req.Certify) signer.SetCertificationLevel(PdfSigner.CERTIFIED_NO_CHANGES_ALLOWED);

        ITSAClient? tsa = string.IsNullOrWhiteSpace(req.TimestampUrl) ? null : new TSAClientBouncyCastle(req.TimestampUrl);
        signer.SignDetached(new X509Signature(req.Certificate), Chain(req.Certificate), null, null, tsa, 0, PdfSigner.CryptoStandard.CMS);
    }

    // ── Checking ─────────────────────────────────────────────────────────────
    public static List<SignatureCheck> Verify(string path)
    {
        var result = new List<SignatureCheck>();
        using var pdf = new PdfDocument(new PdfReader(path));
        var util = new SignatureUtil(pdf);
        int total = util.GetTotalRevisions();
        foreach (var name in util.GetSignatureNames())
        {
            var pk = util.ReadSignatureData(name);
            bool integrity;
            try { integrity = pk.VerifySignatureIntegrityAndAuthenticity(); } catch { integrity = false; }
            var signing = pk.GetSigningCertificate();
            string signedBy = signing.GetSubjectDN().ToString();
            var cn = signedBy.Split(',').Select(p => p.Trim()).FirstOrDefault(p => p.StartsWith("CN=", StringComparison.OrdinalIgnoreCase));
            if (cn != null) signedBy = cn[3..];

            bool trusted = false;
            string note;
            try
            {
                using var x = X509CertificateLoader.LoadCertificate(signing.GetEncoded());
                using var chain = new X509Chain();
                chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                trusted = chain.Build(x);
                note = trusted ? "The signer's certificate is trusted on this PC."
                     : x.Subject == x.Issuer ? "Self-signed certificate — trust it only if you know where it came from."
                     : "The certificate isn't trusted on this PC: " + string.Join("; ", chain.ChainStatus.Select(s => s.StatusInformation.Trim()).Distinct());
            }
            catch (Exception ex) { note = "Couldn't check the certificate: " + ex.Message; }

            result.Add(new SignatureCheck(name, signedBy, pk.GetSignDate(), pk.GetReason() ?? "", pk.GetLocation() ?? "",
                integrity, util.SignatureCoversWholeDocument(name), trusted, note, pk.GetTimeStampTokenInfo() != null,
                util.GetRevision(name), total));
        }
        return result;
    }
}
