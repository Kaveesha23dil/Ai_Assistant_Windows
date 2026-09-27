using WindowsAIAssistant.Core.Enums;

namespace WindowsAIAssistant.Core.Abstractions.AI;

/// <summary>
/// Supplies the credential a cloud provider needs, without any layer above it ever holding the
/// secret itself.
/// <para>
/// The key is read on demand and never cached, never logged, and never written to
/// configuration. Exposing it as a property of this interface rather than as a constructor
/// argument is what keeps a resolved client the only thing in the process that can send it
/// somewhere, and a provider that is never called never reads it.
/// </para>
/// </summary>
public interface IAIApiKeyProvider
{
    /// <summary>Gets the provider this credential belongs to.</summary>
    AIProviderType Provider { get; }

    /// <summary>
    /// Gets a value indicating whether a credential is available, so a settings page can
    /// report readiness without receiving the secret.
    /// </summary>
    bool HasApiKey { get; }

    /// <summary>
    /// Returns the credential, or <see langword="null"/> when none is available.
    /// <para>
    /// The value must not be logged, stored, or included in any message shown to a person.
    /// </para>
    /// </summary>
    string? GetApiKey();
}
