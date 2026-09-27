namespace WindowsAIAssistant.Core.Models;

/// <summary>
/// One request to an AI provider: the conversation to answer, plus the settings to answer it
/// under.
/// <para>
/// This is the only thing a provider is given. It carries no provider-specific options, so a
/// new provider needs no change here, and it carries no key, so nothing that holds a request
/// can leak a credential by accident.
/// </para>
/// </summary>
public sealed record AIRequest
{
    public AIRequest(
        IReadOnlyCollection<AIMessage> messages,
        AIRequestSettings? settings = null)
    {
        ArgumentNullException.ThrowIfNull(messages);
        if (messages.Count == 0)
        {
            throw new ArgumentException("At least one message is required.", nameof(messages));
        }

        Messages = messages;
        Settings = settings ?? new AIRequestSettings();
    }

    /// <summary>Gets the conversation so far, oldest first, ending with the new message.</summary>
    public IReadOnlyCollection<AIMessage> Messages { get; }

    /// <summary>Gets the per-request settings.</summary>
    public AIRequestSettings Settings { get; }

    /// <summary>Creates a request for a single message, using the default settings.</summary>
    public static AIRequest FromMessage(string message) =>
        new([AIMessage.CreateUser(message)]);
}
