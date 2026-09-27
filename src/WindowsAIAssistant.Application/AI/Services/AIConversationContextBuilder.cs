using WindowsAIAssistant.Core.Abstractions.AI;
using WindowsAIAssistant.Core.Models;

namespace WindowsAIAssistant.Application.AI.Services;

/// <summary>
/// Turns the running conversation into the request that is actually sent.
/// <para>
/// Three things happen here that are worth stating plainly. The system instructions go in
/// first, from <see cref="IAISystemPromptProvider"/>, so no caller has to remember them and no
/// provider has to invent them. The history is cut to a bounded number of recent messages, so
/// a long conversation cannot grow without limit and be resent in full on every turn. And the
/// streaming preference is taken from the current configuration unless the caller asked for
/// something specific, so a person changes it in Settings rather than in code.
/// </para>
/// <para>
/// The cut is made from the newest end and always leaves whole messages, so the provider never
/// sees a reply whose question was dropped.
/// </para>
/// </summary>
public sealed class AIConversationContextBuilder
{
    private readonly IAISystemPromptProvider _systemPrompt;
    private readonly IAIRequestDefaults _defaults;

    public AIConversationContextBuilder(IAISystemPromptProvider systemPrompt, IAIRequestDefaults defaults)
    {
        ArgumentNullException.ThrowIfNull(systemPrompt);
        ArgumentNullException.ThrowIfNull(defaults);

        _systemPrompt = systemPrompt;
        _defaults = defaults;
    }

    /// <summary>
    /// Builds the request for a new message in a conversation.
    /// </summary>
    /// <param name="history">The earlier messages, oldest first. May be empty.</param>
    /// <param name="newMessage">The message being sent now.</param>
    /// <param name="useStreaming">
    /// Whether the answer should stream, or <see langword="null"/> to use the configured
    /// default.
    /// </param>
    /// <param name="cancellationToken">Cancels reading the instructions.</param>
    public async Task<AIRequest> BuildAsync(
        IReadOnlyCollection<AIMessage> history,
        AIMessage newMessage,
        bool? useStreaming = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(newMessage);

        var prompt = await _systemPrompt.GetSystemPromptAsync(cancellationToken).ConfigureAwait(false);
        var limit = Math.Max(2, _defaults.MaxConversationMessages);

        var messages = new List<AIMessage>(Math.Min(limit, history.Count) + 2)
        {
            AIMessage.CreateSystem(prompt)
        };

        // Two slots are spoken for before any history is added: the instructions and the message
        // being sent now. Taking a full limit of history as well would put the request one over
        // the configured maximum, which is a limit and not a suggestion.
        messages.AddRange(TakeRecent(history, limit - 2));
        messages.Add(newMessage);

        return new AIRequest(
            messages,
            new AIRequestSettings
            {
                Model = _defaults.Model,
                UseStreaming = useStreaming ?? _defaults.UseStreaming,
            });
    }

    /// <summary>
    /// Keeps the newest messages that fit, dropping the oldest first.
    /// </summary>
    private static IReadOnlyCollection<AIMessage> TakeRecent(
        IReadOnlyCollection<AIMessage> history,
        int limit)
    {
        if (history.Count <= limit)
        {
            return history;
        }

        return history.Skip(history.Count - limit).ToArray();
    }
}
