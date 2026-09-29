namespace WindowsAIAssistant.Core.Enums;

/// <summary>
/// Which embedding provider produced a vector.
/// <para>
/// A closed set for the same reason <see cref="AIProviderType"/> is: the knowledge base needs to
/// know whether a vector came from this machine or from somewhere else before it is compared or
/// sent anywhere, and a free-text name would make that question answerable only by a string
/// comparison scattered through the application. A provider this build does not know becomes
/// <see cref="Custom"/>, which is treated as remote.
/// </para>
/// </summary>
public enum EmbeddingProviderKind
{
    /// <summary>Not assigned or not recognized yet.</summary>
    Unknown = 0,

    /// <summary>An OpenAI-compatible cloud provider.</summary>
    OpenAI = 1,

    /// <summary>
    /// On-device inference. Nothing sends text away, so it needs no cloud-embedding permission
    /// and its vectors may be stored without asking anybody.
    /// </summary>
    Local = 2,

    /// <summary>A provider this build does not recognize. Treated as remote.</summary>
    Custom = 3,

    /// <summary>
    /// The built-in development provider. It produces deterministic vectors without a network,
    /// which is what lets the whole retrieval path be exercised and tested with no account, no
    /// key, and no cost.
    /// </summary>
    Mock = 4,
}

/// <summary>
/// The embedding providers this build can serve, and the local-versus-remote test behind the
/// cloud-embedding permission.
/// <para>
/// Paired with <see cref="AIProviderTypes"/> deliberately. Both answer "does this leave the
/// machine", and they have to agree, or the same question asked through two features would get
/// two different answers.
/// </para>
/// </summary>
public static class EmbeddingProviderKinds
{
    /// <summary>Gets the provider names that may be chosen, in the order they should be offered.</summary>
    public static IReadOnlyList<string> SupportedNames { get; } =
    [
        nameof(EmbeddingProviderKind.Mock),
        nameof(EmbeddingProviderKind.OpenAI),
    ];

    /// <summary>
    /// Gets a value indicating whether a provider runs on this machine, and so whether a chunk
    /// sent to it stays on this machine.
    /// <para>
    /// Pessimistic in the same direction, and for the same reason, as
    /// <see cref="AIProviderTypes.IsLocal"/>: an unrecognized provider counts as remote, so
    /// adding a cloud provider later cannot start posting document text without somebody having
    /// turned a permission on.
    /// </para>
    /// </summary>
    public static bool IsLocal(EmbeddingProviderKind provider) => provider is
        EmbeddingProviderKind.Local or EmbeddingProviderKind.Mock;
}
