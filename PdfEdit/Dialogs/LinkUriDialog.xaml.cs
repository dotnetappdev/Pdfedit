using System.Windows;
using PdfEdit.Services;

namespace PdfEdit.Dialogs;

/// <summary>Add / edit a hyperlink: web page, email, phone number or a page in this document.</summary>
public partial class LinkUriDialog : Window
{
    private readonly int _pageCount;

    public LinkTarget? Target { get; private set; }
    public bool ShowBorder => BorderBox.IsChecked == true;
    public bool RemoveRequested { get; private set; }

    /// <summary>Kept for older callers: the web / mailto address.</summary>
    public string Uri => Target?.Uri ?? string.Empty;

    public LinkUriDialog() : this(1) { }

    public LinkUriDialog(int pageCount, PdfLinkInfo? existing = null)
    {
        _pageCount = Math.Max(1, pageCount);
        InitializeComponent();
        PageOf.Text = $"of {_pageCount}";

        if (existing != null)
        {
            Title = "Edit Link";
            OkBtn.Content = "Save";
            RemoveBtn.Visibility = Visibility.Visible;
            BorderBox.IsChecked = existing.HasBorder;
            var t = existing.Target;
            if (t.PageNumber is { } p) { KindPage.IsChecked = true; ValueBox.Text = p.ToString(); }
            else if (t.Uri is { } u && u.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)) { KindEmail.IsChecked = true; ValueBox.Text = u[7..]; }
            else if (t.Uri is { } ph && ph.StartsWith("tel:", StringComparison.OrdinalIgnoreCase)) { KindPhone.IsChecked = true; ValueBox.Text = ph[4..]; }
            else ValueBox.Text = t.Uri ?? "";
        }
        else ValueBox.Text = "https://";

        UpdateKind();
        Loaded += (_, _) => { ValueBox.Focus(); ValueBox.SelectAll(); };
    }

    private void Kind_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        ValueBox.Text = KindWeb.IsChecked == true ? "https://" : KindPage.IsChecked == true ? "1" : "";
        UpdateKind();
        ValueBox.Focus();
        ValueBox.SelectAll();
    }

    private void UpdateKind()
    {
        ValueLabel.Text = KindWeb.IsChecked == true ? "Web address (URL)"
            : KindEmail.IsChecked == true ? "Email address"
            : KindPhone.IsChecked == true ? "Phone number"
            : "Page number";
        PageOf.Visibility = KindPage.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        ErrorText.Visibility = Visibility.Collapsed;
    }

    private void Fail(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
        ValueBox.Focus();
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        string v = ValueBox.Text.Trim();
        if (KindPage.IsChecked == true)
        {
            if (!int.TryParse(v, out int p) || p < 1 || p > _pageCount) { Fail($"Enter a page number from 1 to {_pageCount}."); return; }
            Target = LinkTarget.ToPage(p);
        }
        else if (KindEmail.IsChecked == true)
        {
            if (v.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)) v = v[7..];
            if (!v.Contains('@') || v.Contains(' ')) { Fail("Enter an email address, e.g. name@example.com."); return; }
            Target = LinkTarget.ToUri("mailto:" + v);
        }
        else if (KindPhone.IsChecked == true)
        {
            string digits = new(v.Where(c => char.IsDigit(c) || c == '+').ToArray());
            if (digits.Length < 3) { Fail("Enter a phone number."); return; }
            Target = LinkTarget.ToUri("tel:" + digits);
        }
        else
        {
            if (v is "" or "https://" or "http://") { Fail("Enter a web address, e.g. https://example.com."); return; }
            if (!v.Contains("://") && !v.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)) v = "https://" + v;
            if (!System.Uri.TryCreate(v, UriKind.Absolute, out _)) { Fail("That doesn't look like a web address."); return; }
            Target = LinkTarget.ToUri(v);
        }
        DialogResult = true;
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        RemoveRequested = true;
        DialogResult = true;
    }
}
