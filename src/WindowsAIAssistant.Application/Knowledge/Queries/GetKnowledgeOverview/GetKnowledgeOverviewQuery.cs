using WindowsAIAssistant.Application.DTOs;
using WindowsAIAssistant.Application.Knowledge.Handlers;
using WindowsAIAssistant.Core.Abstractions.Knowledge;

namespace WindowsAIAssistant.Application.Knowledge.Queries.GetKnowledgeOverview;

/// <summary>
/// Everything the knowledge page shows: the bases, and the documents in the chosen one.
/// <para>
/// One read rather than several. A page that asked for the bases and then the documents of each
/// would make the count and the list disagree whenever a document was added between the two
/// reads, which is exactly when somebody is watching to see whether their document arrived.
/// </para>
/// </summary>
public sealed record KnowledgeOverview(
    IReadOnlyList<KnowledgeBaseDto> Bases,
    IReadOnlyList<KnowledgeDocumentDto> Documents,
    Guid? SelectedBaseId,
    int TotalDocuments,
    int TotalChunks)
{
    /// <summary>Gets a value indicating whether anything at all has been indexed yet.</summary>
    public bool IsEmpty => TotalDocuments == 0;
}

/// <summary>Reads the current state of the knowledge store.</summary>
public sealed class GetKnowledgeOverviewHandler
{
    private readonly IKnowledgeBaseRepository _bases;
    private readonly IKnowledgeDocumentRepository _documents;
    private readonly KnowledgeProcessingLimits _limits;

    public GetKnowledgeOverviewHandler(
        IKnowledgeBaseRepository bases,
        IKnowledgeDocumentRepository documents,
        KnowledgeProcessingLimits limits)
    {
        ArgumentNullException.ThrowIfNull(bases);
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(limits);

        _bases = bases;
        _documents = documents;
        _limits = limits;
    }

    public async Task<KnowledgeOverview> HandleAsync(
        Guid? knowledgeBaseId = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var bases = await _bases.ListAsync(cancellationToken).ConfigureAwait(false);
        var selected = knowledgeBaseId is { } wanted
            ? bases.FirstOrDefault(candidate => candidate.Id == wanted)
            : bases.FirstOrDefault();

        var documents = selected is null
            ? []
            : await _documents.ListAsync(selected.Id, cancellationToken).ConfigureAwait(false);

        var baseDtos = new List<KnowledgeBaseDto>(bases.Count);
        var totalDocuments = 0;
        var totalChunks = 0;

        // Every base's counts are read rather than only the selected one, so the list on the left
        // can show a number beside each base without the view model counting rows itself.
        foreach (var candidate in bases)
        {
            var candidateDocuments = candidate.Id == selected?.Id
                ? documents
                : await _documents.ListAsync(candidate.Id, cancellationToken).ConfigureAwait(false);

            var chunks = candidateDocuments.Sum(document => document.ChunkCount);

            totalDocuments += candidateDocuments.Count;
            totalChunks += chunks;

            baseDtos.Add(KnowledgeMapper.ToDto(candidate, candidateDocuments.Count, chunks));
        }

        return new KnowledgeOverview(
            baseDtos,
            [.. documents.Select(KnowledgeMapper.ToDto)],
            selected?.Id,
            totalDocuments,
            totalChunks);
    }

    /// <summary>
    /// Gets a value indicating whether the store is allowed to hold anything, which the page
    /// shows before a person adds a first document rather than after the attempt is refused.
    /// </summary>
    public bool IsEnabled => _limits.Enabled;
}
