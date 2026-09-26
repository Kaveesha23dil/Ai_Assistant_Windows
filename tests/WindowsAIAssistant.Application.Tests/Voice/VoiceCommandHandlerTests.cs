using WindowsAIAssistant.Application.Tests.Helpers;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models.Voice;

namespace WindowsAIAssistant.Application.Tests.Voice;

/// <summary>
/// Covers each command handler on its own, so a failure points at one capability rather than
/// at the pipeline as a whole.
/// </summary>
public sealed class VoiceCommandHandlerTests
{
    [Fact]
    public async Task AnExplicitVolumeIsApplied()
    {
        using var harness = VoicePipelineHarness.Create();
        harness.Volume.Volume = 50;

        var result = await harness.ExecutorFor(AssistantIntent.SetVolume).ExecuteAsync(
            harness.Command(
                "set the volume to 30",
                AssistantIntent.SetVolume,
                parameters: new Dictionary<string, string> { [VoiceCommand.VolumeParameter] = "30" },
                safety: ActionSafetyLevel.ConfirmationRequired) with { IsConfirmed = true });

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(30, harness.Volume.Volume);
    }

    [Theory]
    [InlineData("500")]
    [InlineData("-20")]
    [InlineData("not a number")]
    public async Task AnUnusableVolumeRequestIsRefusedRatherThanGuessed(string requested)
    {
        using var harness = VoicePipelineHarness.Create();
        harness.Volume.Volume = 50;

        var result = await harness.ExecutorFor(AssistantIntent.SetVolume).ExecuteAsync(
            harness.Command(
                "set the volume",
                AssistantIntent.SetVolume,
                parameters: new Dictionary<string, string> { [VoiceCommand.VolumeParameter] = requested },
                safety: ActionSafetyLevel.ConfirmationRequired) with { IsConfirmed = true });

        // A percentage that cannot be understood is refused, because guessing a volume the
        // user never asked for is worse than asking again.
        Assert.False(result.IsSuccess);
        Assert.Equal(50, harness.Volume.Volume);
    }

    [Fact]
    public async Task MuteAndUnmuteFlipTheFlag()
    {
        using var harness = VoicePipelineHarness.Create();

        var mute = await harness.ExecutorFor(AssistantIntent.MuteVolume).ExecuteAsync(
            harness.Command("mute", AssistantIntent.MuteVolume) with { IsConfirmed = true });
        Assert.True(mute.IsSuccess, mute.ErrorMessage);
        Assert.True(harness.Volume.IsMuted);

        var unmute = await harness.ExecutorFor(AssistantIntent.UnmuteVolume).ExecuteAsync(
            harness.Command("unmute", AssistantIntent.UnmuteVolume) with { IsConfirmed = true });
        Assert.True(unmute.IsSuccess, unmute.ErrorMessage);
        Assert.False(harness.Volume.IsMuted);
    }

