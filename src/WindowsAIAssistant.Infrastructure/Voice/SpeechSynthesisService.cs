using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NAudio.Wave;
using Windows.Foundation;
using Windows.Media.SpeechSynthesis;
using Windows.Storage.Streams;
using WindowsAIAssistant.Core.Abstractions.Voice;
using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Models.Voice;
using WindowsAIAssistant.Infrastructure.Configuration.Options;

namespace WindowsAIAssistant.Infrastructure.Voice;

/// <summary>
/// Speaks assistant replies with the on-device Windows synthesizer.
/// <para>
/// The synthesizer is asked for audio and the audio is played here, rather than letting the
/// platform speak directly. Two reasons: the reply is a buffer that can be cut off the moment
/// the user interrupts, and the stream can be inspected for problems before it reaches the
/// speakers. Voice selection, rate, pitch, and volume are translated from the normalized
/// numbers the contract defines, so no provider voice object crosses the Core boundary.
/// </para>
/// <para>
/// A long reply is shortened before synthesis, because reading a whole answer aloud is
/// unhelpful and would hold on much longer than the user expects. A new request supersedes
/// whatever is playing, so an interrupting command takes effect immediately.
/// </para>
/// </summary>
public sealed class SpeechSynthesisService : ISpeechSynthesisService, IDisposable
{
    private const int DefaultMaximumSpeechLength = 240;
    private const string SentenceEnd = ".";

    private readonly IOptionsMonitor<VoiceOptions> _options;
    private readonly ILogger<SpeechSynthesisService> _logger;
    private readonly SemaphoreSlim _playbackGate = new(1, 1);
    private readonly List<VoiceInformation> _voices = [];

    private SpeechSynthesizer? _synthesizer;
    private IWavePlayer? _player;
    private bool _disposed;

    public SpeechSynthesisService(
        IOptionsMonitor<VoiceOptions> options,
        ILogger<SpeechSynthesisService> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _options = options;
        _logger = logger;
    }

    /// <inheritdoc />
    public bool IsSpeaking
    {
        get
        {
            var player = _player;
            return player is not null && player.PlaybackState == PlaybackState.Playing;
        }
    }

    /// <inheritdoc />
    public bool IsAvailable
    {
        get
        {
            if (_disposed)
            {
                return false;
            }

            try
            {
                // Creating a synthesizer is the only reliable way to find out whether the
                // machine has a usable speech stack; listing the voices is cheaper still.
                return SpeechSynthesizer.AllVoices.Count > 0;
            }
            catch (Exception exception)
            {
                _logger.LogDebug(
                    "The speech synthesizer is unavailable (HRESULT 0x{Result:X8}).",
                    exception.HResult);
                return false;
            }
        }
    }

    /// <inheritdoc />
    public IReadOnlyCollection<string> AvailableVoices
    {
        get
        {
            try
            {
                return SpeechSynthesizer.AllVoices
                    .Select(voice => voice.DisplayName)
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            catch (Exception exception)
            {
                _logger.LogDebug(
                    "Listing the installed voices failed (HRESULT 0x{Result:X8}).",
                    exception.HResult);
                return [];
            }
        }
    }

    /// <inheritdoc />
    public Task<Result> SpeakAsync(string text, CancellationToken cancellationToken = default)
    {
        var settings = _options.CurrentValue;

        return SpeakAsync(
            text,
            new SpeechSynthesisOptions
            {
                Language = settings.Language,
                VoiceName = settings.SpeechVoice,
                Rate = settings.SpeechRate,
                Pitch = settings.SpeechPitch,
                Volume = settings.SpeechVolume,
                MaximumSpeechLength = settings.MaximumSpokenResponseLength
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Result> SpeakAsync(
        string text,
        SpeechSynthesisOptions options,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();

        var effective = options.Normalized();
        var maximumLength = effective.MaximumSpeechLength > 0
            ? effective.MaximumSpeechLength
            : DefaultMaximumSpeechLength;
        var spoken = Shorten(text, maximumLength);
        if (spoken.Length == 0)
        {
            return Result.Success();
        }

        await _playbackGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var synthesizer = EnsureSynthesizer();
            if (synthesizer is null)
            {
                return Result.Failure("Speech synthesis is unavailable on this device.");
            }

            ConfigureVoicing(synthesizer, effective);

            // A new request wins over whatever is playing, so an interrupting command is heard
            // straight away instead of after the current sentence finishes.
            await StopPlaybackAsync().ConfigureAwait(false);

            var stream = await synthesizer
                .SynthesizeTextToStreamAsync(spoken)
                .AsTask()
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);

            var audio = await ReadAllBytesAsync(stream).ConfigureAwait(false);
            if (audio.Length == 0)
            {
                return Result.Failure("The synthesizer produced no audio.");
            }

            await PlayAsync(audio, cancellationToken).ConfigureAwait(false);
            return Result.Success();
        }
        catch (OperationCanceledException)
        {
            return Result.Failure("Speaking was cancelled.");
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                "Speech synthesis failed (HRESULT 0x{Result:X8}).",
                exception.HResult);
            return Result.Failure("The reply could not be spoken.");
        }
        finally
        {
            _playbackGate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<Result> StopSpeakingAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        await _playbackGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await StopPlaybackAsync().ConfigureAwait(false);
            return Result.Success();
        }
        finally
        {
            _playbackGate.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            _player?.Dispose();
        }
        catch (Exception exception)
        {
            _logger.LogDebug(
                "Disposing the audio player reported HRESULT 0x{Result:X8}.",
                exception.HResult);
        }

        _player = null;
        _synthesizer?.Dispose();
        _synthesizer = null;
        _playbackGate.Dispose();
    }

    private SpeechSynthesizer? EnsureSynthesizer()
    {
        if (_synthesizer is not null)
        {
            return _synthesizer;
        }

        try
        {
            _synthesizer = new SpeechSynthesizer();
            return _synthesizer;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                "The speech synthesizer could not be created (HRESULT 0x{Result:X8}).",
                exception.HResult);
            return null;
        }
    }

