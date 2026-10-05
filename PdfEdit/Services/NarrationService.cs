using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Media.SpeechSynthesis;

namespace PdfEdit.Services;

/// <summary>
/// PdfEdit's own voice, for people who want things read out without running a screen reader:
/// announcements, the control that has keyboard focus, and tooltips. Uses the Windows voices.
/// </summary>
public static class NarrationService
{
    private static SpeechSynthesizer? _synth;
    private static MediaPlayer? _player;
    private static int _generation;
    private static bool _registered;

    public static IReadOnlyList<string> Voices()
    {
        try { return SpeechSynthesizer.AllVoices.Select(v => v.DisplayName).ToList(); }
        catch { return Array.Empty<string>(); }
    }

    /// <summary>Whether PdfEdit's voice should speak now (not over a screen reader, not over Read Aloud).</summary>
    public static bool CanSpeak =>
        !(AppSettings.Current.NarrateOnlyWithoutScreenReader && ScreenReader.IsRunning)
        && !ReadAloudService.Instance.IsReading;

    /// <summary>Says <paramref name="text"/>, cutting off anything still being said.</summary>
    public static async void Speak(string? text, bool evenIfQuiet = false)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        if (!evenIfQuiet && !CanSpeak) return;
        int generation = ++_generation;
        try
        {
            var s = AppSettings.Current;
            _synth ??= new SpeechSynthesizer();
            _synth.Voice = SpeechSynthesizer.AllVoices.FirstOrDefault(v => v.DisplayName == s.NarrationVoice) ?? SpeechSynthesizer.DefaultVoice;
            _synth.Options.SpeakingRate = Math.Clamp(s.NarrationRate, 0.5, 3.0);
            _synth.Options.AudioVolume = Math.Clamp(s.NarrationVolume, 0.0, 1.0);
            var stream = await _synth.SynthesizeTextToStreamAsync(text);
            if (generation != _generation) { stream.Dispose(); return; }   // something newer came along
            _player ??= new MediaPlayer();
            _player.Source = MediaSource.CreateFromStream(stream, stream.ContentType);
            _player.Play();
        }
        catch { /* no Windows voice installed */ }
    }

    public static void Stop()
    {
        _generation++;
        try { _player?.Pause(); } catch { }
    }

    /// <summary>Hooks keyboard focus and tooltips in every window. Call once at startup.</summary>
    public static void Register()
    {
        if (_registered) return;
        _registered = true;
        EventManager.RegisterClassHandler(typeof(UIElement), Keyboard.GotKeyboardFocusEvent,
            new KeyboardFocusChangedEventHandler(OnGotKeyboardFocus), true);
        EventManager.RegisterClassHandler(typeof(FrameworkElement), FrameworkElement.ToolTipOpeningEvent,
            new ToolTipEventHandler(OnToolTipOpening), true);
    }

    private static void OnGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (!AppSettings.Current.NarrateFocus || !ReferenceEquals(sender, e.NewFocus) || sender is not UIElement el) return;
        Speak(Describe(el));
    }

    private static void OnToolTipOpening(object sender, ToolTipEventArgs e)
    {
        if (!AppSettings.Current.NarrateTooltips || !ReferenceEquals(sender, e.OriginalSource) || sender is not FrameworkElement fe) return;
        string? text = fe.ToolTip switch
        {
            string str => str,
            ToolTip { Content: string str } => str,
            Fluent.ScreenTip tip => string.Join(". ", new[] { tip.Title, tip.Text }.Where(t => !string.IsNullOrWhiteSpace(t))),
            _ => System.Windows.Automation.AutomationProperties.GetHelpText(fe),
        };
        Speak(text);
    }

    /// <summary>"Save, button", "Theme, combo box, Dark", "Night mode, check box, checked".</summary>
    public static string Describe(UIElement el)
    {
        string name = "", type = "";
        try
        {
            var peer = UIElementAutomationPeer.FromElement(el) ?? UIElementAutomationPeer.CreatePeerForElement(el);
            if (peer != null) { name = peer.GetName(); type = peer.GetLocalizedControlType(); }
        }
        catch { }
        if (string.IsNullOrWhiteSpace(name))
            name = el switch
            {
                HeaderedItemsControl { Header: string h } => h,
                ContentControl { Content: string c } => c,
                _ => "",
            };
        name = name.Replace("_", "").Replace("\n", " ");

        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(name)) parts.Add(name);
        if (!string.IsNullOrWhiteSpace(type)) parts.Add(type);
        switch (el)
        {
            case CheckBox or RadioButton:
                var t = (ToggleButton)el;
                parts.Add(t.IsChecked == true ? "checked" : t.IsChecked == null ? "partly checked" : "not checked");
                break;
            case ToggleButton toggle:
                parts.Add(toggle.IsChecked == true ? "on" : "off");
                break;
            case ComboBox c when c.SelectedItem != null:
                parts.Add(c.Text);
                break;
        }
        if (!el.IsEnabled) parts.Add("unavailable");
        return string.Join(", ", parts);
    }
}
