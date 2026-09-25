namespace WindowsAIAssistant.Core.Models;

/// <summary>
/// A collection of messages exchanged between the user and the assistant.
/// </summary>
public sealed class AIConversation
{
    private readonly List<AIMessage> _messages = [];

    public AIConversation(Guid id, string title, DateTimeOffset createdAt)
    {
        ArgumentNullException.ThrowIfNull(title);

        Id = id;
        Title = title;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    /// <summary>Gets the unique identifier of the conversation.</summary>
    public Guid Id { get; }

    /// <summary>Gets the conversation title.</summary>
    public string Title { get; }

    /// <summary>Gets the messages in the order they were added.</summary>
    public IReadOnlyCollection<AIMessage> Messages => _messages;

    /// <summary>Gets the point in time the conversation was created.</summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>Gets the point in time the conversation was last modified.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Adds a message to the conversation.</summary>
    public void AddMessage(AIMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        _messages.Add(message);
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}