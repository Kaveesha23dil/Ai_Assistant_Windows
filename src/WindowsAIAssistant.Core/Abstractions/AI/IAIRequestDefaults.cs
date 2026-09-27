namespace WindowsAIAssistant.Core.Abstractions.AI;

/// <summary>
/// Reports the request defaults currently in effect.
/// <para>
/// The coordinator needs a timeout, a history limit, and a streaming preference, but it lives
/// in the Application layer and must not read an Infrastructure options type or a
/// configuration file. Reading those numbers through a small abstraction keeps the policy in
/// one place: a host that binds configuration supplies the real values, and a test or a
/// headless host supplies its own without editing a settings file.
/// </para>
/// </summary>
public interface IAIRequestDefaults
{
    /// <summary>Gets the model identifier to use when a request does not name one.</summary>
    string? Model { get; }

    /// <summary>Gets the time budget for a request that does not set its own.</summary>
    TimeSpan RequestTimeout { get; }

    /// <summary>
    /// Gets a value indicating whether answers stream by default. A request may still ask for
    /// a whole answer at once.
    /// </summary>
    bool UseStreaming { get; }

    /// <summary>
    /// Gets how many earlier messages are sent along with a new one. Bounding this is what
    /// stops an unanswered conversation from growing without limit and being sent in full on
    /// every turn.
    /// </summary>
    int MaxConversationMessages { get; }
}