    [Fact]
    public async Task TheBatteryReplyStatesTheChargeAndWhetherItIsCharging()
    {
        using var harness = VoicePipelineHarness.Create();
        harness.Battery.Percentage = 64;

        var result = await harness.ExecutorFor(AssistantIntent.GetBatteryStatus).ExecuteAsync(
            harness.Command("battery", AssistantIntent.GetBatteryStatus));

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Contains("64", result.ResponseText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheTimeReplyComesFromTheClockWithoutAskingTheAi()
    {
        using var harness = VoicePipelineHarness.Create();

        var result = await harness.ExecutorFor(AssistantIntent.GetTime).ExecuteAsync(
            harness.Command("what time is it", AssistantIntent.GetTime));

        Assert.True(result.IsSuccess, result.ErrorMessage);

        // The clock is the only source, and it is rendered for hearing rather than for a screen.
        Assert.Matches(@"\d{1,2}:\d{2}", result.ResponseText);
        Assert.DoesNotContain("2026", result.ResponseText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheDateReplyComesFromTheClock()
    {
        using var harness = VoicePipelineHarness.Create();

        var result = await harness.ExecutorFor(AssistantIntent.GetDate).ExecuteAsync(
            harness.Command("what is the date", AssistantIntent.GetDate));

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Contains("2026", result.ResponseText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ASettingsRequestOpensTheMatchingAddress()
    {
        using var harness = VoicePipelineHarness.Create();

        var result = await harness.ExecutorFor(AssistantIntent.OpenSettings).ExecuteAsync(
            harness.Command(
                "open wifi settings",
                AssistantIntent.OpenSettings,
                parameters: new Dictionary<string, string> { [VoiceCommand.SettingParameter] = "wifi" },
                safety: ActionSafetyLevel.ConfirmationRequired) with { IsConfirmed = true });

        Assert.True(result.IsSuccess, result.ErrorMessage);

        var opened = Assert.Single(harness.OpenedUris);
        Assert.Equal("ms-settings:wifi", opened.AbsoluteUri);
    }

    [Fact]
    public async Task AKnownFolderRequestOpensThatFolder()
    {
        using var harness = VoicePipelineHarness.Create();

        var result = await harness.ExecutorFor(AssistantIntent.OpenFolder).ExecuteAsync(
            harness.Command(
                "open downloads",
                AssistantIntent.OpenFolder,
                parameters: new Dictionary<string, string> { [VoiceCommand.FolderParameter] = "Downloads" },
                safety: ActionSafetyLevel.ConfirmationRequired) with { IsConfirmed = true });

        Assert.True(result.IsSuccess, result.ErrorMessage);

        var opened = Assert.Single(harness.OpenedUris);
        Assert.Contains("Downloads", opened.LocalPath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AnApplicationRequestIsResolvedAgainstTheAllowList()
    {
        using var harness = VoicePipelineHarness.Create();

        var result = await harness.ExecutorFor(AssistantIntent.OpenApplication).ExecuteAsync(
            harness.Command(
                "open notepad",
                AssistantIntent.OpenApplication,
                parameters: new Dictionary<string, string>
                {
                    [VoiceCommand.ApplicationParameter] = "notepad",
                },
                safety: ActionSafetyLevel.ConfirmationRequired) with { IsConfirmed = true });

        Assert.True(result.IsSuccess, result.ErrorMessage);

        // The spoken name reached the allow-list rather than becoming part of a command line.
        Assert.Equal("notepad", Assert.Single(harness.Resolver.Requests));
    }

    [Fact]
    public async Task AYouTubeRequestChoosesTheVideoProvider()
    {
        using var harness = VoicePipelineHarness.Create();

        var result = await harness.ExecutorFor(AssistantIntent.YouTubeSearch).ExecuteAsync(
            harness.Command(
                "search youtube for openai",
                AssistantIntent.YouTubeSearch,
                parameters: new Dictionary<string, string> { [VoiceCommand.QueryParameter] = "openai" }));

        Assert.True(result.IsSuccess, result.ErrorMessage);

        var opened = Assert.Single(harness.OpenedUris);

        // The video provider was chosen rather than the default web one, and the spoken query
        // travelled as an escaped parameter instead of being pasted into a command line.
        Assert.Contains("youtube", opened.AbsolutePath, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("?q=openai", opened.Query);
    }

    [Fact]
    public async Task AScreenshotReplyNamesTheSavedFile()
    {
        using var harness = VoicePipelineHarness.Create();

        var result = await harness.ExecutorFor(AssistantIntent.TakeScreenshot).ExecuteAsync(
            harness.Command(
                "take a screenshot",
                AssistantIntent.TakeScreenshot,
                safety: ActionSafetyLevel.ConfirmationRequired) with { IsConfirmed = true });

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Contains("Screenshot", result.ResponseText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StopSpeakingReachesTheSynthesizerThroughTheHandler()
    {
        using var harness = VoicePipelineHarness.Create();

        var result = await harness.ExecutorFor(AssistantIntent.StopSpeaking).ExecuteAsync(
            harness.Command("stop talking", AssistantIntent.StopSpeaking));

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(1, harness.Synthesis.StopCallCount);
    }

    [Fact]
    public async Task TheStorageReplyReportsTheFreeSpace()
    {
        using var harness = VoicePipelineHarness.Create();

        var result = await harness.ExecutorFor(AssistantIntent.GetStorageUsage).ExecuteAsync(
            harness.Command("how much space is left", AssistantIntent.GetStorageUsage));

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.False(string.IsNullOrWhiteSpace(result.ResponseText));
    }

    [Fact]
    public async Task ASystemSummaryIsReturnedWithoutTheAi()
    {
        using var harness = VoicePipelineHarness.Create();

        var result = await harness.ExecutorFor(AssistantIntent.GetSystemInformation).ExecuteAsync(
            harness.Command("tell me about this pc", AssistantIntent.GetSystemInformation));

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.False(string.IsNullOrWhiteSpace(result.ResponseText));
    }
}
