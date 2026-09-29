using WindowsAIAssistant.Application.DTOs;
using WindowsAIAssistant.Core.Models.Knowledge;

namespace WindowsAIAssistant.Application.Knowledge.Handlers;

/// <summary>
/// Turns what the knowledge services returned into what the interface shows.
/// <para>
/// One place, for the same reason the document mapper is one place: the page, the voice path,
/// and the answer list must not be able to describe the same document or the same citation two
/// different ways, and nothing that should not reach a view model is carried through the gap.
/// </para>
/// </summary>
public static class KnowledgeMapper
{
    public static KnowledgeBaseDto ToDto(KnowledgeBase base_, int documentCount, int chunkCount)
    {
        ArgumentNullException.ThrowIfNull(base_);

        return new KnowledgeBaseDto
        {
            Id = base_.Id,
            Name = base_.Name,
            Description = base_.Description ?? string.Empty,
            CreatedAt = base_.CreatedAt,
            DocumentCount = documentCount,
            ChunkCount = chunkCount,
        };
    }

    public static KnowledgeDocumentDto ToDto(KnowledgeDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        return new KnowledgeDocumentDto
        {
            Id = document.Id,
            KnowledgeBaseId = document.KnowledgeBaseId,
            FileName = document.FileName,
            FileType = document.FileType,
            Status = document.Status,
            StatusMessage = document.StatusMessage ?? string.Empty,
            ChunkCount = document.ChunkCount,
            FileSizeBytes = document.FileSize,
            IndexedAt = document.IndexedAt,
            EmbeddingSpace = document.EmbeddingSpace?.Key ?? string.Empty,
        };
    }

    public static KnowledgeSearchResultDto ToDto(KnowledgeSearchResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new KnowledgeSearchResultDto
        {
            ChunkId = result.ChunkId,
            DocumentId = result.DocumentId,
            FileName = result.FileName,
            FileType = result.FileType,
            Citation = result.CitationText,
            Text = result.Text,
            CombinedScore = result.CombinedScore,
            HasVectorScore = result.HasVectorScore,
        };
    }

    /// <summary>
    /// Maps a service answer, and numbers the sources in the order the context listed them —
    /// which is the order the prompt cited them by, so numbering them again differently here
    /// would point a citation at the wrong passage.
    /// </summary>
    public static KnowledgeAnswerDto ToDto(RagAnswer answer)
    {
        ArgumentNullException.ThrowIfNull(answer);

        return new KnowledgeAnswerDto
        {
            IsSuccess = answer.IsSuccessful,
            Answer = answer.Text,
            Sources = ToSources(answer.Sources),
            RetrievedCount = answer.RetrievedCount,
            WasTruncated = answer.WasContextTruncated,
            IsKeywordOnly = answer.WasLexicalOnly,
            ErrorCode = answer.ErrorCode,
            ErrorMessage = answer.ErrorMessage,
        };
    }
    public static IReadOnlyList<KnowledgeSourceDto> ToSources(IReadOnlyList<KnowledgeSearchResult> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);

        var mapped = new List<KnowledgeSourceDto>(sources.Count);

        for (var index = 0; index < sources.Count; index++)
        {
            var source = sources[index];

            mapped.Add(new KnowledgeSourceDto
            {
                Number = index + 1,
                DocumentId = source.DocumentId,
                FileName = source.FileName,
                Reference = source.ReferenceDisplay,
            });
        }

        return mapped;
    }
}
