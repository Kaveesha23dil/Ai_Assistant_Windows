namespace WindowsAIAssistant.Core.Models.Documents;

/// <summary>
/// What one step of a streamed document answer reports.
/// </summary>
public enum DocumentAnalysisUpdateKind
{
    /// <summary>The provider accepted the request. Carries the provider and model.</summary>
    Started,

    /// <summary>A fragment of the answer, to append to what is already shown.</summary>
    Delta,

    /// <summary>The answer is finished. Carries the whole result, with its references.</summary>
    Completed,

    /// <summary>The answer stopped early. Carries a code and a sentence fit to show.</summary>
    Failed,
}
