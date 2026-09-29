using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models.Vision;
using IOcrProvider = WindowsAIAssistant.Core.Abstractions.Vision.IOcrProvider;

namespace WindowsAIAssistant.Infrastructure.Vision.Ocr;

/// <summary>
/// The engine that reads nothing, and says so.
/// <para>
/// Registered unconditionally so that a machine with no recogniser has something to resolve.
/// <see cref="IOcrProvider"/> promises that asking whether text can be read is unnecessary
/// before asking for it, and that promise is kept by having a provider that is always available
/// and always returns <see cref="OcrResult.Empty"/> rather than by making every caller check
/// first. A screenshot is still a screenshot on a computer that cannot read it, and refusing the
/// whole request because recognition is missing would lose a perfectly good image.
/// </para>
/// <para>
/// It exists so that "no text was found" and "this computer cannot read text" are different
/// answers, and so that the first one does not have to be reported as a fault.
/// </para>
/// </summary>
public sealed class NoOpOcrProvider : IOcrProvider
{
    /// <inheritdoc />
    public OcrProviderKind Kind => OcrProviderKind.None;

    /// <summary>
    /// Gets a value indicating whether this engine can be used. Always true, which is the point:
    /// it is the answer on a machine that has no recogniser.
    /// </summary>
    public bool IsAvailable => true;

    /// <summary>Gets a value indicating whether this engine needs a model. Never.</summary>
    public bool RequiresModelDownload => false;

    /// <inheritdoc />
    public Task<OcrResult> ReadAsync(
        ReadOnlyMemory<byte> imageBytes,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(OcrResult.Empty);
    }
}
