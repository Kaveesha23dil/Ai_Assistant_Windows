using WindowsAIAssistant.Application.Tests.Helpers;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models.Voice;

namespace WindowsAIAssistant.Application.Tests.Voice;

/// <summary>
/// Covers the rules that stand between a spoken request and a change to the machine.
/// <para>
/// Each case asserts the refusal itself, not only the successful path. A test that only proved
/// the happy path would keep passing if a later change let a handler run without asking.
/// </para>
/// </summary>
public sealed class VoiceSafetyTests
{
    [Fact]
    public async Task AConfirmationLevelCommandIsHeldUntilTheUserAgrees()
    {
        using var harness = VoicePipelineHarness.Create();
        harness.Volume.Volume = 50;

        var held = await harness.Assistant.ProcessTranscriptAsync("turn the volume up");

        Assert.True(held.RequiresConfirmation);
        Assert.False(held.IsSuccess);

        // Nothing has changed while the question is outstanding.
        Assert.Equal(50, harness.Volume.Volume);
    }

    [Fact]
    public async Task ConfirmingRunsTheHeldCommandExactlyOnce()
    {
        using var harness = VoicePipelineHarness.Create();
        harness.Volume.Volume = 50;

        var held = await harness.Assistant.ProcessTranscriptAsync("turn the volume up");
        Assert.True(held.RequiresConfirmation);

        var confirmed = await harness.Assistant.ProcessTranscriptAsync("yes");

        Assert.True(confirmed.IsSuccess, confirmed.ErrorMessage);
        Assert.Equal(60, harness.Volume.Volume);

        // A second "yes" must not repeat the action.
        var again = await harness.Assistant.ProcessTranscriptAsync("yes");
        Assert.False(again.IsSuccess);
        Assert.Equal(60, harness.Volume.Volume);
    }

    [Fact]
    public async Task CancellingDiscardsTheHeldCommand()
    {
        using var harness = VoicePipelineHarness.Create();
        harness.Volume.Volume = 50;

        await harness.Assistant.ProcessTranscriptAsync("turn the volume up");
        var cancelled = await harness.Assistant.ProcessTranscriptAsync("cancel that");

        Assert.True(cancelled.IsSuccess, cancelled.ErrorMessage);
        Assert.Equal(50, harness.Volume.Volume);

        // The held command is gone, so agreeing afterwards must not resurrect it.
        var afterCancel = await harness.Assistant.ProcessTranscriptAsync("yes");
        Assert.False(afterCancel.IsSuccess);
        Assert.Equal(50, harness.Volume.Volume);
    }

