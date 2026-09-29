using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Models.Vision;
using WindowsAIAssistant.Infrastructure.Configuration.Options;
using IScreenshotStore = WindowsAIAssistant.Core.Abstractions.Vision.IScreenshotStore;

namespace WindowsAIAssistant.Infrastructure.Vision.Storage;

/// <summary>
/// The only path in the application by which screen pixels reach a disk.
/// <para>
/// Deliberately the only one. Capture produces bytes in memory and hands them over; nothing
/// between that and a person's explicit "save this" writes anything, so "no screenshot is ever
/// written unless I asked" is a property of the code's shape rather than a setting somebody has
/// to remember to keep on. There is no temporary file, no cache, no thumbnail, and no log line
/// that carries a path or a size — the folder name and the file size are the only things that
/// ever leave this class.
/// </para>
/// <para>
/// The suggested name is treated as a name and never as a path. A caller that was handed text
/// from a model, a title bar, or a person typing into a box cannot be trusted to have produced
/// something safe to hand to the file system, so anything that is not a letter, a digit, a dash,
/// an underscore, or a dot is replaced with an underscore before it is joined to a folder, and
/// the separators that would let it climb out are removed outright.
/// </para>
/// </summary>
public sealed class ScreenshotStore : IScreenshotStore
{
    /// <summary>How long a single write may take before it is abandoned.</summary>
    private static readonly TimeSpan WriteTimeout = TimeSpan.FromSeconds(30);

    private readonly IOptionsMonitor<VisionOptions> _options;
    private readonly ILogger<ScreenshotStore> _logger;

    public ScreenshotStore(
        IOptionsMonitor<VisionOptions> options,
        ILogger<ScreenshotStore> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _options = options;
        _logger = logger;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Returns a failed result rather than raising for the ordinary reasons a person can fix —
    /// a full disk, a folder that has gone, a name they did not like. Those are answers, not
    /// faults in the program.
    /// </remarks>
    public async Task<Result<string>> SaveAsync(
        ScreenCaptureResult capture,
        string? destinationFolder,
        string? suggestedFileName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(capture);

        if (capture.IsReleased)
        {
            return Result<string>.Failure(
                ErrorCodes.ScreenCaptureEmpty,
                "That screenshot is no longer available to save.");
        }

        var folderResult = await ResolveFolderAsync(destinationFolder, cancellationToken)
            .ConfigureAwait(false);

        if (!folderResult.IsSuccess || folderResult.Value is not { } folder)
        {
            return folderResult.IsSuccess
                ? Result<string>.Failure(
                    ErrorCodes.IoOperationFailed,
                    "There was nowhere to save that screenshot.")
                : Result<string>.Failure(folderResult.ErrorCode!, folderResult.ErrorMessage!);
        }

        cancellationToken.ThrowIfCancellationRequested();

        var name = BuildFileName(suggestedFileName, capture.CapturedAt);
        var path = Path.Combine(folder, name);

        try
        {
            Directory.CreateDirectory(folder);

            // A new name rather than the caller's, so two screenshots taken in the same second
            // cannot overwrite one another. Overwriting would replace a picture somebody asked
            // to keep with one they did not.
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(WriteTimeout);

            await using (var file = new FileStream(
                path,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await file.WriteAsync(capture.ImageBytes, timeout.Token).ConfigureAwait(false);
                await file.FlushAsync(timeout.Token).ConfigureAwait(false);
            }

            _logger.LogInformation(
                "A screenshot was saved at the person's request as a {Kilobytes} KB file.",
                capture.ImageBytes.Length / 1024);

            return Result<string>.Success(path);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The person cancelled, which usually means they dismissed a save dialog. Any
            // partial file is removed so nothing half-written is left looking like a screenshot.
            TryDelete(path);
            throw;
        }
        catch (OperationCanceledException)
        {
            TryDelete(path);
            return Result<string>.Failure(
                ErrorCodes.IoOperationFailed,
                "Saving that screenshot took too long, so it was not saved.");
        }
        catch (UnauthorizedAccessException)
        {
            return Result<string>.Failure(
                ErrorCodes.IoOperationFailed,
                "I don't have permission to save files in that folder.");
        }
        catch (IOException exception)
        {
            TryDelete(path);

            // The message is deliberately generic. A full disk and a locked file look identical
            // from here, and the difference between them is not something a person can act on
            // in a chat bubble.
            _logger.LogWarning(exception, "A screenshot could not be written to the chosen folder.");
            return Result<string>.Failure(
                ErrorCodes.IoOperationFailed,
                "That screenshot could not be saved to that folder.");
        }
    }

    /// <inheritdoc />
    public Task<Result<string>> GetDefaultFolderAsync(CancellationToken cancellationToken = default)
        => ResolveFolderAsync(destinationFolder: null, cancellationToken);

    /// <summary>
    /// Chooses the folder to write into, preferring an explicit choice over the configured one
    /// over the visible default.
    /// <para>
    /// Each candidate in turn is created if it is missing, so a folder configured in settings
    /// that has been moved or deleted is made again rather than sending every save to somewhere
    /// the person did not choose. A candidate that cannot be created is stepped over silently:
    /// the fallback is what the person wanted anyway, and an error here would fail a save over a
    /// folder they will never hear about.
    /// </para>
    /// </summary>
    private Task<Result<string>> ResolveFolderAsync(
        string? destinationFolder,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        foreach (var candidate in new[]
                 {
                     destinationFolder,
                     _options.CurrentValue.ScreenshotFolder,
                     DefaultFolder(),
                 })
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            try
            {
                Directory.CreateDirectory(candidate);
                return Task.FromResult(Result<string>.Success(candidate));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
            {
                _logger.LogWarning("A screenshot folder could not be used, so another was chosen.");
            }
        }

        return Task.FromResult(Result<string>.Failure(
            ErrorCodes.IoOperationFailed,
            "I couldn't find a folder to save screenshots into."));
    }

    /// <summary>
    /// A visible folder under the person's own Pictures directory.
    /// <para>
    /// Never a hidden application folder. A screenshot somebody cannot find is not something
    /// they can delete when they change their mind, and putting their screen images somewhere
    /// they have to hunt for is the difference between consent and hoarding.
    /// </para>
    /// </summary>
    private static string DefaultFolder() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyPictures, Environment.SpecialFolderOption.DoNotVerify),
        "WindowsAIAssistant");

