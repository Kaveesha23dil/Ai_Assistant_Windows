using Microsoft.Extensions.Logging;
using WindowsAIAssistant.Core.Abstractions.Voice;
using WindowsAIAssistant.Core.Enums;

namespace WindowsAIAssistant.Application.Voice.Services;

/// <summary>
/// Builds the intent-to-handler table from every executor in the container.
/// <para>
/// Because the registry is populated by injection rather than by a hand-written map, adding
/// a capability means registering one more executor. A duplicate registration is a
/// programming error and is reported at construction time instead of silently shadowing a
/// handler.
/// </para>
/// </summary>
public sealed class AssistantActionRegistry : IAssistantActionRegistry
{
    private readonly IReadOnlyDictionary<AssistantIntent, IAssistantActionExecutor> _executors;
    private readonly ILogger<AssistantActionRegistry> _logger;

    public AssistantActionRegistry(
        IEnumerable<IAssistantActionExecutor> executors,
        ILogger<AssistantActionRegistry> logger)
    {
        ArgumentNullException.ThrowIfNull(executors);
        ArgumentNullException.ThrowIfNull(logger);

        Dictionary<AssistantIntent, IAssistantActionExecutor> map = [];
        List<string> duplicates = [];

        foreach (var executor in executors)
        {
            foreach (var intent in executor.Intents)
            {
                if (!map.TryAdd(intent, executor))
                {
                    duplicates.Add($"{intent} ({executor.GetType().Name})");
                }
            }
        }

        if (duplicates.Count > 0)
        {
            logger.LogWarning(
                "Duplicate voice executors ignored: {Duplicates}.",
                string.Join(", ", duplicates));
        }

        _executors = map;
        _logger = logger;
    }

    /// <inheritdoc />
    public IReadOnlyCollection<AssistantIntent> RegisteredIntents => _executors.Keys.ToArray();

    /// <inheritdoc />
    public bool TryGetExecutor(AssistantIntent intent, out IAssistantActionExecutor? executor) =>
        _executors.TryGetValue(intent, out executor);
}
