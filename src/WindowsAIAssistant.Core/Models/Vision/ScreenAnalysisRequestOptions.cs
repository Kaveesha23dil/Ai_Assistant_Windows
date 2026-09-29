using WindowsAIAssistant.Core.Enums;

namespace WindowsAIAssistant.Core.Models.Vision;

/// <summary>
/// What a person asked for, before anything is captured.
/// <para>
/// The moment of intent, separated from the frame. This exists so that the consent gate has
/// something to read: the decision about whether an image may be sent is made from the request
/// as it was asked, never from the fact that a frame happens to be available. A capture left
/// over from a previous question therefore cannot turn a typed message into a cloud request.
/// </para>
/// </summary>
public sealed record ScreenAnalysisRequestOptions
{
    public ScreenAnalysisRequestOptions(
        ScreenAnalysisType analysisType = ScreenAnalysisType.Describe,
        ScreenCaptureType captureType = ScreenCaptureType.Display,
        string? userQuestion = null,
        string? suggestedFileName = null)
    {
        AnalysisType = analysisType;
        CaptureType = captureType;
        UserQuestion = string.IsNullOrWhiteSpace(userQuestion) ? null : userQuestion;
        SuggestedFileName = string.IsNullOrWhiteSpace(suggestedFileName) ? null : suggestedFileName;
    }

    /// <summary>Gets what the person is asking for.</summary>
    public ScreenAnalysisType AnalysisType { get; }

    /// <summary>Gets what should be captured.</summary>
    public ScreenCaptureType CaptureType { get; }

    /// <summary>Gets the person's own words, when they asked a question rather than a task.</summary>
    public string? UserQuestion { get; }

    /// <summary>Gets a name to offer if the frame is later saved.</summary>
    public string? SuggestedFileName { get; }

    /// <summary>Returns these options with a different target, for a second pass over a frame.</summary>
    public ScreenAnalysisRequestOptions WithCaptureType(ScreenCaptureType captureType) =>
        new(AnalysisType, captureType, UserQuestion, SuggestedFileName);

    /// <summary>Returns these options with a different task.</summary>
    public ScreenAnalysisRequestOptions WithAnalysisType(ScreenAnalysisType analysisType) =>
        new(analysisType, CaptureType, UserQuestion, SuggestedFileName);
}
