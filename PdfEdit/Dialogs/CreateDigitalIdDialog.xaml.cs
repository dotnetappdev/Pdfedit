using System.Windows;
using PdfEdit.Services;

namespace PdfEdit.Dialogs;

public partial class CreateDigitalIdDialog : Window
{
    public string? CreatedPath { get; private set; }
    public string Password => Pw1.Password;

    public CreateDigitalIdDialog()
    {
        InitializeComponent();
        NameBox.Text = Environment.UserName;
        Loaded += (_, _) => NameBox.Focus();
    }

    private void Create_Click(object sender, RoutedEventArgs e)
    {
        string? error = NameBox.Text.Trim().Length == 0 ? "Enter your name."
                      : Pw1.Password.Length < 6 ? "Use a password of at least 6 characters."
                      : Pw1.Password != Pw2.Password ? "The passwords don't match." : null;
        if (error != null) { ErrorText.Text = error; ErrorText.Visibility = Visibility.Visible; return; }
        try
        {
            CreatedPath = DigitalSignatureService.CreateDigitalId(NameBox.Text.Trim(), EmailBox.Text.Trim(), OrgBox.Text.Trim(), Pw1.Password);
            DialogResult = true;
        }
        catch (Exception ex) { ErrorText.Text = ex.Message; ErrorText.Visibility = Visibility.Visible; }
    }
}
