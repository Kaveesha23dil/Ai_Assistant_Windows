using WindowsAIAssistant.Application.AI.Services;
using WindowsAIAssistant.Application.Tests.Helpers;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models;

namespace WindowsAIAssistant.Application.Tests.AI;

/// <summary>
/// What the assistant is told before the conversation starts, and how much of the conversation
/// it is told.
/// </summary>
public sealed class AIConversationContextBuilderTests
{
    [Fact]
    public async Task BuildAsync_PutsTheSystemInstructionsFirst()
    {
        var builder = AITestHarness.CreateContextBuilder("Follow these instructions.");

        var request = await builder.BuildAsync([], AIMessage.CreateUser("A question"));

        // First, because an instruction that arrives after the question is a comment on it
        // rather than a rule for the answer.
        var first = request.Messages.First();
        Assert.Equal(AIMessageRole.System, first.Role);
        Assert.Equal("Follow these instructions.", first.Content);
        Assert.Equal("A question", request.Messages.Last().Content);
    }

    [Fact]
    public async Task BuildAsync_WithNoHistory_SendsOnlyTheInstructionsAndTheQuestion()
    {
        var builder = AITestHarness.CreateContextBuilder();

        var request = await builder.BuildAsync([], AIMessage.CreateUser("A question"));

        Assert.Equal(2, request.Messages.Count);
    }

    [Fact]
    public async Task BuildAsync_KeepsEarlierTurnsSoFollowUpQuestionsMakeSense()
    {
        var builder = AITestHarness.CreateContextBuilder();
        List<AIMessage> history =
        [
            AIMessage.CreateUser("What is the capital of France?"),
            AIMessage.CreateAssistant("Paris."),
        ];

        var request = await builder.BuildAsync(history, AIMessage.CreateUser("And of Italy?"));

        var contents = request.Messages.Select(message => message.Content).ToArray();
        Assert.Contains("What is the capital of France?", contents);
        Assert.Contains("Paris.", contents);
        Assert.Equal("And of Italy?", contents[^1]);
    }

    [Fact]
    public async Task BuildAsync_WhenHistoryIsLong_KeepsTheMostRecentTurns()
    {
        const int limit = 6;
        var builder = AITestHarness.CreateContextBuilder(maxConversationMessages: limit);
        var history = Enumerable
            .Range(0, 40)
            .Select(index => AIMessage.CreateUser($"Question {index}"))
            .ToArray();

        var request = await builder.BuildAsync(history, AIMessage.CreateUser("The newest question"));

        // The instructions and the new question are the two that must always be present, so the
        // history is what gets trimmed.
        Assert.True(
            request.Messages.Count <= limit,
            $"Expected at most {limit} messages but got {request.Messages.Count}.");

        var contents = request.Messages.Select(message => message.Content).ToArray();
        Assert.Equal("The newest question", contents[^1]);
        Assert.Contains("Question 39", contents);
        Assert.DoesNotContain("Question 0", contents);
    }

    [Fact]
    public async Task BuildAsync_UsesTheConfiguredStreamingChoice()
    {
        var builder = AITestHarness.CreateContextBuilder();

        var streamed = await builder.BuildAsync([], AIMessage.CreateUser("A question"), useStreaming: true);
        var whole = await builder.BuildAsync([], AIMessage.CreateUser("A question"), useStreaming: false);

        // The provider has to know which shape to produce, so this cannot be left to a default
        // the caller has to know about.
        Assert.True(streamed.Settings.UseStreaming);
        Assert.False(whole.Settings.UseStreaming);
    }

    [Fact]
    public async Task BuildAsync_AppliesTheConfiguredModel()
    {
        var builder = AITestHarness.CreateContextBuilder();

        var request = await builder.BuildAsync([], AIMessage.CreateUser("A question"));

        Assert.Equal("test-model", request.Settings.Model);
    }
}
