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

    /// <summary>
    /// Gets a value indicating whether a provider runs on this machine, and so whether
    /// anything sent to it stays on this machine.
    /// <para>
    /// This is the test behind the document cloud-processing permission, and it is deliberately
    /// pessimistic. A provider this build does not recognize counts as remote, because being
    /// wrong in that direction refuses a request that could have been allowed, while being
    /// wrong the other way would send a document off the machine having asked permission to
    /// keep it.
    /// </para>
    /// </summary>
    public static bool IsLocal(AIProviderType provider) => provider is
        AIProviderType.Local or AIProviderType.Mock;
}
