using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace PdfEdit.Services;

/// <summary>
/// Flashes PdfEdit's taskbar button when a long job (OCR, converting, batch…) finishes while you're
/// in another window, until you come back to it.
/// </summary>
public static class TaskbarFlash
{
    [StructLayout(LayoutKind.Sequential)]
    private struct FlashInfo
    {
        public uint Size;
        public IntPtr Window;
        public uint Flags;
        public uint Count;
        public uint Timeout;
    }

    private const uint FlashAll = 3, FlashUntilForeground = 12;

    [DllImport("user32.dll")]
    private static extern bool FlashWindowEx(ref FlashInfo info);

    /// <summary>Flashes the main window's taskbar button, unless it's the window in use.</summary>
    public static void IfInBackground()
    {
        var window = Application.Current?.MainWindow;
        if (window == null || window.IsActive) return;
        try
        {
            var info = new FlashInfo
            {
                Size = (uint)Marshal.SizeOf<FlashInfo>(),
                Window = new WindowInteropHelper(window).Handle,
                Flags = FlashAll | FlashUntilForeground,
            };
            FlashWindowEx(ref info);
        }
        catch { /* not on Windows */ }
    }
}
