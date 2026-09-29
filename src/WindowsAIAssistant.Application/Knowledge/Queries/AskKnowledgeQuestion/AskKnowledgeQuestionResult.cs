using WindowsAIAssistant.Core.Models.Knowledge;

namespace WindowsAIAssistant.Application.Knowledge.Queries.AskKnowledgeQuestion;

/// <summary>
/// A question answered from the indexed documents, together with the sources it was answered
/// from.
/// <para>
/// The sources travel with the answer rather than being looked up afterwards. A person reading
/// "the renewal is automatic" needs to know which page said it, and asking the index again for
/// that would be a second retrieval that could return a different passage than the one the
/// answer was written from.
/// </para>
/// </summary>
public sealed record KnowledgeAnswer
{
    public required bool IsSuccess { get; init; }

    public required string Answer { get; init; }

    public required IReadOnlyList<KnowledgeSource> Sources { get; init; }

    /// <summary>Gets how many passages were considered before the answer was written.</summary>
    public int RetrievedCount { get; init; }

    /// <summary>Gets a value indicating whether passages were dropped to fit the budget.</summary>
    public bool WasTruncated { get; init; }

    /// <summary>
    /// Gets a value indicating whether the answer came from word matching rather than from
    /// embeddings, which the page says plainly, because an answer built from an exact phrase match
    /// is worth less than one built from meaning and a person should know which they got.
    /// </summary>
    public bool IsKeywordOnly { get; init; }

    public string? ErrorCode { get; init; }

    public string? ErrorMessage { get; init; }

    public static KnowledgeAnswer From(RagAnswer answer)
    {
        ArgumentNullException.ThrowIfNull(answer);

        return new KnowledgeAnswer
        {
            IsSuccess = answer.IsSuccessful,
            Answer = answer.Text,
            Sources = answer.IsSuccessful ? Number(answer.Sources) : [],
            RetrievedCount = answer.RetrievedCount,
            WasTruncated = answer.WasContextTruncated,
            IsKeywordOnly = answer.WasLexicalOnly,
            ErrorCode = answer.ErrorCode,
            ErrorMessage = answer.ErrorMessage,
        };
    }

    /// <summary>
    /// Numbers the sources in the order the context listed them, which is the order the prompt
    /// cited them by. Numbering them again here, differently, would put a citation in the answer
    /// that points at the wrong passage.
    /// </summary>
    private static IReadOnlyList<KnowledgeSource> Number(IReadOnlyList<KnowledgeSearchResult> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);

        var numbered = new List<KnowledgeSource>(sources.Count);

        for (var index = 0; index < sources.Count; index++)
        {
            numbered.Add(KnowledgeSource.From(sources[index], index + 1));
        }

        return numbered;
    }
}

/// <summary>One passage an answer was taken from.</summary>
public sealed record KnowledgeSource
{
    public required int Number { get; init; }

    public required Guid DocumentId { get; init; }

    public required string FileName { get; init; }

    /// <summary>Gets where in the document the passage was, as the document reported it.</summary>
    public string Reference { get; init; } = string.Empty;

    public static KnowledgeSource From(KnowledgeSearchResult result, int number)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new KnowledgeSource
        {
            Number = number,
            DocumentId = result.DocumentId,
            FileName = result.FileName,
            Reference = result.ReferenceDisplay,
        };
    }

    /// <summary>
    /// The source as a reader would cite it: the file, and the place inside it when there is
    /// one. Built the same way everywhere, because a citation that reads differently on the page
    /// than it does aloud is worse than no citation.
    /// </summary>
    public string Citation() =>
        string.IsNullOrEmpty(Reference) ? FileName : $"{FileName} — {Reference}";
}
