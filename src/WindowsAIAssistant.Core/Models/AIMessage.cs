using WindowsAIAssistant.Core.Enums;

namespace WindowsAIAssistant.Core.Models;

/// <summary>
/// An immutable message exchanged within an AI conversation.
/// </summary>
public sealed record AIMessage
{
    public AIMessage(Guid id, AIMessageRole role, string content, DateTimeOffset timestamp)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(content);

        Id = id;
        Role = role;
        Content = content;
        Timestamp = timestamp;
    }

    /// <summary>Gets the unique identifier of the message.</summary>
    public Guid Id { get; }

    /// <summary>Gets the role of the message sender.</summary>
    public AIMessageRole Role { get; }

    /// <summary>Gets the message content. Never <see langword="null"/> or empty.</summary>
    public string Content { get; }

    /// <summary>Gets the point in time the message was created.</summary>
    public DateTimeOffset Timestamp { get; }

    /// <summary>Creates a user message with a new identifier and the current UTC time.</summary>
    public static AIMessage CreateUser(string content) => Create(AIMessageRole.User, content);

    /// <summary>Creates an assistant message with a new identifier and the current UTC time.</summary>
    public static AIMessage CreateAssistant(string content) => Create(AIMessageRole.Assistant, content);

    private static AIMessage Create(AIMessageRole role, string content)
        => new(Guid.NewGuid(), role, content, DateTimeOffset.UtcNow);
}