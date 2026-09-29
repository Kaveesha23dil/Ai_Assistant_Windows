using Microsoft.Extensions.Logging.Abstractions;
using WindowsAIAssistant.Application.Tests.Fakes;
using WindowsAIAssistant.Application.Voice.Commands.Screen;
using WindowsAIAssistant.Core.Abstractions.Vision;
using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models.Vision;
using WindowsAIAssistant.Core.Models.Voice;

namespace WindowsAIAssistant.Application.Tests.Vision;

/// <summary>
/// Covers the one screen intent whose whole purpose is to put pixels on a disk.
/// <para>
/// The other screen intents are about reading or answering; they never write anything. Saving is
/// the opposite, so it is the one that has to prove three things: that the capture path, not the
/// analysis service, answered it; that the frame was released when the write finished; and that
/// a refusal to capture is spoken as a refusal rather than as a fault.
/// </para>
/// </summary>
public sealed class ScreenVoiceHandlerTests
{
    [Fact]
    public void SavingAScreenshotIsAReachableIntentOfTheHandler()
    {
        Assert.Contains(AssistantIntent.SaveScreenshot, Handler().Handler.Intents);
    }

    [Fact]
    public async Task ASavedScreenshotIsSpokenAboutForTheFolderNotTheFile()
    {
        var frame = Frame();
        var capture = new FakeScreenCaptureService(frame);
        var store = new FakeScreenshotStore();
        var (handler, _, _) = Handler(capture: capture, store: store);

        var result = await handler.ExecuteAsync(Command(AssistantIntent.SaveScreenshot));

        Assert.True(result.IsSuccess);

        // The file name is a fact about the person's disk that a voice history does not need; the
        // folder is where they go to find it. Both are available in the data for the page.
        Assert.Contains("WindowsAIAssistant", result.ResponseText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(FakeScreenshotStore.Folder, result.Data["filePath"], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TheWholeDisplayIsCapturedWhenSavingIsSpoken()
    {
        var capture = new FakeScreenCaptureService(Frame());
        var (handler, _, _) = Handler(capture: capture);

        await handler.ExecuteAsync(Command(AssistantIntent.SaveScreenshot));

        // Spoken requests have no pointer to give, so the person chooses the frame via the
        // Windows picker and the intent asks for the whole display, never a region.
        Assert.NotNull(capture.LastRequest);
        Assert.Equal(ScreenCaptureType.Display, capture.LastRequest.CaptureType);
    }

    [Fact]
    public async Task TheFrameIsReleasedOnceTheWriteFinishes()
    {
        var frame = Frame();
        var (handler, _, _) = Handler(capture: new FakeScreenCaptureService(frame), store: new FakeScreenshotStore());

        await handler.ExecuteAsync(Command(AssistantIntent.SaveScreenshot));

        // A screenshot that survives the call in a managed object is a screenshot still readable
        // for the rest of the session, which is exactly the persistence the feature avoids.
        Assert.True(frame.IsReleased);
    }

    [Fact]
    public async Task AScreenCaptureRefusalIsSpokenAsARefusal()
    {
        var refused = Result<ScreenCaptureResult>.Failure(
            ErrorCodes.ScreenCaptureUnsupported,
            "This computer cannot take screenshots.");
        var (handler, _, _) = Handler(capture: new FakeScreenCaptureService(refused));

        var result = await handler.ExecuteAsync(Command(AssistantIntent.SaveScreenshot));

        Assert.False(result.IsSuccess);
        Assert.True(result.IsPermissionDenied);
        Assert.Equal(ErrorCodes.ScreenCaptureUnsupported, result.ErrorCode);
    }

    [Fact]
    public async Task SavingWithoutScreenCapturePermissionIsDeniedBeforeAnyWork()
    {
        var permissions = new FakePermissionService();
        permissions.Denied.Add(PermissionCapability.ScreenCapture);
        var capture = new FakeScreenCaptureService(Frame());
        var store = new FakeScreenshotStore();
        var (handler, _, _) = Handler(capture: capture, store: store, permissions: permissions);

        var result = await handler.ExecuteAsync(Command(AssistantIntent.SaveScreenshot));

        Assert.False(result.IsSuccess);
        Assert.True(result.IsPermissionDenied);
        Assert.Null(capture.LastRequest);
        Assert.Null(store.Saved);
    }

    [Fact]
    public async Task AnalysisIntentsStillGoThroughTheAnalysisService()
    {
        var analysis = new FakeScreenAnalysisService(
            new ScreenAnalysisResult(
                "The screen shows the inbox.",
                provider: "MockVisionProvider"));
        var (handler, analysisService, _) = Handler(analysis: analysis);

        var result = await handler.ExecuteAsync(Command(AssistantIntent.DescribeScreen));

        Assert.True(result.IsSuccess);
        Assert.Equal(ScreenAnalysisType.Describe, analysisService!.LastRequest!.AnalysisType);

        // Saving has to remain out of reach of the analysis path: "save a screenshot" handled by
        // the analysis service would be a screenshot sent to a provider without asking to save.
        var saved = await handler.ExecuteAsync(Command(AssistantIntent.SaveScreenshot));
        Assert.DoesNotContain("analysisType", saved.Data.Keys);
    }

    private static ScreenCaptureResult Frame() =>
        new([1, 2, 3, 4], 2, 1, ScreenPixelFormat.Bgra8, ScreenCaptureType.Display, DateTimeOffset.UtcNow);

    private static (
        ScreenVoiceHandler Handler,
        FakeScreenAnalysisService? Analysis,
        FakeScreenshotStore? Store) Handler(
        IScreenAnalysisService? analysis = null,
        IScreenCaptureService? capture = null,
        IScreenshotStore? store = null,
        FakePermissionService? permissions = null)
    {
        var analysisService = analysis as FakeScreenAnalysisService;
        var screenshotStore = store as FakeScreenshotStore;

        var handler = new ScreenVoiceHandler(
            analysis ?? new FakeScreenAnalysisService(),
            capture ?? new FakeScreenCaptureService(Frame()),
            store ?? new FakeScreenshotStore(),
            permissions ?? new FakePermissionService(),
            NullLogger<ScreenVoiceHandler>.Instance);

        return (handler, analysisService, screenshotStore);
    }

    private static VoiceCommand Command(AssistantIntent intent) =>
        new(
            Guid.NewGuid(),
            "a spoken request",
            intent,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            1d,
            DateTimeOffset.UtcNow,
            ActionSafetyLevel.Safe,
            requiresConfirmation: false);
}