using WindowsAIAssistant.Application.DTOs;
using WindowsAIAssistant.Application.Files.Queries.SearchFiles;
using WindowsAIAssistant.Application.Tests.Fakes;
using WindowsAIAssistant.Core.Models;

namespace WindowsAIAssistant.Application.Tests;

public sealed class SearchFilesHandlerTests
{
    [Fact]
    public async Task HandleAsync_WithValidSearch_ReturnsMappedResults()
    {
        var timestamp = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);
        var service = new FakeFileSearchService
        {
            Results =
            [
                new FileSearchResult("notes.txt", @"C:\notes.txt", ".txt", 42, timestamp, 0.9)
            ]
        };
        var handler = new SearchFilesHandler(service);

        IReadOnlyCollection<FileSearchResultDto> results = await handler.HandleAsync(
            new SearchFilesQuery("notes"));

        var result = Assert.Single(results);
        Assert.Equal("notes.txt", result.Name);
        Assert.Equal(@"C:\notes.txt", result.FullPath);
        Assert.Equal(0.9, result.RelevanceScore);
        Assert.Equal("notes", service.LastQuery);
    }

    [Fact]
    public async Task HandleAsync_WithNoResults_ReturnsEmptyCollection()
    {
        var handler = new SearchFilesHandler(new FakeFileSearchService());

        var results = await handler.HandleAsync(new SearchFilesQuery("missing"));

        Assert.Empty(results);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task HandleAsync_WithEmptySearchTerm_ThrowsArgumentException(string searchTerm)
    {
        var service = new FakeFileSearchService();
        var handler = new SearchFilesHandler(service);

        await Assert.ThrowsAsync<ArgumentException>(() => handler.HandleAsync(new SearchFilesQuery(searchTerm)));

        Assert.Equal(0, service.CallCount);
    }
}
