using Microsoft.Extensions.Logging.Abstractions;
using WindowsAIAssistant.Application.Tests.Fakes;
using WindowsAIAssistant.Application.Voice;
using WindowsAIAssistant.Application.Voice.Commands.AiQuestion;
using WindowsAIAssistant.Core.Abstractions.Security;
using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models;
using WindowsAIAssistant.Core.Models.Voice;

namespace WindowsAIAssistant.Application.Tests.Voice;

/// <summary>
/// The voice path to the assistant, tested on its own.
/// <para>
/// Voice is the one place a reply is read aloud, so it is the one place where losing the
/// detail of a failure does the most harm: a person told only that "something went wrong"
/// has nothing to act on, and cannot tell a missing API key from a question that was
/// misunderstood. The service already produces a short safe sentence for exactly this, and
/// these tests make sure it survives the trip to the speaker.
/// </para>
/// </summary>
public sealed class AiQuestionVoiceHandlerTests
{
    [Fact]
    public async Task AnAnswerIsSpokenAndTheFullTextIsKeptForTheScreen()
    {
        var ai = new FakeAIService { Response = AIResponse.Success(Answer, AIProviderType.Mock) };
        var handler = Create(ai, out _);

        var result = await handler.ExecuteAsync(Question("what is the weather like"));

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(Answer, result.ResponseText);
        Assert.Equal(Answer, result.Data!["answer"]);
    }

    [Fact]
    public async Task ALongAnswerIsShortenedForSpeechButNotForTheScreen()
    {
        var ai = new FakeAIService { Response = AIResponse.Success(Answer, AIProviderType.Mock) };
        var handler = Create(ai, out _, policy: ShortPolicy(40));

        var result = await handler.ExecuteAsync(Question("explain the whole thing"));

        // The screen can show a long answer comfortably; a speaker cannot. The full text has
        // to survive, or the user is left with a truncated reply and no way to read the rest.
        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.True(result.ResponseText.Length < Answer.Length, "the spoken reply was not shortened");
        Assert.Equal(Answer, result.Data!["answer"]);
    }

    [Fact]
    public async Task AMissingKeyIsSpokenAsSuchRatherThanAsAnUnhelpfulApology()
    {
        var ai = new FakeAIService
        {
            Response = AIResponse.Failure(
                "No API key is configured for OpenAI.",
                AIProviderType.OpenAI,
                ErrorCodes.AiCredentialMissing)
        };
        var handler = Create(ai, out _);

        var result = await handler.ExecuteAsync(Question("what is the weather like"));

        // A generic message would leave the user with no idea that a key is the problem, and
        // the code is what lets the settings page or a log line point at the right cause.
        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.AiCredentialMissing, result.ErrorCode);
        Assert.Contains("API key", result.ErrorMessage);
    }

    [Fact]
    public async Task EveryFailureKeepsItsOwnCode()
    {
        foreach (var code in new[]
                 {
                     ErrorCodes.AiAuthenticationFailed,
                     ErrorCodes.AiRateLimited,
                     ErrorCodes.AiTimedOut,
                     ErrorCodes.AiNetworkFailure,
                     ErrorCodes.AiCloudConsentRequired
                 })
        {
            var ai = new FakeAIService
            {
                Response = AIResponse.Failure("Something specific went wrong.", AIProviderType.OpenAI, code)
            };
            var handler = Create(ai, out _);

            var result = await handler.ExecuteAsync(Question("anything"));

            Assert.False(result.IsSuccess);
            Assert.Equal(code, result.ErrorCode);
        }
    }

    [Fact]
    public async Task AQuestionIsRefusedWhenCloudConsentIsWithdrawn()
    {
        var ai = new FakeAIService { Response = AIResponse.Success(Answer, AIProviderType.Mock) };
        var permissions = new FakePermissionService();
        permissions.Denied.Add(PermissionCapability.CloudAI);
        var handler = Create(ai, out _, policy: null, permissions);

        var result = await handler.ExecuteAsync(Question("what is the weather like"));

        // The refusal has to happen before the service is called. Reaching the service at all
        // would already have sent the question off the machine.
        Assert.False(result.IsSuccess);
        Assert.Equal(0, ai.CallCount);
    }

    [Fact]
    public async Task AQuestionIsForwardedExactlyAsItWasAsked()
    {
        var ai = new FakeAIService { Response = AIResponse.Success(Answer, AIProviderType.Mock) };
        var handler = Create(ai, out _);

        await handler.ExecuteAsync(Question("  why is my laptop slow  "));

        // Nothing is added, removed, or tidied: a rephrased question is a different question.
        Assert.Equal("  why is my laptop slow  ", ai.LastMessage!.Content);
    }

    [Fact]
    public async Task AQuestionWithNoTextIsRefusedRatherThanSentEmpty()
    {
        var ai = new FakeAIService { Response = AIResponse.Success(Answer, AIProviderType.Mock) };
        var handler = Create(ai, out _);

        var result = await handler.ExecuteAsync(
            VoiceCommand.Create("ask something", AssistantIntent.AIQuestion));

        Assert.False(result.IsSuccess);
        Assert.Equal(0, ai.CallCount);
    }

    [Fact]
    public async Task AnUnexpectedFailureStillProducesAGenericSafeReply()
    {
        // A defect must never be read aloud: the text of an exception can name hosts, paths,
        // and identifiers that mean nothing to the person hearing it.
        var ai = new FakeAIService { ExceptionToThrow = new InvalidOperationException("api.openai.com/v1 failed") };
        var handler = Create(ai, out _);

        var result = await handler.ExecuteAsync(Question("what is the weather like"));

        Assert.False(result.IsSuccess);
        Assert.DoesNotContain("api.openai.com", result.ErrorMessage);
    }

    private const string Answer =
        "The weather is overcast with a steady drizzle and a light breeze coming in from the west.";

    private static AiQuestionVoiceHandler Create(
        FakeAIService ai,
        out FakePermissionService permissions,
        VoiceIntentPolicy? policy = null,
        FakePermissionService? given = null)
    {
        permissions = given ?? new FakePermissionService();
        return new AiQuestionVoiceHandler(
            ai,
            policy ?? new VoiceIntentPolicy(),
            permissions,
            NullLogger<AiQuestionVoiceHandler>.Instance);
    }

    private static VoiceCommand Question(string text) =>
        VoiceCommand.Create(
            "ask something",
            AssistantIntent.AIQuestion,
            parameters: new Dictionary<string, string> { [VoiceCommand.TextParameter] = text });

    /// <summary>
    /// Builds a policy that speaks at most <paramref name="length"/> characters, the way the
    /// composition root does when configuration is reloaded.
    /// </summary>
    private static VoiceIntentPolicy ShortPolicy(int length)
    {
        var policy = new VoiceIntentPolicy();
        policy.Update(new VoiceIntentPolicyValues { MaximumSpokenResponseLength = length });
        return policy;
    }
}