    [Fact]
    public async Task ConfirmingWithNothingPendingIsRefused()
    {
        using var harness = VoicePipelineHarness.Create();

        var result = await harness.Assistant.ProcessTranscriptAsync("yes");

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task ALowConfidenceMatchIsConfirmedRatherThanGuessed()
    {
        using var harness = VoicePipelineHarness.Create(policy => policy with
        {
            // Above the 0.95 the volume rules report, so the match counts as weak.
            MinimumCommandConfidence = 0.99,
            ConfirmLowConfidenceCommands = true,
        });

        harness.Volume.Volume = 50;

        var result = await harness.Assistant.ProcessTranscriptAsync("turn the volume up");

        Assert.True(result.RequiresConfirmation);
        Assert.Equal(50, harness.Volume.Volume);
    }

    [Fact]
    public async Task ADeniedPermissionStopsTheHandlerBeforeItRuns()
    {
        using var harness = VoicePipelineHarness.Create(
            denied: PermissionCapability.SystemControl);

        var result = await harness.Assistant.ProcessTranscriptAsync("turn the volume up");

        Assert.False(result.IsSuccess);
        Assert.Equal(50, harness.Volume.Volume);
    }

    [Fact]
    public async Task ClipboardAccessIsRefusedWithoutConsent()
    {
        using var harness = VoicePipelineHarness.Create(
            denied: PermissionCapability.Clipboard);

        var result = await harness.Assistant.ProcessTranscriptAsync("what is on my clipboard");

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task FileSearchIsRefusedWithoutConsent()
    {
        using var harness = VoicePipelineHarness.Create(
            denied: PermissionCapability.FileSearch);

        var result = await harness.Assistant.ProcessTranscriptAsync("find my project notes");

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task ScreenshotCaptureIsRefusedWithoutConsent()
    {
        using var harness = VoicePipelineHarness.Create(
            denied: PermissionCapability.ScreenCapture);

        var result = await harness.Assistant.ProcessTranscriptAsync("take a screenshot");

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task AnUnsupportedSettingsPageIsRefusedAndNeverReachesTheShell()
    {
        using var harness = VoicePipelineHarness.Create();

        var result = await harness.Router.RouteAsync(harness.Command(
            "open the secret settings page",
            AssistantIntent.OpenSettings,
            parameters: new Dictionary<string, string>
            {
                [VoiceCommand.SettingParameter] = "definitely-not-a-page",
            },
            safety: ActionSafetyLevel.ConfirmationRequired,
            requiresConfirmation: true) with { IsConfirmed = true });

        Assert.False(result.IsSuccess);

        // No address was handed to the shell, so an arbitrary URI cannot be reached.
        Assert.Empty(harness.OpenedUris);
    }

    [Fact]
    public async Task AnUnknownApplicationIsRefusedRatherThanLaunched()
    {
        using var harness = VoicePipelineHarness.Create();

        var result = await harness.Router.RouteAsync(harness.Command(
            "open something that does not exist",
            AssistantIntent.OpenApplication,
            parameters: new Dictionary<string, string>
            {
                [VoiceCommand.ApplicationParameter] = "does-not-exist",
            },
            safety: ActionSafetyLevel.ConfirmationRequired,
            requiresConfirmation: true) with { IsConfirmed = true });

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task AWebSearchQueryIsEncodedBeforeItReachesTheShell()
    {
        using var harness = VoicePipelineHarness.Create();

        var result = await harness.Assistant.ProcessTranscriptAsync("search the web for c# async");

        Assert.True(result.IsSuccess, result.ErrorMessage);

        var opened = Assert.Single(harness.OpenedUris);
        Assert.Contains("%23", opened.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OpeningAKnownFolderIsAllowedByDefault()
    {
        using var harness = VoicePipelineHarness.Create();

        var result = await harness.Assistant.ProcessTranscriptAsync("open my downloads folder");

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Single(harness.OpenedUris);
    }

    [Fact]
    public async Task TheWakeWordRefusesToStartBecauseNoLocalEngineIsInstalled()
    {
        using var harness = VoicePipelineHarness.Create();

        var result = await harness.WakeWord.StartAsync();

        Assert.True(result.IsFailure);
        Assert.False(harness.WakeWord.IsAvailable);
    }

    [Fact]
    public async Task CommandHistoryCannotHoldATranscriptEvenWhenRecordingIsOn()
    {
        using var harness = VoicePipelineHarness.Create();
        var history = harness.History;
        history.IsEnabled = true;

        await harness.Assistant.ProcessTranscriptAsync("turn the volume up");

        var entry = Assert.Single(history.Entries);
        Assert.Equal(AssistantIntent.IncreaseVolume, entry.Intent);

        // The only text the entry can hold is a stable error code from the shared catalogue, so
        // a transcript has nowhere to go. Asserting the shape beats scanning values for a leak.
        var textProperties = typeof(VoiceCommandHistoryEntry)
            .GetProperties()
            .Where(property => property.PropertyType == typeof(string))
            .Select(property => property.Name)
            .ToArray();

        Assert.Equal(["ErrorCode"], textProperties);
    }

    [Fact]
    public async Task CommandHistoryStaysEmptyWhileRecordingIsOff()
    {
        using var harness = VoicePipelineHarness.Create();

        await harness.Assistant.ProcessTranscriptAsync("turn the volume up");

        Assert.False(harness.History.IsEnabled);
        Assert.Empty(harness.History.Entries);
    }
}
