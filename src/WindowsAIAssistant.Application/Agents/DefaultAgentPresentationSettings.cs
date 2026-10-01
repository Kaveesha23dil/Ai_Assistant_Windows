using WindowsAIAssistant.Core.Abstractions.Agents;
using WindowsAIAssistant.Core.Enums;

namespace WindowsAIAssistant.Application.Agents;

/// <summary>
/// The presentation mode in effect when nothing has configured one.
/// <para>
/// Registered by the Application layer so that the workspace, its view models, and every test
/// that constructs a page can ask for the presentation without a container behind them. A host
/// that binds configuration replaces this one, because its registrations come second.
/// </para>
/// </summary>
public sealed class DefaultAgentPresentationSettings : IAgentPresentationSettings
{
    /// <inheritdoc />
    public AgentPresentationMode Mode => AgentPresentationMode.Standard;
}
