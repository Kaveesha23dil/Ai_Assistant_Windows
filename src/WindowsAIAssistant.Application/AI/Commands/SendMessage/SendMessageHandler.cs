using Microsoft.Extensions.Logging;
using WindowsAIAssistant.Application.AI.Services;
using WindowsAIAssistant.Application.Common.Validation;
using WindowsAIAssistant.Core.Abstractions.AI;
using WindowsAIAssistant.Core.Models;

namespace WindowsAIAssistant.Application.AI.Commands.SendMessage;

/// <summary>
/// Answers a typed question and returns the whole reply at once.
/// <para>
/// The handler owns the conversation, not the provider: it asks
/// <see cref="AIConversationContextBuilder"/> for the request, sends it through the one
/// <see cref="IAIService"/> every caller shares, and records both sides in the conversation.
/// Whether the answer came from a cloud provider or the development stand-in, and whether it
/// succeeded or was refused, the transcript reads the same afterwards.
/// </para>
/// <para>
/// A failure is returned as a failed <see cref="AIResponse"/> rather than thrown. The voice
/// path has to speak such an outcome and the chat page has to show it, and a spoken "the
/// assistant could not answer that" is far more useful than a stack trace crossing the
/// interface. Content never reaches the log: a question is the person's own words.
/// </para>
/// </summary>
public sealed class SendMessageHandler
{
    private readonly IAIService _aiService;
    private readonly AIConversationContextBuilder _context;
    private readonly IConversationService _conversations;
    private readonly ILogger<SendMessageHandler> _logger;

    public SendMessageHandler(
        IAIService aiService,
        AIConversationContextBuilder context,
        IConversationService conversations,
        ILogger<SendMessageHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(aiService);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(conversations);
        ArgumentNullException.ThrowIfNull(logger);

        _aiService = aiService;
        _context = context;
        _logger = logger;
        _conversations = conversations;
    }

    public async Task<AIResponse> HandleAsync(
        SendMessageCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        var content = ValidationHelper.RequireText(
            command.Message,
            nameof(command.Message),
            "Message cannot be empty.");

        var message = AIMessage.CreateUser(content);
        var request = await BuildRequestAsync(command, message, useStreaming: false, cancellationToken)
            .ConfigureAwait(false);

        await RecordAsync(command.ConversationId, message, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("AI message request started.");
        try
        {
            var response = await _aiService
                .SendMessageAsync(request.Messages, cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            if (response.IsSuccessful)
            {
                _logger.LogInformation("AI message request completed using provider {Provider}.", response.Provider);
            }
            else
            {
                _logger.LogWarning(
                    "AI message request failed with code {ErrorCode} using provider {Provider}.",
                    response.ErrorCode,
                    response.Provider);
            }

            if (response.HasContent)
            {
                await RecordAnswerAsync(command.ConversationId, response, cancellationToken).ConfigureAwait(false);
            }

            return response;
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("AI message request cancelled by caller.");
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "AI message request failed.");
            throw;
        }
    }

    /// <summary>
    /// Loads the earlier turns for a conversation and prepares the request.
    /// </summary>
    private async Task<AIRequest> BuildRequestAsync(
        SendMessageCommand command,
        AIMessage message,
        bool useStreaming,
        CancellationToken cancellationToken)
    {
        var history = await LoadHistoryAsync(command.ConversationId, cancellationToken).ConfigureAwait(false);
        return await _context
            .BuildAsync(history, message, useStreaming, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<IReadOnlyCollection<AIMessage>> LoadHistoryAsync(
        Guid? conversationId,
        CancellationToken cancellationToken)
    {
        if (conversationId is null)
        {
            return [];
        }

        var conversation = await _conversations
            .GetConversationAsync(conversationId.Value, cancellationToken)
            .ConfigureAwait(false);

        return conversation.ToRequestMessages();
    }

    private async Task RecordAsync(
        Guid? conversationId,
        AIMessage message,
        CancellationToken cancellationToken)
    {
        if (conversationId is null)
        {
            return;
        }

        await _conversations
            .AddMessageAsync(conversationId.Value, message, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Records the reply, including a partial one. A half-finished answer is still what the
    /// person read, so discarding it would leave the transcript disagreeing with the screen.
    /// </summary>
    private async Task RecordAnswerAsync(
        Guid? conversationId,
        AIResponse response,
        CancellationToken cancellationToken)
    {
        if (conversationId is null || !response.HasContent)
        {
            return;
        }

        await _conversations
            .AddMessageAsync(
                conversationId.Value,
                AIMessage.CreateAssistant(response.Content),
                cancellationToken)
            .ConfigureAwait(false);
    }
}
