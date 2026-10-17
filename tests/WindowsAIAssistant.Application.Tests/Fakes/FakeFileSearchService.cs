using WindowsAIAssistant.Core.Abstractions.Files;
using WindowsAIAssistant.Core.Models;

namespace WindowsAIAssistant.Application.Tests.Fakes;

public sealed class FakeFileSearchService : IFileSearchService
{
    public IReadOnlyCollection<FileSearchResult> Results { get; set; } =
        Array.Empty<FileSearchResult>();

    public string? LastQuery { get; private set; }

    public int CallCount { get; private set; }
    public FileSearchRequest? LastRequest { get; private set; }

    public async Task<FileSearchResponse> SearchAsync(FileSearchRequest request, CancellationToken cancellationToken = default)
    {
        LastRequest = request;
        return new FileSearchResponse(await SearchAsync(request.Query, cancellationToken), "Test search completed.");
    }

    public Task<IReadOnlyCollection<FileSearchResult>> SearchAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CallCount++;
        LastQuery = query;
        return Task.FromResult(Results);
    }
}
