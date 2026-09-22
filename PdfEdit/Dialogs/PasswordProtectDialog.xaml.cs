using System.Windows;

namespace PdfEdit.Dialogs;

public partial class PasswordProtectDialog : Window
{
    public string UserPassword  { get; private set; } = string.Empty;
    public string OwnerPassword { get; private set; } = string.Empty;
    public bool AllowPrinting   { get; private set; } = true;
    public bool AllowCopying    { get; private set; } = false;

    public PasswordProtectDialog() => InitializeComponent();

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        string pwd  = UserPasswordBox.Password;
        string conf = UserPasswordConfirmBox.Password;

        if (string.IsNullOrEmpty(pwd))
        {
            ShowError("Please enter an open password.");
            return;
        }
        if (pwd != conf)
        {
            ShowError("Open passwords do not match.");
            return;
        }

        UserPassword  = pwd;
        OwnerPassword = OwnerPasswordBox.Password;
        AllowPrinting = AllowPrintingBox.IsChecked == true;
        AllowCopying  = AllowCopyingBox.IsChecked  == true;
        DialogResult  = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void ShowError(string message)
    {
        ErrorText.Text       = message;
        ErrorText.Visibility = Visibility.Visible;
    }
}
