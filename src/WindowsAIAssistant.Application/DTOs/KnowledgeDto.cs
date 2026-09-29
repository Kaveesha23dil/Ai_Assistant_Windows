using WindowsAIAssistant.Core.Enums;

namespace WindowsAIAssistant.Application.DTOs;

/// <summary>
/// A knowledge base as the interface shows it.
/// <para>
/// A name and a count. Everything else about a base — where it is stored, how large it is on
/// disk — is the application's business, and a count of documents is the only number a person
/// can do anything with.
/// </para>
/// </summary>
public sealed record KnowledgeBaseDto
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    public string Description { get; init; } = string.Empty;

    public required DateTimeOffset CreatedAt { get; init; }

    public int DocumentCount { get; init; }

    public int ChunkCount { get; init; }
}

/// <summary>
/// A document in a base, as the interface shows it.
/// <para>
/// The file's name and where it is in its indexing, and never its path. The path is needed to
/// reindex and to notice a change, which is why it exists in the index, and it is not needed by
/// anything on screen — so it is not carried here, where a value nobody displays can still be
/// written to a log or put in a screenshot by accident.
/// </para>
/// </summary>
public sealed record KnowledgeDocumentDto
{
    public required Guid Id { get; init; }

    public required Guid KnowledgeBaseId { get; init; }

    public required string FileName { get; init; }

    public DocumentFileType FileType { get; init; }

    public required KnowledgeDocumentStatus Status { get; init; }

    /// <summary>Gets the reason the document is in the state it is in, in words for a person.</summary>
    public string StatusMessage { get; init; } = string.Empty;

    public int ChunkCount { get; init; }

    public long FileSizeBytes { get; init; }

    public DateTimeOffset? IndexedAt { get; init; }

    /// <summary>
    /// Gets the space the document's vectors live in, for display only — never the vectors, and
    /// never enough of a space to reconstruct one.
    /// </summary>
    public string EmbeddingSpace { get; init; } = string.Empty;

    public bool NeedsReindex => Status is KnowledgeDocumentStatus.ReindexRequired or KnowledgeDocumentStatus.Outdated;

    public bool IsIndexed => Status.IsSearchable();
}

/// <summary>
/// One indexed passage, as a search shows it.
/// <para>
/// The passage's text is included because a person looking for a quote needs to read the quote,
/// and the citation beside it because a quote without a place cannot be checked. The vector is
/// not, and neither is the passage's content hash: both are how the index works, and neither is
/// anything a reader would want to see.
/// </para>
/// </summary>
public sealed record KnowledgeSearchResultDto
{
    public required Guid ChunkId { get; init; }

    public required Guid DocumentId { get; init; }

    public required string FileName { get; init; }

    public DocumentFileType FileType { get; init; }

    /// <summary>Gets the citation a reader could follow, such as "notes.pdf — page 4".</summary>
    public required string Citation { get; init; }

    public required string Text { get; init; }

    public double CombinedScore { get; init; }

    public bool HasVectorScore { get; init; }
}

/// <summary>
/// An answer taken from a knowledge base, and where it came from.
/// <para>
/// The passages that were used are carried with the answer rather than looked up afterwards,
/// because the two are decided together: the context was built from the sources that are listed,
/// so a source the model did not actually see cannot appear in the list, and a list that
/// disagreed with the prompt would be a citation to something that was never sent.
/// </para>
/// </summary>
public sealed record KnowledgeAnswerDto
{
    public required bool IsSuccess { get; init; }

    public required string Answer { get; init; }

    public required IReadOnlyList<KnowledgeSourceDto> Sources { get; init; }

    public int RetrievedCount { get; init; }

    /// <summary>
    /// Gets a value indicating whether the context was cut short to fit the limit, which the
    /// person should know before treating the answer as covering their whole base.
    /// </summary>
    public bool WasTruncated { get; init; }

    /// <summary>
    /// Gets a value indicating whether the passages were found by wording alone because
    /// embeddings were unavailable. A different search produced this answer, and it is shown
    /// rather than passed off as a semantic one.
    /// </summary>
    public bool IsKeywordOnly { get; init; }

    public string? ErrorCode { get; init; }

    public string? ErrorMessage { get; init; }

    public bool HasAnswer => IsSuccess && !string.IsNullOrWhiteSpace(Answer);
}

/// <summary>One source an answer was taken from.</summary>
public sealed record KnowledgeSourceDto
{
    public required int Number { get; init; }

    public required Guid DocumentId { get; init; }

    public required string FileName { get; init; }

    /// <summary>Gets where in the document the passage was, as the document reported it.</summary>
    public string Reference { get; init; } = string.Empty;

    /// <summary>
    /// Gets the line the page shows, such as "[1] contract.pdf — page 4".
    /// <para>
    /// Built here rather than in the view, and rather than by relying on the record's own
    /// <c>ToString</c>. A citation is the one thing on this page somebody is expected to read
    /// aloud to check, so its shape should be decided where the meaning is and tested there,
    /// rather than emerging from whatever a record happens to print.
    /// </para>
    /// </summary>
    public string Citation => string.IsNullOrWhiteSpace(Reference)
        ? $"[{Number}] {FileName}"
        : $"[{Number}] {FileName} — {Reference}";
}
