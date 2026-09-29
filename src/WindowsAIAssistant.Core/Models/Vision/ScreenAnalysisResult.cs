using WindowsAIAssistant.Core.Enums;

namespace WindowsAIAssistant.Core.Models.Vision;

/// <summary>
/// What came back from looking at an image.
/// <para>
/// Every field is something a person can act on, and none of them is the image. The warnings
/// matter as much as the answer: a description of a blurry frame is still worth having, as long
/// as the reader is also told it was blurry.
/// </para>
/// </summary>
public sealed record ScreenAnalysisResult
{
    public ScreenAnalysisResult(
        string summary,
        string? answer = null,
        string? extractedText = null,
        string? detectedError = null,
        IReadOnlyCollection<string>? suggestedNextSteps = null,
        string? provider = null,
        IReadOnlyCollection<ScreenWarningKind>? warnings = null)
    {
        ArgumentNullException.ThrowIfNull(summary);

        Summary = summary;
        Answer = string.IsNullOrWhiteSpace(answer) ? null : answer;
        ExtractedText = string.IsNullOrWhiteSpace(extractedText) ? null : extractedText;
        DetectedError = string.IsNullOrWhiteSpace(detectedError) ? null : detectedError;
        SuggestedNextSteps = suggestedNextSteps is null
            ? Array.Empty<string>()
            : suggestedNextSteps.Where(step => !string.IsNullOrWhiteSpace(step)).ToArray();
        Provider = string.IsNullOrWhiteSpace(provider) ? null : provider;
        Warnings = warnings is null ? Array.Empty<ScreenWarningKind>() : warnings.Distinct().ToArray();
    }

    /// <summary>Gets the short description of what is visible. Never empty.</summary>
    public string Summary { get; }

    /// <summary>Gets the answer to the person's question, when they asked one.</summary>
    public string? Answer { get; }

    /// <summary>Gets the text read from the image, when that is what was asked for.</summary>
    public string? ExtractedText { get; }

    /// <summary>Gets an error message, dialog title, or stack of warnings that was recognised.</summary>
    public string? DetectedError { get; }

    /// <summary>Gets what the person could try next, when the answer suggests anything.</summary>
    public IReadOnlyCollection<string> SuggestedNextSteps { get; }

    /// <summary>Gets which provider produced this, for display and for diagnostics.</summary>
    public string? Provider { get; }

    /// <summary>Gets qualifications on this answer that did not stop it being produced.</summary>
    public IReadOnlyCollection<ScreenWarningKind> Warnings { get; }

    /// <summary>Builds the text shown in the assistant, without echoing any warning prose twice.</summary>
    public string BuildDisplayText()
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(Answer))
        {
            parts.Add(Answer!);
        }

        if (DetectedError is not null)
        {
            parts.Add($"Error on screen: {DetectedError}");
        }

        parts.Add(Summary);

        return string.Join("\n\n", parts);
    }

    /// <summary>
    /// Returns this result with additional warnings merged in.
    /// <para>
    /// A method rather than a <c>with</c> expression because the properties are computed once in
    /// the constructor: a record's copy would bypass that, and the normalisation of blank strings
    /// and duplicate warnings is the reason this type exists.
    /// </para>
    /// </summary>
    public ScreenAnalysisResult WithWarnings(IEnumerable<ScreenWarningKind> additional) =>
        new(
            Summary,
            Answer,
            ExtractedText,
            DetectedError,
            SuggestedNextSteps,
            Provider,
            Warnings.Concat(additional).Distinct().ToArray());
}
