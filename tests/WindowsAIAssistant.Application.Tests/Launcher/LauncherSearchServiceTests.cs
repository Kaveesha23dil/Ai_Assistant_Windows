using Microsoft.Extensions.Logging.Abstractions;
using WindowsAIAssistant.Application.Launcher;
using WindowsAIAssistant.Core.Abstractions.Launcher;
using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models.Launcher;

namespace WindowsAIAssistant.Application.Tests.Launcher;

public class LauncherSearchServiceTests
{
    private readonly StubUniversalSearch _universal = new();
    private readonly LauncherSearchService _service;

    public LauncherSearchServiceTests()
    {
        _service = new LauncherSearchService(
            new LauncherIntentService(),
            new CalculatorService(),
            new CommandPaletteService(),
            _universal,
            new LauncherSearchPolicy { MaximumResults = 20, ProviderTimeoutMilliseconds = 700 },
            NullLogger<LauncherSearchService>.Instance);
    }

    [Fact]
    public async Task EmptyQuery_ShowsCommandPalette()
    {
        var result = await Search(string.Empty);

        Assert.True(result.IsSuccess);
        Assert.NotEmpty(result.Items);
        Assert.All(result.Items, item => Assert.NotEqual(SearchResultType.Ai, item.Type));
        Assert.Equal(0, _universal.CallCount);
    }

    [Fact]
    public async Task Calculator_Result_IsReadyToCopy()
    {
        var result = await Search("= 2 + 2");

        var item = Assert.Single(result.Items);
        Assert.Equal(SearchResultType.Calculator, item.Type);
        Assert.Equal("4", item.Title);
        Assert.Equal(LauncherActionKind.CopyText, item.Action.Kind);
        Assert.Equal("4", item.Action.Target);
    }

    [Fact]
    public async Task InvalidCalculation_ReturnsStableFailure()
    {
        var result = await Search("= 2 / 0");

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.LauncherCalculationInvalid, result.ErrorCode);
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task Question_AddsAssistantRow_AndDoesNotSendIt()
    {
        _universal.Results = [FileResult("file:a", "Alpha", 0.5)];

        var result = await Search("what is the privacy policy?");

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Message);
        Assert.Equal(2, result.Items.Count);
        Assert.Equal(SearchResultType.Ai, result.Items[0].Type);
        Assert.Equal(LauncherActionKind.AskAssistant, result.Items[0].Action.Kind);
        Assert.Equal("what is the privacy policy?", result.Items[0].Action.Target);
        Assert.Equal(SearchResultType.File, result.Items[1].Type);
    }

    [Fact]
    public async Task WebPrefix_AddsWebRowFirst()
    {
        _universal.Results = [FileResult("file:a", "Alpha", 0.5)];

        var result = await Search("web winui acrylic");

        Assert.Equal(SearchResultType.Web, result.Items[0].Type);
        Assert.Equal(LauncherActionKind.OpenUri, result.Items[0].Action.Kind);
        Assert.Contains("winui%20acrylic", result.Items[0].Action.Target, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CommandPrefix_FiltersToMatchingCommands()
    {
        var result = await Search("/files");

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Items);
        Assert.Equal(SearchResultType.Command, item.Type);
        Assert.Contains("Files", item.Title, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(LauncherActionKind.Navigate, item.Action.Kind);
    }

    [Fact]
    public async Task UniversalMiss_HasDeliberateNextStep()
    {
        var result = await Search("totally unknown thing");

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Items);
        Assert.Equal(SearchResultType.Ai, item.Type);
        Assert.Equal(LauncherActionKind.AskAssistant, item.Action.Kind);
    }

    [Fact]
    public async Task UniversalFailure_IsReported()
    {
        _universal.Exception = new InvalidOperationException("boom");

        var result = await Search("filename");

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.LauncherSearchFailed, result.ErrorCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(50)]
    [InlineData(500)]
    public async Task MaximumResults_IsClamped(int maximum)
    {
        _universal.Results = Enumerable
            .Range(0, 80)
            .Select(index => FileResult($"file:{index}", $"File {index}", 1 - (index / 100.0)))
            .ToArray();

        var result = await _service.SearchAsync(
            new LauncherRequest { Text = "file", MaximumResults = maximum });

        Assert.InRange(result.Items.Count, 1, 50);
    }

    private Task<LauncherResult> Search(string text) =>
        _service.SearchAsync(new LauncherRequest { Text = text });

    private static SearchResult FileResult(string id, string title, double score) =>
        new()
        {
            Id = id,
            Title = title,
            Type = SearchResultType.File,
            Group = "Files",
            Score = score,
        };

    private sealed class StubUniversalSearch : IUniversalSearchService
    {
        public IReadOnlyList<SearchResult> Results { get; set; } = [];

        public Exception? Exception { get; set; }

        public int CallCount { get; private set; }

        public Task<IReadOnlyList<SearchResult>> SearchAsync(
            string query,
            int maximumResults,
            CancellationToken cancellationToken = default)
        {
            CallCount++;

            return Exception is null
                ? Task.FromResult<IReadOnlyList<SearchResult>>(Results.Take(maximumResults).ToArray())
                : Task.FromException<IReadOnlyList<SearchResult>>(Exception);
        }
    }
}
