using Windows.Media.SpeechRecognition;

namespace PdfEdit.Services;

/// <summary>
/// One spoken question, recognised by Windows (Windows.Media.SpeechRecognition, dictation).
/// Needs a microphone, and "Online speech recognition" turned on in Windows Settings → Privacy &amp;
/// security → Speech.
/// </summary>
public static class VoiceInputService
{
    private static SpeechRecognizer? _current;

    public static async Task<string?> ListenOnceAsync()
    {
        using var rec = new SpeechRecognizer();
        _current = rec;
        try
        {
            rec.Timeouts.InitialSilenceTimeout = TimeSpan.FromSeconds(6);
            rec.Timeouts.EndSilenceTimeout = TimeSpan.FromSeconds(1.2);
            var compile = await rec.CompileConstraintsAsync();
            if (compile.Status != SpeechRecognitionResultStatus.Success)
                throw new InvalidOperationException($"Speech recognition couldn't start ({compile.Status}).");
            var result = await rec.RecognizeAsync();
            return result.Status == SpeechRecognitionResultStatus.Success ? result.Text : null;
        }
        catch (Exception ex) when ((uint)ex.HResult == 0x80045509)
        {
            throw new InvalidOperationException(
                "Windows needs online speech recognition turned on: Settings → Privacy & security → Speech → Online speech recognition.", ex);
        }
        catch (Exception ex) when ((uint)ex.HResult is 0x8004503A or 0x80070005)
        {
            throw new InvalidOperationException(
                "PdfEdit can't use the microphone. Check one is connected, and that Settings → Privacy & security → Microphone lets desktop apps use it.", ex);
        }
        finally { _current = null; }
    }

    public static void Cancel()
    {
        try { _ = _current?.StopRecognitionAsync(); } catch { }
    }
}
