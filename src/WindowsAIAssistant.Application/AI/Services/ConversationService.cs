using System.Collections.Concurrent;
using WindowsAIAssistant.Application.AI.Services;
using WindowsAIAssistant.Application.Common.Exceptions;
using WindowsAIAssistant.Application.DTOs;
using WindowsAIAssistant.Core.Models;

namespace WindowsAIAssistant.Application.AI.Services;

public sealed class ConversationService : IConversationService
{
    private const string DefaultConversationTitle = "New Conversation";
    private readonly ConcurrentDictionary<Guid, ConversationState> _conversations = new();

    public Task<ConversationDto> CreateConversationAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var state = new ConversationState(Guid.NewGuid(), DefaultConversationTitle, DateTimeOffset.UtcNow);
        _conversations[state.Id] = state;

        return Task.FromResult(CreateDto(state));
    }

    public Task AddMessageAsync(
        Guid conversationId,
        AIMessage message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        ValidateConversationId(conversationId);
        cancellationToken.ThrowIfCancellationRequested();

        var state = GetRequiredConversation(conversationId);
        lock (state.SyncRoot)
        {
            state.Messages.Add(message);
            state.UpdatedAt = DateTimeOffset.UtcNow;
        }

        return Task.CompletedTask;
    }

    public Task<ConversationDto?> GetConversationAsync(
        Guid conversationId,
        CancellationToken cancellationToken = default)
    {
        ValidateConversationId(conversationId);
        cancellationToken.ThrowIfCancellationRequested();

        if (!_conversations.TryGetValue(conversationId, out var state))
        {
            return Task.FromResult<ConversationDto?>(null);
        }

        return Task.FromResult<ConversationDto?>(CreateDto(state));
    }

    public Task ClearConversationAsync(
        Guid conversationId,
        CancellationToken cancellationToken = default)
    {
        ValidateConversationId(conversationId);
        cancellationToken.ThrowIfCancellationRequested();

        var state = GetRequiredConversation(conversationId);
        lock (state.SyncRoot)
        {
            state.Messages.Clear();
            state.UpdatedAt = DateTimeOffset.UtcNow;
        }

        return Task.CompletedTask;
    }

    private ConversationState GetRequiredConversation(Guid conversationId)
    {
        if (!_conversations.TryGetValue(conversationId, out var state))
        {
            throw new ConversationNotFoundException(conversationId);
        }

        return state;
    }

    private static ConversationDto CreateDto(ConversationState state)
    {
        lock (state.SyncRoot)
        {
            return new ConversationDto(
                state.Id,
                state.Title,
                state.Messages.Select(message => new AIMessageDto(
                    message.Id,
                    message.Role,
                    message.Content,
                    message.Timestamp)).ToArray(),
                state.CreatedAt,
                state.UpdatedAt);
        }
    }

    private static void ValidateConversationId(Guid conversationId)
    {
        if (conversationId == Guid.Empty)
        {
            throw new ArgumentException("Conversation id cannot be empty.", nameof(conversationId));
        }
    }

    private sealed class ConversationState
    {
        public ConversationState(Guid id, string title, DateTimeOffset createdAt)
        {
            Id = id;
            Title = title;
            CreatedAt = createdAt;
            UpdatedAt = createdAt;
        }

        public Guid Id { get; }
        public string Title { get; }
        public DateTimeOffset CreatedAt { get; }
        public DateTimeOffset UpdatedAt { get; set; }
        public List<AIMessage> Messages { get; } = [];
        public object SyncRoot { get; } = new();
    }
}
