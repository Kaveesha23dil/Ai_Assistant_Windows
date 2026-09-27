namespace WindowsAIAssistant.Core.Enums;

/// <summary>
/// Identifies which part of a streamed answer an update carries.
/// <para>
/// Streaming exists so the user interface can grow one assistant message as the answer
/// arrives. A caller therefore needs to know whether an update opens a message, extends it,
/// closes it, or reports that it stopped early, and that has to be expressible without
/// reference to any provider's own event type.
/// </para>
/// </summary>
public enum AIStreamUpdateKind
{
    /// <summary>
    /// The provider accepted the request. Carries the provider and model so the interface can
    /// label the message before any text has arrived.
    /// </summary>
    Started,

    /// <summary>A fragment of the answer to append to the current message.</summary>
    Delta,

    /// <summary>The provider finished normally. Carries the assembled response.</summary>
    Completed,

    /// <summary>
    /// The request stopped early. Carries whatever text had already arrived, so a partial
    /// answer is kept rather than discarded.
    /// </summary>
    Failed
}
