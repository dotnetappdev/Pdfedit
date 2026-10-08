using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Media.SpeechSynthesis;

namespace PdfEdit.Services;

/// <summary>
/// Read Aloud (Acrobat View → Read Out Loud): speaks page text with Windows' built-in voices.
/// Reads page by page so it can stop, and reports which page it's on.
/// </summary>
public sealed class ReadAloudService
{
    public static ReadAloudService Instance { get; } = new();

    private MediaPlayer? _player;
    private CancellationTokenSource? _cts;

    public bool IsReading => _cts != null;
    public event Action<bool>? ReadingChanged;

    public static IReadOnlyList<string> Voices() =>
        SpeechSynthesizer.AllVoices.Select(v => v.DisplayName).ToList();

    /// <summary>Reads the given pages' text in order (calls <paramref name="onPage"/> before each).</summary>
    public async Task ReadAsync(IReadOnlyList<(int Page, string Text)> pages, Action<int>? onPage, string? voice = null, double rate = 1.0, double volume = 1.0)
    {
        Stop();
        var cts = _cts = new CancellationTokenSource();
        KeepAwake.Set("reading", true);
        ReadingChanged?.Invoke(true);
        try
        {
            using var synth = new SpeechSynthesizer();
            var v = SpeechSynthesizer.AllVoices.FirstOrDefault(x => x.DisplayName == voice);
            if (v != null) synth.Voice = v;
            synth.Options.SpeakingRate = Math.Clamp(rate, 0.5, 3.0);
            synth.Options.AudioVolume = Math.Clamp(volume, 0.0, 1.0);
            foreach (var (page, text) in pages)
            {
                if (cts.IsCancellationRequested) break;
                if (string.IsNullOrWhiteSpace(text)) continue;
                onPage?.Invoke(page);
                var stream = await synth.SynthesizeTextToStreamAsync(text);
                var done = new TaskCompletionSource();
                _player = new MediaPlayer { AutoPlay = false };
                _player.MediaEnded += (_, _) => done.TrySetResult();
                _player.MediaFailed += (_, _) => done.TrySetResult();
                _player.Source = MediaSource.CreateFromStream(stream, stream.ContentType);
                using (cts.Token.Register(() => done.TrySetResult()))
                {
                    _player.Play();
                    await done.Task;
                }
                _player.Dispose();
                _player = null;
            }
        }
        finally
        {
            if (ReferenceEquals(_cts, cts)) { _cts = null; KeepAwake.Set("reading", false); ReadingChanged?.Invoke(false); }
        }
    }

    public void Stop()
    {
        _cts?.Cancel();
        try { _player?.Pause(); } catch { }
    }
}
