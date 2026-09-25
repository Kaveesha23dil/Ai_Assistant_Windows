using WindowsAIAssistant.Application.Common.Mapping;
using WindowsAIAssistant.Application.Common.Validation;
using WindowsAIAssistant.Application.DTOs;
using WindowsAIAssistant.Core.Abstractions.Files;

namespace WindowsAIAssistant.Application.Files.Queries.SearchFiles;

public sealed class SearchFilesHandler
{
    private readonly IFileSearchService _fileSearchService;

    public SearchFilesHandler(IFileSearchService fileSearchService)
    {
        ArgumentNullException.ThrowIfNull(fileSearchService);
        _fileSearchService = fileSearchService;
    }

    public async Task<IReadOnlyCollection<FileSearchResultDto>> HandleAsync(
        SearchFilesQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        var searchTerm = ValidationHelper.RequireText(
            query.SearchTerm,
            nameof(query.SearchTerm),
            "Search term cannot be empty.");
        var results = await _fileSearchService
            .SearchAsync(searchTerm, cancellationToken)
            .ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();
        return results.Select(result => result.ToDto()).ToArray();
    }
}