    private void ConfigureVoicing(SpeechSynthesizer synthesizer, SpeechSynthesisOptions options)
    {
        // The platform rejects out-of-range values outright, so the clamped options are used
        // and anything the platform still objects to falls back to its own default.
        try
        {
            synthesizer.Options.SpeakingRate = options.Rate;
        }
        catch (ArgumentException)
        {
            synthesizer.Options.SpeakingRate = 1.0;
        }

        try
        {
            synthesizer.Options.AudioPitch = options.Pitch;
        }
        catch (ArgumentException)
        {
            synthesizer.Options.AudioPitch = 1.0;
        }

        synthesizer.Options.AudioVolume = options.Volume;

        var voice = SelectVoice(synthesizer, options);
        if (voice is not null)
        {
            synthesizer.Voice = voice;
        }
    }

    private VoiceInformation? SelectVoice(SpeechSynthesizer synthesizer, SpeechSynthesisOptions options)
    {
        if (_voices.Count == 0)
        {
            try
            {
                _voices.AddRange(SpeechSynthesizer.AllVoices);
            }
            catch (Exception exception)
            {
                _logger.LogDebug(
                    "Reading the installed voices failed (HRESULT 0x{Result:X8}).",
                    exception.HResult);
                return null;
            }
        }

        if (_voices.Count == 0)
        {
            return null;
        }

        // An explicitly named voice wins, matched on either the display name or the raw id so
        // that a configuration file can use whichever the user copied from the list.
        if (!string.IsNullOrWhiteSpace(options.VoiceName))
        {
            var named = _voices.FirstOrDefault(voice =>
                string.Equals(voice.DisplayName, options.VoiceName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(voice.Id, options.VoiceName, StringComparison.OrdinalIgnoreCase));

            if (named is not null)
            {
                return named;
            }

            _logger.LogInformation(
                "The configured voice was not installed; a language match will be used instead.");
        }

        var language = options.Language;
        return _voices.FirstOrDefault(voice =>
                   string.Equals(voice.Language, language, StringComparison.OrdinalIgnoreCase))
               ?? synthesizer.Voice;
    }

    private static async Task<byte[]> ReadAllBytesAsync(SpeechSynthesisStream stream)
    {
        using (stream)
        {
            if (stream.Size == 0)
            {
                return [];
            }

            var size = stream.Size > int.MaxValue ? int.MaxValue : (int)stream.Size;
            var reader = new DataReader(stream.GetInputStreamAt(0));
            try
            {
                var loaded = await reader.LoadAsync((uint)size).AsTask().ConfigureAwait(false);
                var bytes = new byte[loaded];
                reader.ReadBytes(bytes);
                return bytes;
            }
            finally
            {
                reader.Dispose();
            }
        }
    }

    private async Task PlayAsync(byte[] audio, CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream(audio, writable: false);
        using var reader = new WaveFileReader(stream);
        using var player = new WaveOutEvent();

        player.Init(reader);

        var finished = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        void OnStopped(object? sender, StoppedEventArgs args) => finished.TrySetResult(true);

        player.PlaybackStopped += OnStopped;
        _player = player;

        try
        {
            // A cancelled token is the caller interrupting, so playback is stopped and the
            // await returns rather than running to the end of the reply.
            using var registration = cancellationToken.Register(() =>
            {
                player.Stop();
                finished.TrySetResult(false);
            });

            player.Play();
            await finished.Task.ConfigureAwait(false);
        }
        finally
        {
            player.PlaybackStopped -= OnStopped;
            _player = null;
        }
    }

    private async Task StopPlaybackAsync()
    {
        var player = _player;
        if (player is null)
        {
            return;
        }

        _player = null;
        player.Stop();
        await Task.CompletedTask.ConfigureAwait(false);
    }

    /// <summary>
    /// Cuts a reply down to a speakable length at a sentence boundary when one is close
    /// enough, so the spoken answer ends where the written one would.
    /// </summary>
    private static string Shorten(string? text, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var collapsed = CollapseWhitespace(text);
        if (collapsed.Length <= maximumLength)
        {
            return collapsed;
        }

        var cutoff = collapsed.LastIndexOf(SentenceEnd, maximumLength - 1, StringComparison.Ordinal);
        if (cutoff > 0)
        {
            return collapsed[..(cutoff + 1)];
        }

        return string.Concat(collapsed.AsSpan(0, maximumLength).TrimEnd(), "...");
    }

    private static string CollapseWhitespace(string text)
    {
        var builder = new StringBuilder(text.Length);
        var pendingSpace = false;

        foreach (var character in text)
        {
            if (char.IsWhiteSpace(character))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(character);
        }

        return builder.ToString();
    }
}
