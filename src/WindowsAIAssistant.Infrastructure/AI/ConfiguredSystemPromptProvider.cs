using Microsoft.Extensions.Options;
using WindowsAIAssistant.Core.Abstractions.AI;
using WindowsAIAssistant.Infrastructure.Configuration.Options;

namespace WindowsAIAssistant.Infrastructure.AI;

/// <summary>
/// Supplies the system instructions from configuration, falling back to the built-in ones.
/// <para>
/// Falling back matters: the built-in prompt is the wording that tells the assistant it
/// cannot run anything, so an empty or missing value must mean "use the safe one" rather than
/// "send no instructions at all". A provider receiving an answer with no instructions is a
/// provider being asked to invent the assistant's character.
/// </para>
/// <para>
/// This is the only place prompt text can be replaced, and it holds no provider knowledge at
/// all: the instructions are handed to whichever provider is selected, unchanged.
/// </para>
/// </summary>
public sealed class ConfiguredSystemPromptProvider : IAISystemPromptProvider
{
    private readonly IOptionsMonitor<AIOptions> _options;

    public ConfiguredSystemPromptProvider(IOptionsMonitor<AIOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options;
    }

    /// <inheritdoc />
    public ValueTask<string> GetSystemPromptAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Read per request rather than once, so an edited prompt takes effect on the next
        // message instead of needing a restart.
        var configured = _options.CurrentValue.SystemPrompt;
        var prompt = string.IsNullOrWhiteSpace(configured) ? DefaultSystemPrompt.Text : configured;

        return ValueTask.FromResult(prompt);
    }
}
