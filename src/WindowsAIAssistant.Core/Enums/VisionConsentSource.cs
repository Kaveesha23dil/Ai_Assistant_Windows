namespace WindowsAIAssistant.Core.Enums;

/// <summary>
/// Where the permission for putting an image in front of a model came from.
/// <para>
/// Carried on the request rather than checked and forgotten, so that the provider, the log
/// line, and the answer itself can all answer the same question: was this allowed, and by what.
/// A provider that receives an image with <see cref="CloudConsent"/> has been handed evidence
/// that both screen-analysis and cloud-analysis were granted, and a provider that receives
/// <see cref="NotAllowed"/> should never have been reached at all.
/// </para>
/// </summary>
public enum VisionConsentSource
{
    /// <summary>No image may be sent anywhere. The request must be answered locally or refused.</summary>
    NotAllowed = 0,

    /// <summary>
    /// The image stays on this machine, which is why it reached a provider at all: the provider
    /// is a local model, or the provider only ever receives text recovered from the image.
    /// </summary>
    LocalOnly = 1,

    /// <summary>The person asked for analysis, and both screen-analysis and cloud-analysis were granted.</summary>
    CloudConsent = 2
}
