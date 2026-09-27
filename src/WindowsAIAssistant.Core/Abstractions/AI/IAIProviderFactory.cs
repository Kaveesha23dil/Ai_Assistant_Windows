using WindowsAIAssistant.Core.Enums;

namespace WindowsAIAssistant.Core.Abstractions.AI;

/// <summary>
/// Chooses the provider that will serve a request.
/// <para>
/// The factory hides the configuration behind the abstraction on purpose. Callers ask for a
/// provider by type and get one back or get nothing; they never read a provider name out of
/// configuration, compare strings, or construct a vendor client. A name that cannot be
/// resolved returns <see langword="null"/> rather than throwing, so a typo in a settings file
/// produces a message the person can act on instead of a failed start.
/// </para>
/// </summary>
public interface IAIProviderFactory
{
    /// <summary>
    /// Gets the provider types this build can serve, whatever the configuration says.
    /// </summary>
    IReadOnlyCollection<AIProviderType> AvailableProviders { get; }

    /// <summary>
    /// Gets the provider the configuration selects, or
    /// <see cref="AIProviderType.Unknown"/> when the configured name is not one this build
    /// can serve.
    /// </summary>
    AIProviderType SelectedProvider { get; }

    /// <summary>Gets the configured model identifier, or <see langword="null"/> when unset.</summary>
    string? SelectedModel { get; }

    /// <summary>
    /// Resolves a provider by type, or returns <see langword="null"/> when no registration
    /// serves it.
    /// </summary>
    IAIProvider? Resolve(AIProviderType providerType);

    /// <summary>
    /// Resolves the configured provider, or returns <see langword="null"/> when the configured
    /// name is unknown or nothing is registered for it.
    /// </summary>
    IAIProvider? ResolveSelected();
}
