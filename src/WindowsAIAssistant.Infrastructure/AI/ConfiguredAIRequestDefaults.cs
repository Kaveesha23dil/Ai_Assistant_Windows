using Microsoft.Extensions.Options;
using WindowsAIAssistant.Core.Abstractions.AI;
using WindowsAIAssistant.Infrastructure.Configuration.Options;

namespace WindowsAIAssistant.Infrastructure.AI;

/// <summary>
/// Projects the bound <c>AI</c> configuration onto the provider-neutral defaults the
/// coordinator reads.
/// <para>
/// This is the seam that keeps the Application layer free of an options type while still
/// honouring configuration: the coordinator asks for "the timeout in effect" and gets a
/// number, not a settings object it would then have to understand.
/// </para>
/// <para>
/// Each value is read from options when it is asked for, not when this is constructed, so a
/// change to the setting is honoured by the next request rather than needing a restart. A
/// request already in flight is unaffected: it was handed its own copy of the settings when it
/// started.
/// </para>
/// </summary>
public sealed class ConfiguredAIRequestDefaults : IAIRequestDefaults
{
    private readonly IOptionsMonitor<AIOptions> _options;

    public ConfiguredAIRequestDefaults(IOptionsMonitor<AIOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options;
    }

    /// <inheritdoc />
    public string? Model => _options.CurrentValue.Model;

    /// <inheritdoc />
    public TimeSpan RequestTimeout =>
        TimeSpan.FromSeconds(_options.CurrentValue.RequestTimeoutSeconds);

    /// <inheritdoc />
    public bool UseStreaming => _options.CurrentValue.UseStreaming;

    /// <inheritdoc />
    public int MaxConversationMessages => _options.CurrentValue.MaxConversationMessages;
}
