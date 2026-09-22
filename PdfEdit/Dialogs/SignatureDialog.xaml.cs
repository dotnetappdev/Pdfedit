using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using PdfEdit.Models;
using PdfEdit.Services;

namespace PdfEdit.Dialogs;

public partial class SignatureDialog : Window
{
    private byte[]? _imageBytes;

    // Script/cursive fonts to offer in the Type tab
    private static readonly string[] ScriptFonts =
    {
        "Segoe Script", "Brush Script MT", "Comic Sans MS",
        "Lucida Handwriting", "Palatino Linotype", "Georgia"
    };

    public SignatureData? Result { get; private set; }

    // When true, the dialog is for creating initials instead of a full signature
    public bool InitialsMode
    {
        set
        {
            if (value)
            {
                Title = "Create Initials";
                LibraryNameTb.Text = "My Initials";
                ApplyBtn.Content = "Apply Initials";
            }
        }
    }

    public SignatureDialog()
    {
        InitializeComponent();

        // Ink: dark blue pen, slightly thick
        InkArea.DefaultDrawingAttributes = new DrawingAttributes
        {
            Color = Colors.DarkBlue,
            Width = 3,
            Height = 3,
            FitToCurve = true,
        };

        // Populate font list
        foreach (var f in ScriptFonts)
            FontStyleBox.Items.Add(f);
        FontStyleBox.SelectedIndex = 0;
    }

    // ── Type tab ────────────────────────────────────────────────────────────

    private void TypedNameBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (SignaturePreview == null) return;
        SignaturePreview.Text = string.IsNullOrWhiteSpace(TypedNameBox.Text)
            ? "Your Name Here"
            : TypedNameBox.Text;
    }

    private void FontStyleBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SignaturePreview == null) return;
        if (FontStyleBox.SelectedItem is string font)
            SignaturePreview.FontFamily = new FontFamily(font);
    }

    // ── Image tab ───────────────────────────────────────────────────────────

    private void BrowseImage_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Select Signature Image",
            Filter = "Image Files (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp"
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            _imageBytes = File.ReadAllBytes(dlg.FileName);
            var bmp = new BitmapImage(new Uri(dlg.FileName));
            ImagePreview.Source = bmp;
            ImagePathLabel.Text = Path.GetFileName(dlg.FileName);
        }
        catch (Exception ex)
        {
            AppDialog.ShowError("Failed to load image.", ex);
        }
    }

    // ── Buttons ─────────────────────────────────────────────────────────────

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        switch (TabCtrl.SelectedIndex)
        {
            case 0: TypedNameBox.Clear(); break;
            case 1: InkArea.Strokes.Clear(); break;
            case 2:
                _imageBytes = null;
                ImagePreview.Source = null;
                ImagePathLabel.Text = "No image selected";
                break;
        }
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            byte[]? bytes = TabCtrl.SelectedIndex switch
            {
                0 => RenderTypedToBytes(),
                1 => RenderInkToBytes(),
                2 => _imageBytes,
                _ => null
            };

            if (bytes == null || bytes.Length == 0)
            {
                AppDialog.ShowInfo("Please provide a signature before clicking Apply.", "No Signature");
                return;
            }

            // Tab order: 0=Type, 1=Draw, 2=Image
            Result = new SignatureData
            {
                Mode = TabCtrl.SelectedIndex switch
                {
                    0 => SignatureMode.Type,
                    1 => SignatureMode.Draw,
                    _ => SignatureMode.Image,
                },
                ImageBytes = bytes
            };

            // Save to library if requested
            if (SaveToLibraryCb.IsChecked == true && !string.IsNullOrWhiteSpace(LibraryNameTb.Text))
            {
                SignatureStore.Add(new SavedSignature
                {
                    Name = LibraryNameTb.Text.Trim(),
                    ImageBytes = bytes!
                });
            }

            DialogResult = true;
        }
        catch (Exception ex)
        {
            AppDialog.ShowError("Failed to capture signature.", ex);
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    // ── Rendering helpers ────────────────────────────────────────────────────

    private byte[] RenderInkToBytes()
    {
        if (InkArea.Strokes.Count == 0) return Array.Empty<byte>();

        int w = Math.Max(10, (int)InkArea.ActualWidth);
        int h = Math.Max(10, (int)InkArea.ActualHeight);

        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);

        // Render white background first
        var bg = new DrawingVisual();
        using (var dc = bg.RenderOpen())
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, w, h));
        rtb.Render(bg);
        rtb.Render(InkArea);

        return EncodePng(rtb);
    }

    private byte[] RenderTypedToBytes()
    {
        string text = TypedNameBox.Text.Trim();
        if (string.IsNullOrEmpty(text)) return Array.Empty<byte>();

        string fontName = FontStyleBox.SelectedItem as string ?? "Segoe Script";

        var tb = new TextBlock
        {
            Text = text,
            FontFamily = new FontFamily(fontName),
            FontSize = 52,
            Foreground = Brushes.DarkBlue,
            Background = Brushes.White,
            Padding = new Thickness(12, 8, 12, 8),
        };
        tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        tb.Arrange(new Rect(tb.DesiredSize));

        int w = Math.Max(10, (int)tb.DesiredSize.Width);
        int h = Math.Max(10, (int)tb.DesiredSize.Height);

        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(tb);
        return EncodePng(rtb);
    }

    private static byte[] EncodePng(RenderTargetBitmap rtb)
    {
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using var ms = new MemoryStream();
        enc.Save(ms);
        return ms.ToArray();
    }
}
