using Microsoft.Extensions.Logging.Abstractions;
using WindowsAIAssistant.Application.AI.Commands.SendMessage;
using WindowsAIAssistant.Application.AI.Services;
using WindowsAIAssistant.Application.Tests.Fakes;
using WindowsAIAssistant.Application.Tests.Helpers;
using WindowsAIAssistant.Core.Abstractions.AI;
using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models;

namespace WindowsAIAssistant.Application.Tests.AI;

/// <summary>
/// The streamed path through the application: what a caller sees while an answer is arriving,
/// and what is left behind afterwards.
/// </summary>
public sealed class StreamMessageHandlerTests
{
    [Fact]
    public async Task HandleAsync_YieldsTheAnswerInPartsAndThenCompletes()
    {
        const string answer = "An answer that arrives in pieces.";
        var service = new FakeAIService
        {
            Response = AIResponse.Success(answer, AIProviderType.Mock, "fake-model"),
        };
        var handler = CreateHandler(service);

        var updates = await CollectAsync(handler, new SendMessageCommand("A question"));

        // The order is the contract: parts first, and a completion last, so a caller can bind to
        // the completion and know the answer is final.
        Assert.Equal(AIStreamUpdateKind.Started, updates[0].Kind);
        Assert.Equal(AIStreamUpdateKind.Completed, updates[^1].Kind);
        Assert.Contains(updates, update => update.Kind == AIStreamUpdateKind.Delta);

        var joined = string.Concat(updates
            .Where(update => update.Kind == AIStreamUpdateKind.Delta)
            .Select(update => update.Text));
        Assert.Equal(answer, joined);
    }

    [Fact]
    public async Task HandleAsync_SendsTheSystemPromptAndTheQuestionTogether()
    {
        var service = new FakeAIService();
        var handler = CreateHandler(service);

        await CollectAsync(handler, new SendMessageCommand("A question"));

        // The instructions are what keep the answer from treating its own text as something to
        // act on, so their absence is a defect rather than a default.
        var request = service.LastRequest;
        Assert.NotNull(request);
        Assert.Equal(AIMessageRole.System, request.Messages.First().Role);
        Assert.Equal("A question", request.Messages.Last().Content);
    }

    [Fact]
    public async Task HandleAsync_RecordsTheAnswerInTheConversation()
    {
        const string answer = "A recorded answer.";
        var service = new FakeAIService
        {
            Response = AIResponse.Success(answer, AIProviderType.Mock, "fake-model"),
        };
        var conversations = AITestHarness.CreateConversations();
        var handler = CreateHandler(service, conversations);

        var conversation = await conversations.CreateConversationAsync();
        await CollectAsync(handler, new SendMessageCommand("A question", conversation.Id));

        var stored = await conversations.GetConversationAsync(conversation.Id);
        Assert.NotNull(stored);
        var recorded = stored.Messages.ToArray();
        Assert.Equal(2, recorded.Length);
        Assert.Equal("A question", recorded[0].Content);
        Assert.Equal(answer, recorded[1].Content);
    }

    [Fact]
    public async Task HandleAsync_WhenTheRequestFails_RecordsNoAnswerAndReportsTheFailure()
    {
        var service = new FakeAIService
        {
            StreamUpdates =
            [
                AIStreamUpdate.Failed(
                    string.Empty,
                    AIResponse.Failure("Not now.", AIProviderType.Mock, ErrorCodes.AiRateLimited),
                    ErrorCodes.AiRateLimited,
                    "Not now."),
            ],
        };
        var conversations = AITestHarness.CreateConversations();
        var handler = CreateHandler(service, conversations);

        var conversation = await conversations.CreateConversationAsync();
        var updates = await CollectAsync(handler, new SendMessageCommand("A question", conversation.Id));

        var failure = Assert.Single(updates, update => update.Kind == AIStreamUpdateKind.Failed);
        Assert.Equal(ErrorCodes.AiRateLimited, failure.ErrorCode);

        // An empty assistant turn would be replayed as a blank answer on the next turn.
        var stored = await conversations.GetConversationAsync(conversation.Id);
        Assert.NotNull(stored);
        Assert.Equal("A question", Assert.Single(stored.Messages).Content);
    }

    [Fact]
    public async Task HandleAsync_WhenTheAnswerStopsPartWay_KeepsTheTextAlreadyReceived()
    {
        // Some text arrives and then the connection drops. This is the case that decides whether
        // a readable half-answer survives or is thrown away.
        var service = new FakeAIService
        {
            StreamUpdates =
            [
                AIStreamUpdate.Started(AIProviderType.Mock, "fake-model"),
                AIStreamUpdate.Delta("Half an "),
                AIStreamUpdate.Delta("answer."),
                AIStreamUpdate.Failed(
                    "Half an answer.",
                    AIResponse.Failure("Dropped.", AIProviderType.Mock, ErrorCodes.AiNetworkFailure),
                    ErrorCodes.AiNetworkFailure,
                    "The connection dropped."),
            ],
        };
        var handler = CreateHandler(service);

        var updates = await CollectAsync(handler, new SendMessageCommand("A question"));

        var failure = Assert.Single(updates, update => update.Kind == AIStreamUpdateKind.Failed);
        Assert.Equal(ErrorCodes.AiNetworkFailure, failure.ErrorCode);
        Assert.Equal("Half an answer.", failure.Text);
    }

    [Fact]
    public async Task HandleAsync_WithAnEmptyMessage_ThrowsBeforeCallingTheService()
    {
        var service = new FakeAIService();
        var handler = CreateHandler(service);

        // Enumerating is what runs the iterator, so this is where the guard is observed.
        await Assert.ThrowsAsync<ArgumentException>(
            async () => await CollectAsync(handler, new SendMessageCommand("   ")));

        Assert.Equal(0, service.CallCount);
    }

    [Fact]
    public async Task HandleAsync_WhenCancelled_StopsWithoutRecordingAnAnswer()
    {
        var service = new FakeAIService
        {
            Response = AIResponse.Success("A long answer that is interrupted.", AIProviderType.Mock),
            FragmentDelay = TimeSpan.FromMilliseconds(30),
        };
        var conversations = AITestHarness.CreateConversations();
        var handler = CreateHandler(service, conversations);
        var conversation = await conversations.CreateConversationAsync();

        using var cancellation = new CancellationTokenSource();
        var seen = 0;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var update in handler.HandleAsync(
                               new SendMessageCommand("A question", conversation.Id),
                               cancellation.Token))
            {
                seen++;
                if (seen == 1)
                {
                    await cancellation.CancelAsync();
                }
            }
        });

        Assert.True(seen > 0);
    }

    private static StreamMessageHandler CreateHandler(
        FakeAIService service,
        ConversationService? conversations = null) =>
        new(
            service,
            AITestHarness.CreateContextBuilder(),
            conversations ?? AITestHarness.CreateConversations(),
            NullLogger<StreamMessageHandler>.Instance);

    private static async Task<List<AIStreamUpdate>> CollectAsync(
        StreamMessageHandler handler,
        SendMessageCommand command,
        CancellationToken cancellationToken = default)
    {
        var updates = new List<AIStreamUpdate>();

        await foreach (var update in handler.HandleAsync(command, cancellationToken))
        {
            updates.Add(update);
        }

        return updates;
    }
}
