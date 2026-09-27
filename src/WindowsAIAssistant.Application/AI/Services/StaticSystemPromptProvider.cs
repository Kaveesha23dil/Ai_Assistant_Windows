using WindowsAIAssistant.Core.Abstractions.AI;

namespace WindowsAIAssistant.Application.AI.Services;

/// <summary>
/// Serves a fixed set of system instructions.
/// <para>
/// This is the fallback for a host that has no configuration of its own, and it is what keeps
/// the Application layer buildable and testable without an Infrastructure project behind it.
/// </para>
/// </summary>
public sealed class StaticSystemPromptProvider : IAISystemPromptProvider
{
    private readonly string _prompt;

    public StaticSystemPromptProvider(string prompt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);
        _prompt = prompt;
    }

    /// <inheritdoc />
    public ValueTask<string> GetSystemPromptAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(_prompt);
    }
}
