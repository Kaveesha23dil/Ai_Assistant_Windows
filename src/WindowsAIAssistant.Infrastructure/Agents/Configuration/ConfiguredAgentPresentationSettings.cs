using Microsoft.Extensions.Options;
using WindowsAIAssistant.Core.Abstractions.Agents;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Infrastructure.Agents;

namespace WindowsAIAssistant.Infrastructure.Agents.Configuration;

/// <summary>
/// The presentation mode as bound configuration states it.
/// <para>
/// A monitor rather than a value, so that a change to the section is picked up by a page that is
/// already open. That matters for a workspace somebody is demonstrating: the mode can be switched
/// without relaunching, and a page that had already built its scenario list rebuilds it.
/// </para>
/// </summary>
public sealed class ConfiguredAgentPresentationSettings : IAgentPresentationSettings
{
    private readonly IOptionsMonitor<AgentOptions> _options;

    public ConfiguredAgentPresentationSettings(IOptionsMonitor<AgentOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options;
    }

    /// <inheritdoc />
    public AgentPresentationMode Mode => _options.CurrentValue.PresentationMode;
}
