using WindowsAIAssistant.Core.Abstractions.Files;
using WindowsAIAssistant.Core.Models;

namespace WindowsAIAssistant.Application.Tests.Fakes;

public sealed class FakeFileSearchService : IFileSearchService
{
    public IReadOnlyCollection<FileSearchResult> Results { get; set; } =
        Array.Empty<FileSearchResult>();

    public string? LastQuery { get; private set; }

    public int CallCount { get; private set; }

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
