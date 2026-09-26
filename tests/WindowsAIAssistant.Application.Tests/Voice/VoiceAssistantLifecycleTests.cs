using WindowsAIAssistant.Application.Tests.Helpers;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models.Voice;

namespace WindowsAIAssistant.Application.Tests.Voice;

/// <summary>
/// Covers the lifecycle the user interface binds to: what happens between pressing the
/// microphone button and hearing an answer.
/// </summary>
public sealed class VoiceAssistantLifecycleTests
{
    [Fact]
    public async Task StartingListeningIsRefusedWithoutMicrophoneConsent()
    {
        using var harness = VoicePipelineHarness.Create(
            denied: PermissionCapability.Microphone);

        var result = await harness.Assistant.StartListeningAsync();

        Assert.True(result.IsFailure);

        // The recognizer was never asked to start, so no device was opened.
        Assert.Equal(0, harness.Recognition.StartCallCount);
    }

    [Fact]
    public async Task StartingListeningIsRefusedWhenTheFeatureIsSwitchedOff()
    {
        using var harness = VoicePipelineHarness.Create(policy => policy with { Enabled = false });

        var result = await harness.Assistant.StartListeningAsync();

        Assert.True(result.IsFailure);
        Assert.Equal(0, harness.Recognition.StartCallCount);
    }

    [Fact]
    public async Task ListeningReportsItsProgressAndSettlesBackToIdle()
    {
        using var harness = VoicePipelineHarness.Create();

        var states = new List<VoiceAssistantState>();
        harness.Assistant.StateChanged += (_, e) => states.Add(e.Current);

        await harness.Assistant.StartListeningAsync();
        await harness.Assistant.StopListeningAsync();

        // A settled transcript is handled off the callback thread, so the pipeline is given a
        // moment to finish before its resting state is asserted.
        await WaitForStateAsync(harness, VoiceAssistantState.Idle);

        Assert.Equal(VoiceAssistantState.Listening, states[0]);
        Assert.Equal(VoiceAssistantState.Idle, states[^1]);
    }

    private static async Task WaitForStateAsync(
        VoicePipelineHarness harness,
        VoiceAssistantState expected)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (harness.Assistant.State == expected)
            {
                return;
            }

