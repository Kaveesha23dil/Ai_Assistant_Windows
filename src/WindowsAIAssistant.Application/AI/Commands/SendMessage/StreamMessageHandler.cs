using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using WindowsAIAssistant.Application.AI.Services;
using WindowsAIAssistant.Application.Common.Validation;
using WindowsAIAssistant.Application.DTOs;
using WindowsAIAssistant.Core.Abstractions.AI;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models;

namespace WindowsAIAssistant.Application.AI.Commands.SendMessage;

/// <summary>
/// Answers a question and reports the reply while it is being written.
/// <para>
/// This is the path the chat page uses, and it is the same service, consent check, timeout, and
/// error translation as the whole-reply handler above — only the delivery differs. That is
/// deliberate: a typed question and a spoken one must not be able to behave differently, so
/// streaming is a change of shape at the boundary and nothing else.
/// </para>
/// <para>
/// The transcript is written from the same updates the interface shows, which is why a
/// stopped or failed answer is still recorded. What the person read is what the conversation
/// contains.
/// </para>
/// </summary>
public sealed class StreamMessageHandler
{
    private readonly IAIService _aiService;
    private readonly AIConversationContextBuilder _context;
    private readonly IConversationService _conversations;
    private readonly ILogger<StreamMessageHandler> _logger;

    public StreamMessageHandler(
        IAIService aiService,
        AIConversationContextBuilder context,
        IConversationService conversations,
        ILogger<StreamMessageHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(aiService);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(conversations);
        ArgumentNullException.ThrowIfNull(logger);

        _aiService = aiService;
        _context = context;
        _conversations = conversations;
        _logger = logger;
    }

    /// <summary>
    /// Streams the answer to a new message in a conversation.
    /// </summary>
    /// <param name="command">The question, and optionally the conversation to answer within.</param>
    /// <param name="cancellationToken">
    /// Cancels the request. Cancelling stops the answer where it is; it is not a failure and is
    /// never reported as one.
    /// </param>
    public async IAsyncEnumerable<AIStreamUpdate> HandleAsync(
        SendMessageCommand command,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var content = ValidationHelper.RequireText(
            command.Message,
            nameof(command.Message),
            "Message cannot be empty.");

        var message = AIMessage.CreateUser(content);

        ConversationDto? conversation = null;
        if (command.ConversationId is not null)
        {
            conversation = await _conversations
                .GetConversationAsync(command.ConversationId.Value, cancellationToken)
                .ConfigureAwait(false);
        }

        var request = await _context
            .BuildAsync(
                conversation.ToRequestMessages(),
                message,
                useStreaming: true,
                cancellationToken)
            .ConfigureAwait(false);

        if (command.ConversationId is not null)
        {
            await _conversations
                .AddMessageAsync(command.ConversationId.Value, message, cancellationToken)
                .ConfigureAwait(false);
        }

        _logger.LogInformation("Streaming AI message request started.");

        var updates = _aiService.StreamMessageAsync(request, cancellationToken)
            .GetAsyncEnumerator(cancellationToken);

        try
        {
            while (true)
            {
                AIStreamUpdate update;
                try
                {
                    if (!await updates.MoveNextAsync().ConfigureAwait(false))
                    {
                        break;
                    }

                    update = updates.Current;
                }
                catch (OperationCanceledException)
                {
                    // The person pressed stop. The interface keeps what it has already shown, so
                    // this is an ordinary ending and not something to report as a failure.
                    _logger.LogInformation("Streaming AI message request cancelled by caller.");
                    throw;
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, "Streaming AI message request failed.");
                    throw;
                }

                if (update.Kind == AIStreamUpdateKind.Completed || update.Kind == AIStreamUpdateKind.Failed)
                {
                    // The answer is stored before it is handed on, so a consumer that stops as
                    // soon as it sees the final update still leaves a complete transcript.
                    await RecordAnswerAsync(command, update).ConfigureAwait(false);
                }

                yield return update;
            }
        }
        finally
        {
            await updates.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Stores the answer the interface is showing, partial text included.
    /// </summary>
    private async Task RecordAnswerAsync(SendMessageCommand command, AIStreamUpdate update)
    {
        if (command.ConversationId is null || string.IsNullOrWhiteSpace(update.Text))
        {
            return;
        }

        await _conversations
            .AddMessageAsync(
                command.ConversationId.Value,
                AIMessage.CreateAssistant(update.Text),
                CancellationToken.None)
            .ConfigureAwait(false);
    }
}