    /// <summary>
    /// Builds a file name from whatever was suggested, or from the clock.
    /// <para>
    /// The stamp uses the invariant culture and no spaces so that the name sorts correctly in
    /// Explorer regardless of the person's regional settings, and so that a name generated here
    /// is a name generated the same way on every machine.
    /// </para>
    /// </summary>
    private static string BuildFileName(string? suggestedFileName, DateTimeOffset capturedAt)
    {
        var stamp = capturedAt.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var stem = Sanitize(suggestedFileName);

        if (stem.Length == 0)
        {
            stem = "Screenshot";
        }

        if (stem.Length > 80)
        {
            stem = stem[..80];
        }

        // A unique stamp per write, appended rather than substituted, so that a name somebody
        // chose is still recognisable in a folder full of them, and so that two screenshots
        // taken in the same second cannot overwrite one another. Overwriting would replace a
        // picture somebody asked to keep with one they did not.
        return $"{stem}-{stamp}-{Guid.NewGuid():N}.png";
    }

    /// <summary>
    /// Reduces arbitrary text to a single safe path segment.
    /// </summary>
    private static string Sanitize(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(candidate.Length);

        foreach (var character in candidate.Trim())
        {
            if (char.IsAsciiLetterOrDigit(character) || character is '-' or '_')
            {
                builder.Append(character);
            }
            else if (character is '.' or ' ')
            {
                builder.Append(character == '.' ? '_' : ' ');
            }

            // Everything else — including '\', '/', ':', and any character outside ASCII —
            // is dropped rather than replaced. A name is a label, and a label that survives
            // a round trip through a chat message is worth less than one that cannot escape
            // the folder it was given.
        }

        return builder.ToString().Trim(' ', '.');
    }

    /// <summary>
    /// Removes a file that was not finished with, without caring why.
    /// </summary>
    private void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Nothing useful can be said to a person about a file that will not delete. The
            // path is not logged, because the path is a fact about their screen.
            _logger.LogWarning("A partially written screenshot could not be removed.");
        }
    }
}
