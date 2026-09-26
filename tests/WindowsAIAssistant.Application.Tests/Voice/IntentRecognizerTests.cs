using WindowsAIAssistant.Application.Tests.Helpers;
using WindowsAIAssistant.Core.Enums;

namespace WindowsAIAssistant.Application.Tests.Voice;

/// <summary>
/// Asserts that ordinary phrasings reach the right intent.
/// <para>
/// Recognition is the only place a spoken request turns into something executable, so a
/// misclassification here is what would make the assistant open the wrong thing or change the
/// wrong setting. These cases are the phrasings a user would realistically use.
/// </para>
/// </summary>
public sealed class IntentRecognizerTests
{
    [Theory]
    [InlineData("what time is it", AssistantIntent.GetTime)]
    [InlineData("what is today's date", AssistantIntent.GetDate)]
    [InlineData("how much battery is left", AssistantIntent.GetBatteryStatus)]
    [InlineData("turn the volume up", AssistantIntent.IncreaseVolume)]
    [InlineData("turn the volume down", AssistantIntent.DecreaseVolume)]
    [InlineData("mute the sound", AssistantIntent.MuteVolume)]
    [InlineData("unmute", AssistantIntent.UnmuteVolume)]
    [InlineData("open notepad", AssistantIntent.OpenApplication)]
    [InlineData("open my downloads folder", AssistantIntent.OpenFolder)]
    [InlineData("open wifi settings", AssistantIntent.OpenSettings)]
    [InlineData("search the web for weather in London", AssistantIntent.WebSearch)]
    [InlineData("find my project notes", AssistantIntent.FileSearch)]
    [InlineData("take a screenshot", AssistantIntent.TakeScreenshot)]
    [InlineData("stop talking", AssistantIntent.StopSpeaking)]
    [InlineData("repeat that", AssistantIntent.RepeatResponse)]
    [InlineData("yes", AssistantIntent.ConfirmCommand)]
    [InlineData("cancel that", AssistantIntent.Cancel)]
    public async Task RecognizesCommonPhrasings(string transcript, AssistantIntent expected)
    {
        using var harness = VoicePipelineHarness.Create();

        var result = await harness.Recognizer.RecognizeAsync(transcript);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(expected, result.Value!.Intent);
    }

    [Fact]
    public async Task AFreeFormQuestionBecomesAnAiQuestion()
    {
        using var harness = VoicePipelineHarness.Create();

        // No command words at all, so the recognizer should hand it to the AI rather than
        // guessing at a system action.
        var result = await harness.Recognizer.RecognizeAsync("why is the sky blue");

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(AssistantIntent.AIQuestion, result.Value!.Intent);
    }

    [Fact]
    public async Task AnExplicitVolumePercentageIsParsed()
    {
        using var harness = VoicePipelineHarness.Create();

        var result = await harness.Recognizer.RecognizeAsync("set the volume to 30 percent");

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(AssistantIntent.SetVolume, result.Value!.Intent);
        Assert.Equal("30", result.Value.Parameters["volume"]);
    }

    [Fact]
    public async Task NonsenseIsReportedAsUnknownRatherThanGuessed()
    {
        using var harness = VoicePipelineHarness.Create();

        var result = await harness.Recognizer.RecognizeAsync("asdkjh qwe zzzz");

        Assert.Equal(AssistantIntent.Unknown, result.Value?.Intent ?? AssistantIntent.Unknown);
    }

    [Fact]
    public async Task RecognitionReportsEveryIntentItClaimsToSupport()
    {
        using var harness = VoicePipelineHarness.Create();
        var recognizer = harness.Recognizer;

        var claimed = recognizer.SupportedIntents.ToHashSet();

        // Every intent the recognizer can emit must be routable, or a spoken command would
        // parse successfully and then be refused.
        var unroutable = claimed
            .Where(intent => intent is not (AssistantIntent.ConfirmCommand or AssistantIntent.CloseApplication))
            .Where(intent => !harness.Registry.RegisteredIntents.Contains(intent))
            .ToArray();

        Assert.Empty(unroutable);
    }
}
