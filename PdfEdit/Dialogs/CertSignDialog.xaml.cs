using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Windows;
using System.Windows.Controls;
using PdfEdit.Models;
using PdfEdit.Services;

namespace PdfEdit.Dialogs;

/// <summary>Choose a Digital ID and how the certificate signature looks, like Acrobat's "Sign with a certificate".</summary>
public partial class CertSignDialog : Window
{
    private sealed record IdChoice(string Label, X509Certificate2? Cert, string? PfxPath)
    {
        public override string ToString() => Label;
    }

    public X509Certificate2? Certificate { get; private set; }
    public string Reason => ReasonBox.Text.Trim();
    public string Location => LocationBox.Text.Trim();
    public string Contact => ContactBox.Text.Trim();
    public bool DrawBox => PlaceDraw.IsChecked == true;
    public string? FieldName => PlaceField.IsChecked == true ? FieldBox.SelectedItem as string : null;
    public byte[]? SignatureImage => UseImageBox.IsChecked == true ? (ImageBox.SelectedItem as SavedSignature)?.ImageBytes : null;
    public string? TimestampUrl => TimestampBox.IsChecked == true ? TsaBox.Text.Trim() : null;
    public bool Certify => CertifyBox.IsChecked == true;

    public CertSignDialog(IReadOnlyList<(string Name, int Page)> emptyFields)
    {
        InitializeComponent();
        foreach (var (name, page) in emptyFields) FieldBox.Items.Add(name);
        if (emptyFields.Count > 0) { FieldBox.SelectedIndex = 0; PlaceField.IsChecked = true; }
        else PlaceField.IsEnabled = false;

        var sigs = SignatureStore.Load();
        ImageBox.ItemsSource = sigs;
        if (sigs.Count > 0) { ImageBox.SelectedIndex = 0; UseImageBox.IsChecked = true; } else UseImageBox.IsEnabled = false;

        LoadIds();
    }

    private void LoadIds(string? select = null)
    {
        CertBox.Items.Clear();
        foreach (var c in DigitalSignatureService.StoreCertificates())
            CertBox.Items.Add(new IdChoice($"{c.GetNameInfo(X509NameType.SimpleName, false)}  (Windows certificate store)", c, null));
        foreach (var f in DigitalSignatureService.SavedDigitalIds())
            CertBox.Items.Add(new IdChoice($"{Path.GetFileNameWithoutExtension(f)}  (PdfEdit Digital ID)", null, f));
        if (select != null)
            CertBox.SelectedItem = CertBox.Items.OfType<IdChoice>().FirstOrDefault(i => i.PfxPath == select);
        if (CertBox.SelectedIndex < 0 && CertBox.Items.Count > 0) CertBox.SelectedIndex = 0;
        if (CertBox.Items.Count == 0) CertInfo.Text = "No Digital ID yet. Use a .pfx file from your certificate provider, or create a self-signed Digital ID.";
    }

    private void CertBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var id = CertBox.SelectedItem as IdChoice;
        PasswordRow.Visibility = id?.PfxPath != null ? Visibility.Visible : Visibility.Collapsed;
        CertInfo.Text = id?.Cert != null ? DigitalSignatureService.Describe(id.Cert)
                      : id?.PfxPath != null ? "Enter the Digital ID's password." : CertInfo.Text;
    }

    private void BrowsePfx_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Digital IDs|*.pfx;*.p12|All files|*.*", Title = "Choose a Digital ID file" };
        if (dlg.ShowDialog(this) != true) return;
        var item = new IdChoice($"{Path.GetFileName(dlg.FileName)}  (file)", null, dlg.FileName);
        CertBox.Items.Add(item);
        CertBox.SelectedItem = item;
        PasswordBox.Focus();
    }

    private void CreateId_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new CreateDigitalIdDialog { Owner = this };
        if (dlg.ShowDialog() != true || dlg.CreatedPath == null) return;
        LoadIds(dlg.CreatedPath);
        PasswordBox.Password = dlg.Password;
    }

    private void Sign_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Visibility = Visibility.Collapsed;
        var id = CertBox.SelectedItem as IdChoice;
        try
        {
            if (id == null) throw new InvalidOperationException("Choose a Digital ID first.");
            Certificate = id.Cert ?? DigitalSignatureService.LoadPfx(id.PfxPath!, PasswordBox.Password);
            if (!Certificate.HasPrivateKey) throw new InvalidOperationException("This certificate has no private key, so it can't sign.");
            if (PlaceField.IsChecked == true && FieldName == null) throw new InvalidOperationException("Choose the signature field.");
            DialogResult = true;
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            ErrorText.Text = "That password doesn't open the Digital ID.";
            ErrorText.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            ErrorText.Text = ex.Message;
            ErrorText.Visibility = Visibility.Visible;
        }
    }
}
