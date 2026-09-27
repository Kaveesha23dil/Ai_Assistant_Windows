using WindowsAIAssistant.Core.Abstractions.AI;

namespace WindowsAIAssistant.Application.AI.Services;

/// <summary>
/// The request defaults used when no host has supplied any.
/// <para>
/// These are the same numbers the shipped configuration starts from, written here so the
/// Application layer can be built and tested on its own. A host that binds configuration
/// registers its own instance afterwards, and the last registration is the one that resolves,
/// so nothing has to be edited in two places to change a default.
/// </para>
/// </summary>
public sealed class StaticAIRequestDefaults : IAIRequestDefaults
{
    public StaticAIRequestDefaults(
        string? model = null,
        TimeSpan? requestTimeout = null,
        bool useStreaming = true,
        int maxConversationMessages = 20)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxConversationMessages, 2);

        Model = model;
        RequestTimeout = requestTimeout ?? TimeSpan.FromSeconds(60);
        UseStreaming = useStreaming;
        MaxConversationMessages = maxConversationMessages;
    }

    /// <inheritdoc />
    public string? Model { get; }

    /// <inheritdoc />
    public TimeSpan RequestTimeout { get; }

    /// <inheritdoc />
    public bool UseStreaming { get; }

    /// <inheritdoc />
    public int MaxConversationMessages { get; }
}
