using Microsoft.Extensions.Logging;
using WindowsAIAssistant.Core.Abstractions.Knowledge;
using WindowsAIAssistant.Core.Abstractions.Launcher;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models.Knowledge;
using WindowsAIAssistant.Core.Models.Launcher;

namespace WindowsAIAssistant.Infrastructure.Launcher.SearchProviders;

/// <summary>
/// Suggests passages from the local knowledge index. It asks for lexical retrieval only, so a
/// keystroke can never trigger an embedding request or a cloud call.
/// </summary>
public sealed class KnowledgeSearchProvider : IUniversalSearchProvider
{
    private readonly IKnowledgeBaseRepository _bases;
    private readonly IRagRetriever _retriever;
    private readonly ILogger<KnowledgeSearchProvider> _logger;

    public KnowledgeSearchProvider(
        IKnowledgeBaseRepository bases,
        IRagRetriever retriever,
        ILogger<KnowledgeSearchProvider> logger)
    {
        ArgumentNullException.ThrowIfNull(bases);
        ArgumentNullException.ThrowIfNull(retriever);
        ArgumentNullException.ThrowIfNull(logger);

        _bases = bases;
        _retriever = retriever;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "Knowledge";

    /// <inheritdoc />
    public async Task<IReadOnlyList<SearchResult>> SearchAsync(
        string query,
        int maximumResults,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query) || maximumResults < 1)
        {
            return [];
        }

        var bases = await _bases.ListAsync(cancellationToken).ConfigureAwait(false);
        if (bases.Count == 0)
        {
            return [];
        }

        var defaultBase = bases.FirstOrDefault(item => item.IsDefault) ?? bases[0];
        var search = KnowledgeQuery.Create(defaultBase.Id, query.Trim(), maximumResults);
        search = search with { LexicalOnly = true };

        var results = await _retriever
            .RetrieveAsync(search, cancellationToken)
            .ConfigureAwait(false);

        _logger.LogDebug("Knowledge search returned {Count} passages.", results.Count);

        return results
            .Select(result => new SearchResult
            {
                Id = $"knowledge:{result.ChunkId}",
                Title = result.FileName,
                Description = Summarize(result),
                Type = SearchResultType.Knowledge,
                Group = "Knowledge",
                Score = Math.Clamp(result.CombinedScore, 0, 1),
                Glyph = "\uE8F1",
                Action = LauncherAction.For(LauncherActionKind.OpenKnowledge, query.Trim()),
            })
            .ToArray();
    }

    private static string Summarize(KnowledgeSearchResult result)
    {
        var section = result.Sections.FirstOrDefault();
        var text = string.Join(
            ' ',
            result.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        if (text.Length > 160)
        {
            text = $"{text[..157]}...";
        }

        return string.IsNullOrWhiteSpace(section) ? text : $"{section}: {text}";
    }
}
