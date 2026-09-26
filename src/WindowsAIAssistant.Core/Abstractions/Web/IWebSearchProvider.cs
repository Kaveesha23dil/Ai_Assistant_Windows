using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Enums;

namespace WindowsAIAssistant.Core.Abstractions.Web;

/// <summary>
/// Builds a search address for one site.
/// <para>
/// A provider only produces a URI. Deciding which provider to use, opening it, and speaking
/// the result are separate concerns, so a new site needs no changes to the voice handlers.
/// </para>
/// </summary>
public interface IWebSearchProvider
{
    /// <summary>Gets the display name spoken back to the user.</summary>
    string Name { get; }

    /// <summary>Gets the provider identifier used in command parameters.</summary>
    WebSearchProviderType ProviderType { get; }

    /// <summary>Gets a value indicating whether this provider is the general-purpose default.</summary>
    bool IsDefault { get; }

    /// <summary>
    /// Builds a fully encoded search address. The query is URL-encoded by the
    /// implementation, never concatenated into a shell string.
    /// </summary>
    Result<Uri> BuildSearchUri(string query);
}
