using Microsoft.Extensions.Logging;
using WindowsAIAssistant.Application.Common.Mapping;
using WindowsAIAssistant.Application.Common.Validation;
using WindowsAIAssistant.Application.DTOs;
using WindowsAIAssistant.Core.Abstractions.Files;

namespace WindowsAIAssistant.Application.Files.Queries.SearchFiles;

public sealed class SearchFilesHandler
{
    private readonly IFileSearchService _fileSearchService;
    private readonly ILogger<SearchFilesHandler> _logger;

    public SearchFilesHandler(IFileSearchService fileSearchService, ILogger<SearchFilesHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(fileSearchService);
        ArgumentNullException.ThrowIfNull(logger);
        _fileSearchService = fileSearchService;
        _logger = logger;
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

        _logger.LogInformation("File search started. {QueryLength}", searchTerm.Length);
        try
        {
            var results = await _fileSearchService
                .SearchAsync(searchTerm, cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            var dtos = results.Select(result => result.ToDto()).ToArray();
            _logger.LogInformation("File search completed with {ResultCount} results.", dtos.Length);
            return dtos;
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("File search cancelled by caller.");
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "File search failed.");
            throw;
        }
    }
}
