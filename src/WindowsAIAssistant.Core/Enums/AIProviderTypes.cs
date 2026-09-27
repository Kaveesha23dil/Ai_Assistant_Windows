namespace WindowsAIAssistant.Core.Enums;

/// <summary>
/// The providers this build can actually serve.
/// <para>
/// This is a fact about the build rather than a setting: the names are here so that the page
/// offering the choice and the handler validating a saved one are reading the same list. Two
/// copies of it would drift, and the drift would show up as a page offering a provider whose
/// own save is then rejected.
/// </para>
/// <para>
/// The names match the members of <see cref="AIProviderType"/> and are the values written to
/// configuration. They are ordered so the one that needs nothing comes first: the provider
/// that works with no account should be the easiest thing to land on.
/// </para>
/// </summary>
public static class AIProviderTypes
{
    /// <summary>
    /// Gets the provider names that may be chosen, in the order they should be offered.
    /// </summary>
    public static IReadOnlyList<string> SupportedNames { get; } =
    [
        nameof(AIProviderType.Mock),
        nameof(AIProviderType.OpenAI),
    ];
}
