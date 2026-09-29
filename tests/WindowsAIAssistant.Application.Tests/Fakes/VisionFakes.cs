using WindowsAIAssistant.Core.Abstractions.Vision;
using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models.Vision;

namespace WindowsAIAssistant.Application.Tests.Fakes;

/// <summary>
/// A text engine that answers from whatever the test told it to, and remembers whether it was
/// asked to read anything at all.
/// <para>
/// Usability and the download requirement are set separately because that separation is the
/// whole decision the resolver makes. A fake that hard-coded either one would let a resolver
/// that ignored both still pass its tests.
/// </para>
/// </summary>
public sealed class FakeOcrProvider(OcrProviderKind kind, bool isAvailable, bool requiresModelDownload) : IOcrProvider
{
    private readonly OcrResult _result = OcrResult.Empty;

    public FakeOcrProvider(OcrProviderKind kind, bool isAvailable, bool requiresModelDownload, OcrResult? result = null)
        : this(kind, isAvailable, requiresModelDownload)
    {
        _result = result ?? OcrResult.Empty;
    }

    /// <inheritdoc />
    public OcrProviderKind Kind { get; } = kind;

    /// <inheritdoc />
    public bool IsAvailable { get; } = isAvailable;

    /// <inheritdoc />
    public bool RequiresModelDownload { get; } = requiresModelDownload;

    /// <summary>Gets the number of times somebody actually tried to read text.</summary>
    public int ReadCount { get; private set; }

    /// <inheritdoc />
    public Task<OcrResult> ReadAsync(ReadOnlyMemory<byte> imageBytes, CancellationToken cancellationToken = default)
    {
        ReadCount++;
        return Task.FromResult(_result);
    }
}

/// <summary>
/// A capture that hands back one frame, and can be told to fail or be dismissed instead.
/// <para>
/// The frame is tracked so a test can assert it was disposed. A capture that nobody disposes is
/// a screenshot of somebody's screen still readable in memory for the rest of the session, which
/// is the same leak the type system was supposed to make hard to write.
/// </para>
/// </summary>
public sealed class FakeScreenCaptureService : IScreenCaptureService
{
    private readonly ScreenCaptureResult? _frame;
    private readonly Result<ScreenCaptureResult> _outcome;

    public FakeScreenCaptureService(ScreenCaptureResult? frame = null)
    {
        _frame = frame;
        _outcome = frame is null
            ? Result<ScreenCaptureResult>.Failure(
                ErrorCodes.ScreenCaptureCancelled,
                "The capture was cancelled.")
            : Result<ScreenCaptureResult>.Success(frame);
    }

    public FakeScreenCaptureService(Result<ScreenCaptureResult> outcome)
    {
        _frame = null;
        _outcome = outcome;
    }

    /// <summary>Gets the last request the caller made, for asserting what was asked for.</summary>
    public ScreenCaptureRequest? LastRequest { get; private set; }

    /// <summary>Gets the frame this fake was built with, for asserting it was released.</summary>
    public ScreenCaptureResult? Frame => _frame;

    /// <inheritdoc />
    public bool IsSupported => true;

    /// <inheritdoc />
    public Task<Result<ScreenCaptureResult>> CaptureAsync(
        ScreenCaptureRequest request,
        CancellationToken cancellationToken = default)
    {
        LastRequest = request;
        return Task.FromResult(_outcome);
    }
}

/// <summary>
/// An analysis service that returns whatever result the test supplied, without touching a
/// recogniser, a provider, or a screen.
/// </summary>
public sealed class FakeScreenAnalysisService : IScreenAnalysisService
{
    private readonly Result<ScreenAnalysisResult> _analyze;
    private readonly Result<ScreenAnalysisResult> _readText;

    /// <summary>Gets the last request routed for analysis, for asserting what was asked.</summary>
    public ScreenAnalysisRequestOptions? LastRequest { get; private set; }

    public FakeScreenAnalysisService(
        ScreenAnalysisResult? result = null,
        ScreenAnalysisResult? readTextResult = null)
    {
        var value = result ?? new ScreenAnalysisResult(
            "The screen shows the settings page.",
            provider: "MockVisionProvider");

        _analyze = Result<ScreenAnalysisResult>.Success(value);
        _readText = Result<ScreenAnalysisResult>.Success(
            readTextResult ?? new ScreenAnalysisResult(
                "Some visible text",
                extractedText: "Some visible text",
                provider: "MockVisionProvider"));
    }

    public FakeScreenAnalysisService(Result<ScreenAnalysisResult> analyze, Result<ScreenAnalysisResult>? readText = null)
    {
        _analyze = analyze;
        _readText = readText ?? analyze;
    }

    /// <inheritdoc />
    public Task<Result<ScreenAnalysisResult>> AnalyzeScreenAsync(
        ScreenAnalysisRequestOptions request,
        CancellationToken cancellationToken = default)
    {
        LastRequest = request;
        return Task.FromResult(_analyze);
    }

    /// <inheritdoc />
    public Task<Result<ScreenAnalysisResult>> ReadScreenTextAsync(
        ScreenAnalysisRequestOptions request,
        CancellationToken cancellationToken = default)
    {
        LastRequest = request;
        return Task.FromResult(_readText);
    }

    /// <inheritdoc />
    public Task<VisionCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(
            new VisionCapabilities(
                captureSupported: true,
                ocrProvider: OcrProviderKind.None,
                ocrAvailable: true,
                modelSupportsImageInput: false,
                cloudConfigured: false,
                cloudVisionAvailable: false,
                imageSizeLimitBytes: 4 * 1024 * 1024,
                maxDimension: 2560,
                source: "MockVisionProvider"));
}

/// <summary>A store that writes nothing and reports where it would have written.</summary>
public sealed class FakeScreenshotStore : IScreenshotStore
{
    public const string Folder = @"C:\Users\Test\Pictures\WindowsAIAssistant";

    /// <summary>Gets the frame the caller handed over, for asserting what was written.</summary>
    public ScreenCaptureResult? Saved { get; private set; }

    /// <summary>Gets the name the caller suggested, which must never become a path.</summary>
    public string? SuggestedName { get; private set; }

    /// <inheritdoc />
    public Task<Result<string>> SaveAsync(
        ScreenCaptureResult capture,
        string? destinationFolder,
        string? suggestedFileName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(capture);

        Saved = capture;
        SuggestedName = suggestedFileName;

        var name = string.IsNullOrWhiteSpace(suggestedFileName) ? "Screenshot.png" : suggestedFileName;
        return Task.FromResult(Result<string>.Success(Path.Combine(Folder, name)));
    }

    /// <inheritdoc />
    public Task<Result<string>> GetDefaultFolderAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Result<string>.Success(Folder));
}
