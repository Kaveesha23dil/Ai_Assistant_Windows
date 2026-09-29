using WindowsAIAssistant.Core.Enums;

namespace WindowsAIAssistant.Core.Models.Vision;

/// <summary>
/// Where a permission for putting an image in front of a model came from.
/// <para>
/// Small on purpose. It travels with the request into the provider, which is what lets the
/// provider refuse to send anything it was handed without consent, and into the log line, which
/// is what lets an audit of the log show that a cloud call was preceded by consent rather than
/// merely accompanied by it.
/// </para>
/// </summary>
public sealed record VisionPrivacyContext
{
    public VisionPrivacyContext(VisionConsentSource consentSource, bool analysisConsent, bool cloudConsent)
    {
        ConsentSource = consentSource;
        AnalysisConsent = analysisConsent;
        CloudConsent = cloudConsent;
    }

    /// <summary>A context for work that never leaves the machine.</summary>
    public static VisionPrivacyContext LocalOnly { get; } =
        new(VisionConsentSource.LocalOnly, analysisConsent: true, cloudConsent: false);

    /// <summary>A context for an image that may not be sent anywhere.</summary>
    public static VisionPrivacyContext NotAllowed { get; } =
        new(VisionConsentSource.NotAllowed, analysisConsent: false, cloudConsent: false);

    /// <summary>Gets the strongest permission in force.</summary>
    public VisionConsentSource ConsentSource { get; }

    /// <summary>Gets a value indicating whether screen analysis was granted.</summary>
    public bool AnalysisConsent { get; }

    /// <summary>Gets a value indicating whether sending screen content to a cloud provider was granted.</summary>
    public bool CloudConsent { get; }

    /// <summary>
    /// Gets a value indicating whether an image from a capture on this machine may be sent to a
    /// cloud provider.
    /// <para>
    /// Both switches, and the moment the person asked. The analysis switch alone would be a
    /// decision about being shown an answer; only the cloud switch is a decision about where the
    /// picture goes, and only the two together with a request are a decision to do it now.
    /// </para>
    /// </summary>
    public bool AllowsCloudImageSubmission =>
        ConsentSource == VisionConsentSource.CloudConsent && AnalysisConsent && CloudConsent;
}
