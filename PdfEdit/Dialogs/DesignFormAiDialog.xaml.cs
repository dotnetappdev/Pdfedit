using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PdfEdit.Services;

namespace PdfEdit.Dialogs;

/// <summary>Describe a form ("a car damage report"), and the AI designs it for the Design canvas.</summary>
public partial class DesignFormAiDialog : Window
{
    private static readonly (string Label, string Description)[] Ideas =
    {
        ("Job application", "A job application form: personal details, the position applied for, education, employment history, two references, right to work, and a signed declaration."),
        ("Car damage report", "A vehicle damage report for a car rental company: driver and vehicle details, date, time and place of the incident, a vehicle outline to mark the damage on, a description of the damage, photos taken yes or no, other party details, and signatures."),
        ("Patient intake", "A new patient registration form for a GP surgery: personal details, emergency contact, current medications, allergies, medical history tick list, lifestyle questions, and consent signature."),
        ("Event registration", "An event registration form: attendee details, ticket type, sessions to attend, dietary requirements, accessibility needs, how they heard about us, and photo consent."),
        ("Rental inspection", "A property check-in inspection form: property address, tenant and landlord, a room-by-room condition check (good, fair, poor) with notes, meter readings, keys handed over, and signatures."),
        ("Customer feedback", "A customer feedback form: visit date, rating questions from poor to excellent, what we did well, what we could improve, would you recommend us, and optional contact details."),
        ("Incident report", "A workplace accident and incident report: person involved, date, time and location, type of incident, injuries, witnesses, first aid given, actions taken, and manager sign-off."),
    };

    private static readonly (string Name, Color Colour)[] Colours =
    {
        ("Blue", Color.FromRgb(0x1E, 0x50, 0xA0)),
        ("Teal", Color.FromRgb(0x0F, 0x76, 0x6E)),
        ("Green", Color.FromRgb(0x2E, 0x7D, 0x32)),
        ("Purple", Color.FromRgb(0x5E, 0x35, 0xB1)),
        ("Red", Color.FromRgb(0xB7, 0x1C, 0x1C)),
        ("Orange", Color.FromRgb(0xC2, 0x5E, 0x00)),
        ("Charcoal", Color.FromRgb(0x37, 0x47, 0x4F)),
    };

    private readonly Func<string, CancellationToken, Task<string>> _complete;
    private CancellationTokenSource? _cts;

    public FormSpec? Spec { get; private set; }
    public bool UseLetter => (PageSizeBox.SelectedItem as ComboBoxItem)?.Tag as string == "Letter";
    public Color Accent => Colours[Math.Max(0, ColourBox.SelectedIndex)].Colour;

    /// <param name="complete">Sends a prompt to the chosen AI and returns its whole reply.</param>
    public DesignFormAiDialog(Func<string, CancellationToken, Task<string>> complete, string? initialDescription = null)
    {
        InitializeComponent();
        _complete = complete;
        foreach (var (label, description) in Ideas)
        {
            var b = new Button
            {
                Content = label, Margin = new Thickness(0, 0, 6, 6), Padding = new Thickness(10, 3, 10, 3),
                Background = (Brush)FindResource("InputBgBrush"), Foreground = (Brush)FindResource("ForegroundBrush"),
                BorderBrush = (Brush)FindResource("InputBorderBrush"), ToolTip = description,
            };
            b.Click += (_, _) => { DescriptionBox.Text = description; DescriptionBox.Focus(); DescriptionBox.CaretIndex = description.Length; };
            IdeasPanel.Children.Add(b);
        }
        foreach (var (name, colour) in Colours)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new Border { Width = 14, Height = 14, CornerRadius = new CornerRadius(3), Background = new SolidColorBrush(colour), Margin = new Thickness(0, 0, 8, 0) });
            row.Children.Add(new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center });
            ColourBox.Items.Add(row);
        }
        ColourBox.SelectedIndex = 0;
        if (System.Globalization.RegionInfo.CurrentRegion.TwoLetterISORegionName is "US" or "CA") PageSizeBox.SelectedIndex = 1;
        if (!string.IsNullOrWhiteSpace(initialDescription)) DescriptionBox.Text = initialDescription;
        Loaded += (_, _) => DescriptionBox.Focus();
        // Esc stops a request in progress, or closes the dialog; closing it stops the request.
        PreviewKeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape) { e.Handled = true; Cancel_Click(this, e); } };
        Closing += (_, _) => _cts?.Cancel();
    }

    private void DescriptionBox_TextChanged(object sender, TextChangedEventArgs e) =>
        DesignBtn.IsEnabled = _cts == null && DescriptionBox.Text.Trim().Length >= 3;

    private async void Design_Click(object sender, RoutedEventArgs e)
    {
        string description = DescriptionBox.Text.Trim();
        if (description.Length < 3) return;
        _cts = new CancellationTokenSource();
        SetBusy(true, "Designing your form… this can take a minute with larger models.");
        try
        {
            FormSpec? spec = null;
            for (int attempt = 0; attempt < 2 && spec == null; attempt++)
            {
                string reply = await _complete(FormDesignAiService.BuildPrompt(description), _cts.Token);
                spec = FormDesignAiService.Parse(reply);
            }
            if (spec == null)
            {
                SetBusy(false, "The AI didn't send back a form PdfEdit could read. Try again, describe it differently, or pick another model.");
                return;
            }
            Spec = spec;
            if (IsVisible) DialogResult = true;
        }
        catch (OperationCanceledException) { if (IsVisible) SetBusy(false, "Stopped."); }
        catch (Exception ex) { if (IsVisible) SetBusy(false, "Couldn't design the form: " + ex.Message); }
        finally { _cts?.Dispose(); _cts = null; }
    }

    private void SetBusy(bool busy, string message)
    {
        Progress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        StatusText.Text = message;
        DesignBtn.IsEnabled = !busy;
        DescriptionBox.IsEnabled = !busy;
        IdeasPanel.IsEnabled = !busy;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        if (_cts != null) { _cts.Cancel(); return; }
        DialogResult = false;
    }
}
