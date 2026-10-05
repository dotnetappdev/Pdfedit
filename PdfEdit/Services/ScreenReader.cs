using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;

namespace PdfEdit.Services;

/// <summary>Talks to screen readers (Narrator, NVDA, JAWS) through UI Automation.</summary>
public static class ScreenReader
{
    private const uint SPI_GETSCREENREADER = 0x0046;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SystemParametersInfo(uint action, uint param, ref bool value, uint winIni);

    /// <summary>True when Windows reports a screen reader running (Narrator, NVDA, JAWS and others set this).</summary>
    public static bool IsRunning
    {
        get
        {
            try { bool on = false; return SystemParametersInfo(SPI_GETSCREENREADER, 0, ref on, 0) && on; }
            catch { return false; }
        }
    }

    /// <summary>
    /// Asks the screen reader to say <paramref name="text"/>. A newer announcement replaces one
    /// that hasn't been read yet, so scrolling through pages doesn't queue up a backlog.
    /// </summary>
    public static void Announce(UIElement source, string text, string activity = "PdfEdit.Status")
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        try
        {
            var peer = UIElementAutomationPeer.FromElement(source) ?? UIElementAutomationPeer.CreatePeerForElement(source);
            peer?.RaiseNotificationEvent(AutomationNotificationKind.Other,
                AutomationNotificationProcessing.ImportantMostRecent, text, activity);
        }
        catch { /* no UI Automation client listening */ }
    }
}
