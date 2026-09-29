using WindowsAIAssistant.Core.Enums;

namespace WindowsAIAssistant.Core.Models.Knowledge;

/// <summary>
/// A question to be answered, narrowed to a base and optionally to particular documents.
/// <para>
/// The filters are part of the question rather than a parameter of the search, because they are
/// part of what was asked. "Search only my research papers" is a different question from the
/// same words with no filter, and keeping the two in one value means a caller cannot build a
/// filtered search by forgetting to pass the filter on the way down.
/// </para>
/// </summary>
public sealed record KnowledgeQuery
{
    /// <summary>Gets the base to search.</summary>
    public required Guid KnowledgeBaseId { get; init; }

    /// <summary>Gets what was asked.</summary>
    public required string Question { get; init; }

    /// <summary>
    /// Gets how many passages to return, before the similarity threshold is applied. The
    /// threshold is allowed to return fewer, and the retriever is expected to: filling a quota
    /// with passages that merely cleared the bar is how an answer comes to be built from
    /// documents that have nothing to do with it.
    /// </summary>
    public int Count { get; init; } = 8;

    /// <summary>
    /// Gets the documents to restrict the search to, or empty for the whole base.
    /// </summary>
    public IReadOnlyList<Guid> DocumentIds { get; init; } = [];

    /// <summary>Gets the file names to restrict the search to, or empty for the whole base.</summary>
    public IReadOnlyList<string> FileNames { get; init; } = [];

    /// <summary>Gets the document families to restrict the search to, or empty for all of them.</summary>
    public IReadOnlyList<DocumentFileType> FileTypes { get; init; } = [];

    /// <summary>
    /// Gets a value indicating whether only passages found by wording may be returned, with no
    /// vector search at all.
    /// <para>
    /// This is the honest degradation when embeddings cannot be produced — a provider with no
    /// credential, no network, or a permission that has been withdrawn. Answering from a
    /// keyword search while calling it a semantic one would misrepresent how the passages were
    /// chosen, so the mode is carried with the result rather than inferred by a caller.
    /// </para>
    /// </summary>
    public bool LexicalOnly { get; init; }

    /// <summary>Creates a query, validating the two values nothing sensible can be built without.</summary>
    public static KnowledgeQuery Create(
        Guid knowledgeBaseId,
        string question,
        int count = 8) =>
        new()
        {
            KnowledgeBaseId = knowledgeBaseId,
            Question = Validate(question),
            Count = count > 0 ? count : 1,
        };

    /// <summary>Returns the query narrowed to a set of documents.</summary>
    public KnowledgeQuery ForDocuments(IEnumerable<Guid> documentIds) =>
        this with { DocumentIds = [.. documentIds] };

    /// <summary>Returns the query narrowed to a set of file names.</summary>
    public KnowledgeQuery ForFileNames(IEnumerable<string> fileNames) =>
        this with { FileNames = [.. fileNames.Select(name => name.Trim()).Where(name => name.Length > 0)] };

    /// <summary>Returns the query narrowed to a set of document families.</summary>
    public KnowledgeQuery ForFileTypes(IEnumerable<DocumentFileType> fileTypes) =>
        this with { FileTypes = [.. fileTypes] };

    private static string Validate(string question)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(question);
        return question.Trim();
    }
}