            await Task.Delay(20);
        }

        Assert.Equal(expected, harness.Assistant.State);
    }

    [Fact]
    public async Task APartialTranscriptIsReplacedByTheFinalOne()
    {
        using var harness = VoicePipelineHarness.Create();
        harness.Recognition.NextTranscript = "turn the volume up";

        var transcripts = new List<string>();
        harness.Assistant.TranscriptUpdated += (_, e) => transcripts.Add(e.Result.Text);

        await harness.Assistant.StartListeningAsync();
        await harness.Assistant.StopListeningAsync();

        // The interface shows partial text as the user speaks, so the final value has to
        // supersede it rather than being appended to.
        Assert.Equal("turn the volume up", transcripts[^1]);
    }

    [Fact]
    public async Task ARecognizerFailureMovesTheAssistantToErrorRatherThanHanging()
    {
        using var harness = VoicePipelineHarness.Create();
        harness.Recognition.FailWith = "no speech was detected";

        var states = new List<VoiceAssistantState>();
        harness.Assistant.StateChanged += (_, e) => states.Add(e.Current);

        await harness.Assistant.StartListeningAsync();
        await harness.Assistant.StopListeningAsync();

        Assert.Equal("NoSpeechRecognized", harness.Recognition.LastFailureCode);

        // A failure has to be visible, otherwise the interface would sit on "listening" with
        // a microphone the user believes is still open.
        Assert.Contains(VoiceAssistantState.Error, states);

        // No transcript was delivered, so nothing was executed.
        Assert.Empty(harness.OpenedUris);
    }

    [Fact]
    public async Task AReplyIsSpokenWhenSpokenResponsesAreOn()
    {
        using var harness = VoicePipelineHarness.Create(policy => policy with
        {
            SpeakResponses = true,
            MaximumSpokenResponseLength = 240,
        });

        var result = await harness.Assistant.ProcessTranscriptAsync("what time is it");

        Assert.True(result.IsSuccess, result.ErrorMessage);

        var spoken = Assert.Single(harness.Synthesis.Spoken);
        Assert.Equal(result.ResponseText, spoken);
    }

    [Fact]
    public async Task AReplyIsNotSpokenWhenSpokenResponsesAreOff()
    {
        using var harness = VoicePipelineHarness.Create(policy => policy with
        {
            SpeakResponses = false,
        });

        var result = await harness.Assistant.ProcessTranscriptAsync("what time is it");

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Empty(harness.Synthesis.Spoken);
    }

    [Fact]
    public async Task ALongReplyIsShortenedToTheConfiguredLimit()
    {
        using var harness = VoicePipelineHarness.Create(policy => policy with
        {
            SpeakResponses = true,
            MaximumSpokenResponseLength = 40,
        });

        // A summary long enough that speaking it in full would run on past the user's attention.
        var longReply = new string('a', 200);
        harness.Synthesis.SpeakTransform = text => text;

        await harness.Assistant.SpeakAsync(longReply);

        var spoken = Assert.Single(harness.Synthesis.Spoken);
        Assert.True(
            spoken.Length <= 40,
            $"The spoken reply was {spoken.Length} characters, above the 40 character limit.");
    }

    [Fact]
    public async Task TheLastResponseIsAvailableForRepeating()
    {
        using var harness = VoicePipelineHarness.Create(policy => policy with
        {
            SpeakResponses = false,
        });

        await harness.Assistant.ProcessTranscriptAsync("what time is it");
        var expected = harness.Assistant.LastResponse;

        Assert.False(string.IsNullOrWhiteSpace(expected));

        var repeat = await harness.Assistant.RepeatAsync();

        Assert.True(repeat.IsSuccess, repeat.ErrorMessage);
    }

    [Fact]
    public async Task RepeatingWithNoPriorResponseIsRefused()
    {
        using var harness = VoicePipelineHarness.Create();

        var result = await harness.Assistant.RepeatAsync();

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task StopSpeakingReachesTheSynthesizer()
    {
        using var harness = VoicePipelineHarness.Create();

        await harness.Assistant.SpeakAsync("this is a long enough reply to interrupt");
        var result = await harness.Assistant.StopSpeakingAsync();

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(1, harness.Synthesis.StopCallCount);
    }

    [Fact]
    public async Task AnUnrecognizableRequestSaysSoInsteadOfGuessing()
    {
        using var harness = VoicePipelineHarness.Create();

        var result = await harness.Assistant.ProcessTranscriptAsync("asdkjh qwe zzzz");

        Assert.False(result.IsSuccess);
        Assert.False(string.IsNullOrWhiteSpace(result.ResponseText));
    }

    [Fact]
    public async Task TheAssistantReturnsToIdleAfterEveryRequest()
    {
        using var harness = VoicePipelineHarness.Create();

        await harness.Assistant.ProcessTranscriptAsync("what time is it");
        Assert.Equal(VoiceAssistantState.Idle, harness.Assistant.State);

        await harness.Assistant.ProcessTranscriptAsync("open my downloads folder");
        Assert.Equal(VoiceAssistantState.Idle, harness.Assistant.State);

        await harness.Assistant.ProcessTranscriptAsync("asdkjh qwe zzzz");
        Assert.Equal(VoiceAssistantState.Idle, harness.Assistant.State);
    }

    [Fact]
    public async Task AResponseEventCarriesTheResultTheUserWillSee()
    {
        using var harness = VoicePipelineHarness.Create(policy => policy with
        {
            SpeakResponses = false,
        });

        VoiceResponseEventArgs? published = null;
        harness.Assistant.ResponseProduced += (_, e) => published = e;

        var result = await harness.Assistant.ProcessTranscriptAsync("what time is it");

        Assert.NotNull(published);
        Assert.Equal(result.CommandId, published.Result.CommandId);
        Assert.False(published.WasSpoken);
    }
}
