using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PdfEdit.Services;

namespace PdfEdit.Dialogs;

/// <summary>the usual Signature panel: each signature, whether it's intact, trusted and timestamped.</summary>
public partial class SignaturesDialog : Window
{
    public SignaturesDialog(IReadOnlyList<SignatureCheck> checks)
    {
        InitializeComponent();
        bool allOk = checks.All(c => c.IntegrityOk);
        Summary.Text = allOk
            ? $"Signed and all {checks.Count} signature(s) are intact."
            : "At least one signature is invalid — the document changed after it was signed, or the signature is damaged.";
        Summary.Foreground = new SolidColorBrush(allOk ? Color.FromRgb(0x4A, 0xCA, 0x6A) : Color.FromRgb(0xE5, 0x53, 0x4B));

        var dim = (Brush)FindResource("DimForegroundBrush");
        foreach (var c in checks.OrderBy(c => c.Revision))
        {
            string status = !c.IntegrityOk ? "✕ Invalid — the signed content was changed"
                          : c.CoversWholeDocument ? "✓ Valid — the document hasn't changed since it was signed"
                          : "✓ Valid — but the document was changed after this signature (later revisions)";
            var card = new Border
            {
                Background = (Brush)FindResource("SidebarBgBrush"), CornerRadius = new CornerRadius(6), Padding = new Thickness(12, 10, 12, 10),
                Margin = new Thickness(0, 0, 0, 8),
            };
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock { Text = $"{c.SignedBy}", FontWeight = FontWeights.SemiBold, FontSize = 14 });
            sp.Children.Add(new TextBlock { Text = status, Margin = new Thickness(0, 4, 0, 0),
                Foreground = new SolidColorBrush(c.IntegrityOk ? Color.FromRgb(0x4A, 0xCA, 0x6A) : Color.FromRgb(0xE5, 0x53, 0x4B)) });
            sp.Children.Add(new TextBlock { Text = (c.Trusted ? "✓ " : "⚠ ") + c.TrustNote, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0),
                Foreground = c.Trusted ? dim : new SolidColorBrush(Color.FromRgb(0xE3, 0xB3, 0x41)) });
            string details = $"Signed {c.SignedAt:g}" + (c.Timestamped ? " (trusted timestamp)" : " (time from the signer's PC)")
                           + (c.Reason.Length > 0 ? $" · Reason: {c.Reason}" : "") + (c.Location.Length > 0 ? $" · {c.Location}" : "")
                           + $" · Field: {c.FieldName} · Revision {c.Revision} of {c.TotalRevisions}";
            sp.Children.Add(new TextBlock { Text = details, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0), FontSize = 11, Foreground = dim });
            card.Child = sp;
            List.Children.Add(card);
        }
    }
}
