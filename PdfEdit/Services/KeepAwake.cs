using System.Runtime.InteropServices;

namespace PdfEdit.Services;

/// <summary>
/// Keeps the screen on (no dimming, sleep or lock) while something is being watched or listened to:
/// Read Aloud and the slide show. Windows forgets the request when the app closes.
/// </summary>
public static class KeepAwake
{
    [Flags]
    private enum ExecutionState : uint
    {
        SystemRequired = 0x00000001,
        DisplayRequired = 0x00000002,
        Continuous = 0x80000000,
    }

    [DllImport("kernel32.dll")]
    private static extern ExecutionState SetThreadExecutionState(ExecutionState state);

    private static readonly HashSet<string> Reasons = new();
    private static readonly object Gate = new();

    /// <summary>Turns a reason ("reading", "slideshow") on or off; the screen stays on while any is on.</summary>
    public static void Set(string reason, bool on)
    {
        // The request belongs to the thread that makes it: always make it on the UI thread.
        var ui = System.Windows.Application.Current?.Dispatcher;
        if (ui != null && !ui.CheckAccess()) { ui.BeginInvoke(() => Set(reason, on)); return; }
        lock (Gate)
        {
            bool changed = on ? Reasons.Add(reason) : Reasons.Remove(reason);
            if (!changed) return;
            try
            {
                SetThreadExecutionState(Reasons.Count > 0
                    ? ExecutionState.Continuous | ExecutionState.DisplayRequired | ExecutionState.SystemRequired
                    : ExecutionState.Continuous);
            }
            catch { /* not on Windows */ }
        }
    }
}
