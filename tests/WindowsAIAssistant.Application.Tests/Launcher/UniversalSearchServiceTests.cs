using Microsoft.Extensions.Logging.Abstractions;
using WindowsAIAssistant.Application.Launcher;
using WindowsAIAssistant.Core.Abstractions.Launcher;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models.Launcher;

namespace WindowsAIAssistant.Application.Tests.Launcher;

public class UniversalSearchServiceTests
{
    [Fact]
    public async Task Search_RanksDeduplicates_AndLimits()
    {
        var providers = new IUniversalSearchProvider[]
        {
            new StubProvider(
                "first",
                Result("file:a", "Alpha", 0.4),
                Result("file:b", "Beta", 0.9)),
            new StubProvider(
                "second",
                Result("file:a", "Alpha duplicate", 0.8),
                Result("file:c", "Gamma", 0.5)),
        };

        var service = CreateService(providers, maximumResults: 2);

        var results = await service.SearchAsync("alpha", 2);

        Assert.Equal(2, results.Count);
        Assert.Equal("file:b", results[0].Id);
        Assert.Equal("file:a", results[1].Id);
        Assert.Equal("Alpha duplicate", results[1].Title);
    }

    [Fact]
    public async Task Search_ReturnsOtherProviders_WhenOneFails()
    {
        var providers = new IUniversalSearchProvider[]
        {
            new ThrowingProvider(),
            new StubProvider("working", Result("file:a", "Alpha", 1)),
        };

        var service = CreateService(providers);

        var results = await service.SearchAsync("alpha", 10);

        var result = Assert.Single(results);
        Assert.Equal("file:a", result.Id);
    }

    [Fact]
    public async Task Search_AbandonsProvider_ThatExceedsItsBudget()
    {
        var providers = new IUniversalSearchProvider[]
        {
            new SlowProvider(),
            new StubProvider("working", Result("file:a", "Alpha", 1)),
        };

        var service = CreateService(providers, timeoutMilliseconds: 50);

        var results = await service.SearchAsync("alpha", 10);

        var result = Assert.Single(results);
        Assert.Equal("file:a", result.Id);
    }

    [Fact]
    public async Task Search_ReturnsEmpty_ForBlankQuery()
    {
        var service = CreateService([new StubProvider("first", Result("file:a", "Alpha", 1))]);

        Assert.Empty(await service.SearchAsync("  ", 10));
    }

    [Fact]
    public async Task Search_PropagatesCallerCancellation()
    {
        var service = CreateService([new SlowProvider()], timeoutMilliseconds: 5000);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.SearchAsync("alpha", 10, cancellation.Token));
    }

    private static UniversalSearchService CreateService(
        IReadOnlyList<IUniversalSearchProvider> providers,
        int maximumResults = 20,
        int timeoutMilliseconds = 700) =>
        new(
            providers,
            new LauncherSearchPolicy
            {
                MaximumResults = maximumResults,
                ProviderTimeoutMilliseconds = timeoutMilliseconds,
            },
            NullLogger<UniversalSearchService>.Instance);

    private static SearchResult Result(string id, string title, double score) =>
        new()
        {
            Id = id,
            Title = title,
            Type = SearchResultType.File,
            Score = score,
        };

    private sealed class StubProvider : IUniversalSearchProvider
    {
        private readonly SearchResult[] _results;

        public StubProvider(string name, params SearchResult[] results)
        {
            Name = name;
            _results = results;
        }

        public string Name { get; }

        public Task<IReadOnlyList<SearchResult>> SearchAsync(
            string query,
            int maximumResults,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SearchResult>>(_results.Take(maximumResults).ToArray());
    }

    private sealed class ThrowingProvider : IUniversalSearchProvider
    {
        public string Name => "throwing";

        public Task<IReadOnlyList<SearchResult>> SearchAsync(
            string query,
            int maximumResults,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("provider failed");
    }

    private sealed class SlowProvider : IUniversalSearchProvider
    {
        public string Name => "slow";

        public async Task<IReadOnlyList<SearchResult>> SearchAsync(
            string query,
            int maximumResults,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return [];
        }
    }
}
