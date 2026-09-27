using Microsoft.Extensions.Options;
using WindowsAIAssistant.Core.Abstractions.AI;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Infrastructure.Configuration.Options;

namespace WindowsAIAssistant.Infrastructure.AI;

/// <summary>
/// Reads the provider key from the person's environment.
/// <para>
/// The variable is read each time a client is built rather than once at start-up, so a key
/// added while the application is open is picked up without a restart, and so nothing holds
/// the value any longer than the client that needs it.
/// </para>
/// <para>
/// The name of the variable is configuration, but its value is not: a key typed into a
/// settings file would be committed, copied into backups, and readable by every other process
/// running as the same person. The environment keeps it out of the repository and out of the
/// application data folder, and out of reach of anything that reads configuration.
/// </para>
/// </summary>
public sealed class EnvironmentApiKeyProvider : IAIApiKeyProvider
{
    private readonly IOptionsMonitor<AIOptions> _options;

    public EnvironmentApiKeyProvider(IOptionsMonitor<AIOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options;
    }

    /// <inheritdoc />
    public AIProviderType Provider => AIProviderType.OpenAI;

    /// <summary>
    /// Gets the name of the environment variable the key is read from. Read per call, so the
    /// name is as configurable as any other setting.
    /// </summary>
    public string VariableName => _options.CurrentValue.ApiKeyEnvironmentVariable;

    /// <inheritdoc />
    public bool HasApiKey => !string.IsNullOrWhiteSpace(Read());

    /// <inheritdoc />
    public string? GetApiKey() => Read();

    /// <summary>
    /// Reads the variable, treating a whitespace-only value as no value at all: a variable
    /// that was exported empty would otherwise be sent as a credential and produce a confusing
    /// authentication failure instead of a clear "no key configured".
    /// </summary>
    private string? Read()
    {
        var value = Environment.GetEnvironmentVariable(VariableName);
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
